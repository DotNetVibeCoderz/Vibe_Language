namespace CSJS.Core.Bundling;

/// <summary>
/// The runtime helper module split into its top-level declarations, so a bundle can carry only
/// the helpers a program uses.
/// <para>
/// Helpers call one another (<c>csjsFormat</c> reaches <c>csjsFormatNumber</c>), and some of what
/// they call is not exported at all — private functions and lookup tables. Selection therefore
/// starts from the helpers the modules reference and follows identifiers to a fixed point, over
/// exported and internal declarations alike.
/// </para>
/// </summary>
public sealed class RuntimeLibrary
{
    /// <summary>One top-level declaration of the runtime.</summary>
    /// <param name="Name">Declared name.</param>
    /// <param name="Text">Source text, leading comment included and <c>export</c> removed.</param>
    /// <param name="Exported">Whether the runtime module exports it.</param>
    /// <param name="References">Other runtime declarations this one mentions.</param>
    public sealed record Declaration(string Name, string Text, bool Exported, IReadOnlySet<string> References);

    private readonly Dictionary<string, Declaration> _byName;

    /// <summary>Declarations in source order.</summary>
    public IReadOnlyList<Declaration> Declarations { get; }

    private RuntimeLibrary(List<Declaration> declarations)
    {
        Declarations = declarations;
        _byName = declarations.ToDictionary(d => d.Name, StringComparer.Ordinal);
    }

    public bool Contains(string name) => _byName.ContainsKey(name);

    private static readonly Lazy<RuntimeLibrary> DefaultLazy = new(() => Parse(Transpiler.RuntimeSource.Value));

    /// <summary>The runtime embedded in <c>CSJS.Core</c>.</summary>
    public static RuntimeLibrary Default => DefaultLazy.Value;

    /// <summary>
    /// The declarations needed to make <paramref name="roots"/> work, in source order, so that a
    /// table declared before the function that reads it stays before it.
    /// </summary>
    public IReadOnlyList<Declaration> Select(IEnumerable<string> roots)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots.Where(Contains));

        while (pending.Count > 0)
        {
            var name = pending.Pop();
            if (!needed.Add(name)) continue;
            foreach (var reference in _byName[name].References)
                if (!needed.Contains(reference)) pending.Push(reference);
        }

        return Declarations.Where(d => needed.Contains(d.Name)).ToList();
    }

    /// <summary>
    /// Splits runtime source into declarations. Anything at the top level that is not a
    /// <c>function</c>, <c>class</c>, <c>const</c>, <c>let</c>, or <c>var</c> declaration is
    /// rejected: a bare statement could have side effects the selection would silently drop.
    /// </summary>
    public static RuntimeLibrary Parse(string source)
    {
        var tokens = JsTokenizer.Tokenize(source);
        var code = new List<int>(); // indices of non-comment tokens
        for (var i = 0; i < tokens.Count; i++)
            if (!tokens[i].IsComment) code.Add(i);

        var raw = new List<(string Name, int Start, int TextStart, int End, bool Exported, int FirstCode, int LastCode)>();
        var c = 0;
        var previousEnd = 0;

        while (c < code.Count)
        {
            var startCode = c;
            var exported = Word(source, tokens[code[c]]) == "export";
            if (exported) c++;

            var keyword = c < code.Count ? Word(source, tokens[code[c]]) : null;
            if (keyword == "async") keyword = Word(source, tokens[code[++c]]);

            string? name;
            int lastCode;

            if (keyword is "function" or "class")
            {
                c++;
                if (Text(source, tokens[code[c]]) == "*") c++;
                name = Word(source, tokens[code[c]]);
                lastCode = MatchingBlockEnd(source, tokens, code, c);
            }
            else if (keyword is "const" or "let" or "var")
            {
                c++;
                name = Word(source, tokens[code[c]]);
                lastCode = StatementEnd(source, tokens, code, c);
            }
            else
            {
                var at = new JsTokenizer.LineIndex(source).Locate(tokens[code[startCode]].Start);
                throw new BundleException(
                    $"csjs-runtime.js({at.Line + 1},{at.Column + 1}): only declarations may appear at the top level of the runtime.");
            }

            if (name is null) throw new BundleException("csjs-runtime.js: a declaration without a name.");

            // Leading comments belong to the declaration they sit directly on top of.
            var firstToken = code[startCode];
            var commentStart = tokens[firstToken].Start;
            for (var t = firstToken - 1; t >= 0 && tokens[t].IsComment && tokens[t].Start >= previousEnd; t--)
            {
                var gap = source.AsSpan(tokens[t].End, commentStart - tokens[t].End);
                if (gap.Count('\n') > 1) break;
                commentStart = tokens[t].Start;
            }

            var textStart = exported ? tokens[code[startCode + 1]].Start : tokens[firstToken].Start;
            raw.Add((name, commentStart, textStart, tokens[code[lastCode]].End, exported, startCode, lastCode));
            previousEnd = tokens[code[lastCode]].End;
            c = lastCode + 1;
        }

        var names = new HashSet<string>(raw.Select(r => r.Name), StringComparer.Ordinal);
        var declarations = new List<Declaration>();

        foreach (var r in raw)
        {
            var references = new HashSet<string>(StringComparer.Ordinal);
            for (var k = r.FirstCode; k <= r.LastCode; k++)
            {
                var token = tokens[code[k]];
                if (token.Kind != JsTokenKind.Word) continue;
                if (k > 0 && Text(source, tokens[code[k - 1]]) is "." or "?.") continue;
                var word = token.Text(source);
                if (word != r.Name && names.Contains(word)) references.Add(word);
            }

            var leading = source[r.Start..tokens[code[r.FirstCode]].Start];
            var text = leading + source[r.TextStart..r.End];
            declarations.Add(new Declaration(r.Name, text, r.Exported, references));
        }

        return new RuntimeLibrary(declarations);
    }

    private static string? Word(string source, JsToken token)
        => token.Kind == JsTokenKind.Word ? token.Text(source) : null;

    private static string Text(string source, JsToken token) => token.Text(source);

    /// <summary>Index (into <paramref name="code"/>) of the brace closing the first block after <paramref name="from"/>.</summary>
    private static int MatchingBlockEnd(string source, List<JsToken> tokens, List<int> code, int from)
    {
        var depth = 0;
        for (var k = from; k < code.Count; k++)
        {
            var text = Text(source, tokens[code[k]]);
            if (text is "(" or "[") depth++;
            else if (text is ")" or "]") depth--;
            else if (text == "{") depth++;
            else if (text == "}")
            {
                depth--;
                if (depth == 0) return k;
            }
        }
        throw new BundleException("csjs-runtime.js: unterminated declaration.");
    }

    /// <summary>Index of the semicolon ending a variable declaration.</summary>
    private static int StatementEnd(string source, List<JsToken> tokens, List<int> code, int from)
    {
        var depth = 0;
        for (var k = from; k < code.Count; k++)
        {
            var text = Text(source, tokens[code[k]]);
            if (text is "(" or "[" or "{") depth++;
            else if (text is ")" or "]" or "}") depth--;
            else if (text == ";" && depth == 0) return k;
        }
        throw new BundleException("csjs-runtime.js: a top-level declaration must end with a semicolon.");
    }
}
