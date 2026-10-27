namespace CSJS.Core.Bindings;

// The syntax tree for the declaration subset of TypeScript.
//
// It models what a binding can use and nothing more. Constructs with no C# counterpart —
// conditional, mapped, and indexed-access types, `keyof`, `typeof`, template literal types — are
// still parsed, so the parser never loses its place, but they arrive as TsUnsupportedType carrying
// their source text and a reason. The generator turns that into a fallback type and a comment.

// ---------------------------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------------------------

public abstract record TsType;

/// <summary>A named type, possibly qualified (<c>React.ReactNode</c>) and generic.</summary>
public sealed record TsTypeReference(string Name, IReadOnlyList<TsType> Arguments) : TsType
{
    public override string ToString()
        => Arguments.Count == 0 ? Name : $"{Name}<{string.Join(", ", Arguments)}>";
}

public sealed record TsArrayType(TsType Element, bool Readonly = false) : TsType
{
    public override string ToString() => $"{Element}[]";
}

public sealed record TsTupleElement(TsType Type, string? Name, bool Optional, bool Rest);

public sealed record TsTupleType(IReadOnlyList<TsTupleElement> Elements) : TsType
{
    public override string ToString() => $"[{string.Join(", ", Elements.Select(e => (e.Rest ? "..." : "") + e.Type))}]";
}

public sealed record TsUnionType(IReadOnlyList<TsType> Types) : TsType
{
    public override string ToString() => string.Join(" | ", Types);
}

public sealed record TsIntersectionType(IReadOnlyList<TsType> Types) : TsType
{
    public override string ToString() => string.Join(" & ", Types);
}

public sealed record TsFunctionType(
    IReadOnlyList<TsTypeParameter> TypeParameters,
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType,
    bool IsConstructor) : TsType
{
    public override string ToString()
        => $"{(IsConstructor ? "new " : "")}({string.Join(", ", Parameters.Select(p => p.Name + ": " + p.Type))}) => {ReturnType}";
}

/// <summary>An inline object type, <c>{ a: string; b(): void }</c>.</summary>
public sealed record TsObjectType(IReadOnlyList<TsMember> Members) : TsType
{
    public override string ToString() => Members.Count == 0 ? "{}" : "{ … }";
}

/// <summary>A literal type: a string, number, boolean, or bigint constant.</summary>
public sealed record TsLiteralType(TsLiteralKind Kind, string Text) : TsType
{
    public override string ToString() => Kind == TsLiteralKind.String ? $"\"{Text}\"" : Text;
}

public enum TsLiteralKind { String, Number, Boolean, BigInt }

/// <summary>A type predicate return (<c>x is T</c>) — a boolean at runtime.</summary>
public sealed record TsPredicateType(string Parameter, TsType? Type, bool Asserts) : TsType
{
    public override string ToString() => Asserts ? $"asserts {Parameter}" : $"{Parameter} is {Type}";
}

/// <summary>
/// An indexed access, <c>T["key"]</c>. Only a literal key into a known object type resolves —
/// React declares its event handlers this way — and anything else is reported.
/// </summary>
public sealed record TsIndexedAccessType(TsType Object, TsType Index, string Text) : TsType
{
    public override string ToString() => Text;
}

/// <summary>
/// A conditional type, <c>T extends U ? X : Y</c>. C# has nothing like it; the generator maps it
/// through <see cref="False"/>, the general case that refinements such as Vue's <c>UnwrapRef</c>
/// fall through to, and says so.
/// </summary>
public sealed record TsConditionalType(TsType Check, TsType Extends, TsType True, TsType False, string Text) : TsType
{
    public override string ToString() => Text;
}

/// <summary><c>typeof x</c> — the type of a value, which the generator can follow when x is a function.</summary>
public sealed record TsTypeQuery(string Name, string Text) : TsType
{
    public override string ToString() => Text;
}

/// <summary>A type the generator cannot express in C#; kept as text so it can be reported.</summary>
public sealed record TsUnsupportedType(string Text, string Reason) : TsType
{
    public override string ToString() => Text;
}

// ---------------------------------------------------------------------------------------------
// Parameters and members
// ---------------------------------------------------------------------------------------------

public sealed record TsTypeParameter(string Name, TsType? Constraint, TsType? Default);

public sealed record TsParameter(string Name, TsType Type, bool Optional, bool Rest);

[Flags]
public enum TsModifiers
{
    None = 0,
    Static = 1,
    Readonly = 2,
    Optional = 4,
    Abstract = 8,
    Protected = 16,
    Private = 32,
}

public abstract record TsMember(string Name, string? Doc, TsModifiers Modifiers)
{
    public bool IsStatic => (Modifiers & TsModifiers.Static) != 0;
    public bool IsOptional => (Modifiers & TsModifiers.Optional) != 0;
    public bool IsReadonly => (Modifiers & TsModifiers.Readonly) != 0;
    public bool IsPrivate => (Modifiers & TsModifiers.Private) != 0;
    public bool IsProtected => (Modifiers & TsModifiers.Protected) != 0;
}

public sealed record TsPropertySignature(string Name, string? Doc, TsModifiers Modifiers, TsType Type)
    : TsMember(Name, Doc, Modifiers);

public sealed record TsMethodSignature(
    string Name,
    string? Doc,
    TsModifiers Modifiers,
    IReadOnlyList<TsTypeParameter> TypeParameters,
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType) : TsMember(Name, Doc, Modifiers);

/// <summary>A class constructor, or an interface construct signature (<c>new (…): T</c>).</summary>
public sealed record TsConstructSignature(
    string? Doc,
    TsModifiers Modifiers,
    IReadOnlyList<TsTypeParameter> TypeParameters,
    IReadOnlyList<TsParameter> Parameters,
    TsType? ReturnType) : TsMember("constructor", Doc, Modifiers);

/// <summary>A call signature, <c>(x: T): R</c>, making the containing type callable.</summary>
public sealed record TsCallSignature(
    string? Doc,
    IReadOnlyList<TsTypeParameter> TypeParameters,
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType) : TsMember("()", Doc, TsModifiers.None);

public sealed record TsIndexSignature(string? Doc, TsModifiers Modifiers, string KeyName, TsType KeyType, TsType ValueType)
    : TsMember("[]", Doc, Modifiers);

/// <summary>A get or set accessor. The generator folds a pair into one property.</summary>
public sealed record TsAccessor(string Name, string? Doc, TsModifiers Modifiers, bool IsSetter, TsType Type)
    : TsMember(Name, Doc, Modifiers);

/// <summary>A member the parser recognised but cannot bind, kept so the output can say so.</summary>
public sealed record TsSkippedMember(string Name, string Reason) : TsMember(Name, null, TsModifiers.None);

// ---------------------------------------------------------------------------------------------
// Declarations
// ---------------------------------------------------------------------------------------------

public abstract record TsDeclaration
{
    public string Name { get; init; } = "";
    public string? Doc { get; init; }
    public bool Exported { get; init; }
    public bool IsDefault { get; init; }
}

public sealed record TsClassDeclaration : TsDeclaration
{
    public IReadOnlyList<TsTypeParameter> TypeParameters { get; init; } = Array.Empty<TsTypeParameter>();
    public TsTypeReference? Extends { get; init; }
    public IReadOnlyList<TsMember> Members { get; init; } = Array.Empty<TsMember>();
    public bool Abstract { get; init; }
}

public sealed record TsInterfaceDeclaration : TsDeclaration
{
    public IReadOnlyList<TsTypeParameter> TypeParameters { get; init; } = Array.Empty<TsTypeParameter>();
    public IReadOnlyList<TsTypeReference> Extends { get; init; } = Array.Empty<TsTypeReference>();
    public IReadOnlyList<TsMember> Members { get; init; } = Array.Empty<TsMember>();
}

public sealed record TsFunctionDeclaration : TsDeclaration
{
    public IReadOnlyList<TsTypeParameter> TypeParameters { get; init; } = Array.Empty<TsTypeParameter>();
    public IReadOnlyList<TsParameter> Parameters { get; init; } = Array.Empty<TsParameter>();
    public TsType ReturnType { get; init; } = new TsTypeReference("void", Array.Empty<TsType>());
}

public sealed record TsVariableDeclaration : TsDeclaration
{
    public TsType? Type { get; init; }
    public bool IsConst { get; init; }

    /// <summary>The initializer of a <c>const x = "literal"</c>, which fixes the type.</summary>
    public TsLiteralType? Literal { get; init; }
}

public sealed record TsEnumMember(string Name, string? Doc, string? Value, bool IsString);

public sealed record TsEnumDeclaration : TsDeclaration
{
    public IReadOnlyList<TsEnumMember> Members { get; init; } = Array.Empty<TsEnumMember>();
    public bool IsConst { get; init; }
}

public sealed record TsTypeAliasDeclaration : TsDeclaration
{
    public IReadOnlyList<TsTypeParameter> TypeParameters { get; init; } = Array.Empty<TsTypeParameter>();
    public TsType Type { get; init; } = new TsTypeReference("any", Array.Empty<TsType>());
}

public enum TsNamespaceKind { Namespace, AmbientModule, Global }

/// <summary><c>namespace N { }</c>, <c>declare module "m" { }</c>, or <c>declare global { }</c>.</summary>
public sealed record TsNamespaceDeclaration : TsDeclaration
{
    public TsNamespaceKind Kind { get; init; }
    public IReadOnlyList<TsDeclaration> Body { get; init; } = Array.Empty<TsDeclaration>();
}

/// <summary><c>export = X;</c></summary>
public sealed record TsExportAssignment : TsDeclaration;

/// <summary><c>export as namespace X;</c> — the UMD global name.</summary>
public sealed record TsExportAsNamespace : TsDeclaration;

public sealed record TsExportSpecifier(string Name, string Alias);

/// <summary>
/// <c>export { a, b as c }</c>, <c>export { … } from "m"</c>, <c>export * from "m"</c>, or
/// <c>export * as ns from "m"</c>. <see cref="TsDeclaration.Name"/> holds the namespace alias
/// of the last form.
/// </summary>
public sealed record TsExportDeclaration : TsDeclaration
{
    public IReadOnlyList<TsExportSpecifier> Specifiers { get; init; } = Array.Empty<TsExportSpecifier>();
    public string? FromModule { get; init; }
    public bool IsStar { get; init; }
}

/// <summary><c>import X, { a as b } from "m"</c>, <c>import * as X from "m"</c>, <c>import X = require("m")</c>.</summary>
public sealed record TsImportDeclaration : TsDeclaration
{
    public string Module { get; init; } = "";
    public string? DefaultName { get; init; }
    public string? NamespaceName { get; init; }
    public IReadOnlyList<TsExportSpecifier> Specifiers { get; init; } = Array.Empty<TsExportSpecifier>();
}

/// <summary>A declaration the parser could not bind, reported in the output rather than dropped.</summary>
public sealed record TsSkippedDeclaration : TsDeclaration
{
    public string Reason { get; init; } = "";
}

/// <summary>A parsed declaration file.</summary>
public sealed record TsSourceFile(string Path, IReadOnlyList<TsDeclaration> Declarations)
{
    /// <summary>True when the file has a top-level import or export, making it a module rather than a global script.</summary>
    public bool IsModule { get; init; }
}
