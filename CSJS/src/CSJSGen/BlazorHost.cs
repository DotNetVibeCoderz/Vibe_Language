using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CSJSGen.Models;
using CSJSGen.Services;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using WinForms = System.Windows.Forms;

namespace CSJSGen;

/// <summary>
/// Embeds a <see cref="BlazorWebView"/> inside the Avalonia window.
/// <para>
/// Avalonia and Windows Forms cannot share a control tree, so the WebView lives in a borderless
/// Win32 host window that Avalonia adopts through <see cref="NativeControlHost"/>. That is the
/// seam that makes "Avalonia + Blazor" one application rather than two processes side by side.
/// </para>
/// </summary>
public sealed class BlazorHost : NativeControlHost
{
    private WinForms.Form? _host;
    private BlazorWebView? _webView;
    private IServiceProvider? _services;

    /// <summary>Raised once the WebView has initialised, so the shell can drop its overlay.</summary>
    public event Action? Ready;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            return base.CreateNativeControlCore(parent);

        var settings = AppSettings.Load();
        _services = BuildServices(settings);

        // Resolved now rather than on first use: nothing asks for it, and it has to be listening
        // before the first panel is dragged.
        var layout = _services.GetRequiredService<LayoutPersistence>();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => layout.Flush();

        _webView = new BlazorWebView
        {
            // Resolved against the app's base directory rather than the working directory, so
            // running from anywhere still finds the host page.
            HostPage = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
            Services = _services,
            BackColor = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
                ? System.Drawing.ColorTranslator.FromHtml("#f7f6fb")
                : System.Drawing.ColorTranslator.FromHtml("#171520"),
        };

        _webView.RootComponents.Add<Components.Main>("#app");
        _webView.BlazorWebViewInitialized += (_, _) => Ready?.Invoke();

        // The form stays a real top-level window until Avalonia reparents its handle. A form with
        // TopLevel = false can never actually be shown, and a WebView inside one spins up its
        // browser process and then paints into nothing. Creating it offscreen avoids the flash.
        _host = new WinForms.Form
        {
            FormBorderStyle = WinForms.FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = WinForms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(-32000, -32000),
            Size = new System.Drawing.Size(1440, 900),
            BackColor = _webView.BackColor,
        };

        _host.Controls.Add(_webView);
        _host.Show();

        // The native dialogs are modal on this form's message loop, so it needs the form itself.
        Services.NativeDialogs.UseOwner(_host);

        ObserveBounds();

        return new PlatformHandle(_host.Handle, "HWND");
    }

    /// <summary>
    /// Keeps the hosted window matched to this control.
    /// <para>
    /// Reparenting an HWND neither moves nor resizes it, and Avalonia resizes the adopted window
    /// with <c>SetWindowPos</c> directly — so WinForms never runs a layout pass and WebView2 never
    /// learns its surface changed. Observing <c>Bounds</c> is the reliable signal; overriding
    /// arrange is not, because <see cref="NativeControlHost"/> manages its own attachment.
    /// </para>
    /// </summary>
    private void ObserveBounds()
    {
        this.GetObservable(BoundsProperty).Subscribe(new BoundsObserver(bounds =>
            ResizeHost(bounds.Width, bounds.Height)));
    }

    /// <summary>Adapts a callback to <see cref="IObserver{T}"/>, which is what Avalonia expects.</summary>
    private sealed class BoundsObserver : IObserver<Avalonia.Rect>
    {
        private readonly Action<Avalonia.Rect> _onNext;
        public BoundsObserver(Action<Avalonia.Rect> onNext) => _onNext = onNext;

        public void OnNext(Avalonia.Rect value) => _onNext(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private void ResizeHost(double width, double height)
    {
        if (_host is null || _webView is null || width <= 0 || height <= 0) return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var pixelWidth = Math.Max(1, (int)Math.Round(width * scaling));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * scaling));


        _host.SetBounds(0, 0, pixelWidth, pixelHeight);
        _webView.SetBounds(0, 0, pixelWidth, pixelHeight);

        _host.PerformLayout();

        // Avalonia owns the message loop, so WinForms.Application.Run never executes and the
        // layout messages queued by the resize are never dispatched. Draining them here is what
        // lets WebView2 see its new surface and reflow the page.
        WinForms.Application.DoEvents();

        if (_webView.WebView is { } inner)
        {
            inner.Size = new System.Drawing.Size(pixelWidth, pixelHeight);
            inner.Refresh();
        }
    }

    /// <summary>
    /// Writes any layout change still waiting out its debounce. Called as the window closes, so
    /// the last drag before quitting is not lost.
    /// </summary>
    public void FlushLayout() => _services?.GetService<LayoutPersistence>()?.Flush();

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _webView?.Dispose();
        _host?.Dispose();
        _webView = null;
        _host = null;
    }

    /// <summary>
    /// Composes the services the Blazor components resolve.
    /// <para>
    /// Registering them here rather than inside a component keeps the UI free of construction
    /// logic and lets the chat, editor, and kernel share one instance each.
    /// </para>
    /// </summary>
    private static IServiceProvider BuildServices(AppSettings settings)
    {
        var services = new ServiceCollection();

        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddSingleton(settings);

        // One HttpClient for the whole app: connection reuse matters for streaming responses.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

        services.AddSingleton<EditorBridge>();
        services.AddSingleton<KernelFactory>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<CodeIntelligence>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<MarkdownRenderer>();
        // The layout a previous session left is restored before anything renders, so panels
        // open at their saved widths instead of jumping there.
        services.AddSingleton(_ =>
        {
            var state = new AppState();
            state.Restore(settings.Layout, settings.Theme);
            return state;
        });
        services.AddSingleton(provider => new LayoutPersistence(provider.GetRequiredService<AppState>()));
        services.AddSingleton<PreviewServer>();
        services.AddSingleton<NodeRunner>();
        services.AddSingleton<NativeDialogs>();

        services.AddSingleton(provider => new ChatService(
            provider.GetRequiredService<KernelFactory>(),
            provider.GetRequiredService<EditorBridge>(),
            provider.GetRequiredService<AppSettings>()));

        return services.BuildServiceProvider();
    }
}
