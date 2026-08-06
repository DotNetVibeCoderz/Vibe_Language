using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using CSJS.Core.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace CSJS.Core;

/// <summary>
/// The CSJS transpiler. Parses C#, binds it with Roslyn, and emits one ESNext module per input
/// file along with its TypeScript declarations and source map.
/// </summary>
public sealed class Transpiler
{
    private readonly TranspileOptions _options;

    public Transpiler(TranspileOptions? options = null) => _options = options ?? new TranspileOptions();

    /// <summary>Transpiles a single source string. Convenience wrapper over <see cref="Transpile"/>.</summary>
    public TranspileResult TranspileSource(string csharp, string path = "Program.cs")
        => Transpile(new[] { new SourceDocument(path, csharp) });

    /// <summary>Transpiles a set of C# documents into JavaScript modules.</summary>
    public TranspileResult Transpile(IReadOnlyList<SourceDocument> documents)
    {
        var diagnostics = new List<CsjsDiagnostic>();

        if (documents.Count == 0)
        {
            return TranspileResult.Failed(new CsjsDiagnostic(
                DiagnosticIds.NoOutput, DiagnosticSeverity.Error, "No source documents were supplied."));
        }

        var parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Preview,
            documentationMode: DocumentationMode.Parse,
            preprocessorSymbols: _options.DefineConstants.Concat(new[] { "CSJS" }));

        var trees = documents
            .Select(d => CSharpSyntaxTree.ParseText(SourceText.From(d.Text, Encoding.UTF8), parseOptions, d.Path))
            .ToList();

        var compilation = CSharpCompilation.Create(
            _options.AssemblyName,
            trees,
            BuildReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false,
                nullableContextOptions: NullableContextOptions.Enable,
                // Roslyn's concurrent build waits on monitors, which single-threaded runtimes —
                // notably WebAssembly, where the Playground runs — cannot do. Inputs here are a
                // handful of files, so serial binding costs almost nothing and works everywhere.
                concurrentBuild: false,
                metadataImportOptions: MetadataImportOptions.Public));

        CollectRoslynDiagnostics(compilation, diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new TranspileResult { Modules = Array.Empty<EmittedModule>(), Diagnostics = diagnostics };

        var names = new NameResolver();
        var declaredTypes = trees
            .SelectMany(tree => DeclaredTypesIn(compilation.GetSemanticModel(tree), tree))
            .ToList();

        names.RegisterTypes(declaredTypes);
        foreach (var type in declaredTypes) names.RegisterMembers(type);

        var typeModules = BuildTypeModuleMap(compilation, trees, documents);

        var modules = new List<EmittedModule>();
        var anyHelperUsed = false;

        for (var i = 0; i < trees.Count; i++)
        {
            var tree = trees[i];
            var document = documents[i];
            var model = compilation.GetSemanticModel(tree);

            var module = EmitModule(tree, model, document, names, typeModules, diagnostics);
            if (module is null) continue;

            modules.Add(module);
            anyHelperUsed |= module.JavaScript.Contains(_options.RuntimeModule, StringComparison.Ordinal);
        }

        return new TranspileResult
        {
            Modules = modules,
            Diagnostics = diagnostics,
            RuntimeJavaScript = anyHelperUsed ? RuntimeSource.Value : null,
        };
    }

    private EmittedModule? EmitModule(
        SyntaxTree tree,
        SemanticModel model,
        SourceDocument document,
        NameResolver names,
        IReadOnlyDictionary<INamedTypeSymbol, string> typeModules,
        List<CsjsDiagnostic> diagnostics)
    {
        var sourceMap = _options.EmitSourceMap ? new SourceMapBuilder() : null;
        var sourceName = Path.GetFileName(document.Path);
        var moduleName = Path.ChangeExtension(sourceName, ".js");

        var writer = new JsWriter(_options.Indent) { SourceMap = sourceMap };
        if (sourceMap is not null)
            writer.SourceIndex = sourceMap.AddSource(sourceName, _options.EmbedSources ? document.Text : null);

        var context = new EmitContext
        {
            Model = model,
            Writer = writer,
            Options = _options,
            Names = names,
            Diagnostics = diagnostics,
            FilePath = document.Path,
            TypeModules = typeModules,
            CurrentModule = ModuleSpecifierFor(document.Path),
        };

        var emitter = new JsEmitter(context);
        emitter.EmitCompilationUnit((CompilationUnitSyntax)tree.GetRoot());

        var body = writer.ToString();
        var header = BuildHeader(context, sourceName);

        // The import block is only known after the body has been walked, so the source map has
        // to shift down by however many lines the header added.
        var headerLineCount = header.Count(c => c == '\n');
        sourceMap?.PrependLines(headerLineCount);

        var javascript = header + body;

        if (_options.EmitSourceMap)
            javascript = javascript.TrimEnd() + $"\n//# sourceMappingURL={moduleName}.map\n";

        var typeDefinitions = _options.EmitTypeDefinitions
            ? new DtsEmitter(model, names).Emit((CompilationUnitSyntax)tree.GetRoot(), moduleName)
            : null;

        return new EmittedModule(
            moduleName,
            javascript,
            sourceMap?.Build(moduleName),
            typeDefinitions,
            document.Path);
    }

    private string BuildHeader(EmitContext context, string sourceName)
    {
        var sb = new StringBuilder();
        sb.Append("// Generated from ").Append(sourceName).Append(" by CSJS. Do not edit.\n");
        sb.Append("// Gravicode Studios — led by Kang Fadhil.\n");

        if (_options.Module != ModuleKind.ESM)
        {
            sb.Append('\n');
            return sb.ToString();
        }

        // Helpers are imported individually rather than as a namespace object, which is what
        // lets a bundler drop the ones this module never touches.
        if (context.Helpers.Count > 0)
        {
            sb.Append("import { ")
              .Append(string.Join(", ", context.Helpers))
              .Append(" } from '")
              .Append(_options.RuntimeModule)
              .Append("';\n");
        }

        foreach (var group in context.Imports.GroupBy(i => i.Module).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var defaults = group.Where(i => i.Export is null).ToList();
            var named = group.Where(i => i.Export is not null).ToList();

            var clauses = new List<string>();
            if (defaults.Count > 0) clauses.Add(defaults[0].Alias);
            if (named.Count > 0)
            {
                var specifiers = named
                    .Select(i => i.Export == i.Alias ? i.Alias : $"{i.Export} as {i.Alias}")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.Ordinal);
                clauses.Add("{ " + string.Join(", ", specifiers) + " }");
            }

            if (clauses.Count == 0) continue;
            sb.Append("import ").Append(string.Join(", ", clauses))
              .Append(" from '").Append(group.Key).Append("';\n");
        }

        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>Module specifier another file would use to import this one.</summary>
    private static string ModuleSpecifierFor(string path)
        => "./" + Path.ChangeExtension(Path.GetFileName(path), ".js");

    private static Dictionary<INamedTypeSymbol, string> BuildTypeModuleMap(
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> trees,
        IReadOnlyList<SourceDocument> documents)
    {
        var map = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);

        for (var i = 0; i < trees.Count; i++)
        {
            var model = compilation.GetSemanticModel(trees[i]);
            var specifier = ModuleSpecifierFor(documents[i].Path);
            foreach (var type in DeclaredTypesIn(model, trees[i]))
                map[type] = specifier;
        }

        return map;
    }

    private static IEnumerable<INamedTypeSymbol> DeclaredTypesIn(SemanticModel model, SyntaxTree tree)
        => tree.GetRoot()
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(node => model.GetDeclaredSymbol(node))
            .OfType<INamedTypeSymbol>();

    private void CollectRoslynDiagnostics(CSharpCompilation compilation, List<CsjsDiagnostic> into)
    {
        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (IgnoredDiagnostics.Contains(diagnostic.Id)) continue;

            var severity = diagnostic.Severity switch
            {
                RoslynSeverity.Error => DiagnosticSeverity.Error,
                RoslynSeverity.Warning => _options.StrictWarnings ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Info,
            };

            if (severity == DiagnosticSeverity.Info) continue;

            var span = diagnostic.Location.GetLineSpan();
            into.Add(new CsjsDiagnostic(
                diagnostic.Id,
                severity,
                diagnostic.GetMessage(),
                diagnostic.Location.IsInSource ? span.Path : null,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1));
        }
    }

    /// <summary>
    /// Diagnostics that are correct for a normal C# build but meaningless for a transpile.
    /// </summary>
    private static readonly HashSet<string> IgnoredDiagnostics = new(StringComparer.Ordinal)
    {
        "CS5001",  // no Main method — top-level statements become module code
        "CS8892",  // more than one entry point
        "CS0028",  // Main has the wrong signature
    };

    // -----------------------------------------------------------------------------------------
    // References
    // -----------------------------------------------------------------------------------------

    private ImmutableArray<MetadataReference> BuildReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Caller-supplied images win outright: a runtime that has no filesystem cannot fall back
        // to host assemblies, and mixing the two sources would give some types two identities.
        if (_options.ReferenceImages.Count > 0)
        {
            foreach (var image in _options.ReferenceImages)
                references.Add(MetadataReference.CreateFromImage(image));

            return references.ToImmutableArray();
        }

        // The host's own framework assemblies are the reference set. This keeps the transpiler
        // free of a pinned reference-assembly package while still binding the whole BCL surface.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.Length == 0 || !File.Exists(path)) continue;
                if (!seen.Add(Path.GetFileName(path))) continue;
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // CSJS.Interop carries the attributes and browser bindings user code compiles against.
        var interopPath = typeof(JsGlobalAttribute).Assembly.Location;
        if (interopPath.Length > 0 && seen.Add(Path.GetFileName(interopPath)))
            references.Add(MetadataReference.CreateFromFile(interopPath));

        foreach (var path in _options.AdditionalReferences)
        {
            if (!File.Exists(path) || !seen.Add(Path.GetFileName(path))) continue;
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references.ToImmutableArray();
    }

    // -----------------------------------------------------------------------------------------
    // Runtime
    // -----------------------------------------------------------------------------------------

    private static readonly Lazy<string> RuntimeSourceLazy = new(() =>
    {
        var assembly = typeof(Transpiler).Assembly;
        using var stream = assembly.GetManifestResourceStream("CSJS.Core.Runtime.csjs-runtime.js");
        if (stream is null) return "// CSJS runtime resource was not embedded.\n";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>The JavaScript runtime helper module, embedded in this assembly.</summary>
    public static Lazy<string> RuntimeSource => RuntimeSourceLazy;
}
