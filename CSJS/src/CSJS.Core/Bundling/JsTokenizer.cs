namespace CSJS.Core.Bundling;

/// <summary>Lexical class of a <see cref="JsToken"/>.</summary>
public enum JsTokenKind
{
    /// <summary>An identifier or keyword, including <c>#private</c> names.</summary>
    Word,
    Number,
    String,
    /// <summary>A whole template literal, its <c>${}</c> substitutions included.</summary>
    Template,
    Regex,
    Punctuator,
    LineComment,
    BlockComment,
}

/// <summary>
/// One token of JavaScript source. <see cref="NewlineBefore"/> records whether a line terminator
/// separated it from the previous token, which is what automatic semicolon insertion keys on.
/// </summary>
public readonly record struct JsToken(JsTokenKind Kind, int Start, int End, bool NewlineBefore)
{
    public int Length => End - Start;

    public string Text(string source) => source.Substring(Start, End - Start);

    public bool IsComment => Kind is JsTokenKind.LineComment or JsTokenKind.BlockComment;
}

/// <summary>
/// A JavaScript tokenizer just precise enough to rewrite source safely: it knows where strings,
/// template literals (with nested <c>${}</c>), regular expression literals, and comments begin
/// and end, which is everything a minifier or bundler needs to avoid corrupting code it does not
/// otherwise understand.
/// <para>
/// The one genuinely ambiguous construct is <c>/</c>, which is division after an expression and
/// a regex literal anywhere else. The tokenizer decides from the previous significant token, the
/// same heuristic every hand-written JavaScript lexer uses.
/// </para>
/// </summary>
public sealed class JsTokenizer
{
    private readonly string _source;
    private int _position;

    private JsTokenizer(string source) => _source = source;

    /// <summary>Tokenizes <paramref name="source"/>, comments included.</summary>
    public static List<JsToken> Tokenize(string source)
    {
        var tokenizer = new JsTokenizer(source);
        var tokens = new List<JsToken>();
        tokenizer.ReadTokens(tokens, stopAtUnbalancedBrace: false);
        return tokens;
    }

    private static readonly string[] Punctuators =
    {
        ">>>=", "...", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=",
        "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=",
        "%=", "&=", "|=", "^=", "**", "<<", ">>",
    };

    /// <summary>Keywords after which a <c>/</c> starts a regex rather than dividing.</summary>
    private static readonly HashSet<string> RegexAfterKeywords = new(StringComparer.Ordinal)
    {
        "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case",
        "do", "else", "yield", "await",
    };

    /// <summary>
    /// Reads tokens until the end of input or, inside a template substitution, until the brace
    /// that closes it. Returns with <see cref="_position"/> on that brace.
    /// </summary>
    private void ReadTokens(List<JsToken> into, bool stopAtUnbalancedBrace)
    {
        var depth = 0;
        JsToken? previous = null;
        var newline = false;

        while (true)
        {
            newline |= SkipWhitespace();
            if (_position >= _source.Length) return;

            var c = _source[_position];
            if (stopAtUnbalancedBrace && c == '}' && depth == 0) return;

            var start = _position;
            JsTokenKind kind;

            if (c == '/' && Peek(1) == '/')
            {
                while (_position < _source.Length && !IsLineTerminator(_source[_position])) _position++;
                kind = JsTokenKind.LineComment;
            }
            else if (c == '/' && Peek(1) == '*')
            {
                var close = _source.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                _position = close < 0 ? _source.Length : close + 2;
                kind = JsTokenKind.BlockComment;
            }
            else if (c is '"' or '\'')
            {
                ReadString(c);
                kind = JsTokenKind.String;
            }
            else if (c == '`')
            {
                ReadTemplate();
                kind = JsTokenKind.Template;
            }
            else if (IsDigit(c) || (c == '.' && IsDigit(Peek(1))))
            {
                ReadNumber();
                kind = JsTokenKind.Number;
            }
            else if (IsWordStart(c))
            {
                while (_position < _source.Length && IsWordPart(_source[_position])) _position++;
                kind = JsTokenKind.Word;
            }
            else if (c == '/' && RegexAllowedAfter(previous))
            {
                ReadRegex();
                kind = JsTokenKind.Regex;
            }
            else
            {
                ReadPunctuator();
                kind = JsTokenKind.Punctuator;
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }

            var token = new JsToken(kind, start, _position, newline);
            into.Add(token);

            // A block comment spanning lines separates its neighbours exactly as a newline does.
            newline = kind == JsTokenKind.BlockComment && ContainsLineTerminator(start, _position);
            if (!token.IsComment) previous = token;
        }
    }

    private bool RegexAllowedAfter(JsToken? previous)
    {
        if (previous is not { } p) return true;

        switch (p.Kind)
        {
            case JsTokenKind.Number:
            case JsTokenKind.String:
            case JsTokenKind.Template:
            case JsTokenKind.Regex:
                return false;
            case JsTokenKind.Word:
                return RegexAfterKeywords.Contains(p.Text(_source));
            default:
                // After `)` or `]` a slash divides. After `}` it is far more likely a statement
                // starting with a regex than an object literal being divided, so it starts one.
                var text = p.Text(_source);
                return text is not (")" or "]" or "++" or "--");
        }
    }

    private void ReadString(char quote)
    {
        _position++;
        while (_position < _source.Length)
        {
            var c = _source[_position++];
            if (c == '\\') { _position++; continue; }
            if (c == quote) return;
        }
    }

    private void ReadTemplate()
    {
        _position++; // opening backtick
        while (_position < _source.Length)
        {
            var c = _source[_position];
            if (c == '\\') { _position += 2; continue; }
            if (c == '`') { _position++; return; }

            if (c == '$' && Peek(1) == '{')
            {
                _position += 2;
                // The substitution is ordinary JavaScript — strings, nested templates, regexes,
                // and object literals included — so it is tokenized properly to find its end.
                ReadTokens(new List<JsToken>(), stopAtUnbalancedBrace: true);
                if (_position < _source.Length) _position++; // closing brace
                continue;
            }

            _position++;
        }
    }

    private void ReadRegex()
    {
        _position++; // opening slash
        var inClass = false;
        while (_position < _source.Length)
        {
            var c = _source[_position++];
            if (c == '\\') { _position++; continue; }
            if (IsLineTerminator(c)) break; // malformed; stop rather than swallow the file
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            else if (c == '/' && !inClass) break;
        }

        // Flags.
        while (_position < _source.Length && IsWordPart(_source[_position])) _position++;
    }

    private void ReadNumber()
    {
        var start = _position;
        var hex = _source[start] == '0' && Peek(1) is 'x' or 'X';

        while (_position < _source.Length)
        {
            var c = _source[_position];
            if (IsWordPart(c) || c == '.')
            {
                _position++;
                continue;
            }

            // Exponent sign: 1e-5, 2E+3. In a hex literal `e` is a digit, so `0x1e-1` subtracts.
            if (c is '+' or '-' && !hex && _source[_position - 1] is 'e' or 'E')
            {
                _position++;
                continue;
            }

            break;
        }
    }

    private void ReadPunctuator()
    {
        foreach (var candidate in Punctuators)
        {
            if (string.CompareOrdinal(_source, _position, candidate, 0, candidate.Length) != 0) continue;

            // `?.5` is a conditional followed by a number, not optional chaining.
            if (candidate == "?." && IsDigit(Peek(2))) continue;

            _position += candidate.Length;
            return;
        }

        _position++;
    }

    /// <summary>Skips whitespace and reports whether a line terminator was among it.</summary>
    private bool SkipWhitespace()
    {
        var newline = false;
        while (_position < _source.Length)
        {
            var c = _source[_position];
            if (IsLineTerminator(c)) newline = true;
            else if (!char.IsWhiteSpace(c) && c != (char)0xFEFF) break;
            _position++;
        }
        return newline;
    }

    private bool ContainsLineTerminator(int start, int end)
    {
        for (var i = start; i < end; i++)
            if (IsLineTerminator(_source[i])) return true;
        return false;
    }

    private char Peek(int offset)
        => _position + offset < _source.Length ? _source[_position + offset] : '\0';

    internal static bool IsLineTerminator(char c) => c is '\n' or '\r' or (char)0x2028 or (char)0x2029;

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    internal static bool IsWordStart(char c)
        => char.IsLetter(c) || c is '_' or '$' or '#' or '\\' || c > 0x7F && !char.IsWhiteSpace(c);

    internal static bool IsWordPart(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '\\' or (char)0x200C or (char)0x200D
           || c > 0x7F && !char.IsWhiteSpace(c);

    /// <summary>Precomputed line starts, for turning offsets into line/column pairs.</summary>
    public sealed class LineIndex
    {
        private readonly List<int> _starts = new() { 0 };

        public LineIndex(string source)
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n') continue;
                if (IsLineTerminator(c)) _starts.Add(i + 1);
            }
        }

        public int LineCount => _starts.Count;

        public int LineStart(int line) => _starts[line];

        public (int Line, int Column) Locate(int offset)
        {
            var index = _starts.BinarySearch(offset);
            if (index < 0) index = ~index - 1;
            return (index, offset - _starts[index]);
        }
    }
}
