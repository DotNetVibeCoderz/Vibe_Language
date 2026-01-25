# GoSharp

Library sederhana yang meniru **goroutines** dan **channels** di bahasa Go menggunakan C# / .NET.

## Fitur
- `Go.Run(...)` untuk menjalankan task background seperti goroutine
- `Channel<T>` untuk komunikasi antar task (buffered & unbuffered)
- `Send/Receive` async & sync
- `Close()` untuk menutup channel

## Cara Menjalankan
```bash
dotnet run
```

## Contoh Penggunaan
```csharp
var ch = new Channel<int>(2);
Go.Run(async () =>
{
    for (int i = 1; i <= 5; i++)
        await ch.SendAsync(i);
    ch.Close();
});

while (true)
{
    var (ok, v) = await ch.ReceiveAsync();
    if (!ok) break;
    Console.WriteLine(v);
}
```

---

# GoSharp (English)

A simple library that mimics **goroutines** and **channels** from Go using C# / .NET.

## Features
- `Go.Run(...)` to run background tasks like goroutines
- `Channel<T>` for task communication (buffered & unbuffered)
- Async & sync `Send/Receive`
- `Close()` to close channel

## How to Run
```bash
dotnet run
```

## Usage Example
```csharp
var ch = new Channel<int>(2);
Go.Run(async () =>
{
    for (int i = 1; i <= 5; i++)
        await ch.SendAsync(i);
    ch.Close();
});

while (true)
{
    var (ok, v) = await ch.ReceiveAsync();
    if (!ok) break;
    Console.WriteLine(v);
}
```
