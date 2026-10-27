using System.Text;

namespace CSJS.Core.Emit;

/// <summary>
/// Builds a Source Map v3 document. The VSCode debug bridge relies on this to step from
/// C# source lines onto the JavaScript actually running in the browser, so every statement
/// the emitter writes registers a mapping here.
/// </summary>
public sealed class SourceMapBuilder
{
    private readonly List<string> _sources = new();
    private readonly List<string?> _sourcesContent = new();
    private readonly List<List<Segment>> _lines = new();

    private readonly record struct Segment(int GeneratedColumn, int SourceIndex, int SourceLine, int SourceColumn);

    /// <summary>Written as the map's <c>sourceRoot</c>, which consumers prefix onto every source path.</summary>
    public string SourceRoot { get; set; } = "";

    /// <summary>Registers a source file and returns its index.</summary>
    public int AddSource(string path, string? content)
    {
        var idx = _sources.IndexOf(path);
        if (idx >= 0) return idx;
        _sources.Add(path);
        _sourcesContent.Add(content);
        return _sources.Count - 1;
    }

    /// <summary>
    /// Records that <paramref name="generatedLine"/>/<paramref name="generatedColumn"/> in the output
    /// came from a 0-based position in a source file.
    /// </summary>
    public void AddMapping(int generatedLine, int generatedColumn, int sourceIndex, int sourceLine, int sourceColumn)
    {
        while (_lines.Count <= generatedLine) _lines.Add(new List<Segment>());

        var line = _lines[generatedLine];
        // Only the first mapping at a given output column matters; later ones would shadow it.
        if (line.Count > 0 && line[^1].GeneratedColumn == generatedColumn) return;
        line.Add(new Segment(generatedColumn, sourceIndex, sourceLine, sourceColumn));
    }

    /// <summary>
    /// Shifts every mapping down by <paramref name="count"/> lines. The emitter discovers which
    /// runtime helpers and JS modules it needs only while walking the body, so the import block is
    /// prepended afterwards — and the map has to move with it.
    /// </summary>
    public void PrependLines(int count)
    {
        if (count <= 0) return;
        for (var i = 0; i < count; i++) _lines.Insert(0, new List<Segment>());
    }

    /// <summary>Serialises the map as JSON.</summary>
    public string Build(string generatedFileName)
    {
        var sb = new StringBuilder();
        sb.Append("{\"version\":3,\"file\":").Append(JsonString(generatedFileName));
        sb.Append(",\"sourceRoot\":").Append(JsonString(SourceRoot)).Append(",\"sources\":[");
        for (var i = 0; i < _sources.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonString(_sources[i].Replace('\\', '/')));
        }
        sb.Append(']');

        if (_sourcesContent.Any(c => c is not null))
        {
            sb.Append(",\"sourcesContent\":[");
            for (var i = 0; i < _sourcesContent.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_sourcesContent[i] is { } c ? JsonString(c) : "null");
            }
            sb.Append(']');
        }

        sb.Append(",\"names\":[],\"mappings\":\"").Append(EncodeMappings()).Append("\"}");
        return sb.ToString();
    }

    private string EncodeMappings()
    {
        var sb = new StringBuilder();
        // These four deltas persist across lines (except generated column, which resets per line).
        var prevSourceIndex = 0;
        var prevSourceLine = 0;
        var prevSourceColumn = 0;

        for (var lineNo = 0; lineNo < _lines.Count; lineNo++)
        {
            if (lineNo > 0) sb.Append(';');

            var prevGeneratedColumn = 0;
            var segments = _lines[lineNo];
            for (var i = 0; i < segments.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var s = segments[i];

                AppendVlq(sb, s.GeneratedColumn - prevGeneratedColumn);
                prevGeneratedColumn = s.GeneratedColumn;

                AppendVlq(sb, s.SourceIndex - prevSourceIndex);
                prevSourceIndex = s.SourceIndex;

                AppendVlq(sb, s.SourceLine - prevSourceLine);
                prevSourceLine = s.SourceLine;

                AppendVlq(sb, s.SourceColumn - prevSourceColumn);
                prevSourceColumn = s.SourceColumn;
            }
        }

        return sb.ToString();
    }

    private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private static void AppendVlq(StringBuilder sb, int value)
    {
        // Base64 VLQ: sign goes in the low bit, then 5-bit groups with a continuation flag.
        var vlq = value < 0 ? ((-value) << 1) | 1 : value << 1;
        do
        {
            var digit = vlq & 0b11111;
            vlq >>= 5;
            if (vlq > 0) digit |= 0b100000;
            sb.Append(Base64Alphabet[digit]);
        }
        while (vlq > 0);
    }

    private static string JsonString(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20 || ch > 0x7E) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
