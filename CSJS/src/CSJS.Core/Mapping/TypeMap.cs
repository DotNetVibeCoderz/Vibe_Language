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
                // A queue and a stack are arrays with their next item at index 0.
                case "System.Collections.Generic.Queue":
                case "System.Collections.Generic.Stack":
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

                // A TimeSpan is a number of milliseconds — what subtracting two Dates yields.
                case "System.TimeSpan":
                    return JsShape.Number;

                // A Guid is its lowercase string form.
                case "System.Guid":
                    return JsShape.String;
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

            // A tuple is emitted as an array, so it is declared as a TypeScript tuple.
            if (named.IsTupleType)
                return $"[{string.Join(", ", named.TupleElements.Select(e => ToTypeScript(e.Type)))}]";

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
                case "System.Collections.Generic.Queue":
                case "System.Collections.Generic.Stack":
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

                case "System.TimeSpan":
                    return "number";

                case "System.Guid":
                    return "string";

                case "System.Text.RegularExpressions.Regex":
                    return "RegExp";

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

    // ---------------------------------------------------------------------------------------
    // The inverse direction: TypeScript into C#
    // ---------------------------------------------------------------------------------------
    //
    // The binding generator reads a library's .d.ts and has to land on C# types the emitter will
    // turn back into exactly what the library expects. Keeping the inverse here, beside
    // ToTypeScript, is what makes that hold: a mapping changed in one direction is in plain sight
    // of the other, and BindingGeneratorTests round-trips every entry through ToTypeScript.

    /// <summary>
    /// The C# type for a TypeScript type name from the shared vocabulary, given its type
    /// arguments already rendered as C#. Returns null for a name outside the vocabulary.
    /// <para>
    /// Where several C# types render as one TypeScript type, the inverse picks the one that loses
    /// nothing: <c>number</c> becomes <c>double</c>, never <c>int</c>.
    /// </para>
    /// </summary>
    public static string? FromTypeScript(string name, IReadOnlyList<string> typeArguments)
    {
        string Arg(int i) => i < typeArguments.Count ? typeArguments[i] : "object";

        switch (name)
        {
            case "string": return "string";
            case "number": return "double";
            case "boolean": return "bool";
            case "bigint": return "long";
            case "void":
            case "undefined": return "void";
            case "any":
            case "unknown":
            case "object": return "object";
            case "Date": return "DateTime";

            // Promise<void> is what a C# Task renders as, so it comes back as one.
            case "Promise":
            case "PromiseLike":
                return typeArguments.Count == 0 || Arg(0) == "void" ? "Task" : $"Task<{Arg(0)}>";

            case "Array":
            case "ReadonlyArray":
                return FromTypeScriptArray(Arg(0));

            // A Dictionary is a real Map and a HashSet a real Set, which is why these are exact.
            case "Map":
            case "ReadonlyMap":
                return $"Dictionary<{Arg(0)}, {Arg(1)}>";

            case "Set":
            case "ReadonlySet":
                return $"HashSet<{Arg(0)}>";
        }

        return null;
    }

    /// <summary>The C# type for <c>T[]</c>. An array is a JavaScript array, so no wrapper is needed.</summary>
    public static string FromTypeScriptArray(string elementType)
        => elementType == "void" ? "object[]" : $"{elementType}[]";

    /// <summary>The C# type for <c>T | null</c>, the inverse of how a nullable renders.</summary>
    public static string FromTypeScriptNullable(string type)
        => type.EndsWith('?') || type == "void" ? type : type + "?";

    /// <summary>The C# type for a tuple <c>[A, B]</c>, which the emitter renders as an array.</summary>
    public static string FromTypeScriptTuple(IReadOnlyList<string> elementTypes)
        => $"({string.Join(", ", elementTypes)})";

    /// <summary>
    /// The C# delegate for a function type, the inverse of how <c>Action</c> and <c>Func</c>
    /// render: a <c>void</c> return is an <c>Action</c>, anything else a <c>Func</c>.
    /// </summary>
    public static string FromTypeScriptFunction(IReadOnlyList<string> parameterTypes, string returnType)
    {
        if (returnType == "void")
            return parameterTypes.Count == 0 ? "Action" : $"Action<{string.Join(", ", parameterTypes)}>";

        return parameterTypes.Count == 0
            ? $"Func<{returnType}>"
            : $"Func<{string.Join(", ", parameterTypes)}, {returnType}>";
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
