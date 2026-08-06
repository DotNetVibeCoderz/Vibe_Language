using Microsoft.CodeAnalysis;

namespace CSJS.Core.Mapping;

/// <summary>How a C# type is represented once it reaches JavaScript.</summary>
public enum JsShape
{
    Number,
    BigInt,
    String,
    Boolean,
    Array,
    Map,
    Set,
    Promise,
    Function,
    Object,
    Void,
    Any,
}

/// <summary>
/// The single source of truth for how C# types cross into JavaScript.
/// <para>
/// Three components read this table and must agree: the expression emitter (which needs
/// <see cref="ShapeOf"/> to pick runtime operations such as truncating integer division),
/// the declaration emitter (which writes <c>.d.ts</c> via <see cref="ToTypeScript"/>), and the
/// interop binding generator (which maps JS library signatures back into C#). Adding a mapping
/// here changes all three at once — that is the point.
/// </para>
/// </summary>
public static class TypeMap
{
    /// <summary>Classifies a type by its JavaScript runtime representation.</summary>
    public static JsShape ShapeOf(ITypeSymbol? type)
    {
        if (type is null) return JsShape.Any;

        if (type.TypeKind == TypeKind.Enum) return JsShape.Number;
        if (type.TypeKind == TypeKind.TypeParameter) return JsShape.Any;
        if (type.TypeKind == TypeKind.Delegate) return JsShape.Function;
        if (type is IArrayTypeSymbol) return JsShape.Array;

        switch (type.SpecialType)
        {
            case SpecialType.System_Void: return JsShape.Void;
            case SpecialType.System_Boolean: return JsShape.Boolean;
            case SpecialType.System_String:
            case SpecialType.System_Char: return JsShape.String;
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64: return JsShape.BigInt;
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: return JsShape.Number;
            case SpecialType.System_Object: return JsShape.Any;
        }

        if (type is INamedTypeSymbol named)
        {
            if (IsNullableValueType(named, out var underlying)) return ShapeOf(underlying);

            var def = Unbound(named);
            switch (def)
            {
                case "System.Threading.Tasks.Task":
                case "System.Threading.Tasks.ValueTask":
                    return JsShape.Promise;

                case "System.Collections.Generic.List":
                case "System.Collections.Generic.IList":
                case "System.Collections.Generic.ICollection":
                case "System.Collections.Generic.IEnumerable":
                case "System.Collections.Generic.IReadOnlyList":
                case "System.Collections.Generic.IReadOnlyCollection":
                case "System.Linq.IOrderedEnumerable":
                // A group is an Array with a key attached, so it is array-shaped.
                case "System.Linq.IGrouping":
                    return JsShape.Array;

                case "System.Collections.Generic.Dictionary":
                case "System.Collections.Generic.IDictionary":
                case "System.Collections.Generic.IReadOnlyDictionary":
                    return JsShape.Map;

                case "System.Collections.Generic.HashSet":
                case "System.Collections.Generic.ISet":
                    return JsShape.Set;

                case "System.Func":
                case "System.Action":
                case "System.Predicate":
                case "System.Comparison":
                    return JsShape.Function;

                case "System.DateTime":
                case "System.DateTimeOffset":
                    return JsShape.Object;
            }
        }

        return JsShape.Object;
    }

    /// <summary>True when arithmetic on this type must be truncated to stay faithful to C#.</summary>
    public static bool IsIntegral(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type.TypeKind == TypeKind.Enum) return true;
        if (type is INamedTypeSymbol n && IsNullableValueType(n, out var underlying)) return IsIntegral(underlying);

        return type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    /// <summary>True for the 64-bit integer types, which map to JavaScript <c>BigInt</c>.</summary>
    public static bool IsBigInt(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol n && IsNullableValueType(n, out var underlying)) return IsBigInt(underlying);
        return type?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    /// <summary>Renders a C# type as its TypeScript equivalent for <c>.d.ts</c> emission.</summary>
    public static string ToTypeScript(ITypeSymbol? type)
    {
        if (type is null) return "any";

        switch (type.SpecialType)
        {
            case SpecialType.System_Void: return "void";
            case SpecialType.System_Boolean: return "boolean";
            case SpecialType.System_String:
            case SpecialType.System_Char: return "string";
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64: return "bigint";
            case SpecialType.System_Object: return "any";
        }

        if (IsIntegral(type) || type.SpecialType is SpecialType.System_Single
            or SpecialType.System_Double or SpecialType.System_Decimal)
        {
            return "number";
        }

        if (type is IArrayTypeSymbol array)
            return $"{ToTypeScript(array.ElementType)}[]";

        if (type is ITypeParameterSymbol tp)
            return tp.Name;

        if (type is INamedTypeSymbol named)
        {
            if (IsNullableValueType(named, out var underlying))
                return $"{ToTypeScript(underlying)} | null";

            var args = named.TypeArguments;
            switch (Unbound(named))
            {
                case "System.Threading.Tasks.Task" when args.Length == 0:
                case "System.Threading.Tasks.ValueTask" when args.Length == 0:
                    return "Promise<void>";
                case "System.Threading.Tasks.Task":
                case "System.Threading.Tasks.ValueTask":
                    return $"Promise<{ToTypeScript(args[0])}>";

                case "System.Collections.Generic.List":
                case "System.Collections.Generic.IList":
                case "System.Collections.Generic.ICollection":
                case "System.Collections.Generic.IEnumerable":
                case "System.Collections.Generic.IReadOnlyList":
                case "System.Collections.Generic.IReadOnlyCollection":
                case "System.Linq.IOrderedEnumerable":
                    return args.Length == 1 ? $"{ToTypeScript(args[0])}[]" : "any[]";

                // A group is an array that also carries its key.
                case "System.Linq.IGrouping":
                    return args.Length == 2
                        ? $"({ToTypeScript(args[1])}[] & {{ key: {ToTypeScript(args[0])} }})"
                        : "any[]";

                case "System.Collections.Generic.Dictionary":
                case "System.Collections.Generic.IDictionary":
                case "System.Collections.Generic.IReadOnlyDictionary":
                    return args.Length == 2 ? $"Map<{ToTypeScript(args[0])}, {ToTypeScript(args[1])}>" : "Map<any, any>";

                case "System.Collections.Generic.HashSet":
                case "System.Collections.Generic.ISet":
                    return args.Length == 1 ? $"Set<{ToTypeScript(args[0])}>" : "Set<any>";

                case "System.Nullable":
                    return $"{ToTypeScript(args[0])} | null";

                case "System.DateTime":
                case "System.DateTimeOffset":
                    return "Date";

                case "System.Action":
                    return args.Length == 0
                        ? "() => void"
                        : $"({RenderLambdaParams(args, args.Length)}) => void";

                case "System.Func":
                    return args.Length == 1
                        ? $"() => {ToTypeScript(args[0])}"
                        : $"({RenderLambdaParams(args, args.Length - 1)}) => {ToTypeScript(args[^1])}";

                case "System.Predicate":
                    return $"(arg0: {ToTypeScript(args[0])}) => boolean";

                case "System.Comparison":
                    return $"(a: {ToTypeScript(args[0])}, b: {ToTypeScript(args[0])}) => number";
            }

            if (named.TypeKind == TypeKind.Delegate)
            {
                var invoke = named.DelegateInvokeMethod;
                if (invoke is not null)
                {
                    var ps = string.Join(", ", invoke.Parameters.Select(p => $"{p.Name}: {ToTypeScript(p.Type)}"));
                    return $"({ps}) => {ToTypeScript(invoke.ReturnType)}";
                }
            }

            // A user-declared type keeps its own name; generics carry their arguments through.
            var name = named.Name;
            if (args.Length > 0)
                return $"{name}<{string.Join(", ", args.Select(ToTypeScript))}>";
            return name;
        }

        return "any";
    }

    private static string RenderLambdaParams(IReadOnlyList<ITypeSymbol> args, int count)
        => string.Join(", ", Enumerable.Range(0, count).Select(i => $"arg{i}: {ToTypeScript(args[i])}"));

    /// <summary>
    /// The JavaScript constructor a BCL type corresponds to, or null when the type has no
    /// built-in counterpart. Used when a user type extends a framework type — every exception
    /// hierarchy roots at <c>Error</c> once it reaches JavaScript.
    /// </summary>
    public static string? JsConstructorName(INamedTypeSymbol type)
    {
        if (type.ContainingAssembly?.Name is not ("System.Runtime" or "System.Private.CoreLib" or "netstandard"))
            return null;

        return Unbound(type) switch
        {
            "System.Exception" or "System.SystemException" or "System.ApplicationException"
                or "System.InvalidOperationException" or "System.ArgumentException"
                or "System.ArgumentNullException" or "System.ArgumentOutOfRangeException"
                or "System.NotSupportedException" or "System.NotImplementedException"
                or "System.FormatException" or "System.IndexOutOfRangeException"
                or "System.NullReferenceException" or "System.InvalidCastException" => "Error",

            "System.Collections.Generic.List" => "Array",
            "System.Collections.Generic.Dictionary" => "Map",
            "System.Collections.Generic.HashSet" => "Set",
            "System.DateTime" => "Date",
            "System.Object" => "Object",
            _ => null,
        };
    }

    /// <summary>Fully-qualified name with generic arity stripped, e.g. <c>System.Collections.Generic.List</c>.</summary>
    public static string Unbound(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n
            ? n.ToDisplayString() + "."
            : "";

        var containers = "";
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
            containers = outer.Name + "." + containers;

        return ns + containers + type.Name;
    }

    public static bool IsNullableValueType(INamedTypeSymbol type, out ITypeSymbol underlying)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type.TypeArguments.Length == 1)
        {
            underlying = type.TypeArguments[0];
            return true;
        }
        underlying = type;
        return false;
    }
}
