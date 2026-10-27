using System.Runtime.Versioning;
using WinForms = System.Windows.Forms;

namespace CSJSGen.Services;

/// <summary>
/// The system's own folder, open, and save dialogs.
///
/// Typing a path into a text box is fine when you already know it and can spell it; it is a poor
/// way to find a file. The dialogs are the platform's own, so they come with the places list,
/// search, recent locations, and the ability to create a folder while you are in there.
///
/// <para>
/// They have to run on the WinForms message loop that owns the host window — a modal dialog shown
/// from Blazor's synchronisation context would block the WebView and never paint. The host form
/// is registered at startup and every call is marshalled onto it.
/// </para>
/// </summary>
public sealed class NativeDialogs
{
    private static WinForms.Form? _owner;

    /// <summary>Registers the form that owns the dialogs. Called once, as the host comes up.</summary>
    public static void UseOwner(WinForms.Form owner) => _owner = owner;

    /// <summary>Filter for CSJS sources, with the related text formats a project also holds.</summary>
    public const string SourceFilter =
        "Sumber CSJS (*.csjs;*.cs)|*.csjs;*.cs|"
        + "Berkas proyek (*.json;*.html;*.css;*.js;*.md)|*.json;*.html;*.css;*.js;*.md|"
        + "Semua berkas (*.*)|*.*";

    /// <summary>
    /// Shows the folder picker and returns the chosen folder, or null when the user cancels or
    /// the platform has no such dialog.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public Task<string?> PickFolderAsync(string description, string? startAt = null) => Show(() =>
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        // Opening where the user already is beats opening at This PC every time.
        if (NearestExisting(startAt) is { } start) dialog.SelectedPath = start;

        return dialog.ShowDialog(_owner) == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    });

    /// <summary>Shows the open-file dialog and returns the chosen file, or null.</summary>
    /// <param name="filter">A WinForms filter string: <c>Label|*.a;*.b|Label|*.*</c>.</param>
    [SupportedOSPlatform("windows")]
    public Task<string?> OpenFileAsync(string title, string filter, string? startAt = null) => Show(() =>
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
        };

        if (NearestExisting(startAt) is { } start) dialog.InitialDirectory = start;

        return dialog.ShowDialog(_owner) == WinForms.DialogResult.OK ? dialog.FileName : null;
    });

    /// <summary>
    /// Shows the open-file dialog allowing several files. Returns an empty list on cancel.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, string filter, string? startAt = null)
    {
        var joined = await Show(() =>
        {
            using var dialog = new WinForms.OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
                Multiselect = true,
                RestoreDirectory = true,
            };

            if (NearestExisting(startAt) is { } start) dialog.InitialDirectory = start;

            // A path cannot contain '|', so it is a safe separator through the string-typed helper.
            return dialog.ShowDialog(_owner) == WinForms.DialogResult.OK
                ? string.Join('|', dialog.FileNames)
                : null;
        });

        return joined is null ? Array.Empty<string>() : joined.Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Shows the save dialog, proposing <paramref name="suggestedPath"/>, and returns the chosen
    /// path or null. The dialog asks before overwriting, so callers do not have to.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public Task<string?> SaveFileAsync(string title, string filter, string? suggestedPath = null) => Show(() =>
    {
        using var dialog = new WinForms.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = "csjs",
            RestoreDirectory = true,
        };

        if (!string.IsNullOrWhiteSpace(suggestedPath))
        {
            dialog.FileName = Path.GetFileName(suggestedPath);
            if (NearestExisting(Path.GetDirectoryName(suggestedPath)) is { } start) dialog.InitialDirectory = start;
        }

        return dialog.ShowDialog(_owner) == WinForms.DialogResult.OK ? dialog.FileName : null;
    });

    /// <summary>
    /// Runs a dialog on the owner's message loop and completes with whatever it returned.
    /// </summary>
    private static Task<string?> Show(Func<string?> dialog)
    {
        if (!OperatingSystem.IsWindows() || _owner is null) return Task.FromResult<string?>(null);

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _owner.BeginInvoke(() =>
        {
            try
            {
                completion.SetResult(dialog());
            }
            catch (Exception)
            {
                // A dialog that fails must not take the app down; it reads as a cancel.
                completion.SetResult(null);
            }
        });

        return completion.Task;
    }

    /// <summary>
    /// The path itself if it is a folder, else its nearest existing parent — so a half-typed or
    /// since-deleted location still opens somewhere useful.
    /// </summary>
    private static string? NearestExisting(string? path)
    {
        var current = path;
        while (!string.IsNullOrWhiteSpace(current) && !Directory.Exists(current))
            current = Path.GetDirectoryName(current);

        return string.IsNullOrWhiteSpace(current) ? null : current;
    }
}
