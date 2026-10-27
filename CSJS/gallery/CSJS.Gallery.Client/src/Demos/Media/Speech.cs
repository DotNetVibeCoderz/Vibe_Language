using CSJS;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Media;

// Speech recognition is not in lib.dom — Chromium ships it prefixed and Firefox not at all — so
// the handful of members this demo uses are declared here, the same way any binding is.

/// <summary>The Web Speech API's recogniser, constructed from whichever name the browser has.</summary>
[JsGlobal("webkitSpeechRecognition")]
public class SpeechRecognition : EventTarget
{
    [JsTemplate("new (window.SpeechRecognition || window.webkitSpeechRecognition)()")]
    public static SpeechRecognition Create() => throw new JsExternException();

    public string Lang { get => throw new JsExternException(); set => throw new JsExternException(); }
    public bool Continuous { get => throw new JsExternException(); set => throw new JsExternException(); }
    public bool InterimResults { get => throw new JsExternException(); set => throw new JsExternException(); }
    public void Start() => throw new JsExternException();
    public void Stop() => throw new JsExternException();
    public void Abort() => throw new JsExternException();
}

[JsGlobal("SpeechRecognitionEvent")]
public class SpeechRecognitionEvent : Event
{
    public int ResultIndex => throw new JsExternException();
    public SpeechRecognitionResult[] Results => throw new JsExternException();
}

[JsGlobal("SpeechRecognitionResult")]
public class SpeechRecognitionResult
{
    public bool IsFinal => throw new JsExternException();
    public SpeechRecognitionAlternative this[int index] => throw new JsExternException();
}

[JsGlobal("SpeechRecognitionAlternative")]
public class SpeechRecognitionAlternative
{
    public string Transcript => throw new JsExternException();
    public double Confidence => throw new JsExternException();
}

[JsGlobal("SpeechRecognitionErrorEvent")]
public class SpeechRecognitionErrorEvent : Event
{
    public string Error => throw new JsExternException();
}

/// <summary>
/// Text to speech with the system's voices, highlighting each word as it is spoken from the
/// utterance's <c>boundary</c> events — and, where the browser offers it, speech to text.
/// </summary>
public sealed class Speech : Demo
{
    public override string Id => "speech";
    public override string Title => "Speech";
    public override string Category => "Media";
    public override string Summary => "The system's voices through speechSynthesis, with each word lit as it is spoken from boundary events. Where the browser has a recogniser, it listens too.";
    public override string[] Apis => new[] { "SpeechSynthesis", "SpeechSynthesisUtterance", "boundary events", "SpeechRecognition" };
    public override string Module => "Demos/Media/Speech";
    public override string Hint => "Choose a voice, press Speak, and watch the words.";

    private const string Sample =
        "This sentence was handed to the speech engine by C# that became JavaScript. " +
        "Each word lights up as the voice reaches it, because the utterance reports a boundary event at every word.";

    private sealed class Word
    {
        public int Start;
        public int End;
        public HTMLElement Element = null!;
    }

    private readonly List<Word> _words = new();
    private HTMLTextAreaElement _text = null!;
    private HTMLElement _reading = null!;
    private HTMLSelectElement _voices = null!;
    private HTMLButtonElement _speak = null!;
    private HTMLButtonElement _pause = null!;
    private HTMLElement _voiceCount = null!;
    private HTMLElement _speechState = null!;
    private double _rate = 1;
    private double _pitch = 1;
    private SpeechSynthesisVoice[] _available = Array.Empty<SpeechSynthesisVoice>();
    private Action<Event> _voicesChanged = null!;

    private SpeechRecognition? _recogniser;
    private bool _listening;
    private HTMLButtonElement? _listen;
    private HTMLElement _transcript = null!;
    private HTMLElement _interim = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        var speak = stage.Add(Ui.El("div", "panel speech-panel"));
        speak.Add(Ui.El("h3", "panel-title", "Text to speech"));

        if (!Controls.Supports(Js.Window, "speechSynthesis"))
        {
            speak.Add(Controls.Notice("This browser cannot speak",
                "It has no speechSynthesis. Recent Chrome, Edge, Safari, and Firefox all do — try one of those.", "warn"));
        }
        else
        {
            BuildSynthesis(speak);
        }

        var listen = stage.Add(Ui.El("div", "panel panel-log speech-panel"));
        listen.Add(Ui.El("h3", "panel-title", "Speech to text"));
        BuildRecognition(listen);
    }

    public override void Unmount()
    {
        if (Controls.Supports(Js.Window, "speechSynthesis"))
        {
            Js.SpeechSynthesis.Cancel();
            Js.SpeechSynthesis.Off("voiceschanged", _voicesChanged);
        }
        _recogniser?.Abort();
    }

    private void BuildSynthesis(HTMLElement panel)
    {
        _text = panel.Add(Dom.Create<HTMLTextAreaElement>("textarea"));
        _text.ClassName = "field speech-text";
        _text.Value = Sample;
        _text.Rows = 3;
        _text.SetAttribute("aria-label", "Text to speak");
        _text.On<Event>("input", _ => Tokenise());

        _reading = panel.Add(Ui.El("p", "speech-reading"));
        _reading.SetAttribute("aria-live", "off");
        Tokenise();

        var row = panel.Add(Ui.El("div", "speech-settings"));
        var voiceBox = row.Add(Ui.El("label", "ctl speech-voice"));
        var head = voiceBox.Add(Ui.El("span", "ctl-head"));
        head.Add(Ui.El("span", "ctl-label", "Voice"));
        _voiceCount = head.Add(Ui.El("span", "ctl-value", "…"));
        _voices = voiceBox.Add(Dom.Create<HTMLSelectElement>("select"));
        _voices.ClassName = "field";
        row.Add(Controls.Slider("Rate", 0.5, 2, 0.05, _rate, v => _rate = v, v => $"{v:F2}×"));
        row.Add(Controls.Slider("Pitch", 0, 2, 0.05, _pitch, v => _pitch = v, v => $"{v:F2}"));

        var actions = panel.Add(Ui.El("div", "row speech-actions"));
        _speak = actions.Add(Ui.Button("Speak", Speak, "btn btn-primary"));
        _pause = actions.Add(Ui.Button("Pause", PauseOrResume));
        actions.Add(Ui.Button("Stop", () => Js.SpeechSynthesis.Cancel()));
        actions.Add(Ui.Readout("State", out _speechState, "idle"));

        // Voices load asynchronously: often empty at first, then announced by voiceschanged.
        _voicesChanged = _ => LoadVoices();
        Js.SpeechSynthesis.On("voiceschanged", _voicesChanged);
        LoadVoices();
    }

    private void LoadVoices()
    {
        _available = Js.SpeechSynthesis.GetVoices()
            .OrderBy(v => v.Lang.StartsWith("en") ? 0 : 1)
            .ThenBy(v => v.Lang)
            .ThenBy(v => v.Name)
            .ToArray();

        var selected = _voices.Value;
        _voices.InnerHTML = "";
        if (_available.Length == 0)
        {
            var none = Ui.Add(_voices, Dom.Create<HTMLOptionElement>("option"));
            none.TextContent = "The system default voice";
            none.Value = "";
            _voiceCount.TextContent = "none listed";
            return;
        }

        // Keep the reader's choice across reloads of the list; otherwise start with a voice in
        // the browser's own language.
        if (selected.Length == 0)
        {
            var language = Js.Navigator.Language;
            var preferred = _available.FirstOrDefault(v => v.Lang == language && v.LocalService)
                            ?? _available.FirstOrDefault(v => v.Lang == language)
                            ?? _available.FirstOrDefault(v => v.Default);
            selected = preferred?.VoiceURI ?? "";
        }

        foreach (var voice in _available)
        {
            var option = Ui.Add(_voices, Dom.Create<HTMLOptionElement>("option"));
            option.Value = voice.VoiceURI;
            option.TextContent = $"{voice.Name} · {voice.Lang}{(voice.LocalService ? "" : " · online")}";
            if (voice.VoiceURI == selected) option.Selected = true;
        }
        _voiceCount.TextContent = $"{_available.Length} available";
    }

    /// <summary>
    /// Splits the text into word spans, remembering where each starts and ends in the string —
    /// a boundary event reports a character index, and this is how it finds its word.
    /// </summary>
    private void Tokenise()
    {
        _words.Clear();
        _reading.InnerHTML = "";
        var text = _text.Value;
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            var isSpace = char.IsWhiteSpace(text[i]);
            while (i < text.Length && char.IsWhiteSpace(text[i]) == isSpace) i++;
            var piece = text.Substring(start, i - start);

            if (isSpace)
            {
                _reading.Append(Js.Document.CreateTextNode(piece));
                continue;
            }

            var span = _reading.Add(Ui.El("span", "speech-word", piece));
            _words.Add(new Word { Start = start, End = i, Element = span });
        }
    }

    private void Speak()
    {
        Js.SpeechSynthesis.Cancel();
        Tokenise();

        var utterance = new SpeechSynthesisUtterance(_text.Value);
        utterance.Rate = _rate;
        utterance.Pitch = _pitch;
        var voice = _available.FirstOrDefault(v => v.VoiceURI == _voices.Value);
        if (voice is not null)
        {
            utterance.Voice = voice;
            utterance.Lang = voice.Lang;
        }

        utterance.On<SpeechSynthesisEvent>("boundary", e =>
        {
            if (e.Name != "word") return;
            foreach (var word in _words)
            {
                var spoken = word.End <= e.CharIndex;
                var current = e.CharIndex >= word.Start && e.CharIndex < word.End;
                word.Element.ClassList.Toggle("is-spoken", spoken);
                word.Element.ClassList.Toggle("is-current", current);
            }
        });
        utterance.On<SpeechSynthesisEvent>("start", _ => SetState("speaking"));
        utterance.On<SpeechSynthesisEvent>("pause", _ => SetState("paused"));
        utterance.On<SpeechSynthesisEvent>("resume", _ => SetState("speaking"));
        utterance.On<SpeechSynthesisEvent>("end", _ =>
        {
            SetState("idle");
            foreach (var word in _words)
            {
                word.Element.ClassList.Remove("is-current");
                word.Element.ClassList.Add("is-spoken");
            }
        });
        utterance.On<SpeechSynthesisErrorEvent>("error", e =>
        {
            // "interrupted" and "canceled" are what Stop and a new Speak cause; not failures.
            if (e.Error is "interrupted" or "canceled") return;
            SetState(e.Error);
        });

        Js.SpeechSynthesis.Speak(utterance);
    }

    private void PauseOrResume()
    {
        if (Js.SpeechSynthesis.Paused) Js.SpeechSynthesis.Resume();
        else Js.SpeechSynthesis.Pause();
    }

    private void SetState(string state)
    {
        _speechState.TextContent = state;
        _pause.TextContent = state == "paused" ? "Resume" : "Pause";
    }

    private void BuildRecognition(HTMLElement panel)
    {
        var supported = Controls.Supports(Js.Window, "SpeechRecognition") || Controls.Supports(Js.Window, "webkitSpeechRecognition");
        if (!supported)
        {
            panel.Add(Controls.Notice("This browser has no speech recogniser",
                "Speech to text is offered by Chrome, Edge, and Safari, which send audio to the vendor's service. Firefox does not ship it. Text to speech, on the left, works everywhere."));
            _transcript = panel.Add(Ui.El("p", "speech-transcript"));
            _interim = _transcript;
            return;
        }

        var row = panel.Add(Ui.El("div", "row"));
        _listen = row.Add(Ui.Button("Start listening", ToggleListening, "btn btn-primary"));
        row.Add(Ui.El("span", "speech-note", "Chromium sends the audio to its speech service."));

        _transcript = panel.Add(Ui.El("p", "speech-transcript"));
        _transcript.Add(Ui.El("span", "empty", "Say something after you press Start listening."));
        _interim = panel.Add(Ui.El("p", "speech-interim"));
    }

    private void ToggleListening()
    {
        if (_listening)
        {
            _recogniser?.Stop();
            return;
        }

        var recogniser = SpeechRecognition.Create();
        recogniser.Lang = Js.Navigator.Language;
        recogniser.Continuous = true;
        recogniser.InterimResults = true;

        recogniser.On<SpeechRecognitionEvent>("result", e =>
        {
            var interim = "";
            for (var i = e.ResultIndex; i < e.Results.Length; i++)
            {
                var result = e.Results[i];
                if (result.IsFinal)
                {
                    if (_transcript.Query<HTMLElement>(".empty") is { } placeholder) placeholder.Remove();
                    _transcript.Append(Js.Document.CreateTextNode(result[0].Transcript.Trim() + " "));
                }
                else
                {
                    interim += result[0].Transcript;
                }
            }
            _interim.TextContent = interim;
        });
        recogniser.On<SpeechRecognitionErrorEvent>("error", e =>
        {
            _interim.TextContent = e.Error == "not-allowed"
                ? "Microphone permission was refused. Allow it for this site, then start again."
                : $"Recognition stopped: {e.Error}.";
        });
        recogniser.On<Event>("end", _ =>
        {
            _listening = false;
            if (_listen is not null) _listen.TextContent = "Start listening";
        });

        recogniser.Start();
        _recogniser = recogniser;
        _listening = true;
        _listen!.TextContent = "Stop listening";
    }
}
