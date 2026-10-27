using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

/// <summary>
/// Decides what every C# symbol is called in JavaScript.
/// <para>
/// Two problems this exists to solve. First, JavaScript has no namespaces, so type names are
/// flattened and only disambiguated when they actually collide — keeping output readable in the
/// common case. Second, JavaScript has no overloading, so overload sets get suffixed names and
/// every call site is resolved through the semantic model to the exact overload it bound to.
/// </para>
/// </summary>
public sealed class NameResolver
{
    private readonly Dictionary<ISymbol, string> _typeNames = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, string> _memberNames = new(SymbolEqualityComparer.Default);

    /// <summary>Reserved words that cannot appear as bare identifiers in JavaScript.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "await", "arguments", "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "enum", "eval", "export", "extends", "false", "finally",
        "for", "function", "if", "implements", "import", "in", "instanceof", "interface", "let",
        "new", "null", "package", "private", "protected", "public", "return", "static", "super",
        "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
        "undefined", "NaN", "Infinity", "globalThis",
    };

    /// <summary>
    /// Assigns flattened names to every type in the compilation, mangling only where a short
    /// name would be ambiguous.
    /// </summary>
    public void RegisterTypes(IEnumerable<INamedTypeSymbol> types)
    {
        var byShortName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var name = ExplicitName(type) ?? type.Name;
            if (!byShortName.TryGetValue(name, out var bucket))
                byShortName[name] = bucket = new List<INamedTypeSymbol>();
            bucket.Add(type);
        }

        foreach (var (shortName, bucket) in byShortName)
        {
            if (bucket.Count == 1)
            {
                _typeNames[bucket[0]] = Escape(shortName);
                continue;
            }

            // Collision: qualify with the containing namespace so both remain reachable.
            foreach (var type in bucket)
            {
                var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n
                    ? n.ToDisplayString().Replace('.', '_') + "_"
                    : "";
                _typeNames[type] = Escape(ns + shortName);
            }
        }
    }

    /// <summary>
    /// Assigns names to a type's members, suffixing overloads after the first.
    /// The first overload declared keeps the plain name so the common case stays clean.
    /// </summary>
    public void RegisterMembers(INamedTypeSymbol type)
    {
        var methodGroups = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor)
            .GroupBy(m => m.MethodKind == MethodKind.Constructor ? ".ctor" : (ExplicitName(m) ?? m.Name), StringComparer.Ordinal);

        foreach (var group in methodGroups)
        {
            var overloads = group.OrderBy(DeclarationOrder).ToList();
            for (var i = 0; i < overloads.Count; i++)
            {
                var method = overloads[i];
                if (method.MethodKind == MethodKind.Constructor)
                {
                    // The first constructor becomes `constructor`; the rest become static factories,
                    // because JavaScript allows exactly one constructor per class.
                    _memberNames[method] = i == 0 ? "constructor" : $"new${i}";
                }
                else
                {
                    // A binding's overloads all describe one JavaScript function, which
                    // dispatches on its arguments at runtime — suffixing them would call
                    // methods the library does not have.
                    var baseName = DefaultName(method);
                    _memberNames[method] = i == 0 || IsBindingMember(method) ? baseName : $"{baseName}${i}";
                }
            }
        }

        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol) continue;
            if (_memberNames.ContainsKey(member)) continue;
            _memberNames[member] = DefaultName(member);
        }
    }

    /// <summary>
    /// The JavaScript name a member gets when nothing overrides it.
    /// <para>
    /// Registration and lookup must agree on this, so both go through here. They did not: only
    /// lookup applied the camelCase convention, and registration ran first — so a binding
    /// declared in the user's own sources emitted <c>renderer.SetSize(...)</c> while the same
    /// binding shipped in <c>CSJS.Interop</c> emitted <c>setSize</c>. Bindings in a referenced
    /// assembly are never registered here, which is exactly why it went unnoticed.
    /// </para>
    /// </summary>
    private static string DefaultName(ISymbol member)
    {
        if (ExplicitName(member) is { } explicitName) return EscapeMember(explicitName);
        return IsBindingMember(member) ? BindingName(member.Name) : Escape(member.Name);
    }

    private static int DeclarationOrder(IMethodSymbol method)
    {
        var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
        return reference?.Span.Start ?? int.MaxValue;
    }

    /// <summary>The JavaScript name for a type.</summary>
    public string TypeName(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
        {
            if (_typeNames.TryGetValue(named.OriginalDefinition, out var name)) return name;
            if (JsGlobalName(named) is { } global) return global;
            return Escape(ExplicitName(named) ?? named.Name);
        }
        return Escape(type.Name);
    }

    /// <summary>The JavaScript name for a member, honouring overload suffixes.</summary>
    public string MemberName(ISymbol member)
    {
        var target = member is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : member;
        target = target.OriginalDefinition;

        if (_memberNames.TryGetValue(target, out var name)) return name;
        if (ExplicitName(target) is { } explicitName) return EscapeMember(explicitName);

        // Members of a binding type name real JavaScript APIs, which are camelCase by
        // convention. Defaulting to that spelling means a binding only needs [JsName] where
        // the JS name genuinely differs, rather than on every single member.
        if (IsBindingMember(target)) return BindingName(target.Name);

        return Escape(target.Name);
    }

    /// <summary>
    /// The JavaScript name a binding member gets from its C# name: the camelCase form, with no
    /// keyword escaping.
    /// <para>
    /// A member name only ever appears after a dot or as an object key, where JavaScript accepts
    /// reserved words — <c>map.delete(k)</c>, <c>{ default: 1 }</c>. Escaping them to
    /// <c>delete_</c> would name a member the library does not have. The binding generator uses
    /// this to decide when a member needs <c>[JsName]</c> at all.
    /// </para>
    /// </summary>
    public static string BindingName(string csharpName) => EscapeMember(CamelCaseRaw(csharpName));

    /// <summary>
    /// Makes a member name a valid JavaScript identifier without touching reserved words, which
    /// are legal in member position. An explicit <c>[JsName]</c> is taken at its word.
    /// </summary>
    private static string EscapeMember(string identifier)
    {
        if (identifier.Length == 0) return "_";
        var first = identifier[0];
        if (!char.IsLetter(first) && first != '_' && first != '$') return "_" + identifier;
        return identifier;
    }

    /// <summary>True when a member belongs to a type declared as a JavaScript binding.</summary>
    internal static bool IsBindingMember(ISymbol member)
    {
        if (member.ContainingType is not { } owner) return false;

        // The attribute may sit on the member itself when only one member is a global.
        if (HasBindingAttribute(member)) return true;

        for (var type = owner; type is not null; type = type.BaseType)
            if (HasBindingAttribute(type)) return true;

        return false;
    }

    private static bool HasBindingAttribute(ISymbol symbol)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.Name is "JsGlobalAttribute" or "JsImportAttribute");

    /// <summary>Lowercases a leading run of capitals: <c>InnerHTML</c> to <c>innerHTML</c>.</summary>
    private static string CamelCaseRaw(string name)
    {
        if (name.Length == 0) return name;
        if (!char.IsUpper(name[0])) return name;

        // A run of capitals is an acronym; only the run's leading portion lowercases, so
        // `URL` becomes `url` but `URLPath` becomes `urlPath`.
        var upperRun = 0;
        while (upperRun < name.Length && char.IsUpper(name[upperRun])) upperRun++;

        return upperRun == name.Length
            ? name.ToLowerInvariant()
            : upperRun == 1
                ? char.ToLowerInvariant(name[0]) + name[1..]
                : name[..(upperRun - 1)].ToLowerInvariant() + name[(upperRun - 1)..];
    }

    /// <summary>A local, parameter, or label name, escaped against JavaScript keywords.</summary>
    public static string LocalName(string identifier) => Escape(identifier);

    /// <summary>The <c>[JsGlobal]</c> identifier for a symbol, when it has one.</summary>
    public static string? JsGlobalName(ISymbol symbol)
    {
        var attribute = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsGlobalAttribute");
        if (attribute is null) return null;
        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string s && s.Length > 0)
            return s;
        return symbol.Name;
    }

    /// <summary>The <c>[JsName]</c> override for a symbol, when present.</summary>
    public static string? ExplicitName(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            switch (attribute.AttributeClass?.Name)
            {
                case "JsNameAttribute":
                    if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string n)
                        return n;
                    break;
                case "JsGlobalAttribute":
                    if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string g && g.Length > 0)
                        return g;
                    break;
            }
        }
        return null;
    }

    /// <summary>Appends <c>_</c> to identifiers that would otherwise be JavaScript keywords.</summary>
    public static string Escape(string identifier)
    {
        if (identifier.Length == 0) return "_";
        if (Reserved.Contains(identifier)) return identifier + "_";

        // C# allows identifiers JavaScript rejects (notably the `@verbatim` form once trimmed).
        var first = identifier[0];
        if (!char.IsLetter(first) && first != '_' && first != '$') return "_" + identifier;
        return identifier;
    }

    /// <summary>True when a name is safe to write as a bare object-literal key.</summary>
    public static bool IsValidPropertyKey(string name)
    {
        if (name.Length == 0) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_' && name[0] != '$') return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$');
    }
}
