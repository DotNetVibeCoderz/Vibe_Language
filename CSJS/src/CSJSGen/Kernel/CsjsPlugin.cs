using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSJS.Core;
using Microsoft.SemanticKernel;

namespace CSJSGen.Kernel;

/// <summary>
/// Kernel functions that generate, compile, and run CSJS code.
/// <para>
/// These are what let the assistant check its own work rather than assert that code compiles.
/// Adding a capability to Jack means adding a function here — the chat loop stays untouched.
/// </para>
/// </summary>
public sealed class CsjsPlugin
{
    private readonly Func<string?> _getEditorCode;
    private readonly Action<string>? _setEditorCode;

    /// <param name="getEditorCode">Reads whatever is currently open in the editor.</param>
    /// <param name="setEditorCode">Writes code into the editor, for the apply-to-editor flow.</param>
    public CsjsPlugin(Func<string?> getEditorCode, Action<string>? setEditorCode = null)
    {
        _getEditorCode = getEditorCode;
        _setEditorCode = setEditorCode;
    }

    [KernelFunction("compile")]
    [Description("Transpile CSJS (C#) source to JavaScript and report any errors. Use this to verify code before presenting it as working.")]
    public string Compile(
        [Description("The C# source to transpile. Omit to compile whatever is open in the editor.")]
        string? code = null)
    {
        var source = code ?? _getEditorCode() ?? "";
        if (source.Trim().Length == 0) return "Nothing to compile: no code was supplied and the editor is empty.";

        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false };
        var result = new Transpiler(options).TranspileSource(source, "Snippet.csjs");

        var report = new StringBuilder();

        if (!result.Success)
        {
            report.AppendLine("COMPILE FAILED");
            foreach (var error in result.Errors) report.AppendLine($"  {error}");
            return report.ToString();
        }

        report.AppendLine("COMPILE OK");
        foreach (var warning in result.Warnings) report.AppendLine($"  warning: {warning}");

        report.AppendLine();
        report.AppendLine("Emitted JavaScript:");
        report.AppendLine(result.Modules[0].JavaScript);
        return report.ToString();
    }

    [KernelFunction("run")]
    [Description("Transpile CSJS source and execute it on Node.js, returning whatever it printed. The code must have a class with a static Main method.")]
    public async Task<string> RunAsync(
        [Description("The C# source to run. Omit to run whatever is open in the editor.")]
        string? code = null)
    {
        var source = code ?? _getEditorCode() ?? "";
        if (source.Trim().Length == 0) return "Nothing to run: no code was supplied and the editor is empty.";

        var options = new TranspileOptions
        {
            EmitSourceMap = false,
            EmitTypeDefinitions = false,
            EmitEntryPointCall = true,
        };

        var result = new Transpiler(options).TranspileSource(source, "Snippet.csjs");
        if (!result.Success)
            return "COMPILE FAILED\n" + string.Join("\n", result.Errors.Select(e => "  " + e));

        var directory = Path.Combine(Path.GetTempPath(), "csjsgen-run", Guid.NewGuid().ToString("n")[..10]);
        Directory.CreateDirectory(directory);

        try
        {
            foreach (var module in result.Modules)
                await File.WriteAllTextAsync(Path.Combine(directory, module.FileName), module.JavaScript);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "csjs-runtime.js"),
                result.RuntimeJavaScript ?? Transpiler.RuntimeSource.Value);

            await File.WriteAllTextAsync(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");

            var (stdout, stderr, exitCode) = await ExecuteAsync("node", result.Modules[0].FileName, directory);

            var report = new StringBuilder();
            report.AppendLine(exitCode == 0 ? "RUN OK" : $"RUN FAILED (exit {exitCode})");
            if (stdout.Length > 0) report.AppendLine("--- output ---").AppendLine(stdout.TrimEnd());
            if (stderr.Length > 0) report.AppendLine("--- errors ---").AppendLine(stderr.TrimEnd());
            return report.ToString();
        }
        catch (Exception ex)
        {
            return $"Could not run the code: {ex.Message}. Node.js must be installed and on PATH.";
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [KernelFunction("emit_type_definitions")]
    [Description("Transpile CSJS source and return the generated TypeScript .d.ts declarations, to check how the C# types surface to TypeScript consumers.")]
    public string EmitTypeDefinitions(
        [Description("The C# source. Omit to use whatever is open in the editor.")]
        string? code = null)
    {
        var source = code ?? _getEditorCode() ?? "";
        if (source.Trim().Length == 0) return "Nothing to process: no code was supplied and the editor is empty.";

        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = true };
        var result = new Transpiler(options).TranspileSource(source, "Snippet.csjs");

        if (!result.Success)
            return "COMPILE FAILED\n" + string.Join("\n", result.Errors.Select(e => "  " + e));

        return result.Modules[0].TypeDefinitions ?? "(no declarations were generated)";
    }

    [KernelFunction("read_editor")]
    [Description("Read the code currently open in the CSJSGen editor.")]
    public string ReadEditor()
    {
        var code = _getEditorCode();
        return string.IsNullOrWhiteSpace(code) ? "The editor is empty." : code;
    }

    [KernelFunction("write_editor")]
    [Description("Replace all code in the CSJSGen editor. Use when writing a program from scratch or when the rewrite is substantial; prefer edit_editor for a targeted change.")]
    public string WriteEditor(
        [Description("The complete C# source to place in the editor.")] string code)
    {
        if (_setEditorCode is null) return "The editor is not writable from here.";

        _setEditorCode(code);
        return $"Editor updated with {CountLines(code)} lines. {Verify(code)}";
    }

    [KernelFunction("edit_editor")]
    [Description("Edit the code already in the editor by replacing an exact snippet. Prefer this over write_editor for a targeted change: it leaves the rest of the file untouched.")]
    public string EditEditor(
        [Description("The exact text to find, matching the file character for character including indentation.")]
        string find,
        [Description("The replacement text.")] string replace,
        [Description("Replace every occurrence rather than only the first.")] bool all = false)
    {
        if (_setEditorCode is null) return "The editor is not writable from here.";

        var current = _getEditorCode() ?? "";
        if (current.Length == 0) return "The editor is empty. Use write_editor instead.";
        if (find.Length == 0) return "The text to find cannot be empty.";

        var occurrences = CountOccurrences(current, find);

        if (occurrences == 0)
        {
            // Reporting what is actually there beats a bare failure: it lets the model retry
            // accurately instead of guessing at the file a second time.
            return $"That snippet was not found. The editor holds {CountLines(current)} lines, starting:\n"
                 + Preview(current);
        }

        if (occurrences > 1 && !all)
        {
            return $"That snippet appears {occurrences} times. Pass all=true to replace every one, "
                 + "or include more surrounding context so it matches only the intended place.";
        }

        var updated = all
            ? current.Replace(find, replace, StringComparison.Ordinal)
            : ReplaceFirst(current, find, replace);

        _setEditorCode(updated);
        return $"Replaced {(all ? occurrences : 1)} occurrence(s). {Verify(updated)}";
    }

    [KernelFunction("insert_into_editor")]
    [Description("Insert code into the editor at the top, at the bottom, or after an exact anchor snippet. Use when adding to a file rather than changing what is already there.")]
    public string InsertIntoEditor(
        [Description("The C# to insert.")] string code,
        [Description("One of: top, bottom, after.")] string position = "bottom",
        [Description("When position is 'after', the exact snippet to insert after.")] string? anchor = null)
    {
        if (_setEditorCode is null) return "The editor is not writable from here.";

        var current = _getEditorCode() ?? "";
        string updated;

        switch (position.ToLowerInvariant())
        {
            case "top":
                updated = code.TrimEnd() + "\n\n" + current;
                break;

            case "after":
                if (string.IsNullOrEmpty(anchor))
                    return "position 'after' needs an anchor snippet.";

                var index = current.IndexOf(anchor, StringComparison.Ordinal);
                if (index < 0)
                    return $"The anchor was not found. The editor holds {CountLines(current)} lines, starting:\n"
                         + Preview(current);

                var cut = index + anchor.Length;
                updated = current[..cut] + "\n\n" + code.Trim() + current[cut..];
                break;

            default:
                updated = current.TrimEnd() + "\n\n" + code.TrimStart();
                break;
        }

        _setEditorCode(updated);
        return $"Inserted {CountLines(code)} lines at {position}. {Verify(updated)}";
    }

    /// <summary>
    /// Compiles what was just written and reports the verdict.
    /// <para>
    /// Checking here means a broken edit is caught while the assistant can still fix it, rather
    /// than being discovered by the user the next time they press Compile.
    /// </para>
    /// </summary>
    private string Verify(string code)
    {
        var report = Compile(code);
        return report.StartsWith("COMPILE OK", StringComparison.Ordinal)
            ? "It compiles."
            : "It does NOT compile — fix it and edit again:\n" + report;
    }

    private static int CountLines(string text) => text.Split('\n').Length;

    private static string Preview(string text)
        => string.Join('\n', text.Split('\n').Take(12));

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        var index = text.IndexOf(find, StringComparison.Ordinal);
        return index < 0 ? text : text[..index] + replace + text[(index + find.Length)..];
    }

    [KernelFunction("check_support")]
    [Description("Check whether a specific C# feature or BCL member can be transpiled by CSJS. Pass a small snippet that uses it.")]
    public string CheckSupport(
        [Description("A small C# snippet using the feature in question.")] string snippet)
    {
        // Wrapping bare statements lets the caller pass either a fragment or a whole file.
        var source = snippet.Contains("class ", StringComparison.Ordinal)
                     || snippet.Contains("namespace ", StringComparison.Ordinal)
            ? snippet
            : $$"""
                using System;
                using System.Collections.Generic;
                using System.Linq;

                public static class Probe
                {
                    public static void Run()
                    {
                {{snippet}}
                    }
                }
                """;

        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false };
        var result = new Transpiler(options).TranspileSource(source, "Probe.csjs");

        if (result.Success)
        {
            var warnings = result.Warnings.ToList();
            var note = warnings.Count > 0
                ? "\nWith caveats:\n" + string.Join("\n", warnings.Select(w => "  " + w.Message))
                : "";
            return $"SUPPORTED.{note}\n\nEmitted:\n{result.Modules[0].JavaScript}";
        }

        return "NOT SUPPORTED\n" + string.Join("\n", result.Errors.Select(e => "  " + e.Message));
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> ExecuteAsync(
        string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // A generated program that loops forever must not hang the assistant.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return ("", "The program did not finish within 20 seconds and was stopped.", -1);
        }

        return (await stdoutTask, await stderrTask, process.ExitCode);
    }
}
