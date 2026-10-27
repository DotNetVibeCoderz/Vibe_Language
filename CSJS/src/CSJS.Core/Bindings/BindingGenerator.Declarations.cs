using System.Text;
using System.Text.RegularExpressions;
using CSJS.Core.Emit;
using CSJS.Core.Mapping;

namespace CSJS.Core.Bindings;

public sealed partial class BindingGenerator
{
    // -------------------------------------------------------------------------------------------
    // Classification
    // -------------------------------------------------------------------------------------------

    /// <summary>What a TypeScript entity becomes in C#.</summary>
    private enum EntityKind
    {
        /// <summary>A real JavaScript class: a <c>[JsImport]</c> / <c>[JsGlobal]</c> binding class.</summary>
        Class,

        /// <summary>An interface with behaviour — a C# interface, since it has no runtime value.</summary>
        Interface,

        /// <summary>A data-only interface or object type — a <c>[JsObjectLiteral]</c> class.</summary>
        ObjectLiteral,

        /// <summary>An interface that is only a call signature — a delegate at every use.</summary>
        Callable,

        /// <summary>Only an index signature — a dictionary-shaped object, bound as <c>JsObject</c>.</summary>
        JsObjectLike,

        NumericEnum,
        StringEnum,

        /// <summary>A type alias, substituted at every use.</summary>
        Alias,

        Namespace,
        Value,
    }

    private readonly Dictionary<Entity, EntityKind> _kinds = new();
    private readonly HashSet<Entity> _classifying = new();

    private EntityKind Classify(Entity entity)
    {
        if (_kinds.TryGetValue(entity, out var known)) return known;
        if (!_classifying.Add(entity)) return EntityKind.Interface;

        try
        {
            var kind = ClassifyCore(entity);
            _kinds[entity] = kind;
            return kind;
        }
        finally
        {
            _classifying.Remove(entity);
        }
    }

    private EntityKind ClassifyCore(Entity entity)
    {
        if (entity.ModuleReference is not null) return EntityKind.Namespace;
        if (entity.Classes.Count > 0) return EntityKind.Class;

        if (entity.Interfaces.Count > 0)
        {
            // `interface Foo { … }` plus `declare var Foo: { new(): Foo }` is how lib.dom and
            // many libraries declare a class.
            if (entity.Variables.Any(v => ConstructorTypeOf(v, entity.Owner) is not null)) return EntityKind.Class;

            var bases = entity.Interfaces.SelectMany(i => i.Extends)
                .Select(b => LookupQualified(entity.Owner, b.Name))
                .Where(b => b is not null)
                .Select(b => Classify(b!))
                .ToList();
            return ClassifyMembers(entity.Interfaces.SelectMany(i => i.Members), bases);
        }

        if (entity.Enum is { } @enum)
            return @enum.Members.Any(m => m.IsString) ? EntityKind.StringEnum : EntityKind.NumericEnum;

        if (entity.Alias is { } alias)
        {
            if (alias.Type is TsObjectType objectType)
            {
                var kind = ClassifyMembers(objectType.Members, Array.Empty<EntityKind>());
                if (kind is EntityKind.ObjectLiteral or EntityKind.Interface
                    && objectType.Members.Any(m => m is not TsSkippedMember)) return kind;
            }
            return EntityKind.Alias;
        }

        if (entity.Namespace is not null) return EntityKind.Namespace;
        return EntityKind.Value;
    }

    private static EntityKind ClassifyMembers(IEnumerable<TsMember> source, IReadOnlyList<EntityKind> bases)
    {
        var members = source.Where(m => m is not TsSkippedMember).ToList();
        var calls = members.OfType<TsCallSignature>().Count();

        if (calls > 0 && members.All(m => m is TsCallSignature || m.IsOptional) && bases.Count == 0)
            return EntityKind.Callable;

        if (members.Count > 0 && members.All(m => m is TsIndexSignature) && bases.Count == 0)
            return EntityKind.JsObjectLike;

        var hasBehaviour = calls > 0
                           || members.Any(m => m is TsConstructSignature)
                           || members.Any(m => m is TsMethodSignature { IsOptional: false });

        if (hasBehaviour || bases.Any(b => b is EntityKind.Interface or EntityKind.Class))
            return EntityKind.Interface;

        return EntityKind.ObjectLiteral;
    }

    private static IReadOnlyList<TsTypeParameter> TypeParametersOf(Entity entity)
    {
        if (entity.Classes.Count > 0) return entity.Classes[0].TypeParameters;
        if (entity.Interfaces.Count > 0) return entity.Interfaces.OrderByDescending(i => i.TypeParameters.Count).First().TypeParameters;
        if (entity.Alias is { } alias) return alias.TypeParameters;
        return Array.Empty<TsTypeParameter>();
    }

    private static IEnumerable<TsCallSignature> CallSignaturesOf(Entity entity)
    {
        var members = entity.Interfaces.SelectMany(i => i.Members);
        if (entity.Alias?.Type is TsObjectType objectType) members = members.Concat(objectType.Members);
        return members.OfType<TsCallSignature>();
    }

    /// <summary>The members of a variable's type when it is a constructor (has <c>new</c> signatures).</summary>
    private IReadOnlyList<TsMember>? ConstructorTypeOf(TsVariableDeclaration variable, Scope scope)
    {
        IReadOnlyList<TsMember>? members = variable.Type switch
        {
            TsObjectType objectType => objectType.Members,
            TsTypeReference reference when LookupQualified(scope, reference.Name) is { } target
                => target.Interfaces.SelectMany(i => i.Members).ToList(),
            _ => null,
        };
        return members is not null && members.Any(m => m is TsConstructSignature) ? members : null;
    }

    // -------------------------------------------------------------------------------------------
    // The C# model
    // -------------------------------------------------------------------------------------------

    private enum CsTypeKind { Class, Interface, ObjectLiteral, StaticClass, Enum, Constants }

    private enum CsMemberKind { Property, Method, Constructor, Indexer, Field, EnumMember, Comment }

    private sealed class CsType
    {
        public required string Name { get; init; }
        public required CsTypeKind Kind { get; init; }
        public List<string> TypeParameters { get; } = new();
        public List<string> Attributes { get; } = new();
        public List<string> Doc { get; } = new();
        public List<string> Notes { get; } = new();
        public List<string> Bases { get; } = new();
        public List<CsType> BaseModels { get; } = new();
        public List<CsMember> Members { get; } = new();
        public bool IsAbstract { get; set; }

        /// <summary>Member names taken so far, with what took them — overloads may share a name.</summary>
        public Dictionary<string, string> TakenNames { get; } = new(StringComparer.Ordinal);

        public string Reference => TypeParameters.Count == 0 ? Name : $"{Name}<{string.Join(", ", TypeParameters)}>";
    }

    private sealed class CsMember
    {
        public required string Name { get; init; }
        public required CsMemberKind Kind { get; init; }
        public bool IsStatic { get; init; }
        public string Access { get; init; } = "public";
        public bool Virtual { get; init; }

        /// <summary>For methods, the name plus parameter types — what decides hiding and duplicates.</summary>
        public string? Signature { get; init; }

        public List<string> Doc { get; init; } = new();
        public List<string> Notes { get; init; } = new();
        public List<string> Attributes { get; init; } = new();

        /// <summary>The declaration after its modifiers, e.g. <c>double X { get; set; }</c>.</summary>
        public required string Declaration { get; init; }
    }

    private readonly List<CsType> _types = new();
    private readonly HashSet<string> _typeNames = new(StringComparer.Ordinal);
    private readonly Dictionary<Entity, CsType> _entityTypes = new();
    private readonly Queue<Action> _queue = new();
    private readonly Dictionary<Entity, string> _access = new();
    private readonly HashSet<Entity> _nestedClasses = new();
    private CsType _container = null!;

    private CsType NewType(string name, CsTypeKind kind)
    {
        var type = new CsType { Name = name, Kind = kind };
        type.TakenNames[name] = "type";
        _types.Add(type);
        return type;
    }

    private static readonly HashSet<string> ReservedTypeNames = new(StringComparer.Ordinal)
    {
        "Action", "Func", "Task", "Dictionary", "HashSet", "DateTime", "Delegate", "Object", "String",
        "System", "CSJS", "JsObject", "JsExternException", "JsImport", "JsGlobal", "JsName",
        "JsObjectLiteral", "JsTemplate", "JsImportAttribute", "JsGlobalAttribute", "JsNameAttribute",
        "JsObjectLiteralAttribute", "JsTemplateAttribute", "Math", "Console", "Exception", "Attribute",
    };

    private void ReserveTypeName(string name) => _typeNames.Add(name);

    private string UniqueTypeName(string preferred, string qualifier)
    {
        var name = preferred;
        if (ReservedTypeNames.Contains(name) || BrowserTypeNames.Contains(name) || _typeNames.Contains(name))
        {
            name = qualifier.Length > 0 ? qualifier + preferred : _containerName + preferred;
            var counter = 2;
            var stem = name;
            while (ReservedTypeNames.Contains(name) || BrowserTypeNames.Contains(name) || _typeNames.Contains(name))
                name = stem + counter++;
        }
        _typeNames.Add(name);
        return name;
    }

    /// <summary>The C# type for an entity, created on first use and built from the queue.</summary>
    private string RequireType(Entity entity)
    {
        if (_entityTypes.TryGetValue(entity, out var existing)) return existing.Name;

        var kind = Classify(entity);
        var csKind = kind switch
        {
            EntityKind.Class => CsTypeKind.Class,
            EntityKind.Interface => CsTypeKind.Interface,
            EntityKind.NumericEnum => CsTypeKind.Enum,
            EntityKind.StringEnum => CsTypeKind.Constants,
            _ => CsTypeKind.ObjectLiteral,
        };

        var qualifier = entity.Owner.NamespacePath.Length == 0
            ? ""
            : string.Concat(entity.Owner.NamespacePath.Split('.').Select(CsTypeName));
        var type = NewType(UniqueTypeName(CsTypeName(entity.Name), qualifier), csKind);
        _entityTypes[entity] = type;

        // Type parameters are fixed now, so references made while building other types agree.
        foreach (var tp in TypeParametersOf(entity))
            type.TypeParameters.Add(UniqueTypeParameter(tp.Name, type.TypeParameters, type.Name));

        _queue.Enqueue(() => BuildEntity(entity, type, kind));
        return type.Name;
    }

    private static string UniqueTypeParameter(string tsName, ICollection<string> taken, string owner)
    {
        var name = CsIdentifier(tsName);
        while (taken.Contains(name) || name == owner) name += "1";
        return name;
    }

    private void DrainQueue()
    {
        while (_queue.Count > 0) _queue.Dequeue()();
    }

    private void BuildEntity(Entity entity, CsType type, EntityKind kind)
    {
        switch (kind)
        {
            case EntityKind.Class: BuildClass(entity, type); break;
            case EntityKind.NumericEnum: BuildEnum(entity.Enum!, type); break;
            case EntityKind.StringEnum: BuildConstants(entity.Enum!, type); break;
            default: BuildShape(entity, type, kind); break;
        }
    }

    private TypeContext ContextForType(Entity entity, CsType type, IReadOnlyList<TsTypeParameter> parameters)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count && i < type.TypeParameters.Count; i++)
            map[parameters[i].Name] = type.TypeParameters[i];

        return new TypeContext
        {
            Scope = entity.Owner,
            TypeParameters = map,
            ThisType = type.Reference,
            Notes = new List<string>(),
            SyntheticName = type.Name,
        };
    }

    // -------------------------------------------------------------------------------------------
    // Classes
    // -------------------------------------------------------------------------------------------

    private void BuildClass(Entity entity, CsType type)
    {
        var declaration = entity.Classes.FirstOrDefault();
        var parameters = TypeParametersOf(entity);
        var context = ContextForType(entity, type, parameters);
        var isPublic = _access.TryGetValue(entity, out var access);
        var nested = _nestedClasses.Contains(entity);

        type.Doc.AddRange(DocLines(declaration?.Doc ?? entity.Interfaces.FirstOrDefault()?.Doc ?? entity.Variables.FirstOrDefault()?.Doc));
        type.IsAbstract = declaration?.Abstract == true;

        if (isPublic)
        {
            type.Attributes.Add(access!);
        }
        else
        {
            // Reachable as a type but not exported under this name: keep the instance surface,
            // drop what would need the class itself at runtime.
            type.Attributes.Add(_globalMode
                ? $"[JsGlobal({Quote(QualifiedJsName(entity))})]"
                : $"[JsImport({Quote(_moduleSpecifier!)}, {Quote(entity.Name)})]");
            type.Notes.Add(nested
                ? $"'{entity}' lives inside a namespace, not as a module export: usable as a type, but not constructible from C#"
                : $"'{entity}' is not exported by '{_moduleSpecifier ?? _globalPrefix}': usable as a type, but not constructible from C#");
        }

        // Base class.
        var members = new List<TsMember>();
        if (declaration?.Extends is { } extends)
        {
            var baseEntity = LookupQualified(entity.Owner, extends.Name);
            if (baseEntity is not null && Classify(baseEntity) == EntityKind.Class)
            {
                var baseText = Map(extends, Flow.Out, context);
                type.Bases.Add(baseText);
                type.BaseModels.Add(_entityTypes[baseEntity]);
            }
            else if (baseEntity is not null && Classify(baseEntity) is EntityKind.Interface or EntityKind.ObjectLiteral)
            {
                members.AddRange(FlattenMembers(baseEntity, new HashSet<Entity>()));
            }
            else
            {
                Skip($"{entity}: base class '{extends}' could not be bound");
                type.Notes.Add($"skipped: base class '{Abbreviate(extends.ToString())}' could not be bound");
            }
        }

        // A class declared the lib.dom way — `interface X extends Y, Mixin` plus a constructor
        // variable — keeps its first class-like base as a real C# base class, so
        // HTMLCanvasElement is an HTMLElement is an Element, and a cast between them compiles.
        // Only the remaining mixins are folded in, minus any the base chain already carries.
        var interfaceBase = declaration is null && type.Bases.Count == 0
            ? entity.Interfaces.SelectMany(i => i.Extends)
                .Select(b => (Reference: b, Entity: LookupQualified(entity.Owner, b.Name)))
                .FirstOrDefault(b => b.Entity is not null && b.Entity != entity && Classify(b.Entity) == EntityKind.Class)
            : default;

        var alreadyInherited = new HashSet<Entity> { entity };
        if (interfaceBase.Entity is { } inheritedEntity)
        {
            type.Bases.Add(Map(interfaceBase.Reference, Flow.Out, context));
            type.BaseModels.Add(_entityTypes[inheritedEntity]);
            foreach (var ancestor in InheritedEntities(inheritedEntity)) alreadyInherited.Add(ancestor);
        }

        foreach (var c in entity.Classes) members.AddRange(c.Members);
        foreach (var i in entity.Interfaces)
        {
            members.AddRange(i.Members);
            foreach (var b in i.Extends)
            {
                if (LookupQualified(entity.Owner, b.Name) is { } merged && !alreadyInherited.Contains(merged))
                    members.AddRange(FlattenMembers(merged, new HashSet<Entity>(alreadyInherited)));
            }
        }

        // A class declared as interface + constructor variable takes its statics and
        // constructors from the variable's type.
        var constructors = members.OfType<TsConstructSignature>().ToList();
        foreach (var variable in entity.Variables)
        {
            if (ConstructorTypeOf(variable, entity.Owner) is not { } constructorType) continue;
            constructors.AddRange(constructorType.OfType<TsConstructSignature>());
            foreach (var member in constructorType)
            {
                if (member is TsConstructSignature || member.Name == "prototype") continue;
                members.Add(member switch
                {
                    TsPropertySignature p => p with { Modifiers = p.Modifiers | TsModifiers.Static },
                    TsMethodSignature m => m with { Modifiers = m.Modifiers | TsModifiers.Static },
                    _ => member,
                });
            }
        }

        // Constructors. Every binding class keeps an accessible parameterless constructor, so a
        // derived class — generated or written by hand — always has one to chain to.
        var hasPublicParameterless = false;
        var canConstruct = isPublic && !nested;
        if (canConstruct)
        {
            var signatures = constructors.Where(c => !c.IsPrivate).ToList();
            if (signatures.Count == 0 && declaration?.Extends is null && entity.Variables.Count == 0)
                signatures.Add(new TsConstructSignature(null, TsModifiers.None, Array.Empty<TsTypeParameter>(), Array.Empty<TsParameter>(), null));
            if (signatures.Count == 0 && declaration?.Extends is not null && type.BaseModels.Count > 0)
                signatures.AddRange(InheritedConstructors(entity));

            foreach (var signature in signatures)
            {
                var accessModifier = signature.IsProtected || type.IsAbstract ? "protected" : "public";
                foreach (var parameterList in ExpandParameters(signature.Parameters, context, type.Name))
                {
                    if (parameterList.Count == 0 || parameterList.All(p => p.Optional))
                        hasPublicParameterless |= accessModifier == "public" || parameterList.Count == 0;
                    AddMember(type, new CsMember
                    {
                        Name = type.Name,
                        Kind = CsMemberKind.Constructor,
                        Access = accessModifier,
                        // Nullable annotations do not distinguish C# overloads, so they do not distinguish signatures.
                        Signature = ".ctor(" + string.Join(",", parameterList.Select(p => p.Type.TrimEnd('?'))) + ")",
                        Doc = DocLines(signature.Doc, parameterList),
                        Notes = TakeNotes(context),
                        Declaration = $"{type.Name}({RenderParameters(parameterList)}) => throw new JsExternException();",
                    });
                }
            }
        }

        if (!hasPublicParameterless && !type.Members.Any(m => m.Kind == CsMemberKind.Constructor && m.Signature == ".ctor()"))
        {
            AddMember(type, new CsMember
            {
                Name = type.Name,
                Kind = CsMemberKind.Constructor,
                Access = "protected",
                Signature = ".ctor()",
                Declaration = $"{type.Name}() => throw new JsExternException();",
            });
        }

        AddMembers(type, members, context, MemberStyle.Class, includeStatics: canConstruct);
        if (!canConstruct && members.Any(m => m.IsStatic))
            type.Notes.Add("skipped: static members, which need the class itself at runtime");

        // A constructor value that is also callable — Angular's `Component({...})`, which is how a
        // decorator is applied without decorator syntax — gets its calls as a static Call method
        // importing the same export.
        if (canConstruct)
        {
            var callAttribute = $"[{access![1..^1]}, JsName({Quote(entity.Name)})]";
            foreach (var variable in entity.Variables)
            {
                if (ConstructorTypeOf(variable, entity.Owner) is not { } constructorType) continue;
                foreach (var call in constructorType.OfType<TsCallSignature>())
                {
                    var method = new TsMethodSignature("call", call.Doc, TsModifiers.Static, call.TypeParameters, call.Parameters, call.ReturnType);
                    AddMethod(type, method, context, MemberStyle.Class, callAttribute);
                }
            }
        }
    }

    private IEnumerable<TsConstructSignature> InheritedConstructors(Entity entity)
    {
        var visited = new HashSet<Entity>();
        for (var current = entity; current is not null && visited.Add(current);)
        {
            var extends = current.Classes.FirstOrDefault()?.Extends;
            if (extends is null) yield break;
            var baseEntity = LookupQualified(current.Owner, extends.Name);
            if (baseEntity is null) yield break;

            var constructors = baseEntity.Classes.SelectMany(c => c.Members).OfType<TsConstructSignature>()
                .Where(c => !c.IsPrivate && !c.IsProtected).ToList();
            if (constructors.Count > 0 || baseEntity.Classes.FirstOrDefault()?.Extends is null)
            {
                // Base parameters may use the base's type parameters; they are not in scope
                // here, so only parameterless or non-generic-looking forms are carried down.
                foreach (var c in constructors)
                {
                    var baseParameters = TypeParametersOf(baseEntity).Select(p => p.Name).ToHashSet();
                    if (c.Parameters.Any(p => MentionsAny(p.Type, baseParameters))) continue;
                    yield return c;
                }
                if (constructors.Count == 0)
                    yield return new TsConstructSignature(null, TsModifiers.None, Array.Empty<TsTypeParameter>(), Array.Empty<TsParameter>(), null);
                yield break;
            }
            current = baseEntity;
        }
    }

    private static bool MentionsAny(TsType type, HashSet<string> names) => type switch
    {
        TsTypeReference r => names.Contains(r.Name) || r.Arguments.Any(a => MentionsAny(a, names)),
        TsArrayType a => MentionsAny(a.Element, names),
        TsUnionType u => u.Types.Any(t => MentionsAny(t, names)),
        TsIntersectionType i => i.Types.Any(t => MentionsAny(t, names)),
        TsTupleType t => t.Elements.Any(e => MentionsAny(e.Type, names)),
        TsFunctionType f => f.Parameters.Any(p => MentionsAny(p.Type, names)) || MentionsAny(f.ReturnType, names),
        TsObjectType => true,
        _ => false,
    };

    /// <summary>
    /// Every entity whose members a generated class already has through its C# base chain: the
    /// class itself, the mixins folded into it, and the same for each base above it.
    /// </summary>
    private HashSet<Entity> InheritedEntities(Entity entity)
    {
        var result = new HashSet<Entity>();
        void Visit(Entity current)
        {
            if (!result.Add(current)) return;
            foreach (var i in current.Interfaces)
                foreach (var b in i.Extends)
                    if (LookupQualified(current.Owner, b.Name) is { } next) Visit(next);
            if (current.Classes.FirstOrDefault()?.Extends is { } extends && LookupQualified(current.Owner, extends.Name) is { } baseClass)
                Visit(baseClass);
        }
        Visit(entity);
        return result;
    }

    /// <summary>Every member of an interface and its bases, for folding into a type that cannot inherit them.</summary>
    private IEnumerable<TsMember> FlattenMembers(Entity entity, HashSet<Entity> visited)
    {
        if (!visited.Add(entity)) yield break;
        foreach (var i in entity.Interfaces)
        {
            foreach (var m in i.Members) yield return m;
            foreach (var b in i.Extends)
            {
                if (LookupQualified(entity.Owner, b.Name) is { } baseEntity)
                    foreach (var m in FlattenMembers(baseEntity, visited)) yield return m;
            }
        }
        foreach (var c in entity.Classes)
            foreach (var m in c.Members.Where(m => !m.IsStatic && m is not TsConstructSignature)) yield return m;
        if (entity.Alias?.Type is TsObjectType objectType)
            foreach (var m in objectType.Members) yield return m;
    }

    // -------------------------------------------------------------------------------------------
    // Interfaces and object literals
    // -------------------------------------------------------------------------------------------

    private void BuildShape(Entity entity, CsType type, EntityKind kind)
    {
        var parameters = TypeParametersOf(entity);
        var context = ContextForType(entity, type, parameters);
        type.Doc.AddRange(DocLines(entity.Interfaces.FirstOrDefault()?.Doc ?? entity.Alias?.Doc));

        var members = new List<TsMember>();
        foreach (var i in entity.Interfaces) members.AddRange(i.Members);
        if (entity.Alias?.Type is TsObjectType objectType) members.AddRange(objectType.Members);

        foreach (var reference in entity.Interfaces.SelectMany(i => i.Extends))
        {
            var baseEntity = LookupQualified(entity.Owner, reference.Name);
            var baseKind = baseEntity is null ? (EntityKind?)null : Classify(baseEntity);

            var inherits = kind == EntityKind.Interface
                ? baseKind == EntityKind.Interface
                : baseKind == EntityKind.ObjectLiteral && type.Bases.Count == 0;

            if (inherits)
            {
                type.Bases.Add(Map(reference, Flow.Out, context));
                type.BaseModels.Add(_entityTypes[baseEntity!]);
            }
            else if (baseEntity is not null && baseKind is EntityKind.ObjectLiteral or EntityKind.Interface or EntityKind.Class or EntityKind.Callable)
            {
                // C# allows one base class and no class bases for an interface; fold the rest in.
                members.AddRange(FlattenMembers(baseEntity, new HashSet<Entity> { entity }));
            }
            else
            {
                Skip($"{entity}: base '{reference}' could not be bound");
                type.Notes.Add($"skipped: base '{Abbreviate(reference.ToString())}' could not be bound");
            }
        }

        if (kind == EntityKind.Interface)
        {
            type.Attributes.Add("[JsGlobal(\"Object\")]");
            type.Doc.Add("/// <remarks>A TypeScript interface: it has no runtime value of its own, so it binds as a plain object.</remarks>");
            AddMembers(type, members, context, MemberStyle.Interface, includeStatics: false);
        }
        else
        {
            type.Attributes.Add("[JsObjectLiteral]");
            AddMembers(type, members, context, MemberStyle.ObjectLiteral, includeStatics: false);
        }
    }

    private readonly Dictionary<string, string> _synthetic = new(StringComparer.Ordinal);

    /// <summary>A structural rendering of a type, for telling repeated inline shapes apart from different ones.</summary>
    private static string Canonical(TsType type) => type switch
    {
        ScopedType s => Canonical(s.Type),
        TsTypeReference r => r.Arguments.Count == 0 ? r.Name : $"{r.Name}<{string.Join(",", r.Arguments.Select(Canonical))}>",
        TsArrayType a => Canonical(a.Element) + "[]",
        TsTupleType t => "[" + string.Join(",", t.Elements.Select(e => Canonical(e.Type))) + "]",
        TsUnionType u => string.Join("|", u.Types.Select(Canonical)),
        TsIntersectionType i => string.Join("&", i.Types.Select(Canonical)),
        TsFunctionType f => "(" + string.Join(",", f.Parameters.Select(p => p.Name + ":" + Canonical(p.Type))) + ")=>" + Canonical(f.ReturnType),
        TsObjectType o => "{" + string.Join(";", o.Members.Select(CanonicalMember)) + "}",
        _ => type.ToString() ?? "",
    };

    private static string CanonicalMember(TsMember member) => member switch
    {
        TsPropertySignature p => $"{p.Name}{(p.IsOptional ? "?" : "")}:{Canonical(p.Type)}",
        TsMethodSignature m => $"{m.Name}(" + string.Join(",", m.Parameters.Select(p => Canonical(p.Type))) + "):" + Canonical(m.ReturnType),
        TsCallSignature c => "(" + string.Join(",", c.Parameters.Select(p => Canonical(p.Type))) + "):" + Canonical(c.ReturnType),
        TsIndexSignature i => $"[{Canonical(i.KeyType)}]:{Canonical(i.ValueType)}",
        TsAccessor a => $"{(a.IsSetter ? "set" : "get")} {a.Name}:{Canonical(a.Type)}",
        _ => member.Name,
    };

    /// <summary>A class for an inline object type, named after where it appeared.</summary>
    private string RequireSynthetic(TsObjectType objectType, TypeContext context)
    {
        // Type parameters the shape mentions must become its own, or the class could not name them.
        var used = new List<string>();
        CollectTypeParameters(objectType, context.TypeParameters, used);

        // Keyed by structure, not identity: one inline shape repeated across overloads must stay
        // one C# type, or the overloads differ only by which copy they name and every call is ambiguous.
        var key = Canonical(objectType) + "<" + string.Join(",", used) + ">";
        if (!_synthetic.TryGetValue(key, out var name))
        {
            name = UniqueTypeName(context.SyntheticName, "");
            _synthetic[key] = name;

            var kind = ClassifyMembers(objectType.Members, Array.Empty<EntityKind>());
            var type = NewType(name, kind == EntityKind.Interface ? CsTypeKind.Interface : CsTypeKind.ObjectLiteral);
            // The shape takes the C# names it was handed, so a type substituted in from an alias
            // argument — which maps in its own context — lands on the same parameter.
            type.TypeParameters.AddRange(used);
            var map = context.TypeParameters
                .Where(p => used.Contains(p.Value))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

            var inner = new TypeContext
            {
                Scope = context.Scope,
                TypeParameters = map,
                ThisType = type.Reference,
                Notes = new List<string>(),
                SyntheticName = name,
            };

            _queue.Enqueue(() =>
            {
                if (type.Kind == CsTypeKind.Interface)
                {
                    type.Attributes.Add("[JsGlobal(\"Object\")]");
                    AddMembers(type, objectType.Members, inner, MemberStyle.Interface, includeStatics: false);
                }
                else
                {
                    type.Attributes.Add("[JsObjectLiteral]");
                    AddMembers(type, objectType.Members, inner, MemberStyle.ObjectLiteral, includeStatics: false);
                }
            });
        }

        return used.Count == 0 ? name : $"{name}<{string.Join(", ", used)}>";
    }

    /// <summary>The C# names of the type parameters a type mentions, looking inside substituted types too.</summary>
    private static void CollectTypeParameters(TsType type, IReadOnlyDictionary<string, string> inScope, List<string> into)
    {
        switch (type)
        {
            case ScopedType scoped:
                CollectTypeParameters(scoped.Type, scoped.Context.TypeParameters, into);
                break;
            case TsTypeReference r:
                if (r.Arguments.Count == 0 && inScope.TryGetValue(r.Name, out var csName) && csName != "object" && !into.Contains(csName))
                    into.Add(csName);
                foreach (var a in r.Arguments) CollectTypeParameters(a, inScope, into);
                break;
            case TsConditionalType c:
                CollectTypeParameters(GeneralBranch(c), inScope, into);
                break;
            case TsArrayType a: CollectTypeParameters(a.Element, inScope, into); break;
            case TsUnionType u: foreach (var t in u.Types) CollectTypeParameters(t, inScope, into); break;
            case TsIntersectionType i: foreach (var t in i.Types) CollectTypeParameters(t, inScope, into); break;
            case TsTupleType t: foreach (var e in t.Elements) CollectTypeParameters(e.Type, inScope, into); break;
            case TsFunctionType f:
                foreach (var p in f.Parameters) CollectTypeParameters(p.Type, inScope, into);
                CollectTypeParameters(f.ReturnType, inScope, into);
                break;
            case TsObjectType o:
                foreach (var m in o.Members)
                {
                    switch (m)
                    {
                        case TsPropertySignature p: CollectTypeParameters(p.Type, inScope, into); break;
                        case TsMethodSignature ms:
                            foreach (var p in ms.Parameters) CollectTypeParameters(p.Type, inScope, into);
                            CollectTypeParameters(ms.ReturnType, inScope, into);
                            break;
                        case TsCallSignature c:
                            foreach (var p in c.Parameters) CollectTypeParameters(p.Type, inScope, into);
                            CollectTypeParameters(c.ReturnType, inScope, into);
                            break;
                        case TsIndexSignature ix: CollectTypeParameters(ix.ValueType, inScope, into); break;
                        case TsAccessor ac: CollectTypeParameters(ac.Type, inScope, into); break;
                    }
                }
                break;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Enums
    // -------------------------------------------------------------------------------------------

    private static readonly Regex SimpleEnumExpression = new(@"^[\w\s\.\(\)<>|&~+\-*]+$", RegexOptions.Compiled);

    private void BuildEnum(TsEnumDeclaration declaration, CsType type)
    {
        type.Doc.AddRange(DocLines(declaration.Doc));
        type.Doc.Add("/// <remarks>Declared here with the library's own values, so it needs no import.</remarks>");

        foreach (var member in declaration.Members)
        {
            var name = CsIdentifier(member.Name);
            if (!type.TakenNames.TryAdd(name, "member")) continue;

            string declarationText;
            if (member.Value is null) declarationText = name;
            else if (SimpleEnumExpression.IsMatch(member.Value)) declarationText = $"{name} = {member.Value}";
            else
            {
                Skip($"{declaration.Name}.{member.Name}: initializer '{member.Value}' is not a constant C# can evaluate");
                type.Members.Add(Comment($"skipped: {member.Name} = {member.Value}, which C# cannot evaluate"));
                continue;
            }

            type.Members.Add(new CsMember
            {
                Name = name,
                Kind = CsMemberKind.EnumMember,
                Doc = DocLines(member.Doc),
                Declaration = declarationText,
            });
        }
    }

    private void BuildConstants(TsEnumDeclaration declaration, CsType type)
    {
        type.Doc.AddRange(DocLines(declaration.Doc));
        type.Doc.Add("/// <remarks>A string enum: C# enums are numeric, so its values are string constants and it is typed as <c>string</c> wherever it appears.</remarks>");

        foreach (var member in declaration.Members)
        {
            var name = CsIdentifier(ToPascal(member.Name));
            if (name == type.Name) name += "_";
            if (!type.TakenNames.TryAdd(name, "member")) continue;

            var value = member.IsString
                ? Quote(member.Value ?? member.Name)
                : member.Value is { } v && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                    ? Quote(v)
                    : null;
            if (value is null)
            {
                Skip($"{declaration.Name}.{member.Name}: a non-string member of a string enum");
                type.Members.Add(Comment($"skipped: {member.Name}, a computed member of a string enum"));
                continue;
            }

            type.Members.Add(new CsMember
            {
                Name = name,
                Kind = CsMemberKind.Field,
                Doc = DocLines(member.Doc),
                Declaration = $"const string {name} = {value};",
            });
        }
    }

    // -------------------------------------------------------------------------------------------
    // Module surface
    // -------------------------------------------------------------------------------------------

    private void BindModuleSurface(TsModule module)
    {
        foreach (var (exportName, entity) in ExportsOf(module))
        {
            var isDefault = exportName == "default";
            var attribute = isDefault
                ? $"[JsImport({Quote(_moduleSpecifier!)}, Default = true)]"
                : $"[JsImport({Quote(_moduleSpecifier!)}, {Quote(exportName)})]";

            // A named default export keeps its own name: `export default function init` binds as Init.
            var name = isDefault && entity.Name != "default" ? entity.Name : exportName;
            BindValueOrType(name, entity, attribute, _container, MemberAttributeStyle.PerMember);
        }
    }

    private void BindGlobalSurface(TsModule module)
    {
        var root = module.Root;
        IEnumerable<KeyValuePair<string, Entity>> entities = root.Names;

        if (_options.Global && !module.IsScript)
            entities = ExportsOf(module).Where(e => e.Key != "default");

        foreach (var (name, entity) in entities)
        {
            var path = _globalPrefix.Length > 0 ? $"{_globalPrefix}.{name}" : name;
            BindValueOrType(name, entity, $"[JsGlobal({Quote(path)})]", _container,
                _globalPrefix.Length > 0 ? MemberAttributeStyle.Container : MemberAttributeStyle.PerMember);
        }
    }

    private enum MemberAttributeStyle
    {
        /// <summary>Each member carries its own <c>[JsImport]</c> / <c>[JsGlobal]</c>.</summary>
        PerMember,

        /// <summary>The containing class carries the binding; members are reached through it.</summary>
        Container,
    }

    /// <summary>Binds one exported name: its runtime value, if any, and its type, if any.</summary>
    private void BindValueOrType(string exportName, Entity entity, string attribute, CsType container, MemberAttributeStyle style)
    {
        if (entity.ModuleReference is { } referenced)
        {
            // `export * as ns from "m"`: a namespace object with m's exports on it.
            var nsType = NewType(UniqueTypeName(CsTypeName(exportName), ""), CsTypeKind.StaticClass);
            nsType.Attributes.Add(attribute);
            foreach (var (name, member) in ExportsOf(referenced))
                BindNamespaceMember(name, member, nsType, depth: 1);
            return;
        }

        var kind = Classify(entity);
        switch (kind)
        {
            case EntityKind.Class:
                _access.TryAdd(entity, attribute);
                RequireType(entity);
                break;

            case EntityKind.Interface or EntityKind.ObjectLiteral or EntityKind.NumericEnum or EntityKind.StringEnum:
                RequireType(entity);
                break;
        }

        if (kind != EntityKind.Class)
        {
            foreach (var function in entity.Functions)
                AddFunction(container, exportName, function, entity.Owner, attribute, style);

            foreach (var variable in entity.Variables)
                AddVariable(container, exportName, variable, entity.Owner, attribute, style);
        }

        if (entity.Namespace is { } ns)
        {
            if (NamespaceHasValues(ns) && kind is not EntityKind.Class)
            {
                var nsType = NewType(UniqueTypeName(CsTypeName(exportName), ""), CsTypeKind.StaticClass);
                nsType.Doc.Add($"/// <summary>The <c>{XmlEscape(exportName)}</c> namespace.</summary>");
                nsType.Attributes.Add(attribute);
                foreach (var (name, member) in ns.Names)
                    BindNamespaceMember(name, member, nsType, depth: 1);
            }
            else
            {
                foreach (var member in ns.Names.Values) BindTypesOnly(member);
            }
        }
    }

    private void BindNamespaceMember(string name, Entity entity, CsType nsType, int depth)
    {
        var kind = Classify(entity);

        if (_globalMode)
        {
            var path = $"{QualifiedJsName(entity.Owner)}.{name}".TrimStart('.');
            if (_globalPrefix.Length > 0) path = $"{_globalPrefix}.{path}";
            if (kind == EntityKind.Class) _access.TryAdd(entity, $"[JsGlobal({Quote(path)})]");
        }
        else if (kind == EntityKind.Class)
        {
            _nestedClasses.Add(entity);
        }

        if (kind is EntityKind.Class or EntityKind.Interface or EntityKind.ObjectLiteral or EntityKind.NumericEnum or EntityKind.StringEnum)
            RequireType(entity);

        if (kind != EntityKind.Class)
        {
            foreach (var function in entity.Functions)
                AddFunction(nsType, name, function, entity.Owner, null, MemberAttributeStyle.Container);
            foreach (var variable in entity.Variables)
                AddVariable(nsType, name, variable, entity.Owner, null, MemberAttributeStyle.Container);
        }

        if (entity.Namespace is { } inner)
        {
            if (NamespaceHasValues(inner) && _globalMode)
            {
                var path = QualifiedJsName(entity);
                if (_globalPrefix.Length > 0) path = $"{_globalPrefix}.{path}";
                var innerType = NewType(UniqueTypeName(CsTypeName(name), nsType.Name), CsTypeKind.StaticClass);
                innerType.Attributes.Add($"[JsGlobal({Quote(path)})]");
                foreach (var (memberName, member) in inner.Names)
                    BindNamespaceMember(memberName, member, innerType, depth + 1);
            }
            else
            {
                if (NamespaceHasValues(inner))
                {
                    Skip($"namespace '{entity}': its values sit two levels deep, which a module import cannot reach");
                    nsType.Members.Add(Comment($"skipped: values of namespace '{entity}', which a module import cannot reach"));
                }
                foreach (var member in inner.Names.Values) BindTypesOnly(member);
            }
        }
    }

    private void BindTypesOnly(Entity entity)
    {
        var kind = Classify(entity);
        if (kind == EntityKind.Class && !_globalMode) _nestedClasses.Add(entity);
        if (kind is EntityKind.Class or EntityKind.Interface or EntityKind.ObjectLiteral or EntityKind.NumericEnum or EntityKind.StringEnum)
            RequireType(entity);
        if (entity.Namespace is { } inner)
            foreach (var member in inner.Names.Values) BindTypesOnly(member);
    }

    private static string QualifiedJsName(Entity entity)
        => entity.Owner.NamespacePath.Length == 0 ? entity.Name : $"{entity.Owner.NamespacePath}.{entity.Name}";

    private static string QualifiedJsName(Scope scope) => scope.NamespacePath;

    private void AddFunction(CsType container, string jsName, TsFunctionDeclaration function, Scope scope, string? attribute, MemberAttributeStyle style)
    {
        var context = new TypeContext { Scope = scope, Notes = new List<string>(), SyntheticName = CsTypeName(jsName) };
        var method = new TsMethodSignature(jsName, function.Doc, TsModifiers.Static, function.TypeParameters, function.Parameters, function.ReturnType);
        AddMethod(container, method, context, style == MemberAttributeStyle.PerMember ? MemberStyle.PerMemberStatic : MemberStyle.Class, attribute);
    }

    private void AddVariable(CsType container, string jsName, TsVariableDeclaration variable, Scope scope, string? attribute, MemberAttributeStyle style)
    {
        var context = new TypeContext { Scope = scope, Notes = new List<string>(), SyntheticName = CsTypeName(jsName) };
        var type = variable.Type ?? (TsType?)variable.Literal ?? AnyType;
        var memberStyle = style == MemberAttributeStyle.PerMember ? MemberStyle.PerMemberStatic : MemberStyle.Class;

        // `const computed: typeof _computed` re-exports a function under another name; it binds
        // as that function, every overload included.
        if (type is TsTypeQuery query && LookupQualified(scope, query.Name) is { Functions.Count: > 0 } target)
        {
            foreach (var queried in target.Functions)
                AddFunction(container, jsName, queried with { Doc = queried.Doc ?? variable.Doc }, target.Owner, attribute, style);
            return;
        }

        // A function-typed constant is called like a function, so it binds as one.
        var probe = context;
        var resolved = Unwrap(type, ref probe);
        if (resolved is TsFunctionType function && !function.IsConstructor)
        {
            var method = new TsMethodSignature(jsName, variable.Doc, TsModifiers.Static, function.TypeParameters, function.Parameters, function.ReturnType);
            AddMethod(container, method, probe.With(syntheticName: CsTypeName(jsName)), memberStyle, attribute);
            return;
        }

        // Imports are read-only bindings, and so is a const.
        var readOnly = variable.IsConst || !_globalMode;
        var property = new TsPropertySignature(jsName, variable.Doc,
            TsModifiers.Static | (readOnly ? TsModifiers.Readonly : TsModifiers.None), type);
        AddProperty(container, property, context, memberStyle, attribute);
    }

    // -------------------------------------------------------------------------------------------
    // Members
    // -------------------------------------------------------------------------------------------

    private enum MemberStyle
    {
        /// <summary>A binding class or a container reached through its own binding attribute.</summary>
        Class,

        /// <summary>A C# interface: signatures only.</summary>
        Interface,

        /// <summary>A <c>[JsObjectLiteral]</c> class: settable auto-properties, explicit names.</summary>
        ObjectLiteral,

        /// <summary>A static member carrying its own <c>[JsImport]</c> or <c>[JsGlobal]</c>.</summary>
        PerMemberStatic,
    }

    private void AddMembers(CsType type, IEnumerable<TsMember> source, TypeContext context, MemberStyle style, bool includeStatics)
    {
        var members = source.ToList();

        // Fold get/set accessor pairs into one property.
        var accessors = members.OfType<TsAccessor>().GroupBy(a => (a.Name, a.IsStatic)).ToList();
        foreach (var group in accessors)
        {
            var getter = group.FirstOrDefault(a => !a.IsSetter);
            var setter = group.FirstOrDefault(a => a.IsSetter);
            var modifiers = (getter ?? setter)!.Modifiers;
            if (setter is null) modifiers |= TsModifiers.Readonly;
            members.Add(new TsPropertySignature(group.Key.Name, getter?.Doc ?? setter?.Doc, modifiers, getter?.Type ?? setter!.Type));
        }

        // A C# indexer is named Item under the hood, so a JavaScript `item()` must step aside.
        if (members.Any(m => m is TsIndexSignature) && style != MemberStyle.ObjectLiteral)
            type.TakenNames.TryAdd("Item", "indexer");

        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (member.IsPrivate) continue;
            if (member.IsStatic && !includeStatics) continue;

            switch (member)
            {
                case TsSkippedMember skipped:
                    Skip($"{type.Name}.{skipped.Name}: {skipped.Reason}");
                    type.Members.Add(Comment($"skipped: {Abbreviate(skipped.Name)} — {skipped.Reason}"));
                    break;

                case TsPropertySignature property:
                    if (!seenProperties.Add((property.IsStatic ? "static " : "") + property.Name)) break;
                    AddProperty(type, property, context, style, null);
                    break;

                case TsMethodSignature method when style == MemberStyle.ObjectLiteral:
                    // An options object carries callbacks as properties.
                    if (!seenProperties.Add(method.Name)) break;
                    AddProperty(type, new TsPropertySignature(method.Name, method.Doc, method.Modifiers,
                        new TsFunctionType(method.TypeParameters, method.Parameters, method.ReturnType, false)), context, style, null);
                    break;

                case TsMethodSignature method:
                    AddMethod(type, method, context, style, null);
                    break;

                case TsIndexSignature index:
                    AddIndexer(type, index, context, style);
                    break;

                case TsCallSignature call when style == MemberStyle.Interface:
                    var invoke = new TsMethodSignature("()", call.Doc, TsModifiers.None, call.TypeParameters, call.Parameters, call.ReturnType);
                    AddMethod(type, invoke, context, style, "[JsTemplate(\"{this}({...})\")]");
                    break;

                case TsCallSignature:
                    Skip($"{type.Name}: call signature has no place on a {DescribeStyle(style)}");
                    type.Members.Add(Comment("skipped: call signature, which a " + DescribeStyle(style) + " cannot carry"));
                    break;

                case TsConstructSignature when type.Kind == CsTypeKind.Class:
                    break; // handled by BuildClass

                case TsConstructSignature:
                    Skip($"{type.Name}: construct signature on a type with no runtime value");
                    type.Members.Add(Comment("skipped: construct signature, on a type with no runtime value"));
                    break;
            }
        }
    }

    private static string DescribeStyle(MemberStyle style) => style switch
    {
        MemberStyle.ObjectLiteral => "object literal",
        MemberStyle.Interface => "interface",
        _ => "class",
    };

    private void AddProperty(CsType type, TsPropertySignature property, TypeContext context, MemberStyle style, string? attribute)
    {
        var propertyContext = context.With(syntheticName: type.Name + CsTypeName(property.Name));
        var flow = style == MemberStyle.ObjectLiteral ? Flow.In : Flow.Out;
        var csType = Map(property.Type, flow, propertyContext);
        if (csType == "void") csType = "object";
        if (property.IsOptional) csType = TypeMap.FromTypeScriptNullable(csType);

        var name = MemberName(type, property.Name, isMethod: false, property.IsStatic);
        var attributes = new List<string>();
        if (attribute is not null) attributes.Add(attribute);
        else if (NeedsJsName(name, property.Name, style)) attributes.Add($"[JsName({Quote(property.Name)})]");

        var readOnly = property.IsReadonly;
        string declaration = style switch
        {
            MemberStyle.Interface => $"{csType} {name} {{ get;{(readOnly ? "" : " set;")} }}",
            MemberStyle.ObjectLiteral => $"{csType} {name} {{ get; {(readOnly ? "init" : "set")}; }}",
            _ => readOnly
                ? $"{csType} {name} => throw new JsExternException();"
                : $"{csType} {name} {{ get => throw new JsExternException(); set => throw new JsExternException(); }}",
        };

        AddMember(type, new CsMember
        {
            Name = name,
            Kind = CsMemberKind.Property,
            IsStatic = property.IsStatic,
            Access = property.IsProtected ? "protected" : "public",
            Doc = DocLines(property.Doc),
            Notes = TakeNotes(context, propertyContext),
            Attributes = attributes,
            Declaration = declaration,
        });
    }

    private void AddMethod(CsType type, TsMethodSignature method, TypeContext context, MemberStyle style, string? attribute)
    {
        var isInvoke = method.Name == "()";
        var name = isInvoke ? MemberName(type, "invoke", isMethod: true, isStatic: false) : MemberName(type, method.Name, isMethod: true, method.IsStatic);

        // Method type parameters, renamed where they would shadow the type's own.
        var typeParameters = new Dictionary<string, string>(context.TypeParameters, StringComparer.Ordinal);
        var ownParameters = new List<string>();
        foreach (var tp in method.TypeParameters)
        {
            // `K extends keyof Tags` or `K extends string` is a string to C#. Keeping it generic
            // only makes `h("div")` ambiguous against the overloads that take a string outright.
            if (tp.Constraint is { } constraint
                && Map(constraint, Flow.In, context.With(typeParameters: typeParameters).WithScratchNotes()) == "string")
            {
                typeParameters[tp.Name] = "string";
                continue;
            }

            var csName = UniqueTypeParameter(tp.Name, type.TypeParameters.Concat(ownParameters).ToList(), type.Name);
            ownParameters.Add(csName);
            typeParameters[tp.Name] = csName;
        }

        var syntheticName = (type == _container ? "" : type.Name) + CsTypeName(isInvoke ? "Invoke" : method.Name);
        var methodContext = context.With(typeParameters: typeParameters, syntheticName: syntheticName);
        var expansions = ExpandParameters(method.Parameters, methodContext, type.Name + CsTypeName(method.Name));

        // A type parameter no parameter mentions cannot be inferred, and C# would demand it at
        // every call — usually because the TypeScript constraint that tied it down is gone.
        var mentioned = string.Join(" ", expansions.SelectMany(e => e.Select(p => p.Type)));
        var unused = ownParameters.Where(p => !Regex.IsMatch(mentioned, $@"\b{Regex.Escape(p)}\b")).ToList();
        if (unused.Count > 0)
        {
            foreach (var tp in method.TypeParameters)
                if (unused.Contains(typeParameters[tp.Name])) typeParameters[tp.Name] = "object";
            ownParameters.RemoveAll(unused.Contains);
            methodContext.Notes.Clear();
            Note(methodContext, $"type parameter(s) {string.Join(", ", unused)} cannot be inferred from the arguments; erased to object");
            methodContext = context.With(typeParameters: typeParameters, syntheticName: syntheticName);
            expansions = ExpandParameters(method.Parameters, methodContext, type.Name + CsTypeName(method.Name));
        }

        var returnType = MapReturn(method.ReturnType, methodContext);
        var generic = ownParameters.Count > 0 ? $"<{string.Join(", ", ownParameters)}>" : "";

        var attributes = new List<string>();
        if (attribute is not null) attributes.Add(attribute);
        if (!isInvoke && attribute is null && NeedsJsName(name, method.Name, style)) attributes.Add($"[JsName({Quote(method.Name)})]");

        var isVirtual = style == MemberStyle.Class && !method.IsStatic && type.Kind == CsTypeKind.Class;

        foreach (var parameters in expansions)
        {
            var rendered = RenderParameters(parameters);
            var body = style == MemberStyle.Interface ? ";" : " => throw new JsExternException();";
            AddMember(type, new CsMember
            {
                Name = name,
                Kind = CsMemberKind.Method,
                IsStatic = method.IsStatic,
                Access = method.IsProtected ? "protected" : "public",
                Virtual = isVirtual,
                Signature = $"{name}{(ownParameters.Count > 0 ? "`" + ownParameters.Count : "")}({string.Join(",", parameters.Select(p => p.Type.TrimEnd('?')))})",
                Doc = DocLines(method.Doc, parameters),
                Notes = TakeNotes(methodContext),
                Attributes = attributes,
                Declaration = $"{returnType} {name}{generic}({rendered}){body}",
            });
        }
    }

    private void AddIndexer(CsType type, TsIndexSignature index, TypeContext context, MemberStyle style)
    {
        if (style == MemberStyle.ObjectLiteral)
        {
            Skip($"{type.Name}: index signature [{index.KeyName}: {index.KeyType}] cannot be set from an object initializer");
            type.Members.Add(Comment($"skipped: index signature [{index.KeyName}: {index.KeyType}], which an object initializer cannot fill"));
            return;
        }

        var keyType = Map(index.KeyType, Flow.In, context) == "double" ? "int" : "string";
        var valueType = Map(index.ValueType, Flow.Out, context);
        if (valueType == "void") valueType = "object";
        var key = CsIdentifier(index.KeyName);

        var declaration = style == MemberStyle.Interface
            ? $"{valueType} this[{keyType} {key}] {{ get;{(index.IsReadonly ? "" : " set;")} }}"
            : index.IsReadonly
                ? $"{valueType} this[{keyType} {key}] => throw new JsExternException();"
                : $"{valueType} this[{keyType} {key}] {{ get => throw new JsExternException(); set => throw new JsExternException(); }}";

        AddMember(type, new CsMember
        {
            Name = "this[]",
            Kind = CsMemberKind.Indexer,
            Signature = $"this[{keyType}]",
            Doc = DocLines(index.Doc),
            Notes = TakeNotes(context),
            Declaration = declaration,
        });
    }

    private static List<string> TakeNotes(params TypeContext[] contexts)
    {
        var notes = contexts.SelectMany(c => c.Notes).Distinct().ToList();
        foreach (var c in contexts) c.Notes.Clear();
        return notes;
    }

    private static CsMember Comment(string text) => new() { Name = "", Kind = CsMemberKind.Comment, Declaration = text };

    /// <summary>Adds a member unless one with the same signature is already there.</summary>
    private static void AddMember(CsType type, CsMember member)
    {
        if (member.Signature is not null
            && type.Members.Any(m => m.Signature == member.Signature && m.IsStatic == member.IsStatic))
        {
            return;
        }
        type.Members.Add(member);
    }

    // -------------------------------------------------------------------------------------------
    // Parameters and overloads
    // -------------------------------------------------------------------------------------------

    private sealed record CsParameter(string Type, string Name, string JsName, bool Optional, bool IsParams);

    private const int MaxOverloads = 12;
    private const int MaxAlternatives = 4;

    /// <summary>
    /// Turns a TypeScript parameter list into C# overloads.
    /// <para>
    /// JavaScript functions dispatch on what they are given, so a union-typed parameter is
    /// really several signatures; each becomes a C# overload, all emitting the same call.
    /// Optional parameters become C# optional ones — omitted trailing arguments are dropped at
    /// the call site, exactly as in JavaScript — except where an optional parameter is itself a
    /// union, when explicit shorter overloads avoid two candidates both matching a short call.
    /// </para>
    /// </summary>
    private List<List<CsParameter>> ExpandParameters(IReadOnlyList<TsParameter> source, TypeContext context, string syntheticStem)
    {
        var parameters = source.Where(p => p.Name != "this").ToList();
        var names = UniqueParameterNames(parameters);

        // Alternatives per parameter, mapped in its own context.
        var options = new List<List<string>>();
        var anyOptionalUnion = false;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var parameterContext = context.With(syntheticName: syntheticStem + CsTypeName(parameter.Name));
            options.Add(ParameterAlternatives(parameter, parameterContext));
            context.Notes.AddRange(parameterContext.Notes);
            if (parameter.Optional && options[i].Count > 1) anyOptionalUnion = true;
        }

        var total = options.Aggregate(1L, (acc, o) => acc * o.Count);
        if (total > MaxOverloads)
        {
            // Too many combinations: collapse each union back to its single fallback type.
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].Count <= 1) continue;
                options[i] = new List<string> { parameters[i].Optional ? "object?" : "object" };
                Note(context, $"parameter '{parameters[i].Name}' accepts several types; mapped to object to keep the overload count down");
            }
            anyOptionalUnion = false;
        }

        var lengths = anyOptionalUnion
            ? Enumerable.Range(parameters.FindIndex(p => p.Optional), parameters.Count - parameters.FindIndex(p => p.Optional) + 1)
            : new[] { parameters.Count };

        var result = new List<List<CsParameter>>();
        foreach (var length in lengths)
        {
            var combinations = new List<List<CsParameter>> { new() };
            for (var i = 0; i < length; i++)
            {
                var parameter = parameters[i];
                var optional = parameter.Optional && !anyOptionalUnion && !parameter.Rest;
                var next = new List<List<CsParameter>>();
                foreach (var prefix in combinations)
                {
                    foreach (var type in options[i])
                    {
                        var csType = type;
                        var isParams = parameter.Rest;
                        if (isParams && !csType.EndsWith("[]", StringComparison.Ordinal)) csType = TypeMap.FromTypeScriptArray(csType);
                        if (optional) csType = TypeMap.FromTypeScriptNullable(csType);
                        next.Add(new List<CsParameter>(prefix) { new(csType, names[i], parameter.Name, optional, isParams) });
                    }
                }
                combinations = next;
            }
            result.AddRange(combinations);
        }

        // Two TypeScript alternatives can land on the same C# type.
        return result
            .GroupBy(r => string.Join(",", r.Select(p => p.Type.TrimEnd('?'))))
            .Select(g => g.First())
            .ToList();
    }

    private List<string> ParameterAlternatives(TsParameter parameter, TypeContext context)
    {
        var type = parameter.Rest ? RestElementType(parameter.Type, context) : parameter.Type;
        var alternatives = Alternatives(type, context, out var nullable, out _);

        // Overloads help where a parameter takes a couple of genuinely different things. A rest
        // parameter, or a union as wide as React's ReactNode, would only multiply signatures —
        // those map to one type instead.
        if (parameter.Rest || alternatives.Count > MaxAlternatives)
        {
            var single = Map(type, Flow.In, context);
            return new List<string> { single == "void" ? "object" : single };
        }

        var mapped = new List<string>();
        foreach (var alternative in alternatives)
        {
            // A callback that may return a cleanup function is two callbacks: one that returns
            // nothing and one that returns the cleanup — which is how C# lambdas tell them apart.
            var functionContext = alternative.Context;
            if (Unwrap(alternative.Type, ref functionContext) is TsFunctionType function && !function.IsConstructor)
            {
                if (SpreadRestCallback(function, functionContext) is { } spread)
                {
                    mapped.AddRange(spread);
                    continue;
                }

                var returnAlternatives = Alternatives(function.ReturnType, functionContext, out _, out var returnsVoid);
                if (returnsVoid && returnAlternatives.Count > 0)
                {
                    mapped.Add(Map(function with { ReturnType = new TsTypeReference("void", Array.Empty<TsType>()) }, Flow.In, functionContext));
                    foreach (var ra in returnAlternatives)
                        mapped.Add(Map(function with { ReturnType = new ScopedType(ra.Type, ra.Context) }, Flow.In, functionContext));
                    continue;
                }
            }

            var csType = Map(alternative.Type, Flow.In, alternative.Context);
            if (csType == "void") csType = "object";
            mapped.Add(csType);
        }

        if (mapped.Count == 0) mapped.Add("object");
        mapped = mapped.Distinct().ToList();
        if (nullable && !parameter.Optional) mapped = mapped.Select(TypeMap.FromTypeScriptNullable).ToList();
        return mapped;
    }

    private const int MaxSpreadArguments = 3;

    /// <summary>
    /// A callback parameter declared <c>(...args: T[]) =&gt; R</c> — SignalR's
    /// <c>on(name, (...args) =&gt; …)</c>, an event emitter's listener — is called with however
    /// many arguments the library has. A C# delegate has a fixed arity, so it becomes one
    /// overload per arity, zero to <see cref="MaxSpreadArguments"/>, and a lambda picks its own by
    /// how many parameters it declares. A callback returning <c>any</c> also gets void-returning
    /// forms: a handler written as a statement returns nothing, and JavaScript does not mind.
    /// Returns null when the callback has no rest parameter.
    /// </summary>
    private List<string>? SpreadRestCallback(TsFunctionType function, TypeContext context)
    {
        var parameters = function.Parameters.Where(p => p.Name != "this").ToList();
        if (parameters.Count == 0 || !parameters[^1].Rest) return null;

        var rest = parameters[^1];
        var element = RestElementType(rest.Type, context);
        var leading = parameters.Take(parameters.Count - 1).ToList();
        var probe = context;
        var returnsAnything = Unwrap(function.ReturnType, ref probe) is TsTypeReference { Name: "any" or "unknown", Arguments.Count: 0 };
        var voidType = new TsTypeReference("void", Array.Empty<TsType>());

        var result = new List<string>();
        for (var arity = 0; arity <= MaxSpreadArguments; arity++)
        {
            var spread = leading
                .Concat(Enumerable.Range(1, arity).Select(i => new TsParameter(rest.Name + i, element, false, false)))
                .ToList();
            var variant = function with { Parameters = spread };
            if (returnsAnything) result.Add(Map(variant with { ReturnType = voidType }, Flow.In, context));
            result.Add(Map(variant, Flow.In, context));
        }

        Note(context, $"callback rest parameter '...{rest.Name}' spread into overloads taking 0 to {MaxSpreadArguments} arguments");
        return result.Distinct().ToList();
    }

    private TsType RestElementType(TsType type, TypeContext context)
    {
        var probe = context;
        return Unwrap(type, ref probe) switch
        {
            TsArrayType array => new ScopedType(array.Element, probe),
            TsTypeReference { Name: "Array" or "ReadonlyArray", Arguments.Count: 1 } r => new ScopedType(r.Arguments[0], probe),
            _ => AnyType,
        };
    }

    private static List<string> UniqueParameterNames(IReadOnlyList<TsParameter> parameters)
    {
        var names = new List<string>();
        foreach (var parameter in parameters)
        {
            var name = CsParameterName(parameter.Name);
            var candidate = name;
            var counter = 2;
            while (names.Contains(candidate)) candidate = name + counter++;
            names.Add(candidate);
        }
        return names;
    }

    private static string RenderParameters(IReadOnlyList<CsParameter> parameters)
        => string.Join(", ", parameters.Select(p =>
            p.IsParams ? $"params {p.Type} {p.Name}"
            : p.Optional ? $"{p.Type} {p.Name} = default"
            : $"{p.Type} {p.Name}"));

    // -------------------------------------------------------------------------------------------
    // Names
    // -------------------------------------------------------------------------------------------

    /// <summary>Picks a C# member name, keeping overloads together and everything else distinct.</summary>
    private static string MemberName(CsType type, string jsName, bool isMethod, bool isStatic)
    {
        var name = CsIdentifier(ToPascal(jsName));
        if (name.TrimStart('@') == type.Name || type.TypeParameters.Contains(name)) name += "_";

        var role = (isMethod ? "method:" : "property:") + (isStatic ? "static:" : "instance:") + jsName;
        while (true)
        {
            if (!type.TakenNames.TryGetValue(name, out var takenBy))
            {
                type.TakenNames[name] = role;
                return name;
            }

            // Overloads of the same JavaScript method share a C# name.
            if (isMethod && takenBy == role) return name;
            name += isStatic && !takenBy.Contains(":static:") ? "Static" : "_";
        }
    }

    private static bool NeedsJsName(string csName, string jsName, MemberStyle style)
    {
        var raw = csName.TrimStart('@');
        return style == MemberStyle.ObjectLiteral
            ? raw != jsName
            : NameResolver.BindingName(raw) != jsName;
    }

    internal static string ToPascal(string jsName)
    {
        if (jsName.Length == 0) return "_";
        var chars = jsName.ToCharArray();
        if (char.IsLower(chars[0])) chars[0] = char.ToUpperInvariant(chars[0]);
        return new string(chars);
    }

    private static string CsTypeName(string tsName) => CsIdentifier(ToPascal(tsName)).TrimStart('@');

    private static string CsParameterName(string tsName)
    {
        var name = CsIdentifier(tsName);
        return name;
    }

    /// <summary>A valid C# identifier for a JavaScript name: invalid characters replaced, keywords escaped.</summary>
    internal static string CsIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        var result = sb.Length == 0 ? "_" : sb.ToString();
        if (char.IsDigit(result[0])) result = "_" + result;
        return CSharpKeywords.Contains(result) ? "@" + result : result;
    }

    private static bool IsPlainIdentifier(string name)
        => name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    private static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    private static string XmlEscape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // -------------------------------------------------------------------------------------------
    // Documentation
    // -------------------------------------------------------------------------------------------

    private static readonly Regex InlineLink = new(@"\{@(?:link|linkcode|linkplain)\s+([^\s}|]+)(?:[\s|]+([^}]*))?\}", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);

    /// <summary>Turns a JSDoc block into XML documentation lines.</summary>
    private static List<string> DocLines(string? jsdoc, IReadOnlyList<CsParameter>? parameters = null)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(jsdoc)) return lines;

        var body = jsdoc.Trim();
        if (body.StartsWith("/**", StringComparison.Ordinal)) body = body[3..];
        if (body.EndsWith("*/", StringComparison.Ordinal)) body = body[..^2];

        var summary = new List<string>();
        var tags = new List<(string Tag, StringBuilder Text)>();
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('*')) line = line[1..].TrimStart();
            if (line.StartsWith('@'))
            {
                var space = line.IndexOf(' ');
                tags.Add(space < 0 ? (line[1..], new StringBuilder()) : (line[1..space], new StringBuilder(line[(space + 1)..])));
            }
            else if (tags.Count > 0)
            {
                tags[^1].Text.Append(' ').Append(line);
            }
            else
            {
                summary.Add(line);
            }
        }

        var summaryText = Inline(string.Join(" ", summary.Where(s => s.Length > 0)));
        if (summaryText.Length > 0) lines.Add($"/// <summary>{summaryText}</summary>");

        foreach (var (tag, text) in tags)
        {
            var value = text.ToString().Trim();
            switch (tag)
            {
                case "param" when parameters is not null:
                {
                    // `@param {type} name description` — the type is optional.
                    if (value.StartsWith('{')) value = value[(value.IndexOf('}') + 1)..].Trim();
                    var space = value.IndexOf(' ');
                    var jsName = (space < 0 ? value : value[..space]).Trim('[', ']').Split('=')[0];
                    var description = space < 0 ? "" : value[(space + 1)..].TrimStart('-', ' ');
                    var parameter = parameters.FirstOrDefault(p => p.JsName == jsName);
                    if (parameter is not null && description.Length > 0)
                        lines.Add($"/// <param name=\"{parameter.Name.TrimStart('@')}\">{Inline(description)}</param>");
                    break;
                }
                case "returns" or "return" when value.Length > 0:
                    if (value.StartsWith('{')) value = value[(value.IndexOf('}') + 1)..].Trim();
                    if (value.Length > 0) lines.Add($"/// <returns>{Inline(value)}</returns>");
                    break;
                case "deprecated":
                    lines.Add($"/// <remarks>Deprecated{(value.Length > 0 ? ": " + Inline(value) : ".")}</remarks>");
                    break;
            }
        }

        return lines;
    }

    private static string Inline(string text)
    {
        text = InlineLink.Replace(text, m => m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : m.Groups[1].Value);
        text = XmlEscape(text);
        return InlineCode.Replace(text, m => $"<c>{m.Groups[1].Value}</c>");
    }

    // -------------------------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------------------------

    private string Render(BindingSource entry)
    {
        var sb = new StringBuilder();
        var source = System.IO.Path.GetFileName(entry.Path);
        var target = _globalMode ? (_globalPrefix.Length > 0 ? $"the global '{_globalPrefix}'" : "globals") : $"'{_moduleSpecifier}'";

        sb.Append("// <auto-generated>\n");
        sb.Append($"//   C# bindings for {target}, generated by CSJS from {source}.\n");
        sb.Append("//   Regenerate rather than edit: `cs2js bind` or `cs2js add`.\n");
        sb.Append("// </auto-generated>\n");
        sb.Append("// Gravicode Studios — led by Kang Fadhil.\n\n");
        sb.Append("#nullable enable\n");
        sb.Append("#pragma warning disable CS8618 // object-literal shapes are filled by initializers, not constructors\n");
        sb.Append("#pragma warning disable CS8714 // TypeScript map keys carry no notnull constraint\n\n");
        sb.Append("using System;\n");
        sb.Append("using System.Collections.Generic;\n");
        sb.Append("using System.Threading.Tasks;\n");
        sb.Append("using CSJS;\n");
        // The platform file generates its own Element and Event, so it takes only JsObject from
        // CSJS.Browser rather than the whole namespace.
        sb.Append(_options.Platform ? "using JsObject = CSJS.Browser.JsObject;\n\n" : "using CSJS.Browser;\n\n");
        sb.Append($"namespace {_namespace};\n");

        if (_skipped.Count > 0)
        {
            sb.Append('\n');
            foreach (var skipped in _skipped.Distinct()) sb.Append("// skipped: ").Append(OneLine(skipped)).Append('\n');
        }

        foreach (var type in _types)
        {
            // An empty container means the module exports only types.
            if (type == _container && type.Members.Count == 0) continue;
            sb.Append('\n');
            RenderType(sb, type);
        }

        return sb.ToString();
    }

    private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

    private void RenderType(StringBuilder sb, CsType type)
    {
        foreach (var note in type.Notes) sb.Append("// ").Append(note.StartsWith("skipped:", StringComparison.Ordinal) ? "" : "note: ").Append(OneLine(note)).Append('\n');
        foreach (var doc in type.Doc) sb.Append(doc).Append('\n');
        foreach (var attribute in type.Attributes) sb.Append(attribute).Append('\n');

        var keyword = type.Kind switch
        {
            CsTypeKind.Interface => "public interface",
            CsTypeKind.StaticClass or CsTypeKind.Constants => "public static class",
            CsTypeKind.Enum => "public enum",
            _ => type.IsAbstract ? "public abstract class" : "public class",
        };
        var generic = type.TypeParameters.Count > 0 && type.Kind != CsTypeKind.Enum ? $"<{string.Join(", ", type.TypeParameters)}>" : "";
        var bases = type.Bases.Count > 0 ? " : " + string.Join(", ", type.Bases) : "";
        sb.Append($"{keyword} {type.Name}{generic}{bases}\n{{\n");

        var first = true;
        foreach (var member in type.Members)
        {
            if (member.Kind == CsMemberKind.Comment)
            {
                sb.Append("    // ").Append(OneLine(member.Declaration)).Append('\n');
                continue;
            }

            if (!first && (member.Doc.Count > 0 || member.Notes.Count > 0)) sb.Append('\n');
            first = false;

            foreach (var note in member.Notes) sb.Append("    // note: ").Append(OneLine(note)).Append('\n');
            foreach (var doc in member.Doc) sb.Append("    ").Append(doc).Append('\n');
            foreach (var attribute in member.Attributes) sb.Append("    ").Append(attribute).Append('\n');

            if (member.Kind == CsMemberKind.EnumMember)
            {
                sb.Append("    ").Append(member.Declaration).Append(",\n");
                continue;
            }

            var modifiers = new List<string>();
            if (type.Kind != CsTypeKind.Interface) modifiers.Add(member.Access);
            if (member.IsStatic && member.Kind != CsMemberKind.Field) modifiers.Add("static");
            if (member.Kind != CsMemberKind.Constructor && Hides(type, member)) modifiers.Add("new");
            if (member.Virtual) modifiers.Add("virtual");

            sb.Append("    ");
            if (modifiers.Count > 0) sb.Append(string.Join(" ", modifiers)).Append(' ');
            sb.Append(member.Declaration).Append('\n');
        }

        sb.Append("}\n");
    }

    /// <summary>True when a base type already declares something this member would hide.</summary>
    private static bool Hides(CsType type, CsMember member)
    {
        // Every class inherits object's members: a JavaScript toString() binds as ToString().
        if (type.Kind != CsTypeKind.Interface && member.Kind == CsMemberKind.Method && member.Signature is { } signature)
        {
            var parameters = signature[(signature.IndexOf('(') + 1)..^1];
            var arity = parameters.Length == 0 ? 0 : parameters.Count(c => c == ',') + 1;
            if (member.Name is "ToString" or "GetHashCode" or "GetType" && arity == 0 && !member.IsStatic) return true;
            if (member.Name == "Equals" && arity == 1 && !member.IsStatic) return true;
        }

        var visited = new HashSet<CsType>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CsType>(type.BaseModels);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;

            foreach (var inherited in current.Members)
            {
                if (inherited.Kind is CsMemberKind.Comment or CsMemberKind.Constructor) continue;
                if (inherited.Name != member.Name) continue;

                // A method hides an inherited method only with the same signature; anything else
                // with the same name is hidden by name alone.
                if (member.Kind == CsMemberKind.Method && inherited.Kind == CsMemberKind.Method)
                {
                    if (inherited.Signature == member.Signature) return true;
                    continue;
                }
                if (member.Kind == CsMemberKind.Indexer || inherited.Kind == CsMemberKind.Indexer)
                {
                    if (member.Kind == inherited.Kind && member.Signature == inherited.Signature) return true;
                    continue;
                }
                return true;
            }

            foreach (var next in current.BaseModels) pending.Push(next);
        }
        return false;
    }
}
