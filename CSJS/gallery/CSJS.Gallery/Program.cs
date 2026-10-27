// CSJS Gallery host: the static site, plus one live endpoint for every protocol the API
// playground speaks. The browser side of all of it is C# transpiled by CSJS.
//
// Gravicode Studios — led by Kang Fadhil.

using CSJS.Gallery.Grpc;
using CSJS.Gallery.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSignalR();
builder.Services.AddSingleton<TodoStore>();
builder.Services.AddSingleton<SocketIoServer>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Source maps and C# sources are what the gallery's code view reads; serve them as text.
    ServeUnknownFileTypes = false,
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

// ---- REST ---------------------------------------------------------------------------------
// A small todo API: list, create, toggle, delete. The client serialises with System.Text.Json
// — the same call it would make on .NET — and CSJS turns that into JSON in the browser.

var todos = app.MapGroup("/api/todos");
todos.MapGet("/", (TodoStore store) => store.All());
todos.MapPost("/", (TodoStore store, NewTodo todo) =>
    string.IsNullOrWhiteSpace(todo.Title)
        ? Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["A title is required."] })
        : Results.Created($"/api/todos", store.Add(todo.Title.Trim())));
todos.MapPut("/{id:int}/toggle", (TodoStore store, int id) =>
    store.Toggle(id) is { } todo ? Results.Ok(todo) : Results.NotFound());
todos.MapDelete("/{id:int}", (TodoStore store, int id) =>
    store.Remove(id) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/time", () => new { Utc = DateTime.UtcNow, Server = Environment.MachineName.Length > 0 ? "csjs-gallery" : "" });

// ---- gRPC-Web ------------------------------------------------------------------------------
// Greeter speaks gRPC; UseGrpcWeb lets a browser call it over plain HTTP/1.1 fetch.

app.MapGrpcService<GreeterService>();

// ---- SignalR -------------------------------------------------------------------------------

app.MapHub<ChatHub>("/hubs/chat");

// ---- Socket.IO -----------------------------------------------------------------------------
// The Engine.IO v4 protocol over WebSocket — enough for socket.io-client with
// `transports: ['websocket']`, which is all the playground uses.

app.Map("/socket.io/", (HttpContext context, SocketIoServer server) => server.AcceptAsync(context));

// ---- Raw WebSocket -------------------------------------------------------------------------
// Echoes every frame back with a server timestamp, and pushes a heartbeat every two seconds.

app.Map("/ws/echo", (HttpContext context) => EchoSocket.AcceptAsync(context));

// ---- Server-Sent Events --------------------------------------------------------------------
// A ticker: one price a second from one shared series. Each event carries its sequence number
// as its id, and the stream ends every 30 live ticks on purpose — the browser reconnects with
// Last-Event-ID and the missed ticks are replayed, which is the point of the demo.

app.MapGet("/sse/ticker", (HttpContext context, CancellationToken cancel) => Ticker.StreamAsync(context, cancel));

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Request body for creating a todo.</summary>
public sealed record NewTodo(string Title);

/// <summary>A todo item.</summary>
public sealed record Todo(int Id, string Title, bool Done, DateTime Created);

/// <summary>In-memory todo list, seeded so the playground never opens empty.</summary>
public sealed class TodoStore
{
    private readonly object _gate = new();
    private readonly List<Todo> _items =
    [
        new(1, "Write it in C#", true, DateTime.UtcNow.AddMinutes(-30)),
        new(2, "Ship it as JavaScript", false, DateTime.UtcNow.AddMinutes(-12)),
        new(3, "Show the seam", false, DateTime.UtcNow.AddMinutes(-3)),
    ];
    private int _next = 4;

    public IReadOnlyList<Todo> All() { lock (_gate) return _items.ToList(); }

    public Todo Add(string title)
    {
        lock (_gate)
        {
            var todo = new Todo(_next++, title, false, DateTime.UtcNow);
            _items.Add(todo);
            return todo;
        }
    }

    public Todo? Toggle(int id)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(t => t.Id == id);
            if (index < 0) return null;
            _items[index] = _items[index] with { Done = !_items[index].Done };
            return _items[index];
        }
    }

    public bool Remove(int id) { lock (_gate) return _items.RemoveAll(t => t.Id == id) > 0; }
}
