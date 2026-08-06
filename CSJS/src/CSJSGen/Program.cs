using Avalonia;

namespace CSJSGen;

// CSJSGen — AI-driven CSJS code generator.
// Gravicode Studios, led by Kang Fadhil.

internal static class Program
{
    /// <summary>Project directory to open at startup, when one was passed on the command line.</summary>
    public static string? StartupProject { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // The Blazor UI is hosted in a WinForms BlazorWebView embedded in the Avalonia window,
        // so WinForms needs initialising even though Avalonia owns the window and message loop.
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        // A project directory may be passed on the command line: `CSJSGen path/to/project`.
        StartupProject = args.FirstOrDefault(a => !a.StartsWith('-') && Directory.Exists(a));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
