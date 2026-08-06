using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSJS.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Testing;

/// <summary>Result of one test.</summary>
/// <param name="Suite">Declaring class.</param>
/// <param name="Name">Display name.</param>
/// <param name="Outcome">passed, failed, or skipped.</param>
/// <param name="Message">Failure or skip reason.</param>
/// <param name="DurationMs">Wall time inside the JavaScript runtime.</param>
public sealed record TestResult(string Suite, string Name, string Outcome, string? Message, double DurationMs)
{
    public bool Passed => Outcome == "passed";
    public bool Failed => Outcome == "failed";
    public bool Skipped => Outcome == "skipped";
}

/// <summary>Aggregate outcome of a run.</summary>
public sealed record TestRunReport(
    IReadOnlyList<TestResult> Results,
    IReadOnlyList<CsjsDiagnostic> Diagnostics,
    bool Compiled)
{
    public int Passed => Results.Count(r => r.Passed);
    public int Failed => Results.Count(r => r.Failed);
    public int SkippedCount => Results.Count(r => r.Skipped);
    public bool Success => Compiled && Failed == 0;
}

/// <summary>
/// Runs C#-authored tests on a JavaScript runtime.
/// <para>
/// The tests are transpiled with the code under test and executed by Node, so what is verified
/// is the emitted JavaScript rather than the C# original. A test that passes on .NET but relies
/// on a semantic the transpiler cannot reproduce will fail here — which is the whole reason to
/// run them this way.
/// </para>
/// </summary>
public sealed class CsjsTestRunner
{
    private readonly TranspileOptions _options;

    public CsjsTestRunner(TranspileOptions? options = null)
    {
        _options = options ?? new TranspileOptions();
        _options.EmitSourceMap = false;
        _options.EmitTypeDefinitions = false;
    }

    /// <summary>Transpiles and runs every <c>[CsjsTest]</c> method found in the documents.</summary>
    public TestRunReport Run(IReadOnlyList<SourceDocument> documents, string? workingDirectory = null)
    {
        // The assertion library is transpiled alongside the tests so Assert and its exception
        // type exist in the emitted JavaScript rather than only on the .NET side.
        var allDocuments = documents.ToList();
        allDocuments.Add(new SourceDocument("CsjsAssertions.cs", AssertionSource));

        var discovered = Discover(allDocuments);
        if (discovered.Count == 0)
        {
            return new TestRunReport(
                Array.Empty<TestResult>(),
                new[] { new CsjsDiagnostic("CSJS0200", CSJS.Core.DiagnosticSeverity.Warning, "No [CsjsTest] methods were found.") },
                Compiled: true);
        }

        allDocuments.Add(new SourceDocument("CsjsTestHarness.cs", GenerateHarness(discovered)));

        var result = new Transpiler(_options).Transpile(allDocuments);
        if (!result.Success)
            return new TestRunReport(Array.Empty<TestResult>(), result.Diagnostics, Compiled: false);

        var work = workingDirectory ?? Path.Combine(Path.GetTempPath(), "csjs-tests", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(work);

        try
        {
            foreach (var module in result.Modules)
            {
                var path = Path.Combine(work, module.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, module.JavaScript);
            }

            File.WriteAllText(
                Path.Combine(work, "csjs-runtime.js"),
                result.RuntimeJavaScript ?? Transpiler.RuntimeSource.Value);

            File.WriteAllText(Path.Combine(work, "package.json"), "{\"type\":\"module\"}");
            File.WriteAllText(Path.Combine(work, "run-tests.mjs"), NodeDriver);

            var output = ExecuteNode(work);
            return new TestRunReport(ParseResults(output), result.Diagnostics, Compiled: true);
        }
        finally
        {
            if (workingDirectory is null)
            {
                try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Discovery
    // -------------------------------------------------------------------------------------------

    private readonly record struct DiscoveredTest(string Suite, string Method, string DisplayName, string? Skip);

    private static List<DiscoveredTest> Discover(IReadOnlyList<SourceDocument> documents)
    {
        var tests = new List<DiscoveredTest>();

        foreach (var document in documents)
        {
            var tree = CSharpSyntaxTree.ParseText(document.Text, path: document.Path);
            var root = tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var attribute = method.AttributeLists
                    .SelectMany(l => l.Attributes)
                    .FirstOrDefault(a => AttributeNameOf(a) is "CsjsTest" or "CsjsTestAttribute");

                if (attribute is null) continue;

                var owner = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (owner is null) continue;

                var displayName = FirstStringArgument(attribute) ?? method.Identifier.Text;
                var skip = NamedStringArgument(attribute, "Skip");

                tests.Add(new DiscoveredTest(owner.Identifier.Text, method.Identifier.Text, displayName, skip));
            }
        }

        return tests;
    }

    private static string AttributeNameOf(AttributeSyntax attribute) => attribute.Name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => attribute.Name.ToString(),
    };

    private static string? FirstStringArgument(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals is null);
        return (argument?.Expression as LiteralExpressionSyntax)?.Token.ValueText;
    }

    private static string? NamedStringArgument(AttributeSyntax attribute, string name)
    {
        var argument = attribute.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == name);
        return (argument?.Expression as LiteralExpressionSyntax)?.Token.ValueText;
    }

    // -------------------------------------------------------------------------------------------
    // Harness
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Generates a C# class that invokes every discovered test. Generating C# rather than
    /// JavaScript means the harness goes through the same transpiler as the tests, so name
    /// mangling and overload resolution stay consistent with the rest of the output.
    /// </summary>
    private static string GenerateHarness(IReadOnlyList<DiscoveredTest> tests)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using CSJS;");
        sb.AppendLine();
        sb.AppendLine("// Generated by CSJS.Testing. Invokes every discovered [CsjsTest] method.");
        sb.AppendLine("[JsExport(Global = true)]");
        sb.AppendLine("public static class CsjsTestHarness");
        sb.AppendLine("{");
        sb.AppendLine("    public static void RunAll()");
        sb.AppendLine("    {");

        foreach (var test in tests)
        {
            var suite = Escape(test.Suite);
            var name = Escape(test.DisplayName);

            if (test.Skip is not null)
            {
                sb.AppendLine($"        Report(\"{suite}\", \"{name}\", \"skipped\", \"{Escape(test.Skip)}\");");
                continue;
            }

            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            {test.Suite}.{test.Method}();");
            sb.AppendLine($"            Report(\"{suite}\", \"{name}\", \"passed\", null);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            Report(\"{suite}\", \"{name}\", \"failed\", ex.Message);");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static void Report(string suite, string name, string outcome, string? message)");
        sb.AppendLine("    {");
        // The marker prefix is what lets the driver separate results from anything the test
        // itself printed to the console.
        sb.AppendLine("        Console.WriteLine($\"##CSJS##{suite}\\u001f{name}\\u001f{outcome}\\u001f{message}\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private const string NodeDriver = """
        // Generated by CSJS.Testing.
        import { CsjsTestHarness } from './CsjsTestHarness.js';

        const started = performance.now();
        CsjsTestHarness.RunAll();
        console.log(`##CSJS-DONE##${(performance.now() - started).toFixed(2)}`);
        """;

    // -------------------------------------------------------------------------------------------
    // Execution
    // -------------------------------------------------------------------------------------------

    private static string ExecuteNode(string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("node", "run-tests.mjs")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start node. Install Node.js to run CSJS tests.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (stderr.Length > 0 && !stdout.Contains("##CSJS##", StringComparison.Ordinal))
            throw new InvalidOperationException($"The test runtime failed:\n{stderr}");

        return stdout;
    }

    private static List<TestResult> ParseResults(string output)
    {
        var results = new List<TestResult>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("##CSJS##", StringComparison.Ordinal)) continue;

            var parts = trimmed["##CSJS##".Length..].Split('');
            if (parts.Length < 3) continue;

            var message = parts.Length > 3 && parts[3] is not ("" or "null") ? parts[3] : null;
            results.Add(new TestResult(parts[0], parts[1], parts[2], message, 0));
        }

        return results;
    }

    // -------------------------------------------------------------------------------------------
    // Embedded assertion source
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The assertion library, as source. It is transpiled with each run rather than referenced
    /// as an assembly, because the tests need Assert to exist in JavaScript.
    /// </summary>
    private static readonly string AssertionSource = """
        using System;

        namespace CSJS.Testing
        {
            /// <summary>Assertions available inside a CSJS test.</summary>
            public static class Assert
            {
                public static void True(bool condition, string? message = null)
                {
                    if (!condition) throw new Exception(message ?? "Expected the condition to be true.");
                }

                public static void False(bool condition, string? message = null)
                {
                    if (condition) throw new Exception(message ?? "Expected the condition to be false.");
                }

                public static void Equal<T>(T expected, T actual, string? message = null)
                {
                    if (!Same(expected, actual))
                        throw new Exception(message ?? $"Expected '{expected}' but got '{actual}'.");
                }

                public static void NotEqual<T>(T expected, T actual, string? message = null)
                {
                    if (Same(expected, actual))
                        throw new Exception(message ?? $"Expected a value other than '{expected}'.");
                }

                public static void Null(object? value, string? message = null)
                {
                    if (value != null) throw new Exception(message ?? $"Expected null but got '{value}'.");
                }

                public static void NotNull(object? value, string? message = null)
                {
                    if (value == null) throw new Exception(message ?? "Expected a non-null value.");
                }

                public static void Close(double expected, double actual, double tolerance = 1e-9, string? message = null)
                {
                    var difference = Math.Abs(expected - actual);
                    if (difference > tolerance)
                        throw new Exception(message ?? $"Expected {expected} +/- {tolerance} but got {actual}.");
                }

                public static void Contains(string haystack, string needle, string? message = null)
                {
                    if (!haystack.Contains(needle))
                        throw new Exception(message ?? $"Expected '{haystack}' to contain '{needle}'.");
                }

                public static void Throws(Action action, string? message = null)
                {
                    var threw = false;
                    try { action(); }
                    catch (Exception) { threw = true; }
                    if (!threw) throw new Exception(message ?? "Expected the action to throw.");
                }

                public static void Fail(string message)
                {
                    throw new Exception(message);
                }

                private static bool Same<T>(T left, T right)
                {
                    if (left == null) return right == null;
                    return left.Equals(right);
                }
            }
        }
        """;
}
