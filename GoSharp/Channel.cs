using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GoSharp;

/// <summary>
/// Channel mirip Go: bisa unbuffered (capacity=0) atau buffered.
/// Mendukung Send/Receive async dan sync serta Close.
/// </summary>
public class Channel<T>
{
    private readonly Queue<T> _queue = new();
    private readonly int _capacity;
    private bool _closed;
    private readonly SemaphoreSlim _items = new(0);
    private readonly SemaphoreSlim _spaces;
    private readonly object _lock = new();

    public Channel(int capacity = 0)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _spaces = new SemaphoreSlim(capacity == 0 ? 1 : capacity); // unbuffered => 1 slot untuk handoff
    }

    public bool IsClosed { get { lock (_lock) return _closed; } }

    /// <summary>Menutup channel. Setelah close, Send akan error, Receive akan drain lalu return false.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
        }
        // Lepas semua waiter untuk unblock.
        _items.Release(int.MaxValue / 2);
        _spaces.Release(int.MaxValue / 2);
    }

    public async Task SendAsync(T item, CancellationToken ct = default)
    {
        if (IsClosed) throw new InvalidOperationException("channel sudah ditutup");
        await _spaces.WaitAsync(ct).ConfigureAwait(false);
        lock (_lock)
        {
            if (_closed)
            {
                _spaces.Release();
                throw new InvalidOperationException("channel sudah ditutup");
            }
            _queue.Enqueue(item);
        }
        _items.Release();
    }

    public void Send(T item, CancellationToken ct = default) => SendAsync(item, ct).GetAwaiter().GetResult();

    /// <summary>
    /// Receive item. Return (false, default) jika channel ditutup dan kosong.
    /// </summary>
    public async Task<(bool ok, T value)> ReceiveAsync(CancellationToken ct = default)
    {
        while (true)
        {
            await _items.WaitAsync(ct).ConfigureAwait(false);
            lock (_lock)
            {
                if (_queue.Count > 0)
                {
                    var v = _queue.Dequeue();
                    _spaces.Release();
                    return (true, v);
                }
                if (_closed)
                {
                    return (false, default!);
                }
            }
        }
    }

    public (bool ok, T value) Receive(CancellationToken ct = default) => ReceiveAsync(ct).GetAwaiter().GetResult();
}

/// <summary>
/// Helper ala goroutine. Menjalankan task background dengan exception handling sederhana.
/// </summary>
public static class Go
{
    public static Task Run(Func<Task> fn)
    {
        return Task.Run(async () =>
        {
            try { await fn().ConfigureAwait(false); }
            catch (Exception ex) { Console.Error.WriteLine($"[goroutine error] {ex}"); }
        });
    }

    public static Task Run(Action fn)
    {
        return Task.Run(() =>
        {
            try { fn(); }
            catch (Exception ex) { Console.Error.WriteLine($"[goroutine error] {ex}"); }
        });
    }
}
