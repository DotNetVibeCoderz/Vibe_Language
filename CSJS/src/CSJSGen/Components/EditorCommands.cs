namespace CSJSGen.Components;

/// <summary>
/// A channel from the menu to the editor.
/// <para>
/// The menu bar and the editor are siblings, so neither can hold a reference to the other
/// without threading one through the shell. A static event is the smaller seam, and edit
/// commands are inherently a broadcast: whichever editor has focus should act.
/// </para>
/// </summary>
public static class EditorCommands
{
    /// <summary>Raised with a command name: cut, copy, paste, goto, find, replace, format.</summary>
    public static event Action<string>? Invoked;

    public static void Raise(string command) => Invoked?.Invoke(command);
}
