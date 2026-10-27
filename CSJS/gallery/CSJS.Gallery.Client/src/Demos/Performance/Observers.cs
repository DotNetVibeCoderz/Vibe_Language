using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Performance;

/// <summary>
/// Two observers that replace scroll and resize polling. IntersectionObserver reveals cards as
/// they enter view and loads more of an endless list when its sentinel appears; ResizeObserver
/// reports a panel's size as you drag its corner, and the panel re-lays itself out by its own
/// width rather than the window's.
/// </summary>
public sealed class Observers : Demo
{
    public override string Id => "observers";
    public override string Title => "Intersection and resize";
    public override string Category => "Performance";
    public override string Summary => "IntersectionObserver reveals cards as they scroll in and loads more of an endless list on demand; ResizeObserver reports a panel's size live and switches its layout by its own width.";
    public override string[] Apis => new[] { "IntersectionObserver", "ResizeObserver", "ResizeObserverEntry.contentRect" };
    public override string Module => "Demos/Performance/Observers";
    public override string Hint => "Scroll the list, then drag the panel's corner.";

    private const int Batch = 12;
    private static readonly string[] Topics =
    {
        "Canvas", "Web Audio", "WebSocket", "IndexedDB", "Workers", "Clipboard", "Geolocation", "WebRTC",
        "Animations", "Fetch", "Streams", "Crypto", "Gamepad", "Speech", "Notifications", "Storage",
    };

    private IntersectionObserver? _reveal;
    private IntersectionObserver? _more;
    private ResizeObserver? _resize;
    private HTMLElement _scroller = null!;
    private HTMLElement _list = null!;
    private HTMLElement _sentinel = null!;
    private HTMLElement _loaded = null!;
    private HTMLElement _visible = null!;
    private HTMLElement _box = null!;
    private HTMLElement _size = null!;
    private HTMLElement _mode = null!;
    private int _count;
    private int _inView;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        // ---- Intersection --------------------------------------------------------------------
        var left = stage.Add(Ui.El("div", "panel obs-panel"));
        var head = left.Add(Ui.El("div", "obs-head"));
        head.Add(Ui.El("h3", "panel-title", "IntersectionObserver"));
        head.Add(Ui.Readout("Loaded", out _loaded, "0"));
        head.Add(Ui.Readout("In view", out _visible, "0"));

        _scroller = left.Add(Ui.El("div", "obs-scroller"));
        _list = _scroller.Add(Ui.El("div", "obs-list"));
        _sentinel = _scroller.Add(Ui.El("div", "obs-sentinel", "Loading more…"));

        // Both observers watch within the scroller, not the window: root is the element whose
        // visible area counts. rootMargin starts loading a little before the sentinel shows.
        _reveal = new IntersectionObserver((entries, observer) =>
        {
            foreach (var entry in entries)
            {
                var card = entry.Target.As<HTMLElement>();
                var wasVisible = card.ClassList.Contains("is-visible");
                if (entry.IsIntersecting && !wasVisible) _inView++;
                if (!entry.IsIntersecting && wasVisible) _inView--;

                // is-in stays once set: a card reveals the first time it is seen, not every time.
                if (entry.IsIntersecting) card.ClassList.Add("is-in");
                card.ClassList.Toggle("is-visible", entry.IsIntersecting);
            }
            _visible.TextContent = _inView.ToString();
        }, new IntersectionObserverInit { Root = _scroller, Threshold = 0.35 });

        _more = new IntersectionObserver((entries, observer) =>
        {
            if (entries.Any(e => e.IsIntersecting)) LoadMore();
        }, new IntersectionObserverInit { Root = _scroller, RootMargin = "0px 0px 160px 0px" });

        LoadMore();
        _more.Observe(_sentinel);

        // ---- Resize --------------------------------------------------------------------------
        var right = stage.Add(Ui.El("div", "panel panel-log obs-panel"));
        var resizeHead = right.Add(Ui.El("div", "obs-head"));
        resizeHead.Add(Ui.El("h3", "panel-title", "ResizeObserver"));
        resizeHead.Add(Ui.Readout("Content box", out _size));
        resizeHead.Add(Ui.Readout("Layout", out _mode));

        _box = right.Add(Ui.El("div", "obs-box"));
        _box.SetAttribute("aria-label", "A resizable panel; drag its bottom-right corner");
        var inner = _box.Add(Ui.El("div", "obs-box-inner"));
        for (var i = 0; i < 6; i++)
        {
            var tile = inner.Add(Ui.El("div", "obs-tile"));
            tile.Add(Ui.El("span", "obs-tile-mark", (i + 1).ToString()));
            tile.Add(Ui.El("span", "obs-tile-text", Topics[i]));
        }
        _box.Add(Ui.El("span", "obs-grip", "drag ⇲"));

        // contentRect is the box without padding or border — the space the content has.
        _resize = new ResizeObserver((entries, observer) =>
        {
            foreach (var entry in entries)
            {
                var width = entry.ContentRect.Width;
                var height = entry.ContentRect.Height;
                _size.TextContent = $"{width:F0} × {height:F0}";
                var mode = width < 260 ? "stack" : width < 420 ? "pairs" : "grid";
                _mode.TextContent = mode;
                inner.ClassName = $"obs-box-inner is-{mode}";
            }
        });
        _resize.Observe(_box);
    }

    public override void Unmount()
    {
        _reveal?.Disconnect();
        _more?.Disconnect();
        _resize?.Disconnect();
    }

    private void LoadMore()
    {
        for (var i = 0; i < Batch; i++)
        {
            _count++;
            var topic = Topics[(_count - 1) % Topics.Length];
            var card = _list.Add(Ui.El("article", "obs-card"));
            card.Style.SetProperty("--hue", ((258 + _count * 9) % 360).ToString());
            card.Add(Ui.El("span", "obs-card-number", _count.ToString("000")));
            var body = card.Add(Ui.El("div", "obs-card-body"));
            body.Add(Ui.El("p", "obs-card-title", topic));
            body.Add(Ui.El("p", "obs-card-text", $"Card {_count} was created when the sentinel came within 160 px of view."));
            _reveal!.Observe(card);
        }
        _loaded.TextContent = _count.ToString();
    }
}
