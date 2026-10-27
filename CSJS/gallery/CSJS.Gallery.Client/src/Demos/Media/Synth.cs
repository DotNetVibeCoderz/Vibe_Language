using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Media;

/// <summary>
/// A polyphonic synthesiser on the Web Audio graph: an oscillator and a gain envelope per note,
/// through one low-pass filter into an analyser. The envelope is scheduled on the audio clock with
/// parameter ramps, so attack and release are sample-accurate however busy the page is.
/// </summary>
public sealed class Synth : Demo
{
    public override string Id => "web-audio-synth";
    public override string Title => "Synthesiser";
    public override string Category => "Media";
    public override string Summary => "Oscillators, an ADSR envelope on gain ramps, and a resonant low-pass filter — played from the on-screen keys or your keyboard, with the spectrum and waveform drawn live from an AnalyserNode.";
    public override string[] Apis => new[] { "Web Audio", "OscillatorNode", "BiquadFilterNode", "AnalyserNode", "AudioParam ramps" };
    public override string Module => "Demos/Media/Synth";
    public override string Hint => "Click a key, or play A W S E D F T G Y H U J K.";

    /// <summary>One sounding note: its oscillator and the gain that shapes it.</summary>
    private sealed class Voice
    {
        public OscillatorNode Oscillator = null!;
        public GainNode Envelope = null!;
    }

    // Two octaves from C4. Computer keys follow the usual tracker layout: the home row is white
    // keys, the row above it black ones.
    private static readonly string[] NoteNames = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };
    private static readonly string[] KeyMap = { "a", "w", "s", "e", "d", "f", "t", "g", "y", "h", "u", "j", "k", "o", "l", "p", ";" };
    private const int FirstMidi = 60;
    private const int KeyCount = 25;

    private AudioContext? _audio;
    private BiquadFilterNode _filter = null!;
    private GainNode _master = null!;
    private AnalyserNode _analyser = null!;
    private Uint8Array _spectrum = null!;
    private Uint8Array _wave = null!;
    private readonly Dictionary<int, Voice> _voices = new();
    private readonly Dictionary<int, HTMLElement> _keys = new();

    private string _shape = "sawtooth";
    private double _attack = 0.02;
    private double _decay = 0.25;
    private double _sustain = 0.55;
    private double _release = 0.6;
    private double _cutoff = 2400;
    private double _resonance = 6;

    private HTMLCanvasElement _canvas = null!;
    private CanvasRenderingContext2D _ctx = null!;
    private ResizeObserver? _resize;
    private HTMLElement _status = null!;
    private double _frame;
    private bool _running;
    private Action<KeyboardEvent> _keyDown = null!;
    private Action<KeyboardEvent> _keyUp = null!;
    private Action<PointerEvent> _pointerUp = null!;
    private int _pointerNote = -1;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("synth-stage");

        var scope = stage.Add(Ui.El("div", "synth-scope"));
        _canvas = scope.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _ctx = _canvas.GetContext2D();
        _status = scope.Add(Ui.El("p", "synth-status", "Audio starts on your first note — browsers wait for a gesture."));

        var panel = stage.Add(Ui.El("div", "synth-panel"));
        var voice = panel.Add(Ui.El("div", "synth-group"));
        voice.Add(Ui.El("h3", "panel-title", "Oscillator"));
        voice.Add(Controls.Segmented("Waveform", new[] { "sine", "triangle", "sawtooth", "square" }, _shape, shape =>
        {
            _shape = shape;
            foreach (var active in _voices.Values) active.Oscillator.Type = shape;
        }));

        var envelope = panel.Add(Ui.El("div", "synth-group synth-sliders"));
        envelope.Add(Ui.El("h3", "panel-title", "Envelope"));
        envelope.Add(Controls.Slider("Attack", 0.005, 1.5, 0.005, _attack, v => _attack = v, Seconds));
        envelope.Add(Controls.Slider("Decay", 0.02, 2, 0.01, _decay, v => _decay = v, Seconds));
        envelope.Add(Controls.Slider("Sustain", 0, 1, 0.01, _sustain, v => _sustain = v, v => $"{v * 100:F0}%"));
        envelope.Add(Controls.Slider("Release", 0.02, 3, 0.01, _release, v => _release = v, Seconds));

        var tone = panel.Add(Ui.El("div", "synth-group synth-sliders"));
        tone.Add(Ui.El("h3", "panel-title", "Filter"));
        tone.Add(Controls.Slider("Cutoff", 80, 12000, 10, _cutoff, v =>
        {
            _cutoff = v;
            // setTargetAtTime glides to the new value instead of stepping, so a fast drag
            // does not click.
            if (_audio is not null) _filter.Frequency.SetTargetAtTime(v, _audio.CurrentTime, 0.02);
        }, v => v >= 1000 ? $"{v / 1000:F1} kHz" : $"{v:F0} Hz"));
        tone.Add(Controls.Slider("Resonance", 0.1, 24, 0.1, _resonance, v =>
        {
            _resonance = v;
            if (_audio is not null) _filter.Q.SetTargetAtTime(v, _audio.CurrentTime, 0.02);
        }, v => $"Q {v:F1}"));

        BuildKeyboard(stage.Add(Ui.El("div", "synth-keys")));

        _keyDown = e =>
        {
            if (e.Repeat || e.CtrlKey || e.MetaKey || IsTyping(e)) return;
            var index = Array.IndexOf(KeyMap, e.Key.ToLower());
            if (index < 0) return;
            e.PreventDefault();
            NoteOn(FirstMidi + index);
        };
        _keyUp = e =>
        {
            var index = Array.IndexOf(KeyMap, e.Key.ToLower());
            if (index >= 0) NoteOff(FirstMidi + index);
        };
        _pointerUp = _ =>
        {
            if (_pointerNote >= 0) NoteOff(_pointerNote);
            _pointerNote = -1;
        };
        Js.Window.On("keydown", _keyDown);
        Js.Window.On("keyup", _keyUp);
        Js.Window.On("pointerup", _pointerUp);

        _resize = new ResizeObserver((entries, observer) =>
        {
            var ratio = Js.DevicePixelRatio;
            var box = _canvas.GetBoundingClientRect();
            _canvas.Width = Math.Floor(box.Width * ratio);
            _canvas.Height = Math.Floor(box.Height * ratio);
            _ctx.SetTransform(ratio, 0, 0, ratio, 0, 0);
        });
        _resize.Observe(_canvas);

        _running = true;
        _frame = Js.RequestAnimationFrame(Draw);
    }

    public override void Unmount()
    {
        _running = false;
        Js.CancelAnimationFrame(_frame);
        _resize?.Disconnect();
        Js.Window.Off("keydown", _keyDown);
        Js.Window.Off("keyup", _keyUp);
        Js.Window.Off("pointerup", _pointerUp);
        _voices.Clear();
        _audio?.Close();
        _audio = null;
    }

    private static string Seconds(double value) => value < 1 ? $"{value * 1000:F0} ms" : $"{value:F2} s";

    private static bool IsTyping(KeyboardEvent e)
        => e.Target is HTMLInputElement or HTMLTextAreaElement;

    private void BuildKeyboard(HTMLElement keys)
    {
        for (var i = 0; i < KeyCount; i++)
        {
            var midi = FirstMidi + i;
            var name = NoteNames[midi % 12];
            var black = name.Length > 1;
            var key = keys.Add(Ui.El("button", black ? "synth-key is-black" : "synth-key"));
            key.SetAttribute("type", "button");
            key.SetAttribute("aria-label", $"{name}{midi / 12 - 1}");
            if (i < KeyMap.Length) key.Add(Ui.El("span", "synth-key-hint", KeyMap[i].ToUpper()));
            if (name == "C") key.Add(Ui.El("span", "synth-key-name", $"C{midi / 12 - 1}"));

            key.On<PointerEvent>("pointerdown", e =>
            {
                e.PreventDefault();
                _pointerNote = midi;
                NoteOn(midi);
            });
            key.On<PointerEvent>("pointerenter", e =>
            {
                // Sliding across the keys with the button held plays each one in turn.
                if (_pointerNote < 0 || e.Buttons == 0) return;
                NoteOff(_pointerNote);
                _pointerNote = midi;
                NoteOn(midi);
            });
            _keys[midi] = key;
        }
    }

    /// <summary>
    /// Builds the shared part of the graph on first use. An AudioContext created before a user
    /// gesture starts suspended, so creating it here — inside the gesture — means it just plays.
    /// </summary>
    private AudioContext Audio()
    {
        if (_audio is not null) return _audio;

        var audio = new AudioContext();
        _filter = audio.CreateBiquadFilter();
        _filter.Type = "lowpass";
        _filter.Frequency.Value = _cutoff;
        _filter.Q.Value = _resonance;

        _master = audio.CreateGain();
        _master.Gain.Value = 0.32;

        _analyser = audio.CreateAnalyser();
        _analyser.FftSize = 2048;
        _analyser.SmoothingTimeConstant = 0.78;
        _spectrum = new Uint8Array((int)_analyser.FrequencyBinCount);
        _wave = new Uint8Array((int)_analyser.FftSize);

        _filter.Connect(_master);
        _master.Connect(_analyser);
        _analyser.Connect(audio.Destination);

        _audio = audio;
        _status.TextContent = $"Running at {audio.SampleRate / 1000:F1} kHz";
        return audio;
    }

    private static double Frequency(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    private void NoteOn(int midi)
    {
        if (_voices.ContainsKey(midi)) return;
        var audio = Audio();
        if (audio.State == "suspended") audio.Resume();

        var now = audio.CurrentTime;
        var oscillator = audio.CreateOscillator();
        oscillator.Type = _shape;
        oscillator.Frequency.Value = Frequency(midi);

        // Attack to full, decay to the sustain level — both scheduled now, on the audio clock.
        var envelope = audio.CreateGain();
        envelope.Gain.SetValueAtTime(0, now);
        envelope.Gain.LinearRampToValueAtTime(1, now + _attack);
        envelope.Gain.SetTargetAtTime(_sustain, now + _attack, _decay / 3);

        oscillator.Connect(envelope);
        envelope.Connect(_filter);
        oscillator.Start(now);

        _voices[midi] = new Voice { Oscillator = oscillator, Envelope = envelope };
        if (_keys.TryGetValue(midi, out var key)) key.ClassList.Add("is-down");
    }

    private void NoteOff(int midi)
    {
        if (_audio is null || !_voices.TryGetValue(midi, out var voice)) return;
        _voices.Remove(midi);

        // Release from wherever the envelope is now — mid-attack included — then stop the
        // oscillator once the tail has died away.
        var now = _audio.CurrentTime;
        voice.Envelope.Gain.CancelScheduledValues(now);
        voice.Envelope.Gain.SetValueAtTime(voice.Envelope.Gain.Value, now);
        voice.Envelope.Gain.SetTargetAtTime(0, now, _release / 4);
        voice.Oscillator.Stop(now + _release * 1.5);

        if (_keys.TryGetValue(midi, out var key)) key.ClassList.Remove("is-down");
    }

    /// <summary>The spectrum as bars on a log frequency axis, the waveform as a line over it.</summary>
    private void Draw(double stamp)
    {
        if (!_running) return;

        var box = _canvas.GetBoundingClientRect();
        var w = box.Width;
        var h = box.Height;
        _ctx.FillStyle = "#0d0b14";
        _ctx.FillRect(0, 0, w, h);

        // Octave gridlines: 100 Hz, 1 kHz, 10 kHz are where the eye looks.
        _ctx.StrokeStyle = "rgba(167, 139, 250, 0.08)";
        _ctx.LineWidth = 1;
        foreach (var hz in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000 })
        {
            var x = Math.Round(LogX(hz, w)) + 0.5;
            _ctx.BeginPath();
            _ctx.MoveTo(x, 0);
            _ctx.LineTo(x, h);
            _ctx.Stroke();
            _ctx.FillStyle = "rgba(163, 157, 189, 0.45)";
            _ctx.Font = "10px 'JetBrains Mono', monospace";
            _ctx.FillText(hz >= 1000 ? $"{hz / 1000:F0}k" : $"{hz:F0}", x + 4, h - 8);
        }

        if (_audio is null)
        {
            _ctx.StrokeStyle = "rgba(251, 191, 36, 0.35)";
            _ctx.BeginPath();
            _ctx.MoveTo(0, h / 2);
            _ctx.LineTo(w, h / 2);
            _ctx.Stroke();
            _frame = Js.RequestAnimationFrame(Draw);
            return;
        }

        _analyser.GetByteFrequencyData(_spectrum);
        _analyser.GetByteTimeDomainData(_wave);

        var nyquist = _audio.SampleRate / 2;
        var bins = _spectrum.Length;
        var gradient = _ctx.CreateLinearGradient(0, h, 0, 0);
        gradient.AddColorStop(0, "rgba(124, 58, 237, 0.25)");
        gradient.AddColorStop(1, "rgba(167, 139, 250, 0.9)");
        _ctx.FillStyle = gradient;

        // Bars of equal width on screen, each the loudest bin that falls under it.
        const int Bars = 96;
        var barWidth = w / Bars;
        for (var bar = 0; bar < Bars; bar++)
        {
            var low = Math.Pow(10, Math.Log10(30) + bar * (Math.Log10(nyquist) - Math.Log10(30)) / Bars);
            var high = Math.Pow(10, Math.Log10(30) + (bar + 1) * (Math.Log10(nyquist) - Math.Log10(30)) / Bars);
            var first = (int)(low / nyquist * bins);
            var last = Math.Max(first, (int)(high / nyquist * bins));
            var peak = 0;
            for (var i = first; i <= last && i < bins; i++) peak = Math.Max(peak, _spectrum[i]);

            var height = peak / 255.0 * (h - 24);
            _ctx.FillRect(bar * barWidth + 1, h - 20 - height, barWidth - 2, height);
        }

        _ctx.StrokeStyle = "#fbbf24";
        _ctx.LineWidth = 2;
        _ctx.ShadowColor = "rgba(251, 191, 36, 0.6)";
        _ctx.ShadowBlur = 8;
        _ctx.BeginPath();
        for (var i = 0; i < _wave.Length; i++)
        {
            var x = (double)i / _wave.Length * w;
            var y = _wave[i] / 255.0 * (h - 20);
            if (i == 0) _ctx.MoveTo(x, y);
            else _ctx.LineTo(x, y);
        }
        _ctx.Stroke();
        _ctx.ShadowBlur = 0;

        _frame = Js.RequestAnimationFrame(Draw);
    }

    private static double LogX(double hz, double width)
        => (Math.Log10(hz) - Math.Log10(30)) / (Math.Log10(24000) - Math.Log10(30)) * width;
}
