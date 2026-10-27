using System.Text;

namespace CSJS.Core.Bindings;

/// <summary>A declaration file handed to the generator: where it came from, and its text.</summary>
public sealed record BindingSource(string Path, string Text);

/// <summary>Knobs for one binding generation.</summary>
public sealed class BindingOptions
{
    /// <summary>
    /// The module specifier the emitted <c>[JsImport]</c> attributes name, e.g. <c>"three"</c>.
    /// When null it is inferred: an ambient <c>declare module "x"</c> names itself, a module file
    /// defaults to <c>./&lt;file&gt;.js</c>, and a global script binds through <c>[JsGlobal]</c>.
    /// </summary>
    public string? Module { get; set; }

    /// <summary>The C# namespace of the generated file. Defaults to <c>Bindings.&lt;Name&gt;</c>.</summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Bind through globals rather than imports — for a UMD library loaded by a script tag. The
    /// global name is the file's <c>export as namespace</c> when it has one.
    /// </summary>
    public bool Global { get; set; }

    /// <summary>The static class holding the module's functions and values. Defaults to the module name.</summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Resolves another declaration file: the importing file's path and the specifier it used.
    /// Relative specifiers point inside the package, bare ones at another package. Without a
    /// resolver, <c>export * from</c> and imported types are reported rather than followed.
    /// </summary>
    public Func<string, string, BindingSource?>? Resolve { get; set; }
}

/// <summary>The generated C# and an account of what could not be bound.</summary>
public sealed class BindingResult
{
    public required string Code { get; init; }
    public required string Namespace { get; init; }
    public required string ContainerName { get; init; }

    /// <summary>Every construct left out, each also written into the code as a <c>// skipped:</c> comment.</summary>
    public required IReadOnlyList<string> Skipped { get; init; }

    /// <summary>Types the generator had to approximate, each also noted in the code.</summary>
    public required IReadOnlyList<string> Approximations { get; init; }

    public int TypeCount { get; init; }
    public int MemberCount { get; init; }
}

/// <summary>
/// Generates C# bindings from TypeScript declarations — the inverse of <see cref="Emit.DtsEmitter"/>.
/// <para>
/// The output is ordinary C# using the interop attributes, so transpiling code that calls it
/// produces exactly the imports and calls the library expects: a class becomes a
/// <c>[JsImport]</c> binding class, an options interface a <c>[JsObjectLiteral]</c> shape, a
/// module's functions static members of one container. Types cross back through
/// <see cref="Mapping.TypeMap.FromTypeScript"/>, the inverse half of the same table the
/// declaration emitter reads.
/// </para>
/// <para>
/// What has no C# counterpart is approximated or skipped — never silently. Every approximation
/// is a <c>// note:</c> beside the member, every omission a <c>// skipped:</c>, and both are
/// listed on the <see cref="BindingResult"/>.
/// </para>
/// </summary>
public sealed partial class BindingGenerator
{
    private readonly BindingOptions _options;

    private BindingGenerator(BindingOptions options) => _options = options;

    /// <summary>Generates bindings for a single declaration text.</summary>
    public static BindingResult Generate(string declarations, BindingOptions? options = null)
        => Generate(new BindingSource("index.d.ts", declarations), options);

    /// <summary>Generates bindings for an entry declaration file, following its re-exports through the resolver.</summary>
    public static BindingResult Generate(BindingSource entry, BindingOptions? options = null)
        => new BindingGenerator(options ?? new BindingOptions()).Run(entry);

    // -------------------------------------------------------------------------------------------
    // Modules, scopes, and entities
    // -------------------------------------------------------------------------------------------

    /// <summary>One module: a declaration file, or an ambient <c>declare module "x"</c> block.</summary>
    private sealed class TsModule
    {
        public required string Path { get; init; }
        public required Scope Root { get; init; }
        public bool IsScript { get; init; }
        public bool IsAmbient { get; init; }
        public Dictionary<string, Entity>? Exports { get; set; }
        public bool ComputingExports { get; set; }
    }

    /// <summary>A lexical scope: a module root or a namespace body.</summary>
    private sealed class Scope
    {
        public Scope? Parent { get; init; }
        public TsModule? Module { get; set; }

        /// <summary>The dotted namespace path from the module root, empty at the root.</summary>
        public string NamespacePath { get; init; } = "";

        public Dictionary<string, Entity> Names { get; } = new(StringComparer.Ordinal);
        public List<TsImportDeclaration> Imports { get; } = new();
        public List<TsExportDeclaration> ExportDeclarations { get; } = new();
        public List<TsSkippedDeclaration> Skipped { get; } = new();
        public string? ExportAssignment { get; set; }
        public string? UmdName { get; set; }
        public bool HasExplicitExports { get; set; }
    }

    /// <summary>Every declaration sharing one name in one scope — TypeScript merges them.</summary>
    private sealed class Entity
    {
        public required string Name { get; init; }
        public required Scope Owner { get; init; }
        public List<TsClassDeclaration> Classes { get; } = new();
        public List<TsInterfaceDeclaration> Interfaces { get; } = new();
        public List<TsFunctionDeclaration> Functions { get; } = new();
        public List<TsVariableDeclaration> Variables { get; } = new();
        public TsEnumDeclaration? Enum { get; set; }
        public TsTypeAliasDeclaration? Alias { get; set; }
        public Scope? Namespace { get; set; }

        /// <summary>Set for <c>import * as ns</c>: the entity stands for a whole module.</summary>
        public TsModule? ModuleReference { get; init; }

        public bool Exported { get; set; }
        public bool DefaultExported { get; set; }

        public bool HasValue => Classes.Count > 0 || Functions.Count > 0 || Variables.Count > 0 || Enum is not null
                                || Namespace is not null && NamespaceHasValues(Namespace);

        public override string ToString() => Owner.NamespacePath.Length == 0 ? Name : $"{Owner.NamespacePath}.{Name}";
    }

    private static bool NamespaceHasValues(Scope scope)
        => scope.Names.Values.Any(e => e.Functions.Count > 0 || e.Variables.Count > 0 || e.Classes.Count > 0
                                       || e.Enum is not null || e.Namespace is not null && NamespaceHasValues(e.Namespace));

    private readonly Dictionary<string, TsModule> _modulesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TsModule> _ambientModules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TsModule> _augmentations = new(StringComparer.Ordinal);
    private readonly Scope _globalScope = new();
    private readonly List<string> _skipped = new();
    private readonly List<string> _approximations = new();

    private TsModule LoadModule(BindingSource source)
    {
        if (_modulesByPath.TryGetValue(source.Path, out var existing)) return existing;

        var file = TsParser.Parse(source.Text, source.Path);
        var root = new Scope();
        var module = new TsModule { Path = source.Path, Root = root, IsScript = !file.IsModule };
        root.Module = module;
        _modulesByPath[source.Path] = module;

        // A global script contributes to the global scope too, which is how a type it declares
        // becomes visible to the module that references it.
        Populate(root, file.Declarations);
        if (module.IsScript)
        {
            foreach (var (name, entity) in root.Names)
                _globalScope.Names.TryAdd(name, entity);
        }

        return module;
    }

    private void Populate(Scope scope, IEnumerable<TsDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case TsImportDeclaration import:
                    scope.Imports.Add(import);
                    break;

                case TsExportDeclaration export:
                    scope.ExportDeclarations.Add(export);
                    scope.HasExplicitExports = true;
                    break;

                case TsExportAssignment assignment:
                    scope.ExportAssignment = assignment.Name;
                    scope.HasExplicitExports = true;
                    break;

                case TsExportAsNamespace umd:
                    scope.UmdName = umd.Name;
                    break;

                case TsSkippedDeclaration skipped:
                    scope.Skipped.Add(skipped);
                    break;

                case TsNamespaceDeclaration { Kind: TsNamespaceKind.AmbientModule } ambient:
                {
                    var root = new Scope();
                    var module = new TsModule { Path = ambient.Name, Root = root, IsAmbient = true };
                    root.Module = module;
                    Populate(root, ambient.Body);

                    // Inside a module file, `declare module "x"` augments a module that exists
                    // elsewhere — Vue's runtime-dom adds to runtime-core this way. Treating the
                    // augmentation as the whole module would hide everything the real one exports,
                    // so it only stands in when the real module cannot be found.
                    var target = scope.Module is { IsScript: false } && !ReferenceEquals(scope, _globalScope)
                        ? _augmentations
                        : _ambientModules;
                    if (target.TryGetValue(ambient.Name, out var existing))
                        MergeScopes(existing.Root, root);
                    else
                        target[ambient.Name] = module;
                    break;
                }

                case TsNamespaceDeclaration { Kind: TsNamespaceKind.Global } global:
                    // `declare global` augments types that live elsewhere; they resolve by name.
                    var globalBody = new Scope { Module = scope.Module };
                    Populate(globalBody, global.Body);
                    foreach (var (name, entity) in globalBody.Names)
                        _globalScope.Names.TryAdd(name, entity);
                    break;

                case TsNamespaceDeclaration ns:
                {
                    var entity = EntityFor(scope, ns.Name, declaration);
                    entity.Namespace ??= new Scope
                    {
                        Parent = scope,
                        Module = scope.Module,
                        NamespacePath = scope.NamespacePath.Length == 0 ? ns.Name : $"{scope.NamespacePath}.{ns.Name}",
                    };
                    Populate(entity.Namespace, ns.Body);
                    break;
                }

                case TsClassDeclaration c: EntityFor(scope, c.Name, c).Classes.Add(c); break;
                case TsInterfaceDeclaration i: EntityFor(scope, i.Name, i).Interfaces.Add(i); break;
                case TsFunctionDeclaration f: EntityFor(scope, f.Name, f).Functions.Add(f); break;
                case TsVariableDeclaration v: EntityFor(scope, v.Name, v).Variables.Add(v); break;
                case TsEnumDeclaration e: EntityFor(scope, e.Name, e).Enum ??= e; break;
                case TsTypeAliasDeclaration a: EntityFor(scope, a.Name, a).Alias ??= a; break;
            }
        }
    }

    private static void MergeScopes(Scope into, Scope from)
    {
        foreach (var (name, entity) in from.Names)
        {
            if (!into.Names.TryAdd(name, entity))
            {
                var target = into.Names[name];
                target.Classes.AddRange(entity.Classes);
                target.Interfaces.AddRange(entity.Interfaces);
                target.Functions.AddRange(entity.Functions);
                target.Variables.AddRange(entity.Variables);
                target.Enum ??= entity.Enum;
                target.Alias ??= entity.Alias;
                target.Exported |= entity.Exported;
            }
        }
        into.Imports.AddRange(from.Imports);
        into.ExportDeclarations.AddRange(from.ExportDeclarations);
        into.HasExplicitExports |= from.HasExplicitExports;
    }

    private static Entity EntityFor(Scope scope, string name, TsDeclaration declaration)
    {
        if (!scope.Names.TryGetValue(name, out var entity))
        {
            entity = new Entity { Name = name, Owner = scope };
            scope.Names[name] = entity;
        }

        if (declaration.IsDefault) entity.DefaultExported = true;
        else if (declaration.Exported) entity.Exported = true;
        return entity;
    }

    /// <summary>Loads the module a specifier names, from an ambient block or through the resolver.</summary>
    private TsModule? ResolveModule(TsModule? from, string specifier)
    {
        if (_ambientModules.TryGetValue(specifier, out var ambient)) return ambient;
        var augmentation = _augmentations.GetValueOrDefault(specifier);
        if (_options.Resolve is null || from is null) return augmentation;

        var basePath = from.IsAmbient ? _entryPath : from.Path;
        BindingSource? source;
        try { source = _options.Resolve(basePath, specifier); }
        catch (Exception ex)
        {
            Skip($"module '{specifier}': could not be resolved ({ex.Message})");
            return augmentation;
        }

        if (source is null) return augmentation;
        return LoadModule(source);
    }

    // -------------------------------------------------------------------------------------------
    // Name lookup
    // -------------------------------------------------------------------------------------------

    private Entity? Lookup(Scope scope, string name)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Names.TryGetValue(name, out var entity)) return entity;

            foreach (var import in current.Imports)
            {
                if (import.NamespaceName == name)
                {
                    var module = ResolveModule(current.Module, import.Module);
                    return module is null ? null : new Entity { Name = name, Owner = current, ModuleReference = module };
                }

                if (import.DefaultName == name)
                    return ResolveModule(current.Module, import.Module) is { } module ? ExportsOf(module).GetValueOrDefault("default") : null;

                foreach (var specifier in import.Specifiers)
                {
                    if (specifier.Alias != name) continue;
                    return ResolveModule(current.Module, import.Module) is { } module
                        ? ExportsOf(module).GetValueOrDefault(specifier.Name)
                        : null;
                }
            }
        }

        return _globalScope.Names.GetValueOrDefault(name);
    }

    private Entity? LookupQualified(Scope scope, string dottedName)
    {
        var parts = dottedName.Split('.');
        var entity = Lookup(scope, parts[0]);

        for (var i = 1; i < parts.Length && entity is not null; i++)
        {
            if (entity.ModuleReference is { } module)
                entity = ExportsOf(module).GetValueOrDefault(parts[i]);
            else if (entity.Namespace is { } ns)
                entity = ns.Names.GetValueOrDefault(parts[i]);
            else
                entity = null;
        }

        return entity;
    }

    /// <summary>What a module exports, by exported name — following re-exports and <c>export =</c>.</summary>
    private Dictionary<string, Entity> ExportsOf(TsModule module)
    {
        if (module.Exports is not null) return module.Exports;
        if (module.ComputingExports) return new Dictionary<string, Entity>(StringComparer.Ordinal);
        module.ComputingExports = true;

        var exports = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var root = module.Root;

        if (root.ExportAssignment is { } target)
        {
            // `export = X`: a namespace's members become the module's named exports, and X itself
            // is what a default import receives.
            var entity = LookupQualified(root, target);
            if (entity?.Namespace is { } ns)
            {
                foreach (var (name, member) in ns.Names) exports.TryAdd(name, member);
            }
            if (entity is not null && (entity.Functions.Count > 0 || entity.Classes.Count > 0 || entity.Variables.Count > 0))
                exports["default"] = entity;
        }

        // An ambient module or script with no export keyword at all exports everything it declares.
        var exportAll = module.IsScript || module.IsAmbient && !root.HasExplicitExports
                                           && !root.Names.Values.Any(e => e.Exported || e.DefaultExported);

        foreach (var (name, entity) in root.Names)
        {
            if (exportAll || entity.Exported) exports.TryAdd(name, entity);
            if (entity.DefaultExported) exports.TryAdd("default", entity);
        }

        foreach (var declaration in root.ExportDeclarations)
        {
            if (declaration.IsStar)
            {
                var source = declaration.FromModule is null ? null : ResolveModule(module, declaration.FromModule);
                if (source is null)
                {
                    Skip($"export * from '{declaration.FromModule}': the module could not be resolved");
                    continue;
                }

                if (declaration.Name.Length > 0)
                {
                    exports.TryAdd(declaration.Name, new Entity { Name = declaration.Name, Owner = root, ModuleReference = source });
                    continue;
                }

                foreach (var (name, entity) in ExportsOf(source))
                    if (name != "default") exports.TryAdd(name, entity);
                continue;
            }

            foreach (var specifier in declaration.Specifiers)
            {
                Entity? entity;
                if (declaration.FromModule is { } from)
                {
                    var source = ResolveModule(module, from);
                    entity = source is null ? null : ExportsOf(source).GetValueOrDefault(specifier.Name);
                    if (source is null)
                    {
                        Skip($"export {{ {specifier.Name} }} from '{from}': the module could not be resolved");
                        continue;
                    }
                }
                else
                {
                    entity = LookupQualified(root, specifier.Name);
                }

                if (entity is not null) exports[specifier.Alias] = entity;
            }
        }

        module.ComputingExports = false;
        module.Exports = exports;
        return exports;
    }

    // -------------------------------------------------------------------------------------------
    // The run
    // -------------------------------------------------------------------------------------------

    private string _entryPath = "";
    private string? _moduleSpecifier;
    private string _globalPrefix = "";
    private bool _globalMode;
    private string _namespace = "Bindings";
    private string _containerName = "Module";

    private BindingResult Run(BindingSource entry)
    {
        _entryPath = entry.Path;
        var module = LoadModule(entry);

        // A file of nothing but `declare module "x" { }` blocks binds those modules.
        var ambientOnly = module.Root.Names.Count == 0 && _ambientModules.Count > 0 && module.Root.ExportAssignment is null
                          && module.Root.ExportDeclarations.Count == 0;
        if (ambientOnly)
        {
            var chosen = _options.Module is { } wanted && _ambientModules.TryGetValue(wanted, out var match)
                ? match
                : _ambientModules.Values.First();
            if (_ambientModules.Count > 1)
            {
                foreach (var other in _ambientModules.Keys.Where(k => k != chosen.Path))
                    Skip($"declare module '{other}': only one module is bound per file — pass --module '{other}' to bind it");
            }
            module = chosen;
            _moduleSpecifier = chosen.Path;
        }
        else if (module.IsScript || _options.Global)
        {
            _globalMode = true;
            _globalPrefix = _options.Global ? module.Root.UmdName ?? "" : "";
        }
        else
        {
            _moduleSpecifier = _options.Module ?? "./" + StripDeclarationExtension(System.IO.Path.GetFileName(entry.Path)) + ".js";
        }

        var displayName = _globalMode
            ? (_globalPrefix.Length > 0 ? _globalPrefix : StripDeclarationExtension(System.IO.Path.GetFileName(entry.Path)))
            : _options.Module ?? _moduleSpecifier!;

        _containerName = _options.ContainerName is { Length: > 0 } containerOption
            ? CsIdentifier(containerOption)
            : TypeNameFromModule(displayName);

        // An exported type named like the module keeps the name — it is what code refers to
        // most — and the container of functions steps aside.
        var surface = _globalMode && module.IsScript ? module.Root.Names : ExportsOf(module);
        if (_options.ContainerName is null
            && surface.Any(e => CsTypeName(e.Key) == _containerName
                                && Classify(e.Value) is EntityKind.Class or EntityKind.Interface or EntityKind.ObjectLiteral
                                    or EntityKind.NumericEnum or EntityKind.StringEnum))
        {
            _containerName += "Module";
        }

        _namespace = _options.Namespace is { Length: > 0 } ns ? ns : "Bindings." + _containerName;

        foreach (var skipped in module.Root.Skipped)
            Skip($"{skipped.Name}: {skipped.Reason}");

        ReserveTypeName(_containerName);
        var container = NewType(_containerName, CsTypeKind.StaticClass);
        container.Doc.Add(_globalMode
            ? $"/// <summary>Global functions and values{(_globalPrefix.Length > 0 ? $" of <c>{XmlEscape(_globalPrefix)}</c>" : "")}.</summary>"
            : $"/// <summary>Functions and values exported by <c>{XmlEscape(_moduleSpecifier!)}</c>. Each member imports itself.</summary>");
        container.Attributes.Add(_globalMode
            ? _globalPrefix.Length > 0 ? $"[JsGlobal({Quote(_globalPrefix)})]" : "[JsGlobal]"
            : $"[JsImport({Quote(_moduleSpecifier!)})]");
        _container = container;

        if (_globalMode) BindGlobalSurface(module);
        else BindModuleSurface(module);

        DrainQueue();

        return new BindingResult
        {
            Code = Render(entry),
            Namespace = _namespace,
            ContainerName = _containerName,
            Skipped = _skipped.Distinct().ToList(),
            Approximations = _approximations.Distinct().ToList(),
            TypeCount = _types.Count(t => t.Members.Count > 0 || t.Kind != CsTypeKind.StaticClass),
            MemberCount = _types.Sum(t => t.Members.Count(m => m.Kind != CsMemberKind.Comment)),
        };
    }

    private static string StripDeclarationExtension(string fileName)
    {
        foreach (var extension in new[] { ".d.ts", ".d.mts", ".d.cts", ".ts" })
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return fileName[..^extension.Length];
        return fileName;
    }

    /// <summary><c>three</c> → <c>Three</c>, <c>@vue/runtime-dom</c> → <c>RuntimeDom</c>, <c>./my-lib.js</c> → <c>MyLib</c>.</summary>
    internal static string TypeNameFromModule(string specifier)
    {
        var last = specifier.TrimEnd('/');
        var slash = last.LastIndexOf('/');
        if (slash >= 0) last = last[(slash + 1)..];
        last = StripDeclarationExtension(last);
        foreach (var extension in new[] { ".js", ".mjs", ".cjs" })
            if (last.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) last = last[..^extension.Length];

        var sb = new StringBuilder();
        var upper = true;
        foreach (var c in last)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        var name = sb.Length == 0 ? "Module" : sb.ToString();
        return char.IsDigit(name[0]) ? "_" + name : name;
    }

    private void Skip(string message) => _skipped.Add(message);

    private void Approximate(string message) => _approximations.Add(message);
}
