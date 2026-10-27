using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace CSJSGen;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The overlay hides the WebView's first blank frame. Its initialisation event is the
        // only honest signal that there is something behind it worth showing.
        if (this.FindControl<BlazorHost>("Host") is { } host)
        {
            host.Ready += () => Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<Border>("LoadingOverlay") is { } overlay)
                    overlay.IsVisible = false;
            });

            Closing += (_, _) => host.FlushLayout();
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
