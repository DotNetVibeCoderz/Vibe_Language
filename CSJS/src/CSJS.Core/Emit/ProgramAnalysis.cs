using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

/// <summary>
/// Facts about the whole program that no single file can establish on its own: which private
/// members are never referenced, and which enums are never turned into text.
/// <para>
/// Both drive optimisations that are only sound with the complete picture. A private member can
/// be referenced from another <c>partial</c> declaration in another file, and an enum is
/// stringified wherever any file interpolates it. The analysis therefore runs once over every
/// tree before any module is emitted, and is conservative wherever it cannot be sure.
/// </para>
/// </summary>
public sealed class ProgramAnalysis
{
    /// <summary>Private members the emitter can leave out.</summary>
    public IReadOnlySet<ISymbol> UnusedPrivateMembers { get; }

    /// <summary>Enums whose members can be written as plain numbers, with no runtime object.</summary>
    public IReadOnlySet<INamedTypeSymbol> InlinedEnums { get; }

    public static ProgramAnalysis Empty { get; } = new(
        new HashSet<ISymbol>(SymbolEqualityComparer.Default),
        new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private ProgramAnalysis(IReadOnlySet<ISymbol> unused, IReadOnlySet<INamedTypeSymbol> inlined)
    {
        UnusedPrivateMembers = unused;
        InlinedEnums = inlined;
    }

    public static ProgramAnalysis Analyze(Compilation compilation, IReadOnlyList<SyntaxTree> trees, TranspileOptions options)
    {
        if (!options.RemoveUnusedPrivateMembers && !options.InlineEnums) return Empty;

        var referenced = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var candidates = new List<(ISymbol Symbol, SyntaxNode Declaration)>();
        var enums = new List<INamedTypeSymbol>();
        var stringifiedEnums = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case EnumDeclarationSyntax enumDeclaration
                        when model.GetDeclaredSymbol(enumDeclaration) is { } enumSymbol:
                        enums.Add(enumSymbol);
                        break;

                    case MemberDeclarationSyntax member when options.RemoveUnusedPrivateMembers:
                        CollectCandidates(model, member, candidates);
                        break;
                }

                if (node is ExpressionSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax
                    or SubpatternSyntax or CrefSyntax or AttributeSyntax)
                {
                    RecordReferences(model, node, referenced);
                }

                if (options.InlineEnums && node is ExpressionSyntax expression)
                    RecordEnumStringification(model, expression, stringifiedEnums);
            }
        }

        var unused = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var (symbol, declaration) in candidates)
        {
            if (!referenced.Contains(symbol.OriginalDefinition) && IsSafeToRemove(declaration))
                unused.Add(symbol.OriginalDefinition);
        }

        var inlined = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        if (options.InlineEnums)
        {
            foreach (var enumSymbol in enums)
            {
                if (IsPubliclyVisible(enumSymbol)) continue;
                if (stringifiedEnums.Contains(enumSymbol)) continue;
                if (enumSymbol.GetAttributes().Any(a => a.AttributeClass?.Name.StartsWith("Js", StringComparison.Ordinal) == true))
                    continue;
                inlined.Add(enumSymbol);
            }
        }

        return new ProgramAnalysis(unused, inlined);
    }

    private static void CollectCandidates(SemanticModel model, MemberDeclarationSyntax member, List<(ISymbol, SyntaxNode)> into)
    {
        // Value types and records compare every field, private ones included, so a field nobody
        // reads still decides equality there.
        if (member.Parent is RecordDeclarationSyntax or StructDeclarationSyntax) return;

        switch (member)
        {
            case MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null } method:
                AddIfPrivate(model.GetDeclaredSymbol(method), method, into);
                break;

            case PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: null } property:
                AddIfPrivate(model.GetDeclaredSymbol(property), property, into);
                break;

            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                    AddIfPrivate(model.GetDeclaredSymbol(variable), variable, into);
                break;

            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                    AddIfPrivate(model.GetDeclaredSymbol(variable), variable, into);
                break;
        }
    }

    private static void AddIfPrivate(ISymbol? symbol, SyntaxNode declaration, List<(ISymbol, SyntaxNode)> into)
    {
        if (symbol is null || symbol.DeclaredAccessibility != Accessibility.Private) return;
        if (symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract) return;
        if (symbol is IMethodSymbol { MethodKind: not MethodKind.Ordinary }) return;

        // An interop attribute means something outside C# reaches the member by name.
        if (symbol.GetAttributes().Any(a => a.AttributeClass?.Name.StartsWith("Js", StringComparison.Ordinal) == true))
            return;

        into.Add((symbol, declaration));
    }

    /// <summary>
    /// A member may only go if dropping it cannot change behaviour. An initialiser that calls
    /// something could have side effects, so only plainly inert ones qualify.
    /// </summary>
    private static bool IsSafeToRemove(SyntaxNode declaration)
    {
        var initializer = declaration switch
        {
            VariableDeclaratorSyntax variable => variable.Initializer?.Value,
            PropertyDeclarationSyntax property => property.Initializer?.Value,
            _ => null,
        };

        return initializer is null || IsInert(initializer);
    }

    private static bool IsInert(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax or DefaultExpressionSyntax or TypeOfExpressionSyntax
            or AnonymousFunctionExpressionSyntax or InterpolatedStringExpressionSyntax => true,
        ParenthesizedExpressionSyntax p => IsInert(p.Expression),
        PrefixUnaryExpressionSyntax u => IsInert(u.Operand),
        CollectionExpressionSyntax c => c.Elements.All(e => e is ExpressionElementSyntax ee && IsInert(ee.Expression)),
        InitializerExpressionSyntax i => i.Expressions.All(IsInert),
        ArrayCreationExpressionSyntax a => a.Initializer is null || a.Initializer.Expressions.All(IsInert),
        ImplicitArrayCreationExpressionSyntax a => a.Initializer.Expressions.All(IsInert),
        // Constructing an empty framework collection runs no user code.
        ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: null } creation
            => creation.Type.ToString() is var t && (t.StartsWith("List<", StringComparison.Ordinal)
                || t.StartsWith("Dictionary<", StringComparison.Ordinal) || t.StartsWith("HashSet<", StringComparison.Ordinal)),
        _ => false,
    };

    private static void RecordReferences(SemanticModel model, SyntaxNode node, HashSet<ISymbol> into)
    {
        var info = model.GetSymbolInfo(node);
        Add(info.Symbol);
        foreach (var candidate in info.CandidateSymbols) Add(candidate);

        // Deconstruction and foreach call members that no identifier names.
        if (node is AssignmentExpressionSyntax { Left: TupleExpressionSyntax or DeclarationExpressionSyntax } assignment)
        {
            var deconstruction = model.GetDeconstructionInfo(assignment);
            Add(deconstruction.Method);
        }

        void Add(ISymbol? symbol)
        {
            if (symbol is null) return;
            into.Add(symbol.OriginalDefinition);
            if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) into.Add(reduced.OriginalDefinition);
            if (symbol is IPropertySymbol property)
            {
                if (property.GetMethod is { } getter) into.Add(getter.OriginalDefinition);
                if (property.SetMethod is { } setter) into.Add(setter.OriginalDefinition);
            }
        }
    }

    /// <summary>
    /// Marks an enum as needing its runtime object whenever one of its values could be turned
    /// into text: interpolated, concatenated, boxed, passed to an <c>Enum</c> API, or used as the
    /// receiver of <c>ToString</c>. Anything not provably numeric counts.
    /// </summary>
    private static void RecordEnumStringification(SemanticModel model, ExpressionSyntax expression, HashSet<INamedTypeSymbol> into)
    {
        var info = model.GetTypeInfo(expression);

        if (info.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var converted = info.ConvertedType;
            var staysNumeric = converted is null
                || SymbolEqualityComparer.Default.Equals(converted, enumType)
                || converted.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32
                    or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Byte or SpecialType.System_SByte
                    or SpecialType.System_UInt64 or SpecialType.System_Double
                || (converted is INamedTypeSymbol nullable
                    && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                    && SymbolEqualityComparer.Default.Equals(nullable.TypeArguments[0], enumType));

            var interpolated = expression.Parent is InterpolationSyntax;
            var receiver = expression.Parent is MemberAccessExpressionSyntax access && access.Expression == expression
                && model.GetSymbolInfo(access).Symbol is IMethodSymbol;

            if (!staysNumeric || interpolated || receiver) into.Add(enumType);
        }

        // Enum.GetValues<T>(), Enum.Parse<T>(…), and friends need the names at runtime.
        if (expression is InvocationExpressionSyntax invocation
            && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_Enum } method)
        {
            foreach (var argument in method.TypeArguments.OfType<INamedTypeSymbol>())
                if (argument.TypeKind == TypeKind.Enum) into.Add(argument);
        }

        if (expression is TypeOfExpressionSyntax typeOf && model.GetTypeInfo(typeOf.Type).Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } typed)
            into.Add(typed);
    }

    private static bool IsPubliclyVisible(INamedTypeSymbol type)
    {
        for (ISymbol? current = type; current is INamedTypeSymbol; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected
                or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }
        return true;
    }
}
