using System.Text;

namespace CSJS.Gallery.Client.Shell;

/// <summary>The two languages the code view shows.</summary>
public enum CodeLanguage { CSharp, JavaScript }

/// <summary>
/// A small syntax highlighter for C# and JavaScript, producing one HTML string per line so the
/// seam can address lines individually. A hand-written scanner rather than regular expressions:
/// strings, comments, and template literals span lines, and the scanner carries that state across
/// them.
/// </summary>
public static class Highlighter
{
    private static readonly HashSet<string> CSharpKeywords = new()
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "get", "goto", "if", "implicit", "in", "init", "int", "interface", "internal", "is", "let", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "record", "ref", "required", "return", "sbyte", "sealed",
        "set", "short", "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
        "volatile", "when", "where", "while", "with", "yield", "field", "extension", "nameof", "and", "or", "not",
    };

    private static readonly HashSet<string> JavaScriptKeywords = new()
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "export", "extends", "false", "finally", "for", "from", "function", "get",
        "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "set", "static", "super",
        "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "yield",
    };

    /// <summary>Highlights <paramref name="code"/>, one HTML string per line.</summary>
    public static List<string> Lines(string code, CodeLanguage language)
    {
        var keywords = language == CodeLanguage.CSharp ? CSharpKeywords : JavaScriptKeywords;
        var text = code.Replace("\r\n", "\n");
        var lines = new List<string>();
        var current = new StringBuilder();

        // A token that crosses a newline is closed at the line end and reopened on the next line,
        // so every line's HTML is balanced on its own.
        void Emit(string tokenText, string className)
        {
            var parts = tokenText.Split('\n');
            for (var p = 0; p < parts.Length; p++)
            {
                if (p > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                if (parts[p].Length == 0) continue;
                if (className.Length == 0) current.Append(Ui.Escape(parts[p]));
                else current.Append("<span class=\"t-").Append(className).Append("\">").Append(Ui.Escape(parts[p])).Append("</span>");
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            // Comments.
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var end = text.IndexOf('\n', i);
                if (end < 0) end = text.Length;
                var comment = text.Substring(i, end - i);
                Emit(comment, comment.StartsWith("///") ? "doc" : "comment");
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2);
                end = end < 0 ? text.Length : end + 2;
                Emit(text.Substring(i, end - i), "comment");
                i = end;
                continue;
            }

            // Strings: "…", '…', `…` (JavaScript template), @"…" and $"…" (C#).
            if (c == '"' || c == '\'' || c == '`'
                || (language == CodeLanguage.CSharp && (c == '$' || c == '@') && i + 1 < text.Length && text[i + 1] == '"'))
            {
                var start = i;
                var verbatim = c == '@';
                if (c == '$' || c == '@') i++;
                var quote = text[i];
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && !verbatim) { i += 2; continue; }
                    if (text[i] == quote) { i++; break; }
                    if (text[i] == '\n' && quote != '`' && !verbatim) break;
                    i++;
                }
                if (i > text.Length) i = text.Length;
                Emit(text.Substring(start, i - start), "string");
                continue;
            }

            // Numbers.
            if (char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '_')) i++;
                Emit(text.Substring(start, i - start), "number");
                continue;
            }

            // Identifiers, keywords, types, calls.
            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$')) i++;
                var word = text.Substring(start, i - start);

                var next = i;
                while (next < text.Length && text[next] == ' ') next++;
                var isCall = next < text.Length && (text[next] == '(' || (text[next] == '<' && language == CodeLanguage.CSharp && char.IsUpper(word[0])));

                var className = keywords.Contains(word) ? "keyword"
                    : word.StartsWith("csjs") ? "runtime"
                    : isCall ? "call"
                    : char.IsUpper(word[0]) ? "type"
                    : "";
                Emit(word, className);
                continue;
            }

            // Attributes in C#: [JsExport], [JsGlobal("…")].
            if (c == '[' && language == CodeLanguage.CSharp && i + 1 < text.Length && char.IsUpper(text[i + 1]))
            {
                var close = text.IndexOf(']', i);
                var lineEnd = text.IndexOf('\n', i);
                if (close > 0 && (lineEnd < 0 || close < lineEnd) && close - i < 60 && !text.Substring(i, close - i).Contains('"'))
                {
                    Emit(text.Substring(i, close - i + 1), "attribute");
                    i = close + 1;
                    continue;
                }
            }

            Emit(c.ToString(), "{}()[];,.".Contains(c) ? "punct" : "=+-*/<>!&|?:%^~".Contains(c) ? "operator" : "");
            i++;
        }

        lines.Add(current.ToString());
        return lines;
    }
}
