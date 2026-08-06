using System.Diagnostics;
using CSJS.Cli;

namespace CSJSGen.Services;

/// <summary>
/// Runs the open project on Node.
///
/// The whole project, not the open file. Transpiling one file on its own fails the moment it
/// references a type declared in a sibling — the bindings file of a three.js project, say — and
/// what surfaces is a wall of C# compiler errors about missing types, which reads as if the
/// transpiler had tried to compile C# rather than emit JavaScript. The file is not broken; it was
/// never compiled with the rest of its project.
///
/// <para>
/// Output is streamed rather than collected. A background service or a chat server never exits,
/// so waiting for the process to finish before showing anything would show nothing at all — and
/// those are two of the templates. <see cref="Stop"/> is how such a run ends.
/// </para>
/// </summary>
public sealed class NodeRunner : IDisposable
{
    private Process? _current;
    private bool _stopped;
    private readonly Lock _gate = new();

    /// <summary>True while a Node process started here is still alive.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate) return _current is { HasExited: false };
        }
    }

    /// <param name="Output">Closing summary; the body arrived through the stream callback.</param>
    /// <param name="NeedsBrowser">The code wants a DOM, so Node was never going to do.</param>
    public sealed record RunResult(string Output, bool NeedsBrowser);

    /// <summary>Builds <paramref name="projectDirectory"/> and executes its entry module.</summary>
    /// <param name="write">Receives output as it arrives, line by line.</param>
    public async Task<RunResult> RunAsync(string projectDirectory, Action<string> write)
    {
        // A previous run still holding the port would make this one fail for the wrong reason.
        Stop();
        lock (_gate) _stopped = false;

        var project = CsjsProject.Load(projectDirectory);
        var report = new BuildRunner(project, projectDirectory, emitEntryPointCall: true).Run(quiet: true);

        if (!report.Success)
            return new RunResult($"Kompilasi gagal — {report.ErrorCount} kesalahan. Tekan Compile untuk rinciannya.", false);

        var outputRoot = project.ResolveOutputRoot(projectDirectory);
        var entry = new[] { "Program.js", "Main.js", "main.js", "index.js" }
            .Select(name => Path.Combine(outputRoot, name))
            .FirstOrDefault(File.Exists);

        if (entry is null)
        {
            return new RunResult(
                "Tidak ada modul entry di output. Beri nama berkas sumber utama Anda Program.csjs.", false);
        }

        var stderrText = new System.Text.StringBuilder();
        int exitCode;

        try
        {
            exitCode = await ExecuteAsync(entry, outputRoot, write, stderrText);
        }
        catch (Exception ex)
        {
            return new RunResult($"Tidak bisa menjalankan: {ex.Message}. Node.js harus terpasang dan ada di PATH.", false);
        }

        var stderr = stderrText.ToString();

        // Two ways a page-shaped project fails on Node, and neither is a defect in the user's
        // code. It reaches for the DOM, which Node does not have; or it imports a library the
        // host page supplies through an import map — three.js from a CDN, say — which Node
        // cannot resolve because it looks in node_modules. Both mean: this wants a browser.
        var wantsDom = stderr.Contains("document is not defined")
                       || stderr.Contains("window is not defined")
                       || stderr.Contains("navigator is not defined");

        var missingCdnPackage = stderr.Contains("ERR_MODULE_NOT_FOUND")
                                && File.Exists(Path.Combine(projectDirectory, "index.html"));

        var needsBrowser = wantsDom || missingCdnPackage;

        if (needsBrowser)
        {
            var why = wantsDom
                ? "Proyek ini menggunakan DOM, dan Node tidak punya DOM."
                : "Proyek ini mengimpor pustaka yang disediakan halaman host lewat import map, "
                  + "yang tidak bisa diselesaikan Node.";
            return new RunResult($"{why}\nTekan tombol Browser (Shift+F5) untuk menjalankannya di browser sungguhan.", true);
        }

        // A process we killed reports an exit code that means nothing to the user; they already
        // know why it ended, because they are the ones who ended it.
        bool wasStopped;
        lock (_gate) wasStopped = _stopped;

        if (wasStopped) return new RunResult("", false);

        return new RunResult(
            exitCode == 0 ? "Selesai." : $"Berhenti dengan kode {exitCode}.",
            false);
    }

    private async Task<int> ExecuteAsync(
        string entryPath, string workingDirectory, Action<string> write, System.Text.StringBuilder stderrText)
    {
        // Node needs to be told the output is ESM; the emitted modules use import/export.
        var packageJson = Path.Combine(workingDirectory, "package.json");
        if (!File.Exists(packageJson))
            await File.WriteAllTextAsync(packageJson, "{\"type\":\"module\"}");

        var startInfo = new ProcessStartInfo("node", $"\"{Path.GetFileName(entryPath)}\"")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Node emits UTF-8. Without this the stream is decoded as the OS codepage and an
            // em-dash arrives as mojibake.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) write(e.Data); };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderrText.AppendLine(e.Data);
            write(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_gate) _current = process;

        try
        {
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, process)) _current = null;
            }
            process.Dispose();
        }
    }

    /// <summary>Ends the running process, and its children — a server may have spawned some.</summary>
    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            process = _current;
            _current = null;
            if (process is not null) _stopped = true;
        }

        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or gone between the check and the kill. Either way there is nothing
            // left to stop.
        }
    }

    public void Dispose() => Stop();
}
