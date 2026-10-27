using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSJS.Core.Bundling;

/// <summary>One decoded source map segment: a generated position and the source position it came from.</summary>
public readonly record struct SourceMapSegment(int GeneratedColumn, int SourceIndex, int SourceLine, int SourceColumn);

/// <summary>
/// A decoded Source Map v3 document. <see cref="Emit.SourceMapBuilder"/> writes maps; this reads
/// them back, which the bundler needs to merge per-module maps and the tests need to check that
/// a generated line really lands on the C# line it claims to.
/// </summary>
public sealed class SourceMapDocument
{
    public List<string> Sources { get; } = new();
    public List<string?> SourcesContent { get; } = new();
    public string SourceRoot { get; set; } = "";

    /// <summary>Segments per generated line, 0-based.</summary>
    public List<List<SourceMapSegment>> Lines { get; } = new();

    public static SourceMapDocument Parse(string json)
    {
        var document = new SourceMapDocument();
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        if (root.TryGetProperty("sections", out _))
            throw new NotSupportedException("Indexed source maps (with \"sections\") are not supported here.");

        if (root.TryGetProperty("sourceRoot", out var sourceRoot) && sourceRoot.ValueKind == JsonValueKind.String)
            document.SourceRoot = sourceRoot.GetString() ?? "";

        if (root.TryGetProperty("sources", out var sources))
            foreach (var source in sources.EnumerateArray()) document.Sources.Add(source.GetString() ?? "");

        if (root.TryGetProperty("sourcesContent", out var contents))
            foreach (var content in contents.EnumerateArray())
                document.SourcesContent.Add(content.ValueKind == JsonValueKind.String ? content.GetString() : null);

        while (document.SourcesContent.Count < document.Sources.Count) document.SourcesContent.Add(null);

        var mappings = root.TryGetProperty("mappings", out var m) ? m.GetString() ?? "" : "";
        document.DecodeMappings(mappings);
        return document;
    }

    /// <summary>
    /// Finds the source position a generated position maps to: the last segment on that line at
    /// or before the column, which is how debuggers and Node's stack traces resolve it.
    /// </summary>
    public (string Source, int Line, int Column)? OriginalPositionFor(int generatedLine, int generatedColumn)
    {
        if (generatedLine < 0 || generatedLine >= Lines.Count) return null;

        SourceMapSegment? best = null;
        foreach (var segment in Lines[generatedLine])
        {
            if (segment.GeneratedColumn > generatedColumn) break;
            best = segment;
        }

        if (best is not { } s || s.SourceIndex < 0) return null;
        return (Sources[s.SourceIndex], s.SourceLine, s.SourceColumn);
    }

    private void DecodeMappings(string mappings)
    {
        var sourceIndex = 0;
        var sourceLine = 0;
        var sourceColumn = 0;
        var line = new List<SourceMapSegment>();
        var generatedColumn = 0;
        var position = 0;

        while (position <= mappings.Length)
        {
            if (position == mappings.Length || mappings[position] == ';')
            {
                Lines.Add(line);
                line = new List<SourceMapSegment>();
                generatedColumn = 0;
                position++;
                continue;
            }

            if (mappings[position] == ',')
            {
                position++;
                continue;
            }

            var fields = new List<int>(5);
            while (position < mappings.Length && mappings[position] is not (',' or ';'))
                fields.Add(ReadVlq(mappings, ref position));

            generatedColumn += fields[0];
            if (fields.Count >= 4)
            {
                sourceIndex += fields[1];
                sourceLine += fields[2];
                sourceColumn += fields[3];
                line.Add(new SourceMapSegment(generatedColumn, sourceIndex, sourceLine, sourceColumn));
            }
            else
            {
                line.Add(new SourceMapSegment(generatedColumn, -1, 0, 0));
            }
        }
    }

    private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private static int ReadVlq(string text, ref int position)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var digit = Base64Alphabet.IndexOf(text[position++]);
            if (digit < 0) throw new FormatException("Invalid base64 VLQ digit in source map.");
            result += (digit & 0b11111) << shift;
            if ((digit & 0b100000) == 0) break;
            shift += 5;
        }

        var negative = (result & 1) == 1;
        result >>= 1;
        return negative ? -result : result;
    }

    /// <summary>
    /// Returns <paramref name="json"/> with its <c>sourceRoot</c> replaced. The emitter names
    /// sources by file name only, which a consumer resolves against the map's own directory; the
    /// build knows where the sources really live and says so here, so a stack trace or debugger
    /// points at the actual <c>.cs</c> file.
    /// </summary>
    public static string WithSourceRoot(string json, string sourceRoot)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["sourceRoot"] = sourceRoot;
        return node.ToJsonString();
    }
}
