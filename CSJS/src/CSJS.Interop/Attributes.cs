namespace CSJS;

/// <summary>
/// Marks a type or member as an existing JavaScript global, so the transpiler emits
/// references to it directly instead of emitting a definition.
/// <code>
/// [JsGlobal("Math")] public static class JsMath { public static double Sqrt(double x) => throw new JsExternException(); }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
                AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
                AttributeTargets.Delegate | AttributeTargets.Enum,
                AllowMultiple = false, Inherited = false)]
public sealed class JsGlobalAttribute : Attribute
{
    public JsGlobalAttribute() { }
    public JsGlobalAttribute(string name) => Name = name;

    /// <summary>JavaScript identifier. Defaults to the C# name when omitted.</summary>
    public string? Name { get; }
}

/// <summary>
/// Overrides the emitted JavaScript name for a type or member. Use when the C# name
/// follows .NET conventions but the JS target expects something else.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
                AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
                AttributeTargets.Event | AttributeTargets.Enum | AttributeTargets.Parameter,
                AllowMultiple = false, Inherited = false)]
public sealed class JsNameAttribute : Attribute
{
    public JsNameAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>
/// Declares that a type or member comes from a JavaScript module. The transpiler emits an
/// ES <c>import</c> for it and tree-shakes the import when the member is never referenced.
/// <code>
/// [JsImport("three", "Scene")] public sealed class Scene { }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
                AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property |
                AttributeTargets.Delegate | AttributeTargets.Enum,
                AllowMultiple = false, Inherited = false)]
public sealed class JsImportAttribute : Attribute
{
    public JsImportAttribute(string module) => Module = module;
    public JsImportAttribute(string module, string export)
    {
        Module = module;
        Export = export;
    }

    /// <summary>Module specifier, e.g. <c>"three"</c> or <c>"./util.js"</c>.</summary>
    public string Module { get; }

    /// <summary>Named export. Defaults to the C# name when omitted.</summary>
    public string? Export { get; }

    /// <summary>When true, imports the module's default export.</summary>
    public bool Default { get; set; }
}

/// <summary>
/// Supplies a raw JavaScript body for a method. The text is emitted verbatim; parameters are
/// in scope by their (possibly <see cref="JsNameAttribute"/>-renamed) names.
/// This is the escape hatch for behaviour the transpiler cannot express.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class JsBodyAttribute : Attribute
{
    public JsBodyAttribute(string code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Rewrites every call site of a member into a JavaScript expression.
/// <para>
/// Unlike <see cref="JsBodyAttribute"/>, which supplies a body for an emitted method, this
/// substitutes at the point of use — so it works for members of binding types, which emit no
/// declaration at all. It is the user-facing form of the transpiler's own BCL mapping table.
/// </para>
/// <para>Placeholders: <c>{this}</c> the receiver, <c>{0}</c>…<c>{n}</c> the arguments,
/// <c>{...}</c> all arguments comma-joined.</para>
/// <code>
/// [JsTemplate("{this}[{0}]")] public T Get&lt;T&gt;(string key) => throw new JsExternException();
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class JsTemplateAttribute : Attribute
{
    public JsTemplateAttribute(string pattern) => Pattern = pattern;
    public string Pattern { get; }
}

/// <summary>
/// Marks a class as a plain JavaScript object literal shape rather than a class.
/// Instances are emitted as <c>{ ... }</c> with no prototype or constructor.
/// Useful for options bags passed to JS libraries.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class JsObjectLiteralAttribute : Attribute
{
}

/// <summary>
/// Exports a type or member from the generated module so host HTML/JS can reach it.
/// Without this, top-level types are module-private and eligible for tree-shaking.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
                AttributeTargets.Method | AttributeTargets.Enum | AttributeTargets.Delegate,
                AllowMultiple = false, Inherited = false)]
public sealed class JsExportAttribute : Attribute
{
    public JsExportAttribute() { }
    public JsExportAttribute(string name) => Name = name;

    public string? Name { get; }

    /// <summary>Also assign the export onto <c>globalThis</c> under this name.</summary>
    public bool Global { get; set; }
}

/// <summary>
/// Excludes a type or member from transpilation entirely. The declaration stays valid C#
/// (useful for server-only helpers living in the same file) but emits nothing.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
public sealed class JsIgnoreAttribute : Attribute
{
}

/// <summary>
/// Thrown by extern-style declarations whose bodies exist only to satisfy the C# compiler.
/// Reaching one at runtime on .NET means the declaration was called outside a transpiled context.
/// </summary>
public sealed class JsExternException : InvalidOperationException
{
    public JsExternException()
        : base("This member is a JavaScript interop declaration and has no .NET implementation. " +
               "It can only be called from code compiled by the CSJS transpiler.")
    {
    }
}
