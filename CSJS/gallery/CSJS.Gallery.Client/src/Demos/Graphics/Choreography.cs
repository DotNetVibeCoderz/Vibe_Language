using CSJS;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Graphics;

/// <summary>
/// A keyframe with the CSS properties this demo animates. lib.dom types a keyframe's properties as
/// an open index signature, which C# cannot initialise; a subclass names the ones in use and still
/// emits as the plain object <c>element.animate</c> expects.
/// </summary>
[JsObjectLiteral]
public class Frame : Keyframe
{
    [JsName("transform")] public string? Transform { get; set; }
    [JsName("backgroundColor")] public string? BackgroundColor { get; set; }
    [JsName("borderRadius")] public string? BorderRadius { get; set; }
    [JsName("opacity")] public double? Opacity { get; set; }
}

/// <summary>
/// Choreography with the Web Animations API: one <c>element.animate</c> per tile, staggered, all
/// driven together — play, pause, reverse, speed, and a scrubber that seeks every animation to
/// the same moment. The browser runs the animations; C# only conducts.
/// </summary>
public sealed class Choreography : Demo
{
    public override string Id => "web-animations";
    public override string Title => "Choreography";
    public override string Category => "Graphics";
    public override string Summary => "Twenty-four tiles animated with element.animate, staggered into a wave. Play, pause, reverse, change speed, or scrub the timeline — every animation follows.";
    public override string[] Apis => new[] { "Web Animations", "element.animate", "Animation.playbackRate", "requestAnimationFrame" };
    public override string Module => "Demos/Graphics/Choreography";
    public override string Hint => "Drag the timeline, or switch the easing and play again.";

    private const int Columns = 8;
    private const int Rows = 3;
    private const double Duration = 1800;
    private const double Stagger = 130;

    private static readonly string[] Easings = { "ease-in-out", "linear", "cubic-bezier(0.7, 0, 0.2, 1.4)", "steps(6)" };
    private static readonly string[] EasingNames = { "Ease", "Linear", "Overshoot", "Steps" };

    private readonly List<Animation> _animations = new();
    private readonly List<HTMLElement> _tiles = new();
    private HTMLElement _floor = null!;
    private HTMLInputElement _scrubber = null!;
    private HTMLElement _state = null!;
    private HTMLElement _clock = null!;
    private HTMLButtonElement _playButton = null!;
    private string _easing = Easings[0];
    private double _rate = 1;
    private double _frame;
    private bool _running;

    /// <summary>One full cycle: the last tile's delay plus there and back again.</summary>
    private static double Cycle => (Columns + Rows - 2) * Stagger + Duration * 2;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("anim-stage");

        _floor = stage.Add(Ui.El("div", "anim-floor"));
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
        {
            var tile = _floor.Add(Ui.El("div", "anim-tile"));
            tile.SetAttribute("aria-hidden", "true");
            _tiles.Add(tile);
        }

        var timeline = stage.Add(Ui.El("div", "anim-timeline"));
        timeline.Add(Ui.El("span", "ctl-label", "Timeline"));
        _scrubber = timeline.Add(Dom.Create<HTMLInputElement>("input"));
        _scrubber.Type = "range";
        _scrubber.ClassName = "ctl-range anim-scrubber";
        _scrubber.Min = "0";
        _scrubber.Max = "1000";
        _scrubber.Value = "0";
        _scrubber.SetAttribute("aria-label", "Timeline position");
        _scrubber.On<Event>("input", _ => Seek(double.Parse(_scrubber.Value) / 1000 * Cycle));
        _clock = timeline.Add(Ui.El("span", "anim-clock", "0 ms"));

        var controls = stage.Add(Ui.El("div", "stage-controls"));
        _playButton = controls.Add(Ui.Button("Pause", TogglePlay, "btn btn-primary"));
        controls.Add(Ui.Button("Reverse", Reverse));
        controls.Add(Controls.Segmented("Easing", EasingNames, EasingNames[0], name =>
        {
            _easing = Easings[Array.IndexOf(EasingNames, name)];
            Build();
        }));
        controls.Add(Controls.Slider("Speed", 0.25, 3, 0.25, 1, value =>
        {
            _rate = Math.Sign(_rate) * value;
            // updatePlaybackRate keeps the current position instead of jumping.
            foreach (var animation in _animations) animation.UpdatePlaybackRate(_rate);
        }, value => $"{value:F2}×"));
        controls.Add(Ui.Readout("State", out _state));

        Build();
        _running = true;
        _frame = Js.RequestAnimationFrame(Tick);
    }

    public override void Unmount()
    {
        _running = false;
        Js.CancelAnimationFrame(_frame);
        foreach (var animation in _animations) animation.Cancel();
        _animations.Clear();
    }

    /// <summary>Creates one animation per tile, each delayed by its distance from the corner.</summary>
    private void Build()
    {
        foreach (var old in _animations) old.Cancel();
        _animations.Clear();

        for (var i = 0; i < _tiles.Count; i++)
        {
            var column = i % Columns;
            var row = i / Columns;
            var hue = 258 - (column + row) * 12;

            var frames = new Keyframe[]
            {
                new Frame { Transform = "translateY(0) rotate(0deg) scale(1)", BackgroundColor = "#2c2742", BorderRadius = "10px", Opacity = 0.55 },
                new Frame { Offset = 0.5, Transform = "translateY(-46px) rotate(90deg) scale(0.72)", BackgroundColor = $"hsl({hue}, 88%, 70%)", BorderRadius = "50%", Opacity = 1 },
                new Frame { Transform = "translateY(0) rotate(180deg) scale(1)", BackgroundColor = "#fbbf24", BorderRadius = "10px", Opacity = 0.9 },
            };

            var animation = _tiles[i].Animate(frames, new KeyframeAnimationOptions
            {
                Duration = Duration,
                Delay = (column + row) * Stagger,
                // Two iterations, alternating: out and back. A finite animation can be reversed
                // (an infinite one cannot), so Tick loops it by hand instead.
                Iterations = 2,
                Direction = "alternate",
                Easing = _easing,
                Fill = "both",
            });
            animation.PlaybackRate = _rate;
            _animations.Add(animation);
        }

        _playButton.TextContent = "Pause";
    }

    private void TogglePlay()
    {
        var playing = _animations.Count > 0 && _animations[0].PlayState == "running";
        foreach (var animation in _animations)
        {
            if (playing) animation.Pause();
            else animation.Play();
        }
        _playButton.TextContent = playing ? "Play" : "Pause";
    }

    private void Reverse()
    {
        _rate = -_rate;
        foreach (var animation in _animations) animation.Reverse();
        _playButton.TextContent = "Pause";
    }

    /// <summary>Pauses everything at one moment. Each tile's own delay keeps the wave intact.</summary>
    private void Seek(double time)
    {
        foreach (var animation in _animations)
        {
            animation.Pause();
            animation.CurrentTime = time;
        }
        _playButton.TextContent = "Play";
    }

    /// <summary>
    /// Mirrors the slowest tile's clock into the scrubber and readouts, and starts the next cycle
    /// once every tile has finished — from the start going forward, from the end in reverse.
    /// </summary>
    private void Tick(double stamp)
    {
        if (!_running) return;

        if (_animations.Count > 0)
        {
            var last = _animations[_animations.Count - 1];
            var time = last.CurrentTime is double current ? current : 0;

            if (_animations.All(a => a.PlayState == "finished"))
            {
                foreach (var animation in _animations)
                {
                    animation.CurrentTime = _rate > 0 ? 0 : Cycle;
                    animation.Play();
                }
            }

            if (last.PlayState == "running") _scrubber.Value = (time / Cycle * 1000).ToString("F0");
            _clock.TextContent = $"{time:F0} ms";
            _state.TextContent = last.PlayState == "running" && _rate < 0 ? "reversed" : last.PlayState;
        }

        _frame = Js.RequestAnimationFrame(Tick);
    }
}
