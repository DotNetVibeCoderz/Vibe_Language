using System.Text;

namespace CSJS.Core.Bindings;

internal enum TsTokenKind
{
    Identifier,
    String,
    Number,
    Template,
    Punctuation,
    EndOfFile,
}

/// <summary>
/// One token of a TypeScript declaration file.
/// <para>
/// <see cref="Doc"/> carries the JSDoc block that immediately preceded the token, so the parser
/// can attach documentation to whatever declaration starts here without a second pass.
/// <see cref="NewLineBefore"/> records automatic-semicolon positions, which declaration files
/// rely on more often than one would hope.
/// </para>
/// </summary>
internal readonly record struct TsToken(TsTokenKind Kind, string Text, int Position, bool NewLineBefore, string? Doc)
{
    public bool Is(string text) => Kind is TsTokenKind.Punctuation or TsTokenKind.Identifier && Text == text;
    public bool IsIdentifier => Kind == TsTokenKind.Identifier;
    public bool IsEnd => Kind == TsTokenKind.EndOfFile;
    public override string ToString() => Text;
}

/// <summary>
/// Tokenizes the declaration subset of TypeScript.
/// <para>
/// Declaration files contain no statements or expressions beyond literals, so the lexer can stay
/// small: no regular expressions, no division, and template literals only in type position. A
/// closing angle bracket is always a single token, which is what keeps <c>Map&lt;K, Set&lt;V&gt;&gt;</c>
/// from lexing as a shift operator.
/// </para>
/// </summary>
internal static class TsLexer
{
    private static readonly string[] MultiCharPunctuation = { "...", "=>", "?.", "??" };

    public static List<TsToken> Tokenize(string text)
    {
        var tokens = new List<TsToken>();
        var i = 0;
        var newLine = true;
        string? doc = null;

        while (true)
        {
            // Trivia: whitespace and comments. A JSDoc block is remembered for the next token.
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\n')
                {
                    newLine = true;
                    i++;
                }
                else if (char.IsWhiteSpace(c) || c == '﻿')
                {
                    i++;
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) end = text.Length - 2;
                    var body = text.Substring(i, end + 2 - i);
                    if (body.StartsWith("/**", StringComparison.Ordinal) && body != "/**/")
                        doc = body;
                    if (body.Contains('\n')) newLine = true;
                    i = end + 2;
                }
                else
                {
                    break;
                }
            }

            if (i >= text.Length)
            {
                tokens.Add(new TsToken(TsTokenKind.EndOfFile, "", text.Length, true, null));
                return tokens;
            }

            var start = i;
            var ch = text[i];
            TsTokenKind kind;
            string value;

            if (IsIdentifierStart(ch))
            {
                i++;
                while (i < text.Length && IsIdentifierPart(text[i])) i++;
                kind = TsTokenKind.Identifier;
                value = text[start..i];
            }
            else if (ch is '"' or '\'')
            {
                value = ReadString(text, ref i, ch);
                kind = TsTokenKind.String;
            }
            else if (ch == '`')
            {
                i++;
                var depth = 0;
                while (i < text.Length)
                {
                    if (text[i] == '\\') { i += 2; continue; }
                    if (depth == 0 && text[i] == '`') { i++; break; }
                    if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{') { depth++; i += 2; continue; }
                    if (depth > 0 && text[i] == '}') depth--;
                    i++;
                }
                kind = TsTokenKind.Template;
                value = text[start..i];
            }
            else if (char.IsDigit(ch) || (ch == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_')) i++;
                kind = TsTokenKind.Number;
                value = text[start..i];
            }
            else
            {
                kind = TsTokenKind.Punctuation;
                value = ch.ToString();
                foreach (var candidate in MultiCharPunctuation)
                {
                    if (string.CompareOrdinal(text, i, candidate, 0, candidate.Length) == 0)
                    {
                        value = candidate;
                        break;
                    }
                }
                i += value.Length;
            }

            tokens.Add(new TsToken(kind, value, start, newLine, doc));
            newLine = false;
            doc = null;
        }
    }

    private static string ReadString(string text, ref int i, char quote)
    {
        var sb = new StringBuilder();
        i++;
        while (i < text.Length && text[i] != quote)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                sb.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => next });
                i += 2;
                continue;
            }
            if (text[i] == '\n') break;
            sb.Append(text[i]);
            i++;
        }
        i++;
        return sb.ToString();
    }

    internal static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$' || c > 0x7F && char.IsLetterOrDigit(c);

    internal static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' || c == '‌' || c == '‍';
}
