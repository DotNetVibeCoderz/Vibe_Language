using System.Text;

namespace CSJS.Core.Bundling;

/// <summary>
/// A conservative JavaScript minifier: it removes comments, indentation, and blank lines, and
/// the spaces between tokens that do not need one. It renames nothing and reorders nothing.
/// <para>
/// Line breaks between statements are kept (collapsed to one), not removed. Automatic semicolon
/// insertion makes a newline meaningful in places a token-level tool cannot see without a parser
/// — <c>return</c> followed by a newline, a line starting with <c>(</c> or <c>[</c> — so keeping
/// them is what makes this safe on any input the tokenizer understands. Most of the size win is
/// indentation and comments anyway.
/// </para>
/// </summary>
public static class JsMinifier
{
    /// <summary>Result of a minification, with the position map needed to rewrite a source map.</summary>
    public sealed class Result
    {
        public required string JavaScript { get; init; }

        // Per original line: (original column, new line, new column) for each token start, sorted.
        internal List<(int Column, int NewLine, int NewColumn)>[] TokenStarts { get; init; } = Array.Empty<List<(int, int, int)>>();

        /// <summary>
        /// Translates a position in the original text to the minified one. A position that is not
        /// a token start resolves to the next token on the same line; source map segments point at
        /// statement starts, which are always token starts, so in practice this is exact.
        /// </summary>
        public (int Line, int Column)? Translate(int line, int column)
        {
            if (line < 0 || line >= TokenStarts.Length) return null;
            var starts = TokenStarts[line];
            foreach (var start in starts)
                if (start.Column >= column) return (start.NewLine, start.NewColumn);
            return null;
        }
    }

    public static Result Minify(string source)
    {
        var tokens = JsTokenizer.Tokenize(source);
        var lines = new JsTokenizer.LineIndex(source);
        var starts = new List<(int, int, int)>[lines.LineCount];
        for (var i = 0; i < starts.Length; i++) starts[i] = new List<(int, int, int)>();

        var output = new StringBuilder(source.Length / 2);
        var newLine = 0;
        var lineStart = 0;
        JsToken? previous = null;
        var pendingNewline = false;

        foreach (var token in tokens)
        {
            if (token.IsComment)
            {
                // A comment that ended a line still separates what came before from what follows.
                pendingNewline |= token.NewlineBefore || token.Kind == JsTokenKind.LineComment;
                continue;
            }

            if (previous is { } p)
            {
                if (pendingNewline || token.NewlineBefore)
                {
                    output.Append('\n');
                    newLine++;
                    lineStart = output.Length;
                }
                else if (NeedsSpace(source, p, token))
                {
                    output.Append(' ');
                }
            }

            pendingNewline = false;

            var (originalLine, originalColumn) = lines.Locate(token.Start);
            starts[originalLine].Add((originalColumn, newLine, output.Length - lineStart));

            var text = token.Text(source);
            output.Append(text);

            // Templates and strings with line continuations carry their own newlines.
            var lastBreak = text.LastIndexOf('\n');
            if (lastBreak >= 0)
            {
                newLine += text.Count(c => c == '\n');
                lineStart = output.Length - (text.Length - lastBreak - 1);
            }

            previous = token;
        }

        if (output.Length > 0) output.Append('\n');
        return new Result { JavaScript = output.ToString(), TokenStarts = starts };
    }

    /// <summary>Whether two adjacent tokens would fuse into something else without a space.</summary>
    internal static bool NeedsSpace(string source, JsToken left, JsToken right)
    {
        var l = source[left.End - 1];
        var r = source[right.Start];

        // `return x`, `a in b`, `/re/g in`, `1 in`: word characters fuse into one word.
        if (JsTokenizer.IsWordPart(l) && JsTokenizer.IsWordPart(r)) return true;

        // `/re/ in x` would read `in` as the regex's flags.
        if (left.Kind == JsTokenKind.Regex && JsTokenizer.IsWordPart(r)) return true;

        // `a + +b`, `a - -b`, `a + ++b`: operators fuse into a different operator.
        if (l == '+' && r == '+') return true;
        if (l == '-' && r == '-') return true;

        // `a / /re/` would open a comment; so would a regex ending in `/` followed by `/` or `*`.
        if (l == '/' && r is '/' or '*') return true;

        // `1 .toString()` is a method call; `1.toString()` is a malformed number.
        if (left.Kind == JsTokenKind.Number && r == '.') return true;
        if (l == '.' && (r == '.' || char.IsDigit(r))) return true;

        // `<!--` and `-->` are HTML-like comments in classic scripts.
        if (l == '<' && r == '!') return true;
        if (l == '-' && r == '>') return true;

        return false;
    }
}
