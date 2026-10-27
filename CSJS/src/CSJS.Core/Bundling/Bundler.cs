using System.Text;
using CSJS.Core.Emit;

namespace CSJS.Core.Bundling;

/// <summary>Raised when the emitted modules cannot be combined into one file.</summary>
public sealed class BundleException(string message) : Exception(message);

/// <summary>Knobs for <see cref="Bundler"/>.</summary>
public sealed class BundleOptions
{
    /// <summary>Name of the bundle file, used for the source map's <c>file</c> and the trailing comment.</summary>
    public string FileName { get; set; } = "bundle.js";

    /// <summary>Strip comments, indentation, and blank lines. See <see cref="JsMinifier"/>.</summary>
    public bool Minify { get; set; }

    /// <summary>Write a merged source map. Requires the modules to carry their own maps.</summary>
    public bool EmitSourceMap { get; set; } = true;

    /// <summary>The bundle map's <c>sourceRoot</c>: where the C# sources live, relative to the bundle.</summary>
    public string SourceRoot { get; set; } = "";

    /// <summary>The specifier modules import helpers from; matches <see cref="TranspileOptions.RuntimeModule"/>.</summary>
    public string RuntimeModule { get; set; } = "./csjs-runtime.js";

    /// <summary>The runtime to draw helpers from. Defaults to the one embedded in <c>CSJS.Core</c>.</summary>
    public RuntimeLibrary? Runtime { get; set; }
}

/// <summary>One module going into a bundle.</summary>
/// <param name="Path">Path relative to the output root, with <c>/</c> separators, e.g. <c>Program.js</c>.</param>
/// <param name="JavaScript">Emitted module text.</param>
/// <param name="SourceMap">Its source map, or null.</param>
public sealed record BundleModule(string Path, string JavaScript, string? SourceMap);

/// <summary>The bundle and what went into it.</summary>
public sealed class BundleResult
{
    public required string JavaScript { get; init; }
    public string? SourceMap { get; init; }

    /// <summary>Module paths in evaluation order.</summary>
    public required IReadOnlyList<string> ModuleOrder { get; init; }

    /// <summary>Runtime declarations carried, exported helpers and their internal dependencies alike.</summary>
    public required IReadOnlyList<string> RuntimeDeclarations { get; init; }

    /// <summary>Specifiers that remain imports of the bundle: packages, URLs, and hand-written files.</summary>
    public required IReadOnlyList<string> ExternalImports { get; init; }
}

/// <summary>
/// Combines the modules of one transpile into a single ES module, so a project ships one file
/// without an external bundler.
/// <para>
/// The emitter's output is regular enough to make this a matter of rearranging rather than
/// rewriting: every top-level name is already unique across the program (the
/// <see cref="NameResolver"/> disambiguates across files), imports between modules name exactly
/// what they bind, and each module's code is self-contained text. So the bundler orders modules
/// as ESM would evaluate them, drops the imports between them, hoists and merges the external
/// imports, and inlines just the runtime helpers in use. Exports are kept as the modules wrote
/// them — every public type — so a page imports from the bundle the same names it imported from
/// the individual modules.
/// </para>
/// </summary>
public static class Bundler
{
    /// <summary>Bundles the modules of a successful transpile.</summary>
    public static BundleResult Bundle(TranspileResult result, BundleOptions? options = null)
        => Bundle(result.Modules.Select(m => new BundleModule(OutputPathOf(m), m.JavaScript, m.SourceMap)).ToList(), options);

    /// <summary>Where the build writes a module, relative to the output root.</summary>
    public static string OutputPathOf(EmittedModule module)
    {
        var directory = Path.GetDirectoryName(module.SourcePath) ?? "";
        return Normalize(directory.Length == 0 ? module.FileName : directory + "/" + module.FileName);
    }

    public static BundleResult Bundle(IReadOnlyList<BundleModule> modules, BundleOptions? options = null)
    {
        options ??= new BundleOptions();
        var runtime = options.Runtime ?? RuntimeLibrary.Default;

        if (modules.Count == 0) throw new BundleException("There are no modules to bundle.");

        var parsed = modules.Select(m => ParsedModule.Parse(m, options.RuntimeModule)).ToList();
        var byPath = parsed.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

        // Resolve local specifiers. The emitter names a sibling module './X.js' wherever it is, so
        // a specifier that does not resolve beside the importer falls back to a unique file name.
        foreach (var module in parsed)
        {
            foreach (var import in module.Imports)
            {
                if (import.IsRuntime || !IsRelative(import.Specifier)) continue;

                var resolved = Normalize(Combine(DirectoryOf(module.Path), import.Specifier));
                if (byPath.ContainsKey(resolved))
                {
                    import.Target = byPath[resolved];
                    continue;
                }

                var byName = parsed.Where(p => FileNameOf(p.Path).Equals(FileNameOf(resolved), StringComparison.OrdinalIgnoreCase)).ToList();
                if (byName.Count == 1) import.Target = byName[0];
                else import.ResolvedPath = resolved; // a hand-written file next to the output
            }
        }

        foreach (var module in parsed)
        {
            if (module.Imports.Any(i => i.Target is not null && (i.Default is not null || i.Namespace is not null)))
                throw new BundleException($"{module.Path}: default and namespace imports of another module cannot be bundled.");
        }

        var order = Order(parsed);

        // External imports, merged per specifier in first-seen order.
        var externals = new List<(string Specifier, ExternalImport Import)>();
        var bound = new Dictionary<string, string>(StringComparer.Ordinal); // local name -> what binds it

        void Bind(string name, string what)
        {
            if (bound.TryGetValue(name, out var existing) && existing != what)
                throw new BundleException($"'{name}' is bound twice in the bundle: {existing} and {what}.");
            bound[name] = what;
        }

        foreach (var module in order)
        {
            foreach (var import in module.Imports.Where(i => i.Target is null && !i.IsRuntime))
            {
                var specifier = import.ResolvedPath is { } path ? RelativeToRoot(path) : import.Specifier;
                var entry = externals.FirstOrDefault(e => e.Specifier == specifier).Import;
                if (entry is null)
                {
                    entry = new ExternalImport();
                    externals.Add((specifier, entry));
                }

                if (import.Default is { } d)
                {
                    if (entry.Default is { } other && other != d)
                        throw new BundleException($"'{specifier}' is default-imported as both '{other}' and '{d}'.");
                    entry.Default = d;
                    Bind(d, $"default of '{specifier}'");
                }
                if (import.Namespace is { } ns)
                {
                    if (entry.Namespace is { } other && other != ns)
                        throw new BundleException($"'{specifier}' is namespace-imported as both '{other}' and '{ns}'.");
                    entry.Namespace = ns;
                    Bind(ns, $"namespace of '{specifier}'");
                }
                foreach (var (export, alias) in import.Named)
                {
                    var specifierText = export == alias ? alias : $"{export} as {alias}";
                    if (!entry.Named.Contains(specifierText)) entry.Named.Add(specifierText);
                    Bind(alias, $"'{export}' of '{specifier}'");
                }
            }
        }

        // Runtime helpers: whatever any module names, plus what those need.
        var used = parsed.SelectMany(p => p.Words).Where(runtime.Contains).Distinct(StringComparer.Ordinal);
        var helpers = runtime.Select(used);

        foreach (var helper in helpers) Bind(helper.Name, "the CSJS runtime");
        foreach (var module in order)
            foreach (var name in module.Declared)
                Bind(name, $"a declaration in {module.Path}");

        // Assemble the body, carrying each module's mappings across line for line.
        var body = new StringBuilder();
        var segments = new List<(int Line, SourceMapSegment Segment)>();
        var sources = new List<string>();
        var sourcesContent = new List<string?>();
        var line = 0;

        void AppendLine(string text)
        {
            body.Append(text).Append('\n');
            line++;
        }

        void AppendBlock(string text)
        {
            foreach (var l in text.Replace("\r\n", "\n").Split('\n')) AppendLine(l);
        }

        foreach (var (specifier, import) in externals)
        {
            var clauses = new List<string>();
            if (import.Default is { } d) clauses.Add(d);
            if (import.Namespace is { } ns) clauses.Add("* as " + ns);
            if (import.Named.Count > 0) clauses.Add("{ " + string.Join(", ", import.Named) + " }");
            AppendLine(clauses.Count == 0
                ? $"import '{specifier}';"
                : $"import {string.Join(", ", clauses)} from '{specifier}';");
        }
        if (externals.Count > 0) AppendLine("");

        if (helpers.Count > 0)
        {
            AppendLine($"// CSJS runtime: {helpers.Count(h => h.Exported)} helper(s) in use, with what they depend on.");
            foreach (var helper in helpers) AppendBlock(helper.Text);
            AppendLine("");
        }

        foreach (var module in order)
        {
            var sourceIndexes = new Dictionary<int, int>();
            if (module.Map is { } map)
            {
                for (var s = 0; s < map.Sources.Count; s++)
                {
                    var key = Normalize(Combine(DirectoryOf(module.Path), map.Sources[s]));
                    var index = sources.IndexOf(key);
                    if (index < 0)
                    {
                        sources.Add(key);
                        sourcesContent.Add(map.SourcesContent[s]);
                        index = sources.Count - 1;
                    }
                    sourceIndexes[s] = index;
                }
            }

            // A local import under another name still needs that name.
            foreach (var import in module.Imports.Where(i => i.Target is not null || i.IsRuntime))
                foreach (var (export, alias) in import.Named.Where(n => n.Export != n.Alias))
                    AppendLine($"const {alias} = {export};");

            for (var l = 0; l < module.Lines.Length; l++)
            {
                if (module.Removed.Contains(l) && string.IsNullOrWhiteSpace(module.Lines[l])) continue;

                if (module.Map is { } m && l < m.Lines.Count)
                {
                    foreach (var segment in m.Lines[l])
                    {
                        if (segment.SourceIndex < 0) continue;
                        segments.Add((line, segment with { SourceIndex = sourceIndexes[segment.SourceIndex] }));
                    }
                }

                AppendLine(module.Lines[l].TrimEnd());
            }
        }

        var javascript = body.ToString();

        if (options.Minify)
        {
            var minified = JsMinifier.Minify(javascript);
            javascript = minified.JavaScript;

            var translated = new List<(int, SourceMapSegment)>();
            foreach (var (l, segment) in segments)
            {
                if (minified.Translate(l, segment.GeneratedColumn) is { } at)
                    translated.Add((at.Line, segment with { GeneratedColumn = at.Column }));
            }
            segments = translated;
        }

        // The attribution banner goes on top, and the map shifts down with it.
        var banner = options.Minify
            ? $"// {options.FileName}: bundled by CSJS. Gravicode Studios, led by Kang Fadhil.\n"
            : $"// {options.FileName}: {order.Count} module(s) bundled by CSJS. Do not edit.\n// Gravicode Studios — led by Kang Fadhil.\n\n";
        var bannerLines = banner.Count(c => c == '\n');
        javascript = banner + javascript.TrimEnd() + "\n";

        string? sourceMap = null;
        if (options.EmitSourceMap && parsed.Any(p => p.Map is not null))
        {
            var builder = new SourceMapBuilder { SourceRoot = options.SourceRoot };
            for (var s = 0; s < sources.Count; s++) builder.AddSource(sources[s], sourcesContent[s]);

            foreach (var (l, segment) in segments
                         .OrderBy(s => s.Item1).ThenBy(s => s.Item2.GeneratedColumn))
            {
                builder.AddMapping(l + bannerLines, segment.GeneratedColumn,
                    segment.SourceIndex, segment.SourceLine, segment.SourceColumn);
            }

            sourceMap = builder.Build(options.FileName);
            javascript += $"//# sourceMappingURL={options.FileName}.map\n";
        }

        return new BundleResult
        {
            JavaScript = javascript,
            SourceMap = sourceMap,
            ModuleOrder = order.Select(m => m.Path).ToList(),
            RuntimeDeclarations = helpers.Select(h => h.Name).ToList(),
            ExternalImports = externals.Select(e => e.Specifier).ToList(),
        };
    }

    /// <summary>
    /// Evaluation order: depth first, dependencies before dependents, imports visited in the order
    /// they are written — the order an ES module graph evaluates in. Cycles are broken where they
    /// are met, again as ESM does; classes referenced only inside methods are unaffected.
    /// </summary>
    private static List<ParsedModule> Order(List<ParsedModule> modules)
    {
        var order = new List<ParsedModule>();
        var state = new Dictionary<ParsedModule, bool>(); // false = visiting, true = done

        void Visit(ParsedModule module)
        {
            if (state.ContainsKey(module)) return;
            state[module] = false;
            foreach (var import in module.Imports)
                if (import.Target is { } target) Visit(target);
            state[module] = true;
            order.Add(module);
        }

        foreach (var module in modules) Visit(module);
        return order;
    }

    // ---------------------------------------------------------------------------------------
    // Paths (always '/'-separated, relative to the output root)
    // ---------------------------------------------------------------------------------------

    private static bool IsRelative(string specifier)
        => specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);

    private static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static string FileNameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string Combine(string directory, string relative)
        => directory.Length == 0 ? relative : directory + "/" + relative;

    private static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == ".." && parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
            else parts.Add(part);
        }
        return string.Join("/", parts);
    }

    private static string RelativeToRoot(string path) => path.StartsWith("../", StringComparison.Ordinal) ? path : "./" + path;

    private sealed class ExternalImport
    {
        public string? Default;
        public string? Namespace;
        public readonly List<string> Named = new();
    }

    // ---------------------------------------------------------------------------------------
    // One module, taken apart
    // ---------------------------------------------------------------------------------------

    private sealed class ModuleImport
    {
        public required string Specifier { get; init; }
        public bool IsRuntime { get; init; }
        public string? Default { get; set; }
        public string? Namespace { get; set; }
        public List<(string Export, string Alias)> Named { get; } = new();
        public ParsedModule? Target { get; set; }
        public string? ResolvedPath { get; set; }
    }

    private sealed class ParsedModule
    {
        public required string Path { get; init; }
        public required string[] Lines { get; init; }
        public required HashSet<int> Removed { get; init; }
        public required List<ModuleImport> Imports { get; init; }
        public required List<string> Declared { get; init; }
        public required HashSet<string> Words { get; init; }
        public SourceMapDocument? Map { get; init; }

        public static ParsedModule Parse(BundleModule module, string runtimeModule)
        {
            var source = module.JavaScript.Replace("\r\n", "\n");
            var tokens = JsTokenizer.Tokenize(source);
            var lines = new JsTokenizer.LineIndex(source);
            var blanked = source.ToCharArray();
            var removed = new HashSet<int>();
            var imports = new List<ModuleImport>();
            var declared = new List<string>();
            var words = new HashSet<string>(StringComparer.Ordinal);

            void Remove(int start, int end)
            {
                for (var i = start; i < end; i++)
                    if (blanked[i] != '\n') blanked[i] = ' ';
                var (first, _) = lines.Locate(start);
                var (last, _) = lines.Locate(Math.Max(start, end - 1));
                for (var l = first; l <= last; l++) removed.Add(l);
            }

            var code = tokens.Where(t => !t.IsComment).ToList();
            var depth = 0;

            for (var k = 0; k < code.Count; k++)
            {
                var token = code[k];
                var text = token.Text(source);

                if (token.Kind == JsTokenKind.Punctuator)
                {
                    if (text is "{" or "(" or "[") depth++;
                    else if (text is "}" or ")" or "]") depth--;
                    continue;
                }

                if (token.Kind != JsTokenKind.Word) continue;

                var afterDot = k > 0 && code[k - 1].Text(source) is "." or "?.";
                if (!afterDot) words.Add(text);

                if (depth != 0 || afterDot) continue;

                var statementStart = k == 0 || code[k - 1].Text(source) is ";" or "}" || token.NewlineBefore;
                if (!statementStart) continue;

                var next = k + 1 < code.Count ? code[k + 1].Text(source) : "";

                if (text == "import" && next is not ("(" or "."))
                {
                    var (import, end) = ParseImport(source, code, k, runtimeModule, module.Path);
                    imports.Add(import);
                    Remove(token.Start, code[end].End);
                    k = end;
                    continue;
                }

                var declarationAt = k;
                if (text == "export")
                {
                    if (next is "{" or "*" or "default")
                        throw new BundleException($"{module.Path}: 'export {next}' is not supported in a bundle.");
                    declarationAt = k + 1;
                }

                if (DeclaredName(source, code, declarationAt) is { } name) declared.Add(name);
            }

            // The module's own map comment points at a file the bundle does not have.
            foreach (var comment in tokens.Where(t => t.Kind == JsTokenKind.LineComment))
            {
                if (comment.Text(source).StartsWith("//# sourceMappingURL=", StringComparison.Ordinal))
                    Remove(comment.Start, comment.End);
            }

            // Names bound by imports are the importer's, not occurrences of runtime helpers.
            var runtimeImports = imports.Where(i => i.IsRuntime).SelectMany(i => i.Named.Select(n => n.Export));
            foreach (var name in runtimeImports) words.Add(name);

            return new ParsedModule
            {
                Path = module.Path,
                Lines = new string(blanked).Split('\n'),
                Removed = removed,
                Imports = imports,
                Declared = declared,
                Words = words,
                Map = module.SourceMap is { } map ? SourceMapDocument.Parse(map) : null,
            };
        }

        private static string? DeclaredName(string source, List<JsToken> code, int k)
        {
            if (k >= code.Count) return null;
            var keyword = code[k].Text(source);
            if (keyword == "async" && k + 1 < code.Count) keyword = code[++k].Text(source);
            if (keyword is not ("class" or "function" or "const" or "let" or "var")) return null;

            k++;
            if (k < code.Count && code[k].Text(source) == "*") k++;
            return k < code.Count && code[k].Kind == JsTokenKind.Word ? code[k].Text(source) : null;
        }

        private static (ModuleImport Import, int End) ParseImport(
            string source, List<JsToken> code, int k, string runtimeModule, string path)
        {
            string Text(int i) => i < code.Count ? code[i].Text(source) : "";
            BundleException Malformed() => new($"{path}: an import the bundler cannot read near '{Text(k)} {Text(k + 1)}'.");

            var i = k + 1;
            string? defaultAlias = null;
            string? namespaceAlias = null;
            var named = new List<(string, string)>();

            if (code.ElementAtOrDefault(i).Kind == JsTokenKind.String && i < code.Count)
            {
                var bare = Unquote(Text(i));
                var end = Text(i + 1) == ";" ? i + 1 : i;
                return (new ModuleImport { Specifier = bare, IsRuntime = bare == runtimeModule }, end);
            }

            if (code[i].Kind == JsTokenKind.Word && Text(i) != "from")
            {
                defaultAlias = Text(i++);
                if (Text(i) == ",") i++;
            }

            if (Text(i) == "*")
            {
                if (Text(i + 1) != "as") throw Malformed();
                namespaceAlias = Text(i + 2);
                i += 3;
            }
            else if (Text(i) == "{")
            {
                i++;
                while (i < code.Count && Text(i) != "}")
                {
                    var export = Text(i++);
                    var alias = export;
                    if (Text(i) == "as")
                    {
                        alias = Text(i + 1);
                        i += 2;
                    }
                    named.Add((export, alias));
                    if (Text(i) == ",") i++;
                }
                i++; // closing brace
            }

            if (Text(i) != "from" || code.ElementAtOrDefault(i + 1).Kind != JsTokenKind.String) throw Malformed();
            var specifier = Unquote(Text(i + 1));
            var last = Text(i + 2) == ";" ? i + 2 : i + 1;

            var import = new ModuleImport
            {
                Specifier = specifier,
                // A module in a subfolder reaches the root-level runtime with ../, so the
                // specifier is compared by where it resolves to, not by its spelling.
                IsRuntime = specifier == runtimeModule
                            || (IsRelative(specifier) && IsRelative(runtimeModule)
                                && Normalize(Combine(DirectoryOf(path), specifier)) == Normalize(runtimeModule)),
                Default = defaultAlias,
                Namespace = namespaceAlias,
            };
            import.Named.AddRange(named);

            if (import.IsRuntime && (defaultAlias is not null || namespaceAlias is not null))
                throw new BundleException($"{path}: the runtime must be imported by name.");

            return (import, last);
        }

        private static string Unquote(string literal) => literal.Length >= 2 ? literal[1..^1] : literal;
    }
}
