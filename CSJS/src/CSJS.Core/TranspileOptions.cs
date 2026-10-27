namespace CSJS.Core;

/// <summary>Module format for emitted JavaScript.</summary>
public enum ModuleKind
{
    /// <summary>ES modules with <c>import</c>/<c>export</c>. The default; required for tree-shaking.</summary>
    ESM,

    /// <summary>A classic script with no module syntax. Exports become <c>globalThis</c> assignments.</summary>
    Script,
}

/// <summary>Knobs for a transpilation run.</summary>
public sealed class TranspileOptions
{
    /// <summary>Module format. Defaults to <see cref="ModuleKind.ESM"/>.</summary>
    public ModuleKind Module { get; set; } = ModuleKind.ESM;

    /// <summary>Emit a <c>.d.ts</c> declaration file alongside each module.</summary>
    public bool EmitTypeDefinitions { get; set; } = true;

    /// <summary>Emit a v3 source map and append a <c>//# sourceMappingURL</c> comment.</summary>
    public bool EmitSourceMap { get; set; } = true;

    /// <summary>Include the original C# text inside the source map's <c>sourcesContent</c>.</summary>
    public bool EmbedSources { get; set; } = true;

    /// <summary>Indentation unit. Defaults to two spaces.</summary>
    public string Indent { get; set; } = "  ";

    /// <summary>
    /// Module specifier used when emitted code needs a runtime helper.
    /// Helpers are imported individually so bundlers can tree-shake the unused ones.
    /// </summary>
    public string RuntimeModule { get; set; } = "./csjs-runtime.js";

    /// <summary>Treat C# compiler warnings as transpile errors.</summary>
    public bool StrictWarnings { get; set; }

    /// <summary>
    /// Makes integer arithmetic and conversions <c>checked</c> by default, as the C# compiler's
    /// <c>CheckForOverflowUnderflow</c> does: overflow throws <c>OverflowException</c>. Explicit
    /// <c>checked</c> and <c>unchecked</c> contexts apply either way.
    /// </summary>
    public bool CheckOverflow { get; set; }

    /// <summary>
    /// Leaves out private members nothing references. JavaScript has no reflection to reach them,
    /// so they are pure weight. Members whose initialisers might have side effects are kept.
    /// </summary>
    public bool RemoveUnusedPrivateMembers { get; set; } = true;

    /// <summary>
    /// Writes members of a non-public enum that is never turned into text as plain numbers, and
    /// drops the enum object itself — the <c>const enum</c> treatment.
    /// </summary>
    public bool InlineEnums { get; set; } = true;

    /// <summary>Extra assembly paths to reference when binding the C# semantic model.</summary>
    public List<string> AdditionalReferences { get; } = new();

    /// <summary>
    /// Reference assemblies supplied as raw bytes rather than file paths.
    /// <para>
    /// WebAssembly has no filesystem to read assemblies from, so the Playground downloads them
    /// and hands them over this way. When this list is non-empty it replaces the host's own
    /// assemblies entirely — mixing the two would risk binding one type to two identities.
    /// </para>
    /// </summary>
    public List<byte[]> ReferenceImages { get; } = new();

    /// <summary>Preprocessor symbols visible to the parsed C#. <c>CSJS</c> is always defined.</summary>
    public List<string> DefineConstants { get; } = new();

    /// <summary>Assembly name used for the underlying Roslyn compilation.</summary>
    public string AssemblyName { get; set; } = "CsjsModule";

    /// <summary>
    /// Appends a call to <c>Main</c> at the end of the module that declares it.
    /// A browser page usually drives the module itself, so this is off by default and turned on
    /// by <c>cs2js run</c>, where the module is the whole program.
    /// </summary>
    public bool EmitEntryPointCall { get; set; }
}
