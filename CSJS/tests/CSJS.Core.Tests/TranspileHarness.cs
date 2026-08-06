using System.Diagnostics;
using System.Text;
using CSJS.Core;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// Shared helpers for the emitter tests.
/// <para>
/// Two kinds of assertion matter here, and they catch different failures. Checking the emitted
/// text catches shape regressions — a stray spread, a lost <c>const</c>. Executing the output on
/// Node catches semantic ones, where the JavaScript is well-formed but means something other than
/// the C# did. Most tests do both.
/// </para>
/// </summary>
public static class Js
{
    /// <summary>Transpiles a snippet and returns the emitted JavaScript, failing on any error.</summary>
    public static string Emit(string csharp, TranspileOptions? options = null)
    {
        var result = Transpile(csharp, options);

        Assert.True(result.Success,
            "Transpilation failed:\n  " + string.Join("\n  ", result.Errors.Select(e => e.ToString())));

        return result.Modules[0].JavaScript;
    }

    public static TranspileResult Transpile(string csharp, TranspileOptions? options = null)
    {
        options ??= new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false };
        return new Transpiler(options).TranspileSource(csharp);
    }

    /// <summary>Transpiles and returns the emitted <c>.d.ts</c>.</summary>
    public static string EmitTypes(string csharp)
    {
        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = true };
        var result = new Transpiler(options).TranspileSource(csharp);

        Assert.True(result.Success,
            "Transpilation failed:\n  " + string.Join("\n  ", result.Errors.Select(e => e.ToString())));

        return result.Modules[0].TypeDefinitions!;
    }

    /// <summary>
    /// Transpiles a snippet, runs it on Node, and returns everything it printed.
    /// The snippet must declare a class with a static <c>Main</c>.
    /// </summary>
    public static string Run(string csharp)
    {
        var options = new TranspileOptions
        {
            EmitSourceMap = false,
            EmitTypeDefinitions = false,
            EmitEntryPointCall = true,
        };

        var result = new Transpiler(options).TranspileSource(csharp);
        Assert.True(result.Success,
            "Transpilation failed:\n  " + string.Join("\n  ", result.Errors.Select(e => e.ToString())));

        var directory = Path.Combine(Path.GetTempPath(), "csjs-tests", Guid.NewGuid().ToString("n")[..10]);
        Directory.CreateDirectory(directory);

        try
        {
            foreach (var module in result.Modules)
                File.WriteAllText(Path.Combine(directory, module.FileName), module.JavaScript);

            File.WriteAllText(
                Path.Combine(directory, "csjs-runtime.js"),
                result.RuntimeJavaScript ?? Transpiler.RuntimeSource.Value);

            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");

            var startInfo = new ProcessStartInfo("node", result.Modules[0].FileName)
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"Node exited with {process.ExitCode}:\n{stderr}\n\nEmitted:\n{result.Modules[0].JavaScript}");
            return stdout.Replace("\r\n", "\n").TrimEnd();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Wraps statements in a Main method, for tests about statement-level emission.</summary>
    public static string InMain(string statements, string extraMembers = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;

        public static class Program
        {
            public static void Main()
            {
        {{Indent(statements, 8)}}
            }
        {{extraMembers}}
        }
        """;

    private static string Indent(string text, int spaces)
    {
        var pad = new string(' ', spaces);
        return string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.Length == 0 ? l : pad + l));
    }

    /// <summary>Asserts that the emitted JavaScript contains a fragment, showing the output when not.</summary>
    public static void Contains(string fragment, string javascript)
        => Assert.True(javascript.Contains(fragment, StringComparison.Ordinal),
            $"Expected the output to contain:\n  {fragment}\n\nActual output:\n{javascript}");

    public static void DoesNotContain(string fragment, string javascript)
        => Assert.False(javascript.Contains(fragment, StringComparison.Ordinal),
            $"Expected the output NOT to contain:\n  {fragment}\n\nActual output:\n{javascript}");
}
