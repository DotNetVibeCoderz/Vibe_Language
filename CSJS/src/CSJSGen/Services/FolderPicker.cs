using System.Runtime.Versioning;
using WinForms = System.Windows.Forms;

namespace CSJSGen.Services;

/// <summary>
/// The system folder picker.
///
/// Typing a path into a text box is fine when you already know it and can spell it; it is a poor
/// way to find a folder. The dialog is the platform's own, so it comes with the places list,
/// search, and the ability to create a folder while you are in there.
///
/// <para>
/// It has to run on the WinForms message loop that owns the host window — a modal dialog shown
/// from Blazor's synchronisation context would block the WebView and never paint. The host form
/// is registered at startup and every call is marshalled onto it.
/// </para>
/// </summary>
public sealed class FolderPicker
{
    private static WinForms.Form? _owner;

    /// <summary>Registers the form that owns the dialog. Called once, as the host comes up.</summary>
    public static void UseOwner(WinForms.Form owner) => _owner = owner;

    /// <summary>
    /// Shows the picker and returns the chosen folder, or null when the user cancels or the
    /// platform has no such dialog.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public Task<string?> PickAsync(string description, string? startAt = null)
    {
        if (!OperatingSystem.IsWindows() || _owner is null) return Task.FromResult<string?>(null);

        var completion = new TaskCompletionSource<string?>();

        _owner.BeginInvoke(() =>
        {
            try
            {
                using var dialog = new WinForms.FolderBrowserDialog
                {
                    Description = description,
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                };

                // Opening where the user already is beats opening at This PC every time.
                if (!string.IsNullOrWhiteSpace(startAt) && Directory.Exists(startAt))
                    dialog.SelectedPath = startAt;

                var result = dialog.ShowDialog(_owner);
                completion.SetResult(result == WinForms.DialogResult.OK ? dialog.SelectedPath : null);
            }
            catch (Exception)
            {
                // A picker that fails must not take the app down; the path box still works.
                completion.SetResult(null);
            }
        });

        return completion.Task;
    }
}
