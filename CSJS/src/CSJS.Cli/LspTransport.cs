using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSJS.Cli;

/// <summary>
/// JSON-RPC over the LSP base protocol: each message is a <c>Content-Length</c> header block,
/// a blank line, and that many bytes of UTF-8 JSON.
/// <para>
/// Small enough to own outright. A framework would bring its own threading model and
/// serializer, and the protocol surface the server uses is a handful of methods.
/// </para>
/// </summary>
public sealed class LspTransport
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _bufferStart;
    private int _bufferEnd;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    public LspTransport(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Reads the next message, or returns null when the input has ended.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var contentLength = -1;

            // Header block: lines until an empty one. Content-Type is optional and always UTF-8
            // in practice, so only the length matters.
            while (true)
            {
                var line = await ReadHeaderLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) return null;
                if (line.Length == 0)
                {
                    if (contentLength >= 0) break;
                    continue; // stray blank line between messages
                }

                var colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var name = line[..colon].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line[(colon + 1)..].Trim(), out var length))
                {
                    contentLength = length;
                }
            }

            var body = new byte[contentLength];
            if (!await ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false)) return null;

            try
            {
                if (JsonNode.Parse(body) is JsonObject message) return message;
            }
            catch (JsonException)
            {
                // A malformed message cannot be answered — it has no readable id — so it is
                // skipped rather than taking the whole session down.
            }
        }
    }

    /// <summary>Writes one message. Safe to call from several threads at once.</summary>
    public async Task WriteAsync(JsonNode message, CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString(WriteOptions));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_bufferStart == _bufferEnd && !await FillAsync(cancellationToken).ConfigureAwait(false))
                return line.Length > 0 ? line.ToString() : null;

            var b = _buffer[_bufferStart++];
            if (b == (byte)'\n')
            {
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                return line.ToString();
            }

            line.Append((char)b);
        }
    }

    private async Task<bool> ReadExactlyAsync(byte[] target, CancellationToken cancellationToken)
    {
        var written = 0;
        while (written < target.Length)
        {
            if (_bufferStart == _bufferEnd && !await FillAsync(cancellationToken).ConfigureAwait(false))
                return false;

            var count = Math.Min(target.Length - written, _bufferEnd - _bufferStart);
            Array.Copy(_buffer, _bufferStart, target, written, count);
            _bufferStart += count;
            written += count;
        }

        return true;
    }

    private async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        _bufferStart = 0;
        _bufferEnd = await _input.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return _bufferEnd > 0;
    }
}
