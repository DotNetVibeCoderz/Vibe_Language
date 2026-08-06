using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CSJSGen.Models;

namespace CSJSGen;

/// <summary>
/// The Avalonia application. It owns the window, the theme, and the lifetime; the editor surface
/// inside that window is Blazor, hosted in a WebView.
/// </summary>
public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The theme is configured, not hardcoded, so the window chrome matches the Blazor UI
            // inside it rather than fighting it.
            var settings = AppSettings.Load();
            RequestedThemeVariant = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
