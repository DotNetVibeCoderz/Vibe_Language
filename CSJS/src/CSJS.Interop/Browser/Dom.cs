namespace CSJS.Browser;

// Declaration-only bindings to the browser DOM. Every body throws: the transpiler replaces
// these call sites with the corresponding JavaScript, and they are never executed on .NET.
#pragma warning disable CA1822 // members can be static — kept instance to mirror the JS shape

/// <summary>The global <c>document</c> object.</summary>
[JsGlobal("document")]
public static class Document
{
    public static Element? QuerySelector(string selectors) => throw new JsExternException();
    public static Element[] QuerySelectorAll(string selectors) => throw new JsExternException();
    public static Element? GetElementById(string id) => throw new JsExternException();
    public static Element[] GetElementsByClassName(string names) => throw new JsExternException();
    public static Element[] GetElementsByTagName(string name) => throw new JsExternException();
    public static Element CreateElement(string tagName) => throw new JsExternException();
    public static Text CreateTextNode(string data) => throw new JsExternException();

    [JsName("body")] public static Element Body => throw new JsExternException();
    [JsName("head")] public static Element Head => throw new JsExternException();
    [JsName("documentElement")] public static Element DocumentElement => throw new JsExternException();
    [JsName("title")] public static string Title { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("readyState")] public static string ReadyState => throw new JsExternException();
    [JsName("activeElement")] public static Element? ActiveElement => throw new JsExternException();

    public static void AddEventListener(string type, Action<Event> listener) => throw new JsExternException();
    public static void RemoveEventListener(string type, Action<Event> listener) => throw new JsExternException();
}

/// <summary>A DOM node.</summary>
[JsGlobal("Node")]
public class Node
{
    [JsName("textContent")] public string TextContent { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("nodeName")] public string NodeName => throw new JsExternException();
    [JsName("parentNode")] public Node? ParentNode => throw new JsExternException();
    [JsName("childNodes")] public Node[] ChildNodes => throw new JsExternException();
    [JsName("firstChild")] public Node? FirstChild => throw new JsExternException();
    [JsName("lastChild")] public Node? LastChild => throw new JsExternException();

    public Node AppendChild(Node child) => throw new JsExternException();
    public Node RemoveChild(Node child) => throw new JsExternException();
    public Node InsertBefore(Node newNode, Node? referenceNode) => throw new JsExternException();
    public Node ReplaceChild(Node newChild, Node oldChild) => throw new JsExternException();
    public Node CloneNode(bool deep) => throw new JsExternException();
    public bool Contains(Node other) => throw new JsExternException();
}

/// <summary>A text node.</summary>
[JsGlobal("Text")]
public class Text : Node
{
}

/// <summary>A DOM element. Covers the common HTMLElement surface as well.</summary>
[JsGlobal("Element")]
public class Element : Node
{
    [JsName("id")] public string Id { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("className")] public string ClassName { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("innerHTML")] public string InnerHtml { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("outerHTML")] public string OuterHtml { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("innerText")] public string InnerText { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("tagName")] public string TagName => throw new JsExternException();
    [JsName("value")] public string Value { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("checked")] public bool Checked { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("disabled")] public bool Disabled { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("hidden")] public bool Hidden { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("style")] public Style Style => throw new JsExternException();
    [JsName("classList")] public ClassList ClassList => throw new JsExternException();
    [JsName("dataset")] public JsObject Dataset => throw new JsExternException();
    [JsName("children")] public Element[] Children => throw new JsExternException();
    [JsName("parentElement")] public Element? ParentElement => throw new JsExternException();
    [JsName("clientWidth")] public double ClientWidth => throw new JsExternException();
    [JsName("clientHeight")] public double ClientHeight => throw new JsExternException();
    [JsName("scrollTop")] public double ScrollTop { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("scrollHeight")] public double ScrollHeight => throw new JsExternException();

    public Element? QuerySelector(string selectors) => throw new JsExternException();
    public Element[] QuerySelectorAll(string selectors) => throw new JsExternException();
    public Element? Closest(string selectors) => throw new JsExternException();
    public bool Matches(string selectors) => throw new JsExternException();

    public string? GetAttribute(string name) => throw new JsExternException();
    public void SetAttribute(string name, string value) => throw new JsExternException();
    public void RemoveAttribute(string name) => throw new JsExternException();
    public bool HasAttribute(string name) => throw new JsExternException();

    public void AddEventListener(string type, Action<Event> listener) => throw new JsExternException();
    public void RemoveEventListener(string type, Action<Event> listener) => throw new JsExternException();
    public void Append(Node child) => throw new JsExternException();
    public void Remove() => throw new JsExternException();
    public void Focus() => throw new JsExternException();
    public void Blur() => throw new JsExternException();
    public void Click() => throw new JsExternException();
    public void ScrollIntoView() => throw new JsExternException();
    public DomRect GetBoundingClientRect() => throw new JsExternException();
}

/// <summary>The <c>classList</c> token list.</summary>
[JsGlobal("DOMTokenList")]
public class ClassList
{
    [JsName("length")] public int Length => throw new JsExternException();
    public void Add(string token) => throw new JsExternException();
    public void Remove(string token) => throw new JsExternException();
    public bool Toggle(string token) => throw new JsExternException();
    public bool Toggle(string token, bool force) => throw new JsExternException();
    public bool Contains(string token) => throw new JsExternException();
}

/// <summary>Inline CSS style declaration. Property names use their JS camelCase spelling.</summary>
[JsGlobal("CSSStyleDeclaration")]
public class Style
{
    [JsName("display")] public string Display { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("color")] public string Color { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("backgroundColor")] public string BackgroundColor { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("width")] public string Width { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("height")] public string Height { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("margin")] public string Margin { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("padding")] public string Padding { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("border")] public string Border { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("borderRadius")] public string BorderRadius { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("opacity")] public string Opacity { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("transform")] public string Transform { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("transition")] public string Transition { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("position")] public string Position { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("top")] public string Top { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("left")] public string Left { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("fontSize")] public string FontSize { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("fontWeight")] public string FontWeight { get => throw new JsExternException(); set => throw new JsExternException(); }

    public void SetProperty(string name, string value) => throw new JsExternException();
    public string GetPropertyValue(string name) => throw new JsExternException();
    public void RemoveProperty(string name) => throw new JsExternException();
}

/// <summary>Result of <c>getBoundingClientRect()</c>.</summary>
[JsGlobal("DOMRect")]
public class DomRect
{
    [JsName("x")] public double X => throw new JsExternException();
    [JsName("y")] public double Y => throw new JsExternException();
    [JsName("width")] public double Width => throw new JsExternException();
    [JsName("height")] public double Height => throw new JsExternException();
    [JsName("top")] public double Top => throw new JsExternException();
    [JsName("right")] public double Right => throw new JsExternException();
    [JsName("bottom")] public double Bottom => throw new JsExternException();
    [JsName("left")] public double Left => throw new JsExternException();
}

/// <summary>A DOM event.</summary>
[JsGlobal("Event")]
public class Event
{
    [JsName("type")] public string Type => throw new JsExternException();
    [JsName("target")] public Element Target => throw new JsExternException();
    [JsName("currentTarget")] public Element CurrentTarget => throw new JsExternException();
    [JsName("defaultPrevented")] public bool DefaultPrevented => throw new JsExternException();

    public void PreventDefault() => throw new JsExternException();
    public void StopPropagation() => throw new JsExternException();
    public void StopImmediatePropagation() => throw new JsExternException();
}

/// <summary>A mouse event.</summary>
[JsGlobal("MouseEvent")]
public class MouseEvent : Event
{
    [JsName("clientX")] public double ClientX => throw new JsExternException();
    [JsName("clientY")] public double ClientY => throw new JsExternException();
    [JsName("offsetX")] public double OffsetX => throw new JsExternException();
    [JsName("offsetY")] public double OffsetY => throw new JsExternException();
    [JsName("button")] public int Button => throw new JsExternException();
    [JsName("shiftKey")] public bool ShiftKey => throw new JsExternException();
    [JsName("ctrlKey")] public bool CtrlKey => throw new JsExternException();
    [JsName("altKey")] public bool AltKey => throw new JsExternException();
}

/// <summary>A keyboard event.</summary>
[JsGlobal("KeyboardEvent")]
public class KeyboardEvent : Event
{
    [JsName("key")] public string Key => throw new JsExternException();
    [JsName("code")] public string Code => throw new JsExternException();
    [JsName("repeat")] public bool Repeat => throw new JsExternException();
    [JsName("shiftKey")] public bool ShiftKey => throw new JsExternException();
    [JsName("ctrlKey")] public bool CtrlKey => throw new JsExternException();
    [JsName("altKey")] public bool AltKey => throw new JsExternException();
}

#pragma warning restore CA1822
