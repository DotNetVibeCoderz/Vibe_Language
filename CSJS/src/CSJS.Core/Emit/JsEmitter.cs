using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

/// <summary>
/// Walks a C# syntax tree and writes the equivalent ESNext module.
/// <para>
/// The emitter always resolves through the semantic model rather than matching on syntax alone.
/// That is what lets it pick the right overload at a call site, know whether a division is integral,
/// and tell a user type apart from a mapped BCL type — none of which are decidable from syntax.
/// </para>
/// </summary>
public sealed partial class JsEmitter
{
    private readonly EmitContext _ctx;
    private JsWriter Out => _ctx.Writer;

    /// <summary>Set while emitting an iterator method, so <c>yield</c> is legal.</summary>
    private bool _inIterator;

    /// <summary>The type whose body is being emitted, for resolving implicit <c>this</c>.</summary>
    private INamedTypeSymbol? _currentType;

    public JsEmitter(EmitContext context) => _ctx = context;

    /// <summary>Emits every declaration in a parsed file, plus any top-level statements.</summary>
    public void EmitCompilationUnit(CompilationUnitSyntax unit)
    {
        var types = OrderForEvaluation(CollectTypeDeclarations(unit).ToList());

        foreach (var type in types)
        {
            EmitTypeDeclaration(type);
            Out.BlankLine();
        }

        var topLevel = unit.Members.OfType<GlobalStatementSyntax>().ToList();
        if (topLevel.Count > 0)
        {
            Out.WriteLine("// Top-level statements");
            foreach (var statement in topLevel)
                EmitStatement(statement.Statement);
        }

        if (_ctx.Options.EmitEntryPointCall) EmitEntryPointCall(types);
    }

    /// <summary>
    /// Calls <c>Main</c> so the emitted module runs as a program. Browser hosts drive the module
    /// from their own script tag instead, which is why this is opt-in.
    /// </summary>
    private void EmitEntryPointCall(IEnumerable<MemberDeclarationSyntax> types)
    {
        foreach (var declaration in types.OfType<TypeDeclarationSyntax>())
        {
            if (_ctx.Model.GetDeclaredSymbol(declaration) is not { } symbol) continue;

            var main = symbol.GetMembers("Main")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 0);

            if (main is null) continue;

            Out.BlankLine();
            Out.WriteLine("// Entry point");
            var call = $"{_ctx.Names.TypeName(symbol)}.{_ctx.Names.MemberName(main)}()";
            Out.WriteLine(main.IsAsync ? $"await {call};" : $"{call};");
            return;
        }
    }

    /// <summary>
    /// Orders a module's declarations so each is defined before anything that needs it while
    /// the module evaluates.
    /// <para>
    /// C# does not care about declaration order; JavaScript does. A class binding is in its
    /// temporal dead zone until its declaration runs, so <c>class B extends A</c> fails when A comes
    /// later, and so does a static field initialised with <c>P.$default()</c>. Enums and structs
    /// depend on nothing at evaluation time, so they go first; classes follow with every base
    /// class ahead of the classes that extend it. Source order is kept wherever it is already safe.
    /// </para>
    /// </summary>
    private List<MemberDeclarationSyntax> OrderForEvaluation(List<MemberDeclarationSyntax> declarations)
    {
        var symbols = declarations.ToDictionary(d => d, d => _ctx.Model.GetDeclaredSymbol(d) as INamedTypeSymbol);
        var ordered = new List<MemberDeclarationSyntax>();
        var placed = new HashSet<MemberDeclarationSyntax>();

        foreach (var declaration in declarations.Where(d => d is EnumDeclarationSyntax or StructDeclarationSyntax
                     or RecordDeclarationSyntax { ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword }))
        {
            ordered.Add(declaration);
            placed.Add(declaration);
        }

        void Place(MemberDeclarationSyntax declaration, int depth)
        {
            if (placed.Contains(declaration) || depth > declarations.Count) return;

            // Base class first, when it is declared in this same module.
            if (symbols[declaration]?.BaseType is { } baseType)
            {
                var baseDeclaration = declarations.FirstOrDefault(d =>
                    SymbolEqualityComparer.Default.Equals(symbols[d], baseType.OriginalDefinition));
                if (baseDeclaration is not null) Place(baseDeclaration, depth + 1);
            }

            if (placed.Add(declaration)) ordered.Add(declaration);
        }

        foreach (var declaration in declarations) Place(declaration, 0);
        return ordered;
    }

    private static IEnumerable<MemberDeclarationSyntax> CollectTypeDeclarations(SyntaxNode root)
    {
        foreach (var member in root.ChildNodes().OfType<MemberDeclarationSyntax>())
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    // JavaScript has no namespaces; NameResolver already flattened the names.
                    foreach (var nested in CollectTypeDeclarations(ns)) yield return nested;
                    break;

                case TypeDeclarationSyntax or EnumDeclarationSyntax:
                    yield return member;
                    break;
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------------------------

    private void EmitTypeDeclaration(MemberDeclarationSyntax declaration)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
        if (symbol is null) return;
        if (HasAttribute(symbol, "JsIgnoreAttribute")) return;

        // Attributes are compile-time metadata with no runtime presence in JavaScript.
        if (InheritsFromAttribute(symbol)) return;

        // Types that already exist in JavaScript are declarations only — emitting them would
        // shadow the real global or re-declare an imported binding.
        if (NameResolver.JsGlobalName(symbol) is not null) return;
        if (GetJsImport(symbol) is not null) return;

        // An object-literal type has no runtime identity; construction sites emit `{ ... }`
        // directly, so a class declaration would never be referenced.
        if (HasAttribute(symbol, "JsObjectLiteralAttribute")) return;

        switch (declaration)
        {
            // An inlined enum's members are written as numbers wherever they are used, so the
            // object would never be read.
            case EnumDeclarationSyntax when IsInlinedEnum(symbol):
                break;

            case EnumDeclarationSyntax enumDecl:
                EmitEnum(enumDecl, symbol);
                break;

            case InterfaceDeclarationSyntax:
                // Interfaces are erased: they carry no runtime behaviour. They still reach the
                // .d.ts through DtsEmitter, which is where their value actually is.
                break;

            case TypeDeclarationSyntax typeDecl:
                EmitClass(typeDecl, symbol);
                break;
        }
    }

    private void EmitEnum(EnumDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        var name = _ctx.Names.TypeName(symbol);
        WriteDocComment(declaration);
        Out.MapTo(declaration.Identifier.GetLocation());
        Write(ExportPrefix(symbol));
        Out.WriteLine($"const {name} = Object.freeze({{");

        Out.InBlock(() =>
        {
            long next = 0;
            var pairs = new List<(string Name, long Value)>();

            foreach (var member in declaration.Members)
            {
                var memberSymbol = _ctx.Model.GetDeclaredSymbol(member);
                var value = memberSymbol?.ConstantValue is { } constant
                    ? Convert.ToInt64(constant)
                    : next;
                next = value + 1;
                pairs.Add((member.Identifier.Text, value));
                Out.WriteLine($"{member.Identifier.Text}: {value},");
            }

            // The reverse map makes `MyEnum[value]` yield the member name, which is what
            // enum-to-string conversions rely on.
            Out.WriteLine();
            foreach (var (memberName, value) in pairs.DistinctBy(p => p.Value))
                Out.WriteLine($"{value}: '{memberName}',");
        });

        Out.WriteLine("});");
        EmitGlobalAlias(symbol, name);
    }

    private void EmitClass(TypeDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        var name = _ctx.Names.TypeName(symbol);
        var previousType = _currentType;
        _currentType = symbol;

        WriteDocComment(declaration);
        Out.MapTo(declaration.Identifier.GetLocation());
        Write(ExportPrefix(symbol));
        Write($"class {name}");

        // A struct's base type is System.ValueType and an enum's is System.Enum; neither exists
        // in JavaScript, and extending them would produce a reference to nothing.
        var baseType = symbol.BaseType;
        if (baseType is not null
            && baseType.TypeKind == TypeKind.Class
            && baseType.SpecialType is not (SpecialType.System_Object
                or SpecialType.System_ValueType or SpecialType.System_Enum))
        {
            Write($" extends {ReferenceToType(baseType)}");
        }

        Out.WriteLine(" {");
        Out.InBlock(() => EmitClassBody(declaration, symbol));
        Out.WriteLine("}");

        EmitStaticInitializer(declaration, symbol, name);
        EmitGlobalAlias(symbol, name);
        _currentType = previousType;
    }

    private void EmitClassBody(TypeDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        var members = declaration.Members.Where(m => !IsIgnored(m) && !IsRemoved(m)).ToList();

        // Fields and auto-properties become class fields. Doing them first keeps the shape
        // of the emitted class close to how the C# reads.
        foreach (var field in members.OfType<FieldDeclarationSyntax>())
            EmitField(field);

        foreach (var property in members.OfType<PropertyDeclarationSyntax>())
            EmitProperty(property);

        // A primary constructor declares state in the parameter list rather than the body. Records
        // always capture their parameters as members; a class or struct captures only the ones its
        // members actually reference, so emitting all of them is both simpler and harmless.
        if (declaration.ParameterList is { } primaryParameters)
            EmitPrimaryConstructor(primaryParameters, symbol, declaration is RecordDeclarationSyntax);

        foreach (var constructor in members.OfType<ConstructorDeclarationSyntax>())
            EmitConstructor(constructor, symbol);

        // A class with fields but no constructor still needs one when it has a base type,
        // otherwise JavaScript's implicit constructor forwards arguments it should not.
        foreach (var method in members.OfType<MethodDeclarationSyntax>())
            EmitMethod(method);

        foreach (var block in members.OfType<ExtensionBlockDeclarationSyntax>())
            EmitExtensionBlock(block);

        foreach (var indexer in members.OfType<IndexerDeclarationSyntax>())
            EmitIndexer(indexer);

        foreach (var eventDecl in members.OfType<EventFieldDeclarationSyntax>())
            EmitEventField(eventDecl);

        foreach (var op in members.OfType<OperatorDeclarationSyntax>())
            EmitOperator(op);

        if (declaration is RecordDeclarationSyntax record)
            EmitRecordEquality(record, symbol);

        if (symbol.TypeKind == TypeKind.Struct)
            EmitStructSemantics(declaration, symbol);

        EmitToStringBridge(declaration, symbol);
    }

    /// <summary>
    /// The JavaScript-side state of a struct: every field the emitted class carries, with its type.
    /// Explicit fields, auto-property storage, <c>field</c>-keyword storage, and the captured
    /// parameters of a non-record primary constructor all count.
    /// </summary>
    private List<(string Name, ITypeSymbol Type)> StructState(TypeDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        var state = new List<(string, ITypeSymbol)>();

        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsStatic || field.IsConst) continue;

            if (field.AssociatedSymbol is IPropertySymbol property)
                state.Add((IsAutoProperty(property) ? _ctx.Names.MemberName(property) : FieldKeywordStorage(property), field.Type));
            else if (!field.IsImplicitlyDeclared)
                state.Add((_ctx.Names.MemberName(field), field.Type));
        }

        if (declaration is not RecordDeclarationSyntax && declaration.ParameterList is { } primary)
        {
            foreach (var parameter in primary.Parameters)
            {
                if (_ctx.Model.GetDeclaredSymbol(parameter) is { } p)
                    state.Add((NameResolver.Escape(p.Name), p.Type));
            }
        }

        return state.DistinctBy(s => s.Item1).ToList();
    }

    /// <summary>
    /// Gives a struct value semantics. JavaScript has only reference types, so a struct is a class
    /// whose copies have to be made explicitly: <c>$clone</c> is what assignment, argument passing,
    /// and <c>return</c> call, and <c>$default</c> is <c>default(T)</c> — every field zeroed, no
    /// constructor run. Without these, <c>var b = a; b.X = 9;</c> changes <c>a</c> too.
    /// </summary>
    private void EmitStructSemantics(TypeDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        var name = _ctx.Names.TypeName(symbol);
        var state = StructState(declaration, symbol);

        Out.WriteLine();
        Out.WriteLine("/** A copy, as assigning a struct produces in C#. */");
        Out.WriteLine("$clone() {");
        Out.InBlock(() =>
        {
            Out.WriteLine($"const copy = Object.create({name}.prototype);");
            foreach (var (field, type) in state)
            {
                var copied = IsMutableStruct(type)
                    ? (IsNullableValue(type) ? $"this.{field}?.$clone() ?? null" : $"this.{field}.$clone()")
                    : $"this.{field}";
                Out.WriteLine($"copy.{field} = {copied};");
            }
            Out.WriteLine("return copy;");
        });
        Out.WriteLine("}");

        Out.WriteLine();
        Out.WriteLine($"/** The zero value, as default({symbol.Name}) produces in C#. */");
        Out.WriteLine("static $default() {");
        Out.InBlock(() =>
        {
            Out.WriteLine($"const value = Object.create({name}.prototype);");
            foreach (var (field, type) in state)
                Out.WriteLine($"value.{field} = {DefaultValueFor(type)};");
            Out.WriteLine("return value;");
        });
        Out.WriteLine("}");

        // ValueType.Equals compares field by field. A record struct already has its own Equals,
        // and a struct that writes Equals(object) has decided for itself.
        var declaresEquals = declaration.Members.OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.Text == "Equals" && m.ParameterList.Parameters.Count == 1);
        if (declaration is RecordDeclarationSyntax || declaresEquals) return;

        Out.WriteLine();
        Out.WriteLine("/** Field-by-field equality, matching ValueType.Equals. */");
        Out.WriteLine("Equals(other) {");
        Out.InBlock(() =>
        {
            Out.WriteLine($"if (!(other instanceof {name})) return false;");
            if (state.Count == 0)
            {
                Out.WriteLine("return true;");
                return;
            }

            var comparisons = state.Select(s =>
            {
                if (TypeMap.ShapeOf(s.Type) is JsShape.Number or JsShape.String or JsShape.Boolean or JsShape.BigInt
                    && !IsNullableValue(s.Type))
                {
                    return $"this.{s.Name} === other.{s.Name}";
                }
                _ctx.UseHelper("csjsEquals");
                return $"csjsEquals(this.{s.Name}, other.{s.Name})";
            });
            Out.WriteLine($"return {string.Join(" && ", comparisons)};");
        });
        Out.WriteLine("}");
        Out.WriteLine();
        Out.WriteLine("equals(other) { return this.Equals(other); }");
    }

    /// <summary>
    /// True for a struct declared in this compilation whose instances can change after
    /// construction. Copying a <c>readonly struct</c> is unobservable, so it is shared instead.
    /// </summary>
    internal static bool IsMutableStruct(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol named && TypeMap.IsNullableValueType(named, out var underlying)) type = underlying;
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsReadOnly: false } s
               && s.DeclaringSyntaxReferences.Length > 0
               && !s.IsRefLikeType;
    }

    private static bool IsNullableValue(ITypeSymbol type)
        => type is INamedTypeSymbol named && TypeMap.IsNullableValueType(named, out _);

    /// <summary>A struct declared in this compilation, mutable or not.</summary>
    private static bool IsDeclaredStruct(ITypeSymbol? type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Struct } s && s.DeclaringSyntaxReferences.Length > 0;

    private static bool IsAutoProperty(IPropertySymbol property)
        => property.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(p => p.ExpressionBody is null
                      && p.AccessorList is { } list
                      && list.Accessors.All(a => a.Body is null && a.ExpressionBody is null))
           // A record's positional parameters are auto-properties without a property declaration.
           || property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).Any(n => n is ParameterSyntax);

    /// <summary>
    /// Storage for a property that uses the C# 14 <c>field</c> keyword. The accessors stay real
    /// accessors, so the value needs a slot of its own beside them.
    /// </summary>
    private string FieldKeywordStorage(IPropertySymbol property) => "$" + _ctx.Names.MemberName(property);

    /// <summary>
    /// Adds a lowercase <c>toString</c> delegating to the user's <c>ToString</c>.
    /// Without it, string interpolation and <c>console.log</c> would show [object Object]
    /// even though the type defines exactly the conversion the developer expected.
    /// </summary>
    private void EmitToStringBridge(TypeDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        var hasToString = declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.Text == "ToString" && m.ParameterList.Parameters.Count == 0);

        if (!hasToString) return;

        Out.WriteLine();
        Out.WriteLine("toString() { return this.ToString(); }");
    }

    private void EmitField(FieldDeclarationSyntax field)
    {
        var isStatic = field.Modifiers.Any(SyntaxKind.StaticKeyword);
        var isConst = field.Modifiers.Any(SyntaxKind.ConstKeyword);

        foreach (var variable in field.Declaration.Variables)
        {
            var symbol = _ctx.Model.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (symbol is null || HasAttribute(symbol, "JsIgnoreAttribute") || IsRemoved(symbol)) continue;

            Out.MapTo(variable.GetLocation());
            // `const` fields are still class fields; freezing them individually is not worth
            // the output noise, and the C# compiler already rejected reassignment.
            if (isStatic || isConst) Write("static ");
            Write(_ctx.Names.MemberName(symbol));

            if (variable.Initializer is not null)
            {
                Write(" = ");
                EmitExpressionAs(variable.Initializer.Value, symbol.Type);
            }
            else
            {
                Write($" = {DefaultValueFor(symbol.Type)}");
            }

            Out.WriteLine(";");
        }
    }

    private void EmitProperty(PropertyDeclarationSyntax property)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(property);
        if (symbol is null || HasAttribute(symbol, "JsIgnoreAttribute") || IsRemoved(symbol)) return;

        var name = _ctx.Names.MemberName(symbol);
        var isStatic = property.Modifiers.Any(SyntaxKind.StaticKeyword);
        var prefix = isStatic ? "static " : "";

        // Expression-bodied property: `public int X => expr;`
        if (property.ExpressionBody is not null)
        {
            Out.MapTo(property.Identifier.GetLocation());
            Write($"{prefix}get {name}() {{ return ");
            EmitExpression(property.ExpressionBody.Expression);
            Out.WriteLine("; }");
            return;
        }

        var accessors = property.AccessorList?.Accessors ?? default;
        var getter = accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
        var setter = accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))
                     ?? accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.InitAccessorDeclaration));

        static bool Bodiless(AccessorDeclarationSyntax? a) => a is { Body: null, ExpressionBody: null };

        // An abstract property has no implementation to emit, and a field would be an own
        // property of every instance — shadowing the getter the subclass defines on its prototype.
        if (symbol.IsAbstract) return;

        // A virtual auto-property must stay an accessor for the same reason: an override's
        // accessors live on the subclass prototype, which an own field would hide.
        var isAuto = accessors.Count > 0 && accessors.All(Bodiless) && !symbol.IsVirtual && !symbol.IsOverride;

        if (isAuto)
        {
            // An auto-property is just storage. Emitting a plain field instead of a
            // getter/setter pair keeps the output readable and avoids a pointless indirection.
            Out.MapTo(property.Identifier.GetLocation());
            Write($"{prefix}{name} = ");
            if (property.Initializer is not null) EmitExpressionAs(property.Initializer.Value, symbol.Type);
            else Write(DefaultValueFor(symbol.Type));
            Out.WriteLine(";");
            return;
        }

        // C# 14's `field` keyword, or a bodiless accessor beside a bodied one, means the
        // compiler synthesised storage. The accessors stay accessors and the storage gets a slot.
        var usesStorage = accessors.Any(Bodiless)
                          || property.DescendantNodes().Any(n => n.IsKind(SyntaxKind.FieldExpression));
        var storage = FieldKeywordStorage(symbol);
        if (usesStorage)
        {
            Out.MapTo(property.Identifier.GetLocation());
            Write($"{prefix}{storage} = ");
            if (property.Initializer is not null) EmitExpressionAs(property.Initializer.Value, symbol.Type);
            // Synthesised storage starts at the CLR default — null for a string, whatever its
            // annotation says — and `field ?? fallback` getters depend on exactly that.
            else Write(symbol.Type.SpecialType == SpecialType.System_String ? "null" : DefaultValueFor(symbol.Type));
            Out.WriteLine(";");
        }

        var target = isStatic ? _ctx.Names.TypeName(symbol.ContainingType) : "this";

        if (getter is not null)
        {
            Out.MapTo(getter.GetLocation());
            Write($"{prefix}get {name}() ");
            if (Bodiless(getter)) Out.WriteLine($"{{ return {target}.{storage}; }}");
            else EmitAccessorBody(getter, isGetter: true);
        }

        if (setter is not null)
        {
            Out.MapTo(setter.GetLocation());
            Write($"{prefix}set {name}(value) ");
            if (Bodiless(setter)) Out.WriteLine($"{{ {target}.{storage} = value; }}");
            else EmitAccessorBody(setter, isGetter: false);
        }
    }

    private void EmitAccessorBody(AccessorDeclarationSyntax accessor, bool isGetter)
    {
        // A lambda inside an accessor must not inherit the enclosing method's ref/out rewrite.
        var previousByRef = _byRefReturn;
        _byRefReturn = null;
        try
        {
            EmitAccessorBodyCore(accessor, isGetter);
        }
        finally
        {
            _byRefReturn = previousByRef;
        }
    }

    private void EmitAccessorBodyCore(AccessorDeclarationSyntax accessor, bool isGetter)
    {
        if (accessor.Body is not null)
        {
            EmitBlock(accessor.Body);
            return;
        }

        if (accessor.ExpressionBody is not null)
        {
            Write("{ ");
            if (isGetter) Write("return ");
            EmitExpression(accessor.ExpressionBody.Expression);
            Out.WriteLine("; }");
            return;
        }

        Out.WriteLine("{ }");
    }

    /// <summary>
    /// Emits the fields and constructor a primary constructor implies.
    /// <para>
    /// A captured parameter is state, so it has to become a field: the members that reference it
    /// are separate JavaScript functions and cannot see a constructor argument.
    /// </para>
    /// </summary>
    private void EmitPrimaryConstructor(
        ParameterListSyntax parameters, INamedTypeSymbol symbol, bool isRecord)
    {
        foreach (var parameter in parameters.Parameters)
        {
            var type = _ctx.Model.GetDeclaredSymbol(parameter)?.Type;
            Out.WriteLine($"{NameResolver.Escape(parameter.Identifier.Text)} = {DefaultValueFor(type)};");
        }

        Out.WriteLine();
        Out.Write("constructor(");
        WriteJoined(parameters.Parameters, p => Write(NameResolver.Escape(p.Identifier.Text)));
        Out.WriteLine(") {");
        Out.InBlock(() =>
        {
            if (NeedsSuperCall(symbol)) Out.WriteLine("super();");

            foreach (var parameter in parameters.Parameters)
            {
                var field = NameResolver.Escape(parameter.Identifier.Text);
                Out.WriteLine($"this.{field} = {field};");
            }

            // A record's field initialisers are its parameters; a class may also declare its own,
            // and those are emitted as class fields, which run before the constructor body.
        });
        Out.WriteLine("}");
    }

    /// <summary>True when the emitted class extends something and therefore must call super.</summary>
    private static bool NeedsSuperCall(INamedTypeSymbol symbol)
        => symbol.BaseType is { TypeKind: TypeKind.Class } baseType
           && baseType.SpecialType is not (SpecialType.System_Object
               or SpecialType.System_ValueType or SpecialType.System_Enum);

    private void EmitRecordEquality(RecordDeclarationSyntax declaration, INamedTypeSymbol symbol)
    {
        // Only the members the developer actually wrote participate. The compiler synthesises
        // EqualityContract on every record, and comparing it would be comparing nothing useful.
        var fields = new List<string>();

        if (declaration.ParameterList is { } parameters)
            fields.AddRange(parameters.Parameters.Select(p => NameResolver.Escape(p.Identifier.Text)));

        foreach (var member in declaration.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.StaticKeyword):
                    if (_ctx.Model.GetDeclaredSymbol(property) is { } p) fields.Add(_ctx.Names.MemberName(p));
                    break;

                case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword):
                    foreach (var variable in field.Declaration.Variables)
                        if (_ctx.Model.GetDeclaredSymbol(variable) is { } f) fields.Add(_ctx.Names.MemberName(f));
                    break;
            }
        }

        fields = fields.Distinct(StringComparer.Ordinal).ToList();
        if (fields.Count == 0) return;

        var hasCustomToString = declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.Text == "ToString" && m.ParameterList.Parameters.Count == 0);

        Out.WriteLine();
        Out.WriteLine("/** Value equality, matching C# record semantics. */");
        Out.WriteLine("Equals(other) {");
        Out.InBlock(() =>
        {
            Out.WriteLine("if (other === null || other === undefined) return false;");
            Out.WriteLine("if (other.constructor !== this.constructor) return false;");
            var comparisons = string.Join(" && ", fields.Select(f => $"this.{f} === other.{f}"));
            Out.WriteLine($"return {comparisons};");
        });
        Out.WriteLine("}");

        // csjsEquals looks for a lowercase `equals`, so both spellings are provided.
        Out.WriteLine();
        Out.WriteLine("equals(other) { return this.Equals(other); }");

        if (hasCustomToString) return;

        Out.WriteLine();
        Out.WriteLine("ToString() {");
        Out.InBlock(() =>
        {
            var parts = string.Join(", ", fields.Select(f => $"{f} = ${{this.{f}}}"));
            Out.WriteLine($"return `{_ctx.Names.TypeName(symbol)} {{ {parts} }}`;");
        });
        Out.WriteLine("}");

        Out.WriteLine();
        Out.WriteLine("toString() { return this.ToString(); }");
    }

    // -------------------------------------------------------------------------------------------
    // Methods and constructors
    // -------------------------------------------------------------------------------------------

    private void EmitConstructor(ConstructorDeclarationSyntax constructor, INamedTypeSymbol owner)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(constructor);
        if (symbol is null) return;

        var name = _ctx.Names.MemberName(symbol);
        Out.WriteLine();
        WriteDocComment(constructor);
        Out.MapTo(constructor.Identifier.GetLocation());

        if (name == "constructor")
        {
            Write("constructor(");
            EmitParameterList(constructor.ParameterList);
            Out.WriteLine(") {");
            Out.InBlock(() =>
            {
                EmitConstructorInitializer(constructor, owner);
                EmitStatementsOf(constructor.Body, constructor.ExpressionBody);
            });
            Out.WriteLine("}");
            return;
        }

        // Secondary constructors become static factories: JavaScript permits only one
        // `constructor`, and call sites already resolve to this exact overload by name.
        Write($"static {name}(");
        EmitParameterList(constructor.ParameterList);
        Out.WriteLine(") {");
        Out.InBlock(() =>
        {
            Out.WriteLine($"const self = Object.create({_ctx.Names.TypeName(owner)}.prototype);");
            _thisAlias = "self";
            EmitStatementsOf(constructor.Body, constructor.ExpressionBody);
            _thisAlias = null;
            Out.WriteLine("return self;");
        });
        Out.WriteLine("}");
    }

    private void EmitConstructorInitializer(ConstructorDeclarationSyntax constructor, INamedTypeSymbol owner)
    {
        var initializer = constructor.Initializer;
        var hasBaseClass = NeedsSuperCall(owner);

        if (initializer is null)
        {
            if (hasBaseClass) Out.WriteLine("super();");
            return;
        }

        if (initializer.IsKind(SyntaxKind.BaseConstructorInitializer))
        {
            Write("super(");
            EmitArgumentList(initializer.ArgumentList.Arguments);
            Out.WriteLine(");");
            return;
        }

        // `this(...)` chains to a sibling overload, which is now a static factory. Copying the
        // result's own properties reproduces the delegation without a second allocation escaping.
        if (hasBaseClass) Out.WriteLine("super();");
        var target = _ctx.Model.GetSymbolInfo(initializer).Symbol as IMethodSymbol;
        if (target is not null)
        {
            Write($"Object.assign(this, {_ctx.Names.TypeName(owner)}.{_ctx.Names.MemberName(target)}(");
            EmitArgumentList(initializer.ArgumentList.Arguments);
            Out.WriteLine("));");
        }
    }

    private void EmitMethod(MethodDeclarationSyntax method)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(method);
        if (symbol is null || HasAttribute(symbol, "JsIgnoreAttribute") || IsRemoved(symbol)) return;

        // An abstract or extern declaration has no body to emit.
        if (method.Body is null && method.ExpressionBody is null && GetJsBody(symbol) is null)
        {
            if (symbol.IsAbstract) return;
            if (method.Modifiers.Any(SyntaxKind.PartialKeyword)) return;
            return;
        }

        var name = _ctx.Names.MemberName(symbol);
        var isIterator = IsIterator(method);

        Out.WriteLine();
        WriteDocComment(method);
        Out.MapTo(method.Identifier.GetLocation());

        if (symbol.IsStatic) Write("static ");
        if (symbol.IsAsync) Write("async ");
        if (isIterator) Write("*");

        Write($"{name}(");
        EmitParameterList(method.ParameterList);
        Out.WriteLine(") {");

        var previousIterator = _inIterator;
        _inIterator = isIterator;
        Out.InBlock(() =>
        {
            if (GetJsBody(symbol) is { } raw) WriteRawJs(raw);
            else EmitFunctionBody(symbol, method.Body, method.ExpressionBody);
        });
        _inIterator = previousIterator;

        Out.WriteLine("}");
    }

    /// <summary>
    /// The names a function hands back after its result, when it has <c>ref</c> or <c>out</c>
    /// parameters; null otherwise. Set while emitting such a function's own body.
    /// </summary>
    private List<string>? _byRefReturn;

    /// <summary>
    /// Emits a method or local function body, rewriting <c>ref</c> and <c>out</c> parameters.
    /// <para>
    /// JavaScript passes everything by value, so a by-reference parameter becomes part of the
    /// result instead: every <c>return x</c> turns into <c>return [x, a, b]</c>, and the call
    /// site writes the extra elements back into the variables it passed. That is exactly what the
    /// caller observes in C# — the variables hold the callee's final values once it returns.
    /// </para>
    /// </summary>
    private void EmitFunctionBody(IMethodSymbol symbol, BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
    {
        var byRef = symbol.Parameters.Where(p => p.RefKind is RefKind.Ref or RefKind.Out).ToList();
        var previous = _byRefReturn;

        if (byRef.Count == 0)
        {
            _byRefReturn = null;
            EmitStatementsOf(body, expressionBody);
            _byRefReturn = previous;
            return;
        }

        var names = byRef.Select(p => NameResolver.Escape(p.Name)).ToList();
        foreach (var parameter in byRef.Where(p => p.RefKind == RefKind.Out))
            Out.WriteLine($"let {NameResolver.Escape(parameter.Name)};");

        _byRefReturn = names;
        var tail = string.Join(", ", names);

        if (expressionBody is not null)
        {
            DeclareHoistedVariables(expressionBody.Expression);
            Out.MapTo(expressionBody.Expression.GetLocation());
            if (symbol.ReturnsVoid)
            {
                EmitExpression(expressionBody.Expression);
                Out.WriteLine(";");
                Out.WriteLine($"return [undefined, {tail}];");
            }
            else
            {
                Write("return [");
                EmitExpressionAs(expressionBody.Expression, symbol.ReturnType);
                Out.WriteLine($", {tail}];");
            }
        }
        else if (body is not null)
        {
            EmitStatementsOf(body, null);

            // A void method may simply run off its end; that is a return too.
            if (symbol.ReturnsVoid && body.Statements.LastOrDefault() is not (ReturnStatementSyntax or ThrowStatementSyntax))
                Out.WriteLine($"return [undefined, {tail}];");
        }

        _byRefReturn = previous;
    }

    private void EmitIndexer(IndexerDeclarationSyntax indexer)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(indexer);
        if (symbol is null) return;

        // JavaScript has no user-defined indexers, so they surface as $get/$set methods.
        // The expression emitter rewrites `obj[i]` on a user type into these calls.
        var accessors = indexer.AccessorList?.Accessors ?? default;
        var getter = accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
        var setter = accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

        if (indexer.ExpressionBody is not null)
        {
            Out.WriteLine();
            Write("$get(");
            EmitParameterList(indexer.ParameterList);
            Write(") { return ");
            EmitExpression(indexer.ExpressionBody.Expression);
            Out.WriteLine("; }");
            return;
        }

        if (getter is not null)
        {
            Out.WriteLine();
            Write("$get(");
            EmitParameterList(indexer.ParameterList);
            Write(") ");
            EmitAccessorBody(getter, isGetter: true);
        }

        if (setter is not null)
        {
            Out.WriteLine();
            Write("$set(");
            EmitParameterList(indexer.ParameterList);
            Write(", value) ");
            EmitAccessorBody(setter, isGetter: false);
        }
    }

    private void EmitEventField(EventFieldDeclarationSyntax eventField)
    {
        // An event is a list of handlers plus add/remove. Emitting it as an array keeps
        // `+=` / `-=` / invocation all expressible without a runtime helper.
        foreach (var variable in eventField.Declaration.Variables)
        {
            var symbol = _ctx.Model.GetDeclaredSymbol(variable);
            if (symbol is null) continue;
            var name = _ctx.Names.MemberName(symbol);
            var prefix = eventField.Modifiers.Any(SyntaxKind.StaticKeyword) ? "static " : "";
            Out.WriteLine($"{prefix}{name} = [];");
        }
    }

    private void EmitOperator(OperatorDeclarationSyntax op)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(op);
        if (symbol is null) return;

        // Operators become static methods; the expression emitter routes operands here when
        // the semantic model reports a user-defined operator.
        var name = OperatorMethodName(op.OperatorToken.Kind());
        Out.WriteLine();
        Write($"static {name}(");
        EmitParameterList(op.ParameterList);
        Out.WriteLine(") {");
        Out.InBlock(() => EmitStatementsOf(op.Body, op.ExpressionBody));
        Out.WriteLine("}");
    }

    internal static string OperatorMethodName(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PlusToken => "op_Addition",
        SyntaxKind.MinusToken => "op_Subtraction",
        SyntaxKind.AsteriskToken => "op_Multiply",
        SyntaxKind.SlashToken => "op_Division",
        SyntaxKind.PercentToken => "op_Modulus",
        SyntaxKind.EqualsEqualsToken => "op_Equality",
        SyntaxKind.ExclamationEqualsToken => "op_Inequality",
        SyntaxKind.LessThanToken => "op_LessThan",
        SyntaxKind.GreaterThanToken => "op_GreaterThan",
        SyntaxKind.LessThanEqualsToken => "op_LessThanOrEqual",
        SyntaxKind.GreaterThanEqualsToken => "op_GreaterThanOrEqual",
        _ => "op_Unknown",
    };

    private void EmitStaticInitializer(TypeDeclarationSyntax declaration, INamedTypeSymbol symbol, string typeName)
    {
        var staticCtor = declaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => c.Modifiers.Any(SyntaxKind.StaticKeyword));

        if (staticCtor is null) return;

        // C# runs a static constructor lazily; running it at module evaluation is the closest
        // ESM equivalent and is deterministic, which matters more here than laziness.
        Out.WriteLine();
        Out.WriteLine($"// Static constructor for {typeName}");
        Out.WriteLine("(() => {");
        _thisAlias = typeName;
        Out.InBlock(() => EmitStatementsOf(staticCtor.Body, staticCtor.ExpressionBody));
        _thisAlias = null;
        Out.WriteLine("})();");
    }

    // -------------------------------------------------------------------------------------------
    // Parameters
    // -------------------------------------------------------------------------------------------

    private void EmitParameterList(BaseParameterListSyntax parameters)
    {
        var first = true;
        foreach (var parameter in parameters.Parameters)
        {
            // An out parameter is not passed in; the callee hands it back in its result tuple.
            if (parameter.Modifiers.Any(SyntaxKind.OutKeyword)) continue;

            if (!first) Write(", ");
            first = false;

            if (parameter.Modifiers.Any(SyntaxKind.ParamsKeyword)) Write("...");
            Write(NameResolver.Escape(parameter.Identifier.Text));

            if (parameter.Default is not null)
            {
                Write(" = ");
                EmitExpression(parameter.Default.Value);
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>Alias used for <c>this</c>, set while emitting bodies that are not real methods.</summary>
    private string? _thisAlias;

    private void Write(string text) => Out.Write(text);

    private void WriteJoined<T>(IEnumerable<T> items, Action<T> emit, string separator = ", ")
    {
        var first = true;
        foreach (var item in items)
        {
            if (!first) Write(separator);
            first = false;
            emit(item);
        }
    }

    private void EmitStatementsOf(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
    {
        if (body is not null)
        {
            DeclareHoistedVariables(body);
            foreach (var statement in body.Statements)
                EmitStatement(statement);
            return;
        }

        if (expressionBody is null) return;

        // An expression-bodied member has no block, but its expression can still introduce
        // pattern variables that need a binding before the assignment rewrite runs.
        DeclareHoistedVariables(expressionBody.Expression);

        var producesValue = ExpressionProducesValue(expressionBody.Expression);
        Out.MapTo(expressionBody.Expression.GetLocation());
        if (producesValue) Write("return ");
        EmitExpression(expressionBody.Expression);
        Out.WriteLine(";");
    }

    private bool ExpressionProducesValue(ExpressionSyntax expression)
    {
        // An expression-bodied member returns unless the expression is a void call or assignment.
        if (expression is AssignmentExpressionSyntax) return false;
        var type = _ctx.TypeFor(expression);
        return type is null || type.SpecialType != SpecialType.System_Void;
    }

    private string ExportPrefix(INamedTypeSymbol symbol)
    {
        if (_ctx.Options.Module != ModuleKind.ESM) return "";
        if (HasAttribute(symbol, "JsExportAttribute")) return "export ";
        // Public types are exported so host pages and other modules can reach them; bundlers
        // drop the unreferenced ones, so this costs nothing in the final output.
        return symbol.DeclaredAccessibility == Accessibility.Public ? "export " : "";
    }

    private void EmitGlobalAlias(INamedTypeSymbol symbol, string name)
    {
        var attribute = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsExportAttribute");
        if (attribute is null) return;

        var isGlobal = attribute.NamedArguments.Any(a => a.Key == "Global" && a.Value.Value is true);
        if (!isGlobal) return;

        var exported = attribute.ConstructorArguments.Length > 0
                       && attribute.ConstructorArguments[0].Value is string s && s.Length > 0
            ? s
            : name;

        Out.WriteLine($"globalThis.{exported} = {name};");
    }

    /// <summary>Emits a reference to a type, importing it when it lives in another module.</summary>
    private string ReferenceToType(INamedTypeSymbol type)
    {
        // A framework type that JavaScript already has resolves to the built-in constructor,
        // so `class MyError : Exception` becomes `class MyError extends Error`.
        if (TypeMap.JsConstructorName(type) is { } builtIn) return builtIn;

        if (GetJsImport(type) is { } import)
        {
            var alias = _ctx.Names.TypeName(type);
            _ctx.UseImport(import.Module, import.IsDefault ? null : import.Export ?? type.Name, alias);
            return alias;
        }

        if (NameResolver.JsGlobalName(type) is { } global) return global;

        var name = _ctx.Names.TypeName(type);
        if (_ctx.TypeModules.TryGetValue(type.OriginalDefinition, out var module)
            && !string.Equals(module, _ctx.CurrentModule, StringComparison.OrdinalIgnoreCase))
        {
            _ctx.UseImport(ModulePaths.Relative(_ctx.CurrentModule, module), name, name);
        }

        return name;
    }

    private string DefaultValueFor(ITypeSymbol? type)
    {
        if (type is null) return "null";
        if (type.TypeKind == TypeKind.Enum) return "0";

        // default(T) for a struct is a zeroed instance, never null.
        if (IsDeclaredStruct(type) && type is INamedTypeSymbol structType)
            return $"{ReferenceToType(structType)}.$default()";

        return TypeMap.ShapeOf(type) switch
        {
            JsShape.Number => "0",
            JsShape.BigInt => "0n",
            JsShape.Boolean => "false",
            // C# strings default to null, not empty, and code frequently null-checks them.
            JsShape.String => type.NullableAnnotation == NullableAnnotation.NotAnnotated ? "''" : "null",
            _ => "null",
        };
    }

    private static bool IsIterator(MethodDeclarationSyntax method)
        => method.Body is not null
           && method.Body.DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax
                                                                     and not LocalFunctionStatementSyntax)
               .Any(n => n is YieldStatementSyntax);

    private bool IsIgnored(MemberDeclarationSyntax member)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(member);
        return symbol is not null && HasAttribute(symbol, "JsIgnoreAttribute");
    }

    /// <summary>True when whole-program analysis found a member nothing references.</summary>
    private bool IsRemoved(MemberDeclarationSyntax member)
        => _ctx.Model.GetDeclaredSymbol(member) is { } symbol && IsRemoved(symbol);

    private bool IsRemoved(ISymbol symbol) => _ctx.Analysis.UnusedPrivateMembers.Contains(symbol.OriginalDefinition);

    /// <summary>True for an enum written as plain numbers, with no runtime object.</summary>
    private bool IsInlinedEnum(INamedTypeSymbol type) => _ctx.Analysis.InlinedEnums.Contains(type.OriginalDefinition);

    /// <summary>
    /// True when a parameter comes from a primary constructor, and so was emitted as a field.
    /// </summary>
    private static bool IsPrimaryConstructorCapture(IParameterSymbol parameter)
    {
        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } ctor)
            return false;

        // A primary constructor is declared by the type itself rather than by a constructor
        // declaration, which is exactly what distinguishes it from an ordinary one.
        return ctor.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .Any(node => node is TypeDeclarationSyntax);
    }

    internal static bool InheritsFromAttribute(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "System.Attribute") return true;
        return false;
    }

    internal static bool HasAttribute(ISymbol symbol, string attributeTypeName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.Name == attributeTypeName);

    /// <summary>The <c>[JsTemplate]</c> pattern for a member, when it has one.</summary>
    internal static string? GetJsTemplate(ISymbol symbol)
    {
        var attribute = symbol.OriginalDefinition.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "JsTemplateAttribute");
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    internal static string? GetJsBody(ISymbol symbol)
    {
        var attribute = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsBodyAttribute");
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    internal readonly record struct JsImportInfo(string Module, string? Export, bool IsDefault);

    internal static JsImportInfo? GetJsImport(ISymbol symbol)
    {
        var attribute = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsImportAttribute");
        if (attribute is null) return null;

        var module = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;
        if (module is null) return null;

        var export = attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Value as string
            : null;

        var isDefault = attribute.NamedArguments.Any(a => a.Key == "Default" && a.Value.Value is true);
        return new JsImportInfo(module, export, isDefault);
    }

    private void WriteRawJs(string code)
    {
        foreach (var line in code.Replace("\r\n", "\n").Split('\n'))
            Out.WriteLine(line.TrimEnd());
    }

    private void WriteDocComment(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (trivia is null) return;

        var summary = trivia.Content
            .OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.LocalName.Text == "summary");

        if (summary is null) return;

        var text = FlattenDocText(summary.Content.ToFullString());
        if (text.Length == 0) return;

        // JSDoc has no equivalent of <para>, so long summaries become one line rather than
        // carrying XML tags into JavaScript where nothing reads them.
        Out.WriteLine($"/** {text} */");
    }

    /// <summary>Reduces XML doc content to a single line of readable prose.</summary>
    internal static string FlattenDocText(string xml)
    {
        var text = xml.Replace("///", "");

        // Inline elements carry the text worth keeping; block tags are structure only.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"</?(para|summary|remarks|list|item|description)[^>]*>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<(c|code)>(.*?)</\1>", "$2",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<see\s+cref=""[A-Za-z]:([^""]+)""\s*/>", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<paramref\s+name=""([^""]+)""\s*/>", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", " ");

        var joined = string.Join(" ", text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));

        // Comment terminators inside a summary would end the JSDoc block early.
        return System.Text.RegularExpressions.Regex.Replace(joined, @"\s+", " ")
            .Replace("*/", "*\\/")
            .Trim();
    }
}
