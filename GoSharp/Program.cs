using System;
using System.Threading.Tasks;
using GoSharp;

Console.WriteLine("Demo GoSharp: goroutine & channel");

// Buffered channel (capacity 2)
var ch = new Channel<int>(2);

// Producer
Go.Run(async () =>
{
    for (int i = 1; i <= 5; i++)
    {
        await ch.SendAsync(i);
        Console.WriteLine($"send: {i}");
    }
    ch.Close();
});

// Consumer
while (true)
{
    var (ok, v) = await ch.ReceiveAsync();
    if (!ok) break;
    Console.WriteLine($"recv: {v}");
}

Console.WriteLine("Selesai");
