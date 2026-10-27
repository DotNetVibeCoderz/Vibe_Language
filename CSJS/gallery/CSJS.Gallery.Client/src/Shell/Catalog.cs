using CSJS.Gallery.Client.Demos.Graphics;
using CSJS.Gallery.Client.Demos.Network;

namespace CSJS.Gallery.Client.Shell;

/// <summary>A gallery section: a name and the one-line promise its demos keep.</summary>
public sealed record Category(string Name, string Blurb);

/// <summary>Every demo in the gallery, in the order the rail lists them.</summary>
public static class Catalog
{
    public static readonly List<Category> Categories = new()
    {
        new("Graphics", "Canvas, animation, and pixels, drawn from C#."),
        new("Media", "Sound, camera, and speech."),
        new("Device", "What the browser knows about where and how it is running."),
        new("Data", "Files, storage, crypto, and the clipboard."),
        new("Performance", "Workers, observers, and staying off the main thread."),
        new("Network", "REST, gRPC-Web, SignalR, Socket.IO, WebSocket, and Server-Sent Events."),
    };

    public static readonly List<Demo> All = new()
    {
        new FlowField(),
        new RestTodos(),
    };

    public static Demo? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    public static List<Demo> In(string category) => All.Where(d => d.Category == category).ToList();
}
