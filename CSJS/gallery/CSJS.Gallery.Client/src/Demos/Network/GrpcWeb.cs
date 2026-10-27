using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Network;

// gRPC-Web by hand: the protobuf encoding and the five-byte framing, small enough to read whole.
// A real project would generate this from the .proto; here it is written out so the wire format
// is something you can see rather than something a library hides.

/// <summary>Protobuf wire types — the low three bits of every field tag.</summary>
public static class WireType
{
    public const int Varint = 0;
    public const int Fixed64 = 1;
    public const int LengthDelimited = 2;
    public const int Fixed32 = 5;
}

/// <summary>
/// Writes protobuf fields. A field is a tag — <c>(field number &lt;&lt; 3) | wire type</c>, as a
/// varint — then its value: a varint for integers, a varint length and the bytes for strings.
/// </summary>
public sealed class ProtoWriter
{
    private static readonly TextEncoder Utf8 = new();
    private readonly List<int> _bytes = new();

    /// <summary>Base-128, least significant group first, high bit set on every byte but the last.</summary>
    public void WriteVarint(int value)
    {
        while (value >= 128)
        {
            _bytes.Add(value % 128 + 128);
            value /= 128;
        }
        _bytes.Add(value);
    }

    public void WriteTag(int field, int wireType) => WriteVarint(field * 8 + wireType);

    public void WriteInt32(int field, int value)
    {
        // proto3 leaves default values off the wire entirely.
        if (value == 0) return;
        WriteTag(field, WireType.Varint);
        WriteVarint(value);
    }

    public void WriteString(int field, string value)
    {
        if (value.Length == 0) return;
        var bytes = Utf8.Encode(value);
        WriteTag(field, WireType.LengthDelimited);
        WriteVarint(bytes.Length);
        for (var i = 0; i < bytes.Length; i++) _bytes.Add(bytes[i]);
    }

    public Uint8Array ToBytes() => new Uint8Array(_bytes.ToArray());
}

/// <summary>Reads protobuf fields back, skipping any it does not know — as protobuf requires.</summary>
public sealed class ProtoReader
{
    private static readonly TextDecoder Utf8 = new();
    private readonly Uint8Array _data;
    private int _position;

    public ProtoReader(Uint8Array data) => _data = data;

    public bool AtEnd => _position >= _data.Length;

    public int ReadVarint()
    {
        var value = 0;
        var scale = 1;
        while (true)
        {
            var b = _data[_position++];
            value += (b % 128) * scale;
            if (b < 128) return value;
            scale *= 128;
        }
    }

    /// <summary>The next tag, split into field number and wire type.</summary>
    public (int Field, int WireType) ReadTag()
    {
        var tag = ReadVarint();
        return (tag / 8, tag % 8);
    }

    public string ReadString()
    {
        var length = ReadVarint();
        var text = Utf8.Decode(_data.Subarray(_position, _position + length));
        _position += length;
        return text;
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case WireType.Varint: ReadVarint(); break;
            case WireType.Fixed64: _position += 8; break;
            case WireType.LengthDelimited: _position += ReadVarint(); break;
            case WireType.Fixed32: _position += 4; break;
            default: throw new InvalidOperationException($"Unknown wire type {wireType}.");
        }
    }
}

/// <summary>One gRPC-Web frame: a flag byte, a four-byte big-endian length, and the payload.</summary>
public sealed class GrpcFrame
{
    public const int Data = 0x00;
    public const int Trailer = 0x80;

    public GrpcFrame(int flag, Uint8Array payload, Uint8Array raw)
    {
        Flag = flag;
        Payload = payload;
        Raw = raw;
    }

    public int Flag { get; }
    public Uint8Array Payload { get; }

    /// <summary>The frame exactly as it crossed the wire, header included.</summary>
    public Uint8Array Raw { get; }

    public bool IsTrailer => Flag == Trailer;

    /// <summary>Frames a message: <c>00</c>, then its length in four bytes, then the message.</summary>
    public static Uint8Array Wrap(Uint8Array message)
    {
        var frame = new Uint8Array(5 + message.Length);
        var length = message.Length;
        frame[0] = Data;
        frame[1] = length / 16777216 % 256;
        frame[2] = length / 65536 % 256;
        frame[3] = length / 256 % 256;
        frame[4] = length % 256;
        frame.Set(message, 5);
        return frame;
    }

    /// <summary>A trailer frame's payload is HTTP-style text: <c>grpc-status:0\r\ngrpc-message:…</c>.</summary>
    public Dictionary<string, string> ReadTrailers()
    {
        var text = new TextDecoder().Decode(Payload);
        var trailers = new Dictionary<string, string>();
        foreach (var line in text.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) trailers[line.Substring(0, colon).Trim().ToLowerInvariant()] = line.Substring(colon + 1).Trim();
        }
        return trailers;
    }
}

/// <summary>
/// Cuts a byte stream into frames. Network chunks do not respect frame boundaries — one chunk may
/// hold half a frame or three — so bytes are buffered until a whole frame is there.
/// </summary>
public sealed class GrpcFrameReader
{
    private Uint8Array _buffer = new(0);

    public List<GrpcFrame> Push(Uint8Array chunk)
    {
        var joined = new Uint8Array(_buffer.Length + chunk.Length);
        joined.Set(_buffer, 0);
        joined.Set(chunk, _buffer.Length);
        _buffer = joined;

        var frames = new List<GrpcFrame>();
        while (_buffer.Length >= 5)
        {
            var length = _buffer[1] * 16777216 + _buffer[2] * 65536 + _buffer[3] * 256 + _buffer[4];
            if (_buffer.Length < 5 + length) break;
            frames.Add(new GrpcFrame(_buffer[0], _buffer.Slice(5, 5 + length), _buffer.Slice(0, 5 + length)));
            _buffer = _buffer.Slice(5 + length, _buffer.Length);
        }
        return frames;
    }

    /// <summary>Bytes left over once the stream ended — a truncated frame, if not zero.</summary>
    public int Pending => _buffer.Length;
}

/// <summary>The messages of <c>gallery.proto</c>, encoded and decoded field by field.</summary>
public static class GreeterMessages
{
    public static Uint8Array HelloRequest(string name)
    {
        var writer = new ProtoWriter();
        writer.WriteString(1, name);
        return writer.ToBytes();
    }

    public static Uint8Array CountdownRequest(int from)
    {
        var writer = new ProtoWriter();
        writer.WriteInt32(1, from);
        return writer.ToBytes();
    }

    public static (string Message, int Length) HelloReply(Uint8Array bytes)
    {
        var reader = new ProtoReader(bytes);
        var message = "";
        var length = 0;
        while (!reader.AtEnd)
        {
            var (field, wire) = reader.ReadTag();
            if (field == 1 && wire == WireType.LengthDelimited) message = reader.ReadString();
            else if (field == 2 && wire == WireType.Varint) length = reader.ReadVarint();
            else reader.Skip(wire);
        }
        return (message, length);
    }

    public static (int Remaining, string Label) Tick(Uint8Array bytes)
    {
        var reader = new ProtoReader(bytes);
        var remaining = 0;
        var label = "";
        while (!reader.AtEnd)
        {
            var (field, wire) = reader.ReadTag();
            if (field == 1 && wire == WireType.Varint) remaining = reader.ReadVarint();
            else if (field == 2 && wire == WireType.LengthDelimited) label = reader.ReadString();
            else reader.Skip(wire);
        }
        return (remaining, label);
    }
}

/// <summary>Formats bytes as hex, the way the demo shows frames.</summary>
public static class Hex
{
    public static string Of(Uint8Array bytes, int start, int end)
    {
        var parts = new List<string>();
        for (var i = start; i < end; i++) parts.Add(bytes[i].ToString("x2"));
        return string.Join(" ", parts);
    }

    /// <summary>
    /// <c>grpc-message</c> is percent-encoded. <c>Uri.UnescapeDataString</c> has no CSJS mapping —
    /// <c>decodeURIComponent</c> throws on a malformed escape where .NET leaves it alone, so it is
    /// not a faithful one — and the server only ever sends well-formed ones, so call it directly.
    /// </summary>
    [JsTemplate("decodeURIComponent({0})")]
    public static string DecodeUriComponent(string text) => throw new JsExternException();
}
