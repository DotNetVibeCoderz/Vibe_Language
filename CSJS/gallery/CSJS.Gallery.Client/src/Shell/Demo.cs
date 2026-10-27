using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>
/// One gallery entry. A demo knows how to describe itself, how to start inside the stage it is
/// handed, and how to stop again — every timer, socket, stream, and listener it opened is closed in
/// <see cref="Unmount"/>, because navigating away must leave nothing running.
/// </summary>
public abstract class Demo
{
    /// <summary>The URL slug: <c>#/demo/{Id}</c>.</summary>
    public abstract string Id { get; }

    public abstract string Title { get; }

    /// <summary>One of the categories in <see cref="Catalog.Categories"/>.</summary>
    public abstract string Category { get; }

    /// <summary>One or two sentences: what it shows, in the reader's terms.</summary>
    public abstract string Summary { get; }

    /// <summary>The web APIs it uses, as their names appear on MDN.</summary>
    public abstract string[] Apis { get; }

    /// <summary>
    /// The emitted module's path under <c>js/</c>, without the extension — e.g.
    /// <c>Demos/Graphics/FlowField</c>. The code view reads its <c>.js</c> and <c>.js.map</c>; the
    /// map carries the C# source itself.
    /// </summary>
    public abstract string Module { get; }

    /// <summary>A short line shown on the demo's card, e.g. what to try first.</summary>
    public virtual string Hint => "";

    public abstract void Mount(HTMLElement stage);

    public virtual void Unmount() { }
}
