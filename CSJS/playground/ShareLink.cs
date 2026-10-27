using System.IO.Compression;
using System.Text;

namespace CSJS.Playground;

/// <summary>
/// Encodes a program into a URL and back, so a bug report or a lesson is one link.
/// <para>
/// The code travels in the fragment (<c>#code=…</c>), which browsers never send to a server:
/// the Playground is static hosting, and sharing a link must not mean uploading the code.
/// Deflate keeps a typical sample to a few hundred characters; base64url keeps it URL-safe.
/// </para>
/// </summary>
public static class ShareLink
{
    private const string Key = "code=";

    /// <summary>The fragment for <paramref name="source"/>, without the leading <c>#</c>.</summary>
    public static string Encode(string source)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            deflate.Write(bytes, 0, bytes.Length);
        }

        var base64 = Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return Key + base64;
    }

    /// <summary>
    /// The program a URL carries, or null when it carries none or the fragment is damaged —
    /// a truncated link should open the default sample, not an error.
    /// </summary>
    public static string? Decode(string url)
    {
        var hash = url.IndexOf('#');
        if (hash < 0) return null;

        var fragment = url[(hash + 1)..];
        var start = fragment.IndexOf(Key, StringComparison.Ordinal);
        if (start < 0) return null;

        var encoded = fragment[(start + Key.Length)..];
        var end = encoded.IndexOf('&');
        if (end >= 0) encoded = encoded[..end];

        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');

            using var input = new MemoryStream(Convert.FromBase64String(base64));
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            return null;
        }
    }
}
