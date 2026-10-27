using System.Reflection;
using CSJS.Core.Mapping;

namespace CSJS.Core.Bindings;

public sealed partial class BindingGenerator
{
    /// <summary>
    /// Which side supplies a value. C# code supplies parameters and receives returns; inside a
    /// callback the two swap. It decides the fallback for a type C# cannot name: something C#
    /// passes in can be any <c>object</c>, while something C# reads back is most useful as a
    /// <c>JsObject</c>, whose properties it can still reach.
    /// </summary>
    private enum Flow { In, Out }

    private static Flow Flip(Flow flow) => flow == Flow.In ? Flow.Out : Flow.In;

    /// <summary>Everything type mapping needs to know about where it is.</summary>
    private sealed class TypeContext
    {
        public required Scope Scope { get; init; }

        /// <summary>TypeScript type parameter names in scope, and the C# names they map to.</summary>
        public Dictionary<string, string> TypeParameters { get; init; } = new(StringComparer.Ordinal);

        /// <summary>What <c>this</c> means as a type, inside a class or interface.</summary>
        public string? ThisType { get; init; }

        /// <summary>Approximations made while mapping, written as notes beside the member.</summary>
        public required List<string> Notes { get; init; }

        /// <summary>A base name for any inline object type that needs a class of its own.</summary>
        public string SyntheticName { get; set; } = "Options";

        public HashSet<object> Expanding { get; init; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>A copy whose notes go nowhere, for probing what a type maps to.</summary>
        public TypeContext WithScratchNotes()
            => new()
            {
                Scope = Scope,
                TypeParameters = TypeParameters,
                ThisType = ThisType,
                Notes = new List<string>(),
                SyntheticName = SyntheticName,
                Expanding = Expanding,
            };

        public TypeContext With(Scope? scope = null, Dictionary<string, string>? typeParameters = null, string? syntheticName = null)
            => new()
            {
                Scope = scope ?? Scope,
                TypeParameters = typeParameters ?? TypeParameters,
                ThisType = ThisType,
                Notes = Notes,
                SyntheticName = syntheticName ?? SyntheticName,
                Expanding = Expanding,
            };
    }

    /// <summary>
    /// A type carried into another scope by alias substitution: the alias body resolves in the
    /// alias's scope, but its arguments must still resolve where the alias was used.
    /// </summary>
    private sealed record ScopedType(TsType Type, TypeContext Context) : TsType
    {
        public override string ToString() => Type.ToString();
    }

    private void Note(TypeContext context, string message)
    {
        context.Notes.Add(message);
        Approximate(message);
    }

    private static string Fallback(Flow flow) => flow == Flow.Out ? "JsObject" : "object";

    private static readonly TsType AnyType = new TsTypeReference("any", Array.Empty<TsType>());

    // -------------------------------------------------------------------------------------------
    // Entry points
    // -------------------------------------------------------------------------------------------

    /// <summary>Maps a return type: <c>void</c>, <c>undefined</c>, and <c>never</c> mean no value.</summary>
    private string MapReturn(TsType type, TypeContext context)
    {
        var probe = context;
        var resolved = Unwrap(type, ref probe);
        if (resolved is TsTypeReference { Name: "void" or "undefined" or "never", Arguments.Count: 0 }) return "void";
        if (resolved is TsPredicateType { Asserts: true }) return "void";
        return Map(type, Flow.Out, context);
    }

    private int _mapDepth;

    private string Map(TsType type, Flow flow, TypeContext context)
    {
        // Aliases may refer to themselves through a union or a type argument — React's
        // ReactNode includes Iterable<ReactNode> — and expansion has to stop somewhere.
        if (_mapDepth > 24)
        {
            Note(context, $"recursive type '{Abbreviate(type.ToString())}' mapped to {Fallback(flow)}");
            return Fallback(flow);
        }

        _mapDepth++;
        try
        {
            return MapCore(type, flow, context);
        }
        finally
        {
            _mapDepth--;
        }
    }

    private string MapCore(TsType type, Flow flow, TypeContext context)
    {
        switch (type)
        {
            case ScopedType scoped:
                return Map(scoped.Type, flow, scoped.Context.With());

            case TsTypeReference reference:
                return MapReference(reference, flow, context);

            case TsArrayType array:
            {
                var element = Map(array.Element, flow, context);
                return TypeMap.FromTypeScriptArray(element);
            }

            case TsTupleType tuple:
                return MapTuple(tuple, flow, context);

            case TsUnionType union:
                return MapUnion(union, flow, context);

            case TsIntersectionType intersection:
                return MapIntersection(intersection, flow, context);

            case TsFunctionType function:
                return MapFunction(function, flow, context);

            case TsObjectType objectType:
                return MapObjectType(objectType, flow, context);

            case TsLiteralType literal:
                return literal.Kind switch
                {
                    TsLiteralKind.String => "string",
                    TsLiteralKind.Number => "double",
                    TsLiteralKind.Boolean => "bool",
                    _ => "long",
                };

            case TsPredicateType predicate:
                return predicate.Asserts ? "void" : "bool";

            case TsConditionalType conditional:
                Note(context, $"conditional type '{Abbreviate(conditional.Text)}' mapped through its general branch");
                return Map(GeneralBranch(conditional), flow, context);

            case TsTypeQuery query:
                Note(context, $"type query '{Abbreviate(query.Text)}' mapped to {Fallback(flow)}");
                return Fallback(flow);

            case TsIndexedAccessType indexed:
                if (ResolveIndexedAccess(indexed, context) is { } member) return Map(member.Type, flow, member.Context);
                Note(context, $"indexed access type '{Abbreviate(indexed.Text)}' has no C# equivalent; mapped to {Fallback(flow)}");
                return Fallback(flow);

            case TsUnsupportedType { Reason: "keyof type" } keyOf:
                // Property keys are strings in all but the rarest libraries.
                Note(context, $"'{Abbreviate(keyOf.Text)}' mapped to string");
                return "string";

            case TsUnsupportedType unsupported:
                Note(context,$"{unsupported.Reason} '{Abbreviate(unsupported.Text)}' has no C# equivalent; mapped to {Fallback(flow)}");
                return Fallback(flow);
        }

        return Fallback(flow);
    }

    // -------------------------------------------------------------------------------------------
    // References
    // -------------------------------------------------------------------------------------------

    private string MapReference(TsTypeReference reference, Flow flow, TypeContext context)
    {
        var name = reference.Name;

        if (!name.Contains('.'))
        {
            if (context.TypeParameters.TryGetValue(name, out var typeParameter)) return typeParameter;

            switch (name)
            {
                case "this":
                    return context.ThisType ?? Fallback(flow);
                case "null":
                case "undefined":
                case "never":
                case "void":
                    return flow == Flow.Out ? "object" : "object";
                case "symbol":
                    Note(context, "'symbol' has no C# equivalent; mapped to object");
                    return "object";
                case "Function":
                    Note(context, "'Function' is untyped; mapped to Delegate");
                    return "Delegate";
                case "Object":
                    return "object";
            }
        }

        // A library's own declarations win over the shared vocabulary, exactly as in TypeScript.
        var entity = LookupQualified(context.Scope, name);
        if (entity is not null)
        {
            var mapped = MapEntityReference(entity, reference, flow, context);
            if (mapped is not null) return mapped;
        }

        var simple = name.Contains('.') ? null : name;
        if (simple is not null)
        {
            if (reference.Arguments.Count > 0 && simple is "Partial" or "Readonly" or "Required" or "Awaited" or "NonNullable" or "NoInfer")
            {
                if (simple is "Partial")
                    Note(context, $"'{Abbreviate(reference.ToString())}' mapped to its argument; every property can still be left out of an object literal");
                return Map(reference.Arguments[0], flow, context);
            }

            var arguments = reference.Arguments.Select(a => MapTypeArgument(a, flow, context)).ToList();
            if (TypeMap.FromTypeScript(simple, arguments) is { } vocabulary)
                return vocabulary == "void" ? "object" : vocabulary;

            switch (simple)
            {
                case "Iterable" or "IterableIterator" or "ArrayLike" or "Iterator" or "Generator" or "AsyncIterable":
                    if (flow == Flow.In && simple is "Iterable" or "ArrayLike")
                        return TypeMap.FromTypeScriptArray(arguments.Count > 0 ? arguments[0] : "object");
                    Note(context, $"'{simple}' is not an array; mapped to JsObject");
                    return "JsObject";

                case "Record":
                    Note(context, $"'{Abbreviate(reference.ToString())}' is a plain object, not a Map; mapped to JsObject");
                    return "JsObject";

                case "WeakMap" or "WeakSet" or "WeakRef" or "Error" or "RegExp" or "ArrayBuffer" or "Uint8Array" or "AbortSignal":
                    Note(context, $"'{simple}' has no binding; mapped to JsObject");
                    return "JsObject";
            }

            if (KnownBrowserType(simple) is { } browser) return browser;
        }

        Note(context, $"unknown type '{Abbreviate(reference.ToString())}' mapped to {Fallback(flow)}");
        return Fallback(flow);
    }

    /// <summary>A generic argument; <c>void</c> is not a C# type argument, so it becomes <c>object</c>.</summary>
    private string MapTypeArgument(TsType argument, Flow flow, TypeContext context)
    {
        var probe = context;
        var resolved = Unwrap(argument, ref probe);
        if (resolved is TsTypeReference { Name: "void" or "undefined", Arguments.Count: 0 }) return "void";
        var mapped = Map(argument, flow, context);
        return mapped == "void" ? "object" : mapped;
    }

    private string? MapEntityReference(Entity entity, TsTypeReference reference, Flow flow, TypeContext context)
    {
        var kind = Classify(entity);
        switch (kind)
        {
            case EntityKind.Class:
            case EntityKind.Interface:
            case EntityKind.ObjectLiteral:
            case EntityKind.NumericEnum:
            {
                var csName = RequireType(entity);
                var parameters = TypeParametersOf(entity);
                if (parameters.Count == 0) return csName;

                var arguments = new List<string>();
                var substitution = new Dictionary<string, TsType>(StringComparer.Ordinal);
                for (var i = 0; i < parameters.Count; i++)
                {
                    string argument;
                    if (i < reference.Arguments.Count)
                    {
                        argument = MapTypeArgument(reference.Arguments[i], flow, context);
                        substitution[parameters[i].Name] = new ScopedType(reference.Arguments[i], context.With());
                    }
                    else if (parameters[i].Default is { } fallback)
                    {
                        // A default may mention earlier parameters, so it resolves in the entity's
                        // own scope with those already bound.
                        var bound = Substitute(fallback, substitution);
                        argument = MapTypeArgument(bound, flow, ContextFor(entity.Owner, context.Notes));
                    }
                    else
                    {
                        argument = "object";
                    }
                    arguments.Add(argument == "void" ? "object" : argument);
                }
                return $"{csName}<{string.Join(", ", arguments)}>";
            }

            case EntityKind.StringEnum:
                return "string";

            case EntityKind.JsObjectLike:
                return "JsObject";

            case EntityKind.Callable:
            {
                var signature = CallSignaturesOf(entity).First();
                var substitution = BindTypeArguments(TypeParametersOf(entity), reference.Arguments, context);
                var function = new TsFunctionType(signature.TypeParameters, signature.Parameters, signature.ReturnType, false);
                return Map(Substitute(function, substitution), flow, ContextFor(entity.Owner, context.Notes));
            }

            case EntityKind.Alias:
            {
                var alias = entity.Alias!;
                if (context.Expanding.Contains(alias))
                {
                    Note(context, $"recursive type '{entity.Name}' mapped to {Fallback(flow)}");
                    return Fallback(flow);
                }

                var substitution = BindTypeArguments(alias.TypeParameters, reference.Arguments, context);
                var body = Substitute(alias.Type, substitution);
                var aliasContext = ContextFor(entity.Owner, context.Notes, Expanded(context, alias));
                aliasContext.SyntheticName = CsTypeName(entity.Name);
                return Map(body, flow, aliasContext);
            }

            case EntityKind.Namespace:
                return null;

            default:
                Note(context, $"'{entity.Name}' is a value, not a type; mapped to {Fallback(flow)}");
                return Fallback(flow);
        }
    }

    /// <summary>
    /// The aliases being expanded, plus one more. Each expansion gets its own set, so a cycle is
    /// caught on its first repeat along one path without blocking the same alias elsewhere.
    /// </summary>
    private static HashSet<object> Expanded(TypeContext context, object alias)
        => new(context.Expanding, ReferenceEqualityComparer.Instance) { alias };

    private TypeContext ContextFor(Scope scope, List<string> notes, HashSet<object>? expanding = null)
        => new() { Scope = scope, Notes = notes, Expanding = expanding ?? new HashSet<object>(ReferenceEqualityComparer.Instance) };

    private static Dictionary<string, TsType> BindTypeArguments(
        IReadOnlyList<TsTypeParameter> parameters, IReadOnlyList<TsType> arguments, TypeContext context)
    {
        var substitution = new Dictionary<string, TsType>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
        {
            var argument = i < arguments.Count
                ? new ScopedType(arguments[i], context.With())
                : parameters[i].Default is { } fallback ? Substitute(fallback, substitution) : AnyType;
            substitution[parameters[i].Name] = argument;
        }
        return substitution;
    }

    /// <summary>
    /// <c>{ m(e: E): void }["m"]</c> is the member's own type. That is the one indexed access
    /// with a C# answer, and React's every event handler is declared through it.
    /// </summary>
    private Alternative? ResolveIndexedAccess(TsIndexedAccessType indexed, TypeContext context)
    {
        var indexContext = context;
        if (Unwrap(indexed.Index, ref indexContext) is not TsLiteralType { Kind: TsLiteralKind.String } key) return null;

        var objectContext = context;
        var target = Unwrap(indexed.Object, ref objectContext);
        IEnumerable<TsMember> members = target switch
        {
            TsObjectType objectType => objectType.Members,
            TsTypeReference reference when LookupQualified(objectContext.Scope, reference.Name) is { } entity
                => entity.Interfaces.SelectMany(i => i.Members),
            _ => Array.Empty<TsMember>(),
        };

        foreach (var member in members)
        {
            if (member.Name != key.Text) continue;
            switch (member)
            {
                case TsPropertySignature property:
                    return new Alternative(property.Type, objectContext);
                case TsMethodSignature method:
                    return new Alternative(new TsFunctionType(method.TypeParameters, method.Parameters, method.ReturnType, false), objectContext);
            }
        }

        return null;
    }

    /// <summary>
    /// The branch of a conditional type that stands for the general case. Conditional types in
    /// library typings mostly refine an answer — unwrap a ref, pick a narrower overload result —
    /// and fall through to the plain answer in the false branch. When that branch is
    /// <c>never</c>, the condition is a filter and the true branch is the answer.
    /// </summary>
    private static TsType GeneralBranch(TsConditionalType conditional)
        => conditional.False is TsTypeReference { Name: "never", Arguments.Count: 0 } ? conditional.True : conditional.False;

    /// <summary>Resolves alias references and scoped wrappers until something structural remains.</summary>
    private TsType Unwrap(TsType type, ref TypeContext context)
    {
        for (var depth = 0; depth < 16; depth++)
        {
            switch (type)
            {
                case ScopedType scoped:
                    context = scoped.Context.With();
                    type = scoped.Type;
                    continue;

                case TsConditionalType conditional:
                    type = GeneralBranch(conditional);
                    continue;

                case TsTypeReference reference when !context.TypeParameters.ContainsKey(reference.Name):
                {
                    var entity = LookupQualified(context.Scope, reference.Name);
                    if (entity is null) return type;

                    var kind = Classify(entity);
                    if (kind == EntityKind.Alias)
                    {
                        if (context.Expanding.Contains(entity.Alias!)) return type;
                        var substitution = BindTypeArguments(entity.Alias!.TypeParameters, reference.Arguments, context);
                        type = Substitute(entity.Alias!.Type, substitution);
                        context = ContextFor(entity.Owner, context.Notes, Expanded(context, entity.Alias!));
                        continue;
                    }

                    if (kind == EntityKind.Callable)
                    {
                        var signature = CallSignaturesOf(entity).First();
                        var substitution = BindTypeArguments(TypeParametersOf(entity), reference.Arguments, context);
                        type = Substitute(new TsFunctionType(signature.TypeParameters, signature.Parameters, signature.ReturnType, false), substitution);
                        context = ContextFor(entity.Owner, context.Notes, context.Expanding);
                        continue;
                    }

                    return type;
                }

                default:
                    return type;
            }
        }

        return type;
    }

    // -------------------------------------------------------------------------------------------
    // Unions and intersections
    // -------------------------------------------------------------------------------------------

    /// <summary>One alternative of a union, with the context it resolves in.</summary>
    private readonly record struct Alternative(TsType Type, TypeContext Context);

    /// <summary>
    /// Flattens a union into its distinct alternatives: aliases resolved, nested unions spread,
    /// <c>null</c> and <c>undefined</c> folded into <paramref name="nullable"/>, and literal
    /// members collapsed into their primitive.
    /// </summary>
    private List<Alternative> Alternatives(TsType type, TypeContext context, out bool nullable, out bool hasVoid)
    {
        nullable = false;
        hasVoid = false;
        var result = new List<Alternative>();
        var literalKinds = new HashSet<TsLiteralKind>();
        Collect(type, context, result, literalKinds, ref nullable, ref hasVoid, 0);

        foreach (var kind in literalKinds)
        {
            var primitive = kind switch
            {
                TsLiteralKind.String => "string",
                TsLiteralKind.Number => "number",
                TsLiteralKind.Boolean => "boolean",
                _ => "bigint",
            };
            if (!result.Any(a => a.Type is TsTypeReference r && r.Name == primitive))
                result.Add(new Alternative(new TsTypeReference(primitive, Array.Empty<TsType>()), context));
        }

        return result;
    }

    private void Collect(TsType type, TypeContext context, List<Alternative> into, HashSet<TsLiteralKind> literals,
        ref bool nullable, ref bool hasVoid, int depth)
    {
        if (depth > 12)
        {
            into.Add(new Alternative(type, context));
            return;
        }

        var resolved = Unwrap(type, ref context);
        switch (resolved)
        {
            case TsUnionType union:
                foreach (var member in union.Types)
                    Collect(member, context, into, literals, ref nullable, ref hasVoid, depth + 1);
                return;

            case TsTypeReference { Name: "null" or "undefined", Arguments.Count: 0 }:
                nullable = true;
                return;

            case TsTypeReference { Name: "void", Arguments.Count: 0 }:
                hasVoid = true;
                nullable = true;
                return;

            case TsTypeReference { Name: "never", Arguments.Count: 0 }:
                return;

            case TsLiteralType literal:
                literals.Add(literal.Kind);
                return;

            // An object type whose every member was unbindable exists only to brand a type;
            // it contributes nothing a C# caller could produce.
            case TsObjectType objectType when objectType.Members.Count > 0 && objectType.Members.All(m => m is TsSkippedMember):
                return;

            case TsTypeReference { Name: "boolean", Arguments.Count: 0 }:
                literals.Add(TsLiteralKind.Boolean);
                return;

            case TsIntersectionType intersection when SimplifyIntersection(intersection, context) is { } single:
                Collect(single, context, into, literals, ref nullable, ref hasVoid, depth + 1);
                return;
        }

        into.Add(new Alternative(resolved, context));
    }

    private string MapUnion(TsUnionType union, Flow flow, TypeContext context)
    {
        var alternatives = Alternatives(union, context, out var nullable, out var hasVoid);
        if (alternatives.Count == 0) return hasVoid ? "void" : "object";

        var pairs = alternatives.Select(a => (Alternative: a, CSharp: Map(a.Type, flow, a.Context))).ToList();

        // `T | Promise<T>` — a value now or later. The synchronous form is the one C# can
        // write directly, and JavaScript accepts it either way.
        var synchronous = pairs
            .Where(p => !(p.CSharp.StartsWith("Task<", StringComparison.Ordinal)
                          && pairs.Any(q => $"Task<{q.CSharp}>" == p.CSharp)))
            .ToList();
        if (synchronous.Count < pairs.Count)
        {
            Note(context, $"union '{Abbreviate(union.ToString())}' mapped to its synchronous form");
            pairs = synchronous;
            alternatives = pairs.Select(p => p.Alternative).ToList();
        }

        var mapped = pairs.Select(p => p.CSharp).Distinct().ToList();
        if (mapped.Count == 1)
            return nullable ? TypeMap.FromTypeScriptNullable(mapped[0]) : mapped[0];

        // A value or a function producing it — `S | ((prev: S) => S)` — is most usefully the value.
        var values = alternatives.Where(a => a.Type is not TsFunctionType).ToList();
        if (values.Count == 1 && values.Count < alternatives.Count)
        {
            var chosen = Map(values[0].Type, flow, values[0].Context);
            Note(context, $"union '{Abbreviate(union.ToString())}' mapped to its value form {chosen}");
            return nullable ? TypeMap.FromTypeScriptNullable(chosen) : chosen;
        }

        var fallback = nullable ? "object?" : "object";
        Note(context, $"union '{Abbreviate(union.ToString())}' has no single C# type; mapped to {fallback}");
        return fallback;
    }

    /// <summary>
    /// The one meaningful member of an intersection, when there is one: <c>string &amp; {}</c> is a
    /// string, and <c>Props &amp; { children?: Node }</c> is mostly <c>Props</c>.
    /// </summary>
    private TsType? SimplifyIntersection(TsIntersectionType intersection, TypeContext context)
    {
        var parts = intersection.Types
            .Where(t => t is not TsObjectType { Members.Count: 0 })
            .Where(t => t is not TsTypeReference { Name: "unknown" or "object" or "{}" })
            .ToList();
        return parts.Count == 1 ? parts[0] : null;
    }

    private string MapIntersection(TsIntersectionType intersection, Flow flow, TypeContext context)
    {
        if (SimplifyIntersection(intersection, context) is { } single) return Map(single, flow, context);

        // Prefer a member that is one of this library's own named types.
        foreach (var part in intersection.Types)
        {
            if (part is TsTypeReference reference && LookupQualified(context.Scope, reference.Name) is { } entity
                && Classify(entity) is EntityKind.Class or EntityKind.Interface or EntityKind.ObjectLiteral)
            {
                var chosen = Map(part, flow, context);
                Note(context, $"intersection '{Abbreviate(intersection.ToString())}' mapped to {chosen}");
                return chosen;
            }
        }

        Note(context, $"intersection '{Abbreviate(intersection.ToString())}' mapped to {Fallback(flow)}");
        return Fallback(flow);
    }

    // -------------------------------------------------------------------------------------------
    // Functions, tuples, and object types
    // -------------------------------------------------------------------------------------------

    private string MapFunction(TsFunctionType function, Flow flow, TypeContext context)
    {
        if (function.IsConstructor)
        {
            Note(context, $"constructor type '{Abbreviate(function.ToString())}' mapped to {Fallback(flow)}");
            return Fallback(flow);
        }

        // A delegate type cannot be generic at the point of use, so the function's own type
        // parameters are erased to object.
        var inner = context;
        if (function.TypeParameters.Count > 0)
        {
            var parameters = new Dictionary<string, string>(context.TypeParameters, StringComparer.Ordinal);
            foreach (var tp in function.TypeParameters) parameters[tp.Name] = "object";
            inner = context.With(typeParameters: parameters);
            Note(context, $"generic callback '{Abbreviate(function.ToString())}' has its type parameters erased to object");
        }

        var parameterTypes = new List<string>();
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Name == "this") continue;
            if (parameter.Rest)
            {
                Note(context, $"callback rest parameter '...{parameter.Name}' dropped; the callback receives only the leading arguments");
                break;
            }
            var mapped = Map(parameter.Type, Flip(flow), inner);
            if (mapped == "void") mapped = "object";
            parameterTypes.Add(parameter.Optional ? TypeMap.FromTypeScriptNullable(mapped) : mapped);
        }

        if (parameterTypes.Count > 16)
        {
            Note(context, "callback with more than 16 parameters mapped to Delegate");
            return "Delegate";
        }

        var returnType = MapReturn(function.ReturnType, inner);
        return TypeMap.FromTypeScriptFunction(parameterTypes, returnType);
    }

    private string MapTuple(TsTupleType tuple, Flow flow, TypeContext context)
    {
        var elements = tuple.Elements;
        if (elements.Count == 1 && elements[0].Rest)
            return Map(elements[0].Type, flow, context);

        if (elements.Count is < 2 or > 7 || elements.Any(e => e.Rest))
        {
            var element = elements.Count == 1 ? Map(elements[0].Type, flow, context) : "object";
            Note(context, $"tuple '{Abbreviate(tuple.ToString())}' mapped to an array");
            return TypeMap.FromTypeScriptArray(element);
        }

        var mapped = elements.Select(e =>
        {
            var type = MapTypeArgument(e.Type, flow, context);
            if (type == "void") type = "object";
            if (e.Optional) type = TypeMap.FromTypeScriptNullable(type);
            return e.Name is { } name && IsPlainIdentifier(name) && !CSharpKeywords.Contains(name) ? $"{type} {name}" : type;
        }).ToList();

        // C# rejects a tuple whose element names are duplicated.
        if (mapped.Select(m => m.Split(' ').Last()).Distinct().Count() != mapped.Count)
            mapped = elements.Select(e => MapTypeArgument(e.Type, flow, context)).ToList();

        return TypeMap.FromTypeScriptTuple(mapped);
    }

    private string MapObjectType(TsObjectType objectType, Flow flow, TypeContext context)
    {
        var members = objectType.Members.Where(m => m is not TsSkippedMember).ToList();
        if (members.Count == 0) return "object";

        var calls = members.OfType<TsCallSignature>().ToList();
        if (calls.Count > 0 && members.All(m => m is TsCallSignature || m.IsOptional))
        {
            if (calls.Count > 1 || calls.Count < members.Count)
                Note(context, $"callable object type bound through its first call signature");
            var call = calls[0];
            return Map(new TsFunctionType(call.TypeParameters, call.Parameters, call.ReturnType, false), flow, context);
        }

        if (members.All(m => m is TsIndexSignature))
        {
            Note(context, "object type with only an index signature mapped to JsObject");
            return "JsObject";
        }

        if (members.Any(m => m is TsConstructSignature))
        {
            Note(context, $"constructor object type mapped to {Fallback(flow)}");
            return Fallback(flow);
        }

        return RequireSynthetic(objectType, context);
    }

    // -------------------------------------------------------------------------------------------
    // Substitution
    // -------------------------------------------------------------------------------------------

    /// <summary>Replaces type parameter references with the types bound to them.</summary>
    private static TsType Substitute(TsType type, IReadOnlyDictionary<string, TsType> map)
    {
        if (map.Count == 0) return type;

        return type switch
        {
            TsTypeReference { Arguments.Count: 0 } r when map.TryGetValue(r.Name, out var bound) => bound,
            TsTypeReference r => r with { Arguments = r.Arguments.Select(a => Substitute(a, map)).ToList() },
            TsArrayType a => a with { Element = Substitute(a.Element, map) },
            TsTupleType t => t with { Elements = t.Elements.Select(e => e with { Type = Substitute(e.Type, map) }).ToList() },
            TsUnionType u => u with { Types = u.Types.Select(t => Substitute(t, map)).ToList() },
            TsIntersectionType i => i with { Types = i.Types.Select(t => Substitute(t, map)).ToList() },
            TsFunctionType f => SubstituteFunction(f, map),
            TsObjectType o => o with { Members = o.Members.Select(m => SubstituteMember(m, map)).ToList() },
            TsPredicateType p when p.Type is not null => p with { Type = Substitute(p.Type, map) },
            TsIndexedAccessType ia => ia with { Object = Substitute(ia.Object, map), Index = Substitute(ia.Index, map) },
            TsConditionalType c => c with
            {
                Check = Substitute(c.Check, map),
                Extends = Substitute(c.Extends, map),
                True = Substitute(c.True, map),
                False = Substitute(c.False, map),
            },
            _ => type,
        };
    }

    private static TsType SubstituteFunction(TsFunctionType function, IReadOnlyDictionary<string, TsType> map)
    {
        var inner = Shadow(map, function.TypeParameters);
        return function with
        {
            Parameters = function.Parameters.Select(p => p with { Type = Substitute(p.Type, inner) }).ToList(),
            ReturnType = Substitute(function.ReturnType, inner),
        };
    }

    private static TsMember SubstituteMember(TsMember member, IReadOnlyDictionary<string, TsType> map) => member switch
    {
        TsPropertySignature p => p with { Type = Substitute(p.Type, map) },
        TsMethodSignature m => m with
        {
            Parameters = m.Parameters.Select(p => p with { Type = Substitute(p.Type, Shadow(map, m.TypeParameters)) }).ToList(),
            ReturnType = Substitute(m.ReturnType, Shadow(map, m.TypeParameters)),
        },
        TsCallSignature c => c with
        {
            Parameters = c.Parameters.Select(p => p with { Type = Substitute(p.Type, Shadow(map, c.TypeParameters)) }).ToList(),
            ReturnType = Substitute(c.ReturnType, Shadow(map, c.TypeParameters)),
        },
        TsIndexSignature i => i with { ValueType = Substitute(i.ValueType, map) },
        TsAccessor a => a with { Type = Substitute(a.Type, map) },
        _ => member,
    };

    private static IReadOnlyDictionary<string, TsType> Shadow(IReadOnlyDictionary<string, TsType> map, IReadOnlyList<TsTypeParameter> shadowing)
    {
        if (shadowing.Count == 0 || !shadowing.Any(tp => map.ContainsKey(tp.Name))) return map;
        var copy = new Dictionary<string, TsType>(map, StringComparer.Ordinal);
        foreach (var tp in shadowing) copy.Remove(tp.Name);
        return copy;
    }

    // -------------------------------------------------------------------------------------------
    // Bindings that already ship with CSJS
    // -------------------------------------------------------------------------------------------

    private static readonly Lazy<Dictionary<string, string>> BrowserTypes = new(() =>
    {
        // Read straight off CSJS.Interop, so a binding added there is picked up here with no
        // second table to keep in step.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in typeof(JsGlobalAttribute).Assembly.GetExportedTypes())
        {
            if (type.IsAbstract && type.IsSealed) continue; // static classes are not types a value can have
            var attribute = type.GetCustomAttribute<JsGlobalAttribute>();
            if (attribute?.Name is { Length: > 0 } jsName) map.TryAdd(jsName, type.Name);
        }
        return map;
    });

    /// <summary>Names the generated file uses from referenced namespaces, which a generated type must not shadow.</summary>
    private static IEnumerable<string> BrowserTypeNames => BrowserTypes.Value.Values;

    private static string? KnownBrowserType(string tsName)
    {
        if (BrowserTypes.Value.TryGetValue(tsName, out var csName)) return csName;

        // CSJS.Browser.Element covers the HTMLElement surface too.
        if ((tsName.StartsWith("HTML", StringComparison.Ordinal) || tsName.StartsWith("SVG", StringComparison.Ordinal))
            && tsName.EndsWith("Element", StringComparison.Ordinal))
        {
            return "Element";
        }

        return null;
    }

    private static string Abbreviate(string text) => text.Length <= 80 ? text : text[..77] + "...";
}
