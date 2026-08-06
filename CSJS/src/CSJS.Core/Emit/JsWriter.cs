using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSJS.Core.Emit;

/// <summary>
/// Buffered writer for generated JavaScript. Tracks indentation and the current output
/// position so <see cref="SourceMapBuilder"/> can be fed without the emitter doing bookkeeping.
/// </summary>
public sealed class JsWriter
{
    private readonly StringBuilder _sb = new();
    private readonly string _indentUnit;
    private int _depth;
    private bool _atLineStart = true;

    private int _line;
    private int _column;

    public JsWriter(string indentUnit = "  ") => _indentUnit = indentUnit;

    /// <summary>Source map under construction, or null when maps are disabled.</summary>
    public SourceMapBuilder? SourceMap { get; init; }

    /// <summary>Index of the current file within <see cref="SourceMap"/>.</summary>
    public int SourceIndex { get; set; }

    public int Line => _line;
    public int Column => _column;

    public void Indent() => _depth++;

    public void Outdent()
    {
        if (_depth > 0) _depth--;
    }

    /// <summary>Runs <paramref name="body"/> one indentation level deeper.</summary>
    public void InBlock(Action body)
    {
        Indent();
        body();
        Outdent();
    }

    public void Write(string text)
    {
        if (text.Length == 0) return;
        EnsureIndent();
        AppendTracked(text);
    }

    public void Write(char ch)
    {
        EnsureIndent();
        AppendTracked(ch.ToString());
    }

    /// <summary>Writes text and terminates the line.</summary>
    public void WriteLine(string text)
    {
        Write(text);
        WriteLine();
    }

    public void WriteLine()
    {
        _sb.Append('\n');
        _line++;
        _column = 0;
        _atLineStart = true;
    }

    /// <summary>Writes a blank line unless output already ends with one.</summary>
    public void BlankLine()
    {
        if (_sb.Length == 0) return;
        var text = _sb.ToString();
        if (text.EndsWith("\n\n", StringComparison.Ordinal)) return;
        if (!text.EndsWith('\n')) WriteLine();
        WriteLine();
    }

    /// <summary>
    /// Records that whatever is written next originated at <paramref name="location"/> in C#.
    /// Call immediately before emitting a statement or expression worth stepping onto.
    /// </summary>
    public void MapTo(Location location)
    {
        if (SourceMap is null || !location.IsInSource) return;
        var span = location.GetLineSpan().StartLinePosition;
        MapTo(span);
    }

    public void MapTo(LinePosition position)
    {
        if (SourceMap is null) return;
        // The mapping must point at the column the text will land on, which for a fresh
        // line is after the indentation we have not written yet.
        var column = _atLineStart ? _depth * _indentUnit.Length : _column;
        SourceMap.AddMapping(_line, column, SourceIndex, position.Line, position.Character);
    }

    private void EnsureIndent()
    {
        if (!_atLineStart) return;
        _atLineStart = false;
        for (var i = 0; i < _depth; i++)
        {
            _sb.Append(_indentUnit);
            _column += _indentUnit.Length;
        }
    }

    private void AppendTracked(string text)
    {
        _sb.Append(text);
        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            _column += text.Length;
        }
        else
        {
            _line += text.Count(c => c == '\n');
            _column = text.Length - lastNewline - 1;
            _atLineStart = _column == 0;
        }
    }

    /// <summary>Captures the current output as a nested writer would see it. Used for lookahead.</summary>
    public int Length => _sb.Length;

    /// <summary>Removes everything written after <paramref name="length"/>. Used to undo speculative output.</summary>
    public void Rewind(int length)
    {
        if (length >= _sb.Length) return;
        _sb.Length = length;
        var text = _sb.ToString();
        _line = text.Count(c => c == '\n');
        var lastNewline = text.LastIndexOf('\n');
        _column = text.Length - lastNewline - 1;
        _atLineStart = _column == 0;
    }

    public override string ToString() => _sb.ToString();
}
