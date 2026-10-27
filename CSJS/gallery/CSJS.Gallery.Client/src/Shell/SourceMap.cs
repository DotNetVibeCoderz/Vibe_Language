using System.Text.Json;

namespace CSJS.Gallery.Client.Shell;

/// <summary>The fields of a v3 source map the gallery reads.</summary>
public class SourceMapFile
{
    public int Version { get; set; }
    public List<string> Sources { get; set; } = new();
    public List<string> SourcesContent { get; set; } = new();
    public string Mappings { get; set; } = "";
}

/// <summary>
/// A decoded source map: which C# lines each JavaScript line came from, and the reverse.
/// <para>
/// The <c>mappings</c> field is base64 VLQ — groups of 5-bit digits with a continuation bit, each
/// value relative to the previous one. Decoding it here, in C# that is itself running as
/// JavaScript, is what lets the seam draw a thread from any C# line to the code it became.
/// </para>
/// </summary>
public sealed class SourceMap
{
    private const string Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>The C# the map was built from, from <c>sourcesContent</c>.</summary>
    public string Source { get; private set; } = "";

    /// <summary>The C# file name.</summary>
    public string SourceName { get; private set; } = "";

    private readonly Dictionary<int, List<int>> _generatedToSource = new();
    private readonly Dictionary<int, List<int>> _sourceToGenerated = new();

    public static SourceMap Parse(string json)
    {
        var file = JsonSerializer.Deserialize<SourceMapFile>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        })!;

        var map = new SourceMap
        {
            Source = file.SourcesContent.Count > 0 ? file.SourcesContent[0] : "",
            SourceName = file.Sources.Count > 0 ? file.Sources[0] : "",
        };
        map.Decode(file.Mappings);
        return map;
    }

    /// <summary>The 0-based C# lines a 0-based JavaScript line came from.</summary>
    public List<int> SourceLinesFor(int generatedLine)
        => _generatedToSource.TryGetValue(generatedLine, out var lines) ? lines : new List<int>();

    /// <summary>The 0-based JavaScript lines a 0-based C# line became.</summary>
    public List<int> GeneratedLinesFor(int sourceLine)
        => _sourceToGenerated.TryGetValue(sourceLine, out var lines) ? lines : new List<int>();

    /// <summary>Every C# line with at least one mapping, in order.</summary>
    public List<int> MappedSourceLines() => _sourceToGenerated.Keys.OrderBy(k => k).ToList();

    private void Decode(string mappings)
    {
        // Source index, source line, and source column carry over from segment to segment and
        // from line to line; only the generated column resets at each ';'.
        var sourceLine = 0;
        var generatedLine = 0;

        foreach (var line in mappings.Split(';'))
        {
            if (line.Length > 0)
            {
                foreach (var segment in line.Split(','))
                {
                    var values = DecodeSegment(segment);
                    if (values.Count < 4) continue;
                    sourceLine += values[2];
                    Link(generatedLine, sourceLine);
                }
            }
            generatedLine++;
        }
    }

    private void Link(int generated, int source)
    {
        if (!_generatedToSource.TryGetValue(generated, out var sources))
        {
            sources = new List<int>();
            _generatedToSource[generated] = sources;
        }
        if (!sources.Contains(source)) sources.Add(source);

        if (!_sourceToGenerated.TryGetValue(source, out var generatedLines))
        {
            generatedLines = new List<int>();
            _sourceToGenerated[source] = generatedLines;
        }
        if (!generatedLines.Contains(generated)) generatedLines.Add(generated);
    }

    /// <summary>One segment's VLQ values: 5-bit digits, low bits first, bit 6 means "more".</summary>
    private static List<int> DecodeSegment(string segment)
    {
        var values = new List<int>();
        var value = 0;
        var shift = 0;

        foreach (var c in segment)
        {
            var digit = Base64.IndexOf(c);
            if (digit < 0) return values;

            value += (digit & 31) << shift;
            if ((digit & 32) != 0)
            {
                shift += 5;
                continue;
            }

            // The lowest bit is the sign.
            values.Add((value & 1) == 1 ? -(value >> 1) : value >> 1);
            value = 0;
            shift = 0;
        }

        return values;
    }
}
