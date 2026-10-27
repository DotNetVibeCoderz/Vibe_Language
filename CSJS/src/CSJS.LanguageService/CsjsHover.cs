using CSJS.Core.Emit;
using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;

namespace CSJS.LanguageService;

/// <summary>
/// The CSJS half of a hover: what a symbol becomes in JavaScript.
/// <para>
/// Everything here is read from the engine's own tables — <see cref="MemberMap"/> for BCL members,
/// <see cref="TypeMap"/> for types, and the interop attributes for bindings — so the hover cannot
/// claim a translation the emitter does not perform.
/// </para>
/// </summary>
internal static class CsjsHover
{
    public static string? Describe(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;

        if (Binding(symbol) is { } binding) return binding;

        // Declared in the project: emitted from its own source, nothing to explain.
        if (symbol.DeclaringSyntaxReferences.Length > 0) return null;

        switch (symbol)
        {
            case INamedTypeSymbol type when MemberMap.RequiresMapping(type):
                return $"**CSJS** — in JavaScript: `{TypeMap.ToTypeScript(type)}`";

            case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol:
                if (MemberMap.Lookup(symbol) is { } template)
                {
                    var helper = template.Helper is null
                        ? ""
                        : $"\n\nUses the runtime helper `{template.Helper}`, imported on its own so bundlers can drop it when unused.";
                    return $"**CSJS** — emits:\n\n```javascript\n{template.Pattern}\n```{helper}";
                }

                if (MemberMap.RequiresMapping(symbol))
                {
                    return "**CSJS** — this framework member has no JavaScript mapping. " +
                           "Where it reaches emission the build reports `CSJS0002`; use a supported " +
                           "member, or declare a binding with `[JsGlobal]` or `[JsBody]`.";
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>Interop attributes say exactly what a binding emits.</summary>
    private static string? Binding(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            var args = attribute.ConstructorArguments;
            string? First() => args.Length > 0 ? args[0].Value as string : null;

            switch (name)
            {
                case "JsTemplateAttribute" when First() is { } pattern:
                    return $"**CSJS** — `[JsTemplate]`, every call site becomes:\n\n```javascript\n{pattern}\n```";

                case "JsBodyAttribute" when First() is { } code:
                    return $"**CSJS** — `[JsBody]`, emitted with the body:\n\n```javascript\n{code}\n```";

                case "JsGlobalAttribute" when symbol is INamedTypeSymbol:
                    return $"**CSJS** — binds the JavaScript global `{NameResolver.JsGlobalName(symbol)}`; nothing is declared.";

                case "JsImportAttribute" when First() is { } module && symbol is INamedTypeSymbol:
                    var export = args.Length > 1 ? args[1].Value as string : null;
                    return $"**CSJS** — imported: `import {{ {export ?? symbol.Name} }} from '{module}'`";
            }
        }

        // A member of a binding type names a real JavaScript API. The name comes from the
        // emitter's own resolver, so [JsName] overrides and the camelCase default both agree
        // with what a build writes.
        if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol
            && IsBinding(symbol.ContainingType))
        {
            var member = new NameResolver().MemberName(symbol);
            var receiver = symbol.IsStatic && symbol.ContainingType is { } owner
                ? (NameResolver.JsGlobalName(owner) ?? owner.Name)
                : "";
            var call = symbol is IMethodSymbol ? "(…)" : "";
            return $"**CSJS** — emits `{receiver}.{member}{call}`";
        }

        return null;
    }

    private static bool IsBinding(INamedTypeSymbol? type)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (type.GetAttributes().Any(a => a.AttributeClass?.Name is "JsGlobalAttribute" or "JsImportAttribute"))
                return true;
        }

        return false;
    }
}
