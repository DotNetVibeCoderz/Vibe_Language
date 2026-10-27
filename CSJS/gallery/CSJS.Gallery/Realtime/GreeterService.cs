using Grpc.Core;

namespace CSJS.Gallery.Grpc;

/// <summary>The gRPC service behind the gRPC-Web playground.</summary>
public sealed class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? "stranger" : request.Name.Trim();
        var message = $"Hello, {name} — from gRPC, read by C# running as JavaScript.";
        return Task.FromResult(new HelloReply { Message = message, Length = message.Length });
    }

    public override async Task Countdown(CountdownRequest request, IServerStreamWriter<Tick> responseStream, ServerCallContext context)
    {
        var from = Math.Clamp(request.From, 1, 20);
        for (var remaining = from; remaining >= 0 && !context.CancellationToken.IsCancellationRequested; remaining--)
        {
            await responseStream.WriteAsync(new Tick
            {
                Remaining = remaining,
                Label = remaining == 0 ? "liftoff" : $"T-{remaining}",
            });
            if (remaining > 0) await Task.Delay(400, context.CancellationToken);
        }
    }
}
