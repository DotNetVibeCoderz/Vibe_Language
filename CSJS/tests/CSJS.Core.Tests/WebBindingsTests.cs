using CSJS.Core.Bindings;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// CSJS.Web — the web platform generated from lib.dom — has to emit exactly the JavaScript each
/// call names. These transpile real usage against the shipped assembly and check the output.
/// </summary>
public class WebBindingsTests
{
    private static string ClassBody(string code, string name)
    {
        var start = code.IndexOf($"public class {name} ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"class {name} not generated");
        var end = code.IndexOf("\n}", start, StringComparison.Ordinal);
        return code[start..end];
    }

    private static string Emit(string csharp)
    {
        var options = new TranspileOptions { EmitSourceMap = false, EmitTypeDefinitions = false };
        options.AdditionalReferences.Add(typeof(CSJS.Web.Dom).Assembly.Location);
        return Js.Emit(csharp, options);
    }

    [Fact]
    public void CanvasAndDomCallsEmitPlainJavaScript()
    {
        var js = Emit("""
            using CSJS.Web;
            public static class Demo
            {
                public static void Run()
                {
                    var canvas = Dom.Query<HTMLCanvasElement>("#stage")!;
                    var ctx = canvas.GetContext2D();
                    ctx.FillStyle = "#a78bfa";
                    ctx.FillRect(0, 0, canvas.Width, canvas.Height);
                    var card = Dom.Create("div", "card");
                    card.On<MouseEvent>("click", e => ctx.Arc(e.OffsetX, e.OffsetY, 4, 0, 6.28));
                    Dom.Body.Append(card);
                }
            }
            """);

        Js.Contains("document.querySelector('#stage')", js);
        Js.Contains("canvas.getContext('2d')", js);
        Js.Contains("ctx.fillStyle = '#a78bfa';", js);
        Js.Contains("ctx.fillRect(0, 0, canvas.width, canvas.height);", js);
        Js.Contains("Object.assign(document.createElement('div'), { className: 'card' })", js);
        Js.Contains("card.addEventListener('click', (e) => ctx.arc(e.offsetX, e.offsetY, 4, 0, 6.28))", js);
        Js.Contains("document.body.append(card);", js);
    }

    [Fact]
    public void PlatformObjectsAreConstructedAndReachedThroughTheirGlobals()
    {
        var js = Emit("""
            using System.Threading.Tasks;
            using CSJS.Web;
            public static class Demo
            {
                public static async Task Run()
                {
                    var audio = new AudioContext();
                    var osc = audio.CreateOscillator();
                    osc.Frequency.Value = 440;
                    osc.Connect(audio.Destination);

                    var socket = new WebSocket("wss://example.org");
                    socket.Send("hi");

                    await Js.Navigator.Clipboard.WriteText("copied");
                    var digest = await Js.Crypto.Subtle.Digest("SHA-256", new TextEncoder().Encode("abc"));
                    var bytes = new Uint8Array(digest);
                    Js.LocalStorage.SetItem("length", bytes.Length.ToString());
                }
            }
            """);

        Js.Contains("new AudioContext()", js);
        Js.Contains("osc.frequency.value = 440;", js);
        Js.Contains("osc.connect(audio.destination);", js);
        Js.Contains("new WebSocket('wss://example.org')", js);
        Js.Contains("await navigator.clipboard.writeText('copied');", js);
        Js.Contains("await crypto.subtle.digest('SHA-256', new TextEncoder().encode('abc'))", js);
        Js.Contains("new Uint8Array(digest)", js);
        Js.Contains("localStorage.setItem('length', String(bytes.length));", js);
    }

    [Fact]
    public void ElementsInheritSoCastsBetweenThemCompile()
    {
        // HTMLCanvasElement : HTMLElement : Element : Node : EventTarget. A cast is a C# matter
        // only; JavaScript has none.
        var js = Emit("""
            using CSJS.Web;
            public static class Demo
            {
                public static double Width(Element element) => ((HTMLCanvasElement)element).Width;
                public static void Listen(HTMLCanvasElement canvas) => Watch(canvas);
                private static void Watch(EventTarget target) => target.DispatchEvent(new Event("ready"));
            }
            """);

        Js.Contains("return (element).width;", js);
        Js.Contains("target.dispatchEvent(new Event('ready'));", js);
    }

    [Fact]
    public void PlatformModeGeneratesInheritanceAndRealNames()
    {
        var result = BindingGenerator.Generate(new BindingSource("lib.dom.d.ts", """
            interface EventTarget { addEventListener(type: string, listener: (e: Event) => void): void; }
            declare var EventTarget: { prototype: EventTarget; new(): EventTarget; };
            interface Event { readonly type: string; }
            declare var Event: { prototype: Event; new(type: string): Event; };
            interface Animatable { animate(keyframes: any[]): void; }
            interface Element extends EventTarget, Animatable { id: string; }
            declare var Element: { prototype: Element; new(): Element; };
            interface HTMLElement extends Element { hidden: boolean; }
            declare var HTMLElement: { prototype: HTMLElement; new(): HTMLElement; };
            declare var document: { title: string };
            """), new BindingOptions { Platform = true, Global = true, Namespace = "CSJS.Web", ContainerName = "Js" });

        Assert.Contains("public class Element : EventTarget", result.Code);
        Assert.Contains("public class HTMLElement : Element", result.Code);
        Assert.Contains("public class Event", result.Code);
        Assert.DoesNotContain("IndexElement", result.Code);
        Assert.DoesNotContain("using CSJS.Browser;", result.Code);

        // The Animatable mixin is folded into Element, and HTMLElement inherits it rather than
        // carrying a second copy.
        Assert.Contains("Animate(", ClassBody(result.Code, "Element"));
        Assert.DoesNotContain("Animate(", ClassBody(result.Code, "HTMLElement"));
    }
}
