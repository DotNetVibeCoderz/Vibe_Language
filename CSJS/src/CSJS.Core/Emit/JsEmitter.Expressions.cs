using System.Text;
using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

public sealed partial class JsEmitter
{
    private void EmitExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal: EmitLiteral(literal); break;
            case IdentifierNameSyntax identifier: EmitIdentifier(identifier); break;
            case MemberAccessExpressionSyntax member: EmitMemberAccess(member); break;
            case MemberBindingExpressionSyntax binding: EmitMemberBinding(binding); break;
            case ElementBindingExpressionSyntax elementBinding: EmitElementBinding(elementBinding); break;
            case ConditionalAccessExpressionSyntax conditional: EmitConditionalAccess(conditional); break;
            case InvocationExpressionSyntax invocation: EmitInvocation(invocation); break;
            case ObjectCreationExpressionSyntax creation: EmitObjectCreation(creation, creation.Type); break;
            case ImplicitObjectCreationExpressionSyntax implicitCreation: EmitImplicitObjectCreation(implicitCreation); break;
            case ArrayCreationExpressionSyntax array: EmitArrayCreation(array); break;
            case ImplicitArrayCreationExpressionSyntax implicitArray: EmitInitializer(implicitArray.Initializer); break;
            case CollectionExpressionSyntax collection: EmitCollectionExpression(collection); break;
            case InitializerExpressionSyntax initializer: EmitInitializer(initializer); break;
            case AnonymousObjectCreationExpressionSyntax anonymous: EmitAnonymousObject(anonymous); break;
            case BinaryExpressionSyntax binary: EmitBinary(binary); break;
            case PrefixUnaryExpressionSyntax prefix: EmitPrefixUnary(prefix); break;
            case PostfixUnaryExpressionSyntax postfix: EmitPostfixUnary(postfix); break;
            case AssignmentExpressionSyntax assignment: EmitAssignment(assignment); break;
            case ConditionalExpressionSyntax ternary: EmitTernary(ternary); break;
            case ParenthesizedExpressionSyntax parenthesized: EmitParenthesized(parenthesized); break;
            case SimpleLambdaExpressionSyntax lambda: EmitLambda(lambda.Parameter is null ? Array.Empty<ParameterSyntax>() : new[] { lambda.Parameter }, lambda.Body, lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)); break;
            case ParenthesizedLambdaExpressionSyntax lambda: EmitLambda(lambda.ParameterList.Parameters, lambda.Body, lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)); break;
            case AnonymousMethodExpressionSyntax anonymousMethod: EmitLambda(anonymousMethod.ParameterList?.Parameters ?? default, anonymousMethod.Body, anonymousMethod.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)); break;
            case AwaitExpressionSyntax await: EmitAwait(await); break;
            case ThisExpressionSyntax: Write(_thisAlias ?? "this"); break;
            case BaseExpressionSyntax: Write("super"); break;
            case CastExpressionSyntax cast: EmitCast(cast); break;
            case IsPatternExpressionSyntax isPattern: EmitIsPattern(isPattern); break;
            case SwitchExpressionSyntax switchExpression: EmitSwitchExpression(switchExpression); break;
            case InterpolatedStringExpressionSyntax interpolated: EmitInterpolatedString(interpolated); break;
            case ElementAccessExpressionSyntax elementAccess: EmitElementAccess(elementAccess); break;
            case TupleExpressionSyntax tuple: EmitTuple(tuple); break;
            case DeclarationExpressionSyntax declaration: EmitDeclarationExpression(declaration); break;
            case ThrowExpressionSyntax throwExpression: EmitThrowExpression(throwExpression); break;
            case DefaultExpressionSyntax defaultExpression: Write(DefaultValueFor(_ctx.TypeFor(defaultExpression))); break;
            case TypeOfExpressionSyntax typeOf: EmitTypeOf(typeOf); break;
            case WithExpressionSyntax with: EmitWith(with); break;
            case RangeExpressionSyntax range: EmitRange(range); break;
            case PredefinedTypeSyntax predefined: Write(_ctx.Names.TypeName(_ctx.Model.GetTypeInfo(predefined).Type!)); break;
            case GenericNameSyntax generic: EmitGenericName(generic); break;
            case QualifiedNameSyntax qualified: EmitExpression(qualified.Right); break;
            case CheckedExpressionSyntax @checked: EmitExpression(@checked.Expression); break;
            case QueryExpressionSyntax query: EmitQuery(query); break;
            case ImplicitElementAccessSyntax implicitAccess: EmitImplicitElementAccess(implicitAccess); break;

            case BaseObjectCreationExpressionSyntax fallback:
                EmitObjectCreation(fallback, null);
                break;

            default:
                _ctx.Error(DiagnosticIds.UnsupportedSyntax,
                    $"Unsupported expression: {expression.Kind()}.", expression);
                Write("undefined");
                break;
        }
    }

    /// <summary>
    /// Emits an expression that is about to flow into <paramref name="target"/>.
    /// Only conversions JavaScript will not perform on its own are materialised.
    /// </summary>
    private void EmitExpressionAs(ExpressionSyntax expression, ITypeSymbol? target)
    {
        var source = _ctx.TypeFor(expression);

        // int -> long crosses from Number to BigInt and needs an explicit conversion.
        if (target is not null && TypeMap.IsBigInt(target) && source is not null && !TypeMap.IsBigInt(source)
            && TypeMap.IsIntegral(source))
        {
            Write("BigInt(");
            EmitExpression(expression);
            Write(")");
            return;
        }

        EmitExpression(expression);
    }

    // -------------------------------------------------------------------------------------------
    // Literals and names
    // -------------------------------------------------------------------------------------------

    private void EmitLiteral(LiteralExpressionSyntax literal)
    {
        Out.MapTo(literal.GetLocation());

        switch (literal.Kind())
        {
            case SyntaxKind.NullLiteralExpression:
                Write("null");
                break;

            case SyntaxKind.TrueLiteralExpression:
                Write("true");
                break;

            case SyntaxKind.FalseLiteralExpression:
                Write("false");
                break;

            case SyntaxKind.StringLiteralExpression:
                Write(QuoteString(literal.Token.ValueText));
                break;

            case SyntaxKind.CharacterLiteralExpression:
                // C# char is a UTF-16 unit; JavaScript models it as a one-character string.
                Write(QuoteString(literal.Token.ValueText));
                break;

            case SyntaxKind.NumericLiteralExpression:
                var type = _ctx.TypeFor(literal);
                var text = literal.Token.ValueText;
                if (TypeMap.IsBigInt(type)) Write($"{text}n");
                else Write(text);
                break;

            case SyntaxKind.DefaultLiteralExpression:
                Write(DefaultValueFor(_ctx.ConvertedTypeFor(literal)));
                break;

            default:
                Write(literal.Token.Text);
                break;
        }
    }

    private void EmitIdentifier(IdentifierNameSyntax identifier)
    {
        var symbol = _ctx.SymbolFor(identifier);

        switch (symbol)
        {
            case null:
                Write(NameResolver.Escape(identifier.Identifier.Text));
                return;

            case IParameterSymbol parameter when IsPrimaryConstructorCapture(parameter):
                // The parameter became a field, and the member reading it is a separate function
                // that cannot see a constructor argument.
                Write($"{_thisAlias ?? "this"}.{NameResolver.Escape(parameter.Name)}");
                return;

            case ILocalSymbol or IParameterSymbol or IRangeVariableSymbol:
                Write(NameResolver.Escape(identifier.Identifier.Text));
                return;

            case ITypeSymbol type:
                Write(type is INamedTypeSymbol named ? ReferenceToType(named) : _ctx.Names.TypeName(type));
                return;
        }

        // An unqualified member reference is implicitly `this.` or `TypeName.`.
        if (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol)
        {
            if (TryEmitGlobalMember(symbol)) return;

            if (MemberMap.Find(symbol) is { } template)
            {
                ApplyTemplate(template, emitThis: null, Array.Empty<Action>(), identifier);
                return;
            }

            var container = symbol.ContainingType;
            if (symbol.IsStatic && container is not null)
                Write($"{ReferenceToType(container)}.");
            else if (!symbol.IsStatic)
                Write($"{_thisAlias ?? "this"}.");

            Write(_ctx.Names.MemberName(symbol));
            return;
        }

        Write(NameResolver.Escape(identifier.Identifier.Text));
    }

    private void EmitGenericName(GenericNameSyntax generic)
    {
        // Generic arguments are erased; only the constructed name survives into JavaScript.
        var symbol = _ctx.SymbolFor(generic);
        if (symbol is INamedTypeSymbol named)
        {
            Write(ReferenceToType(named));
            return;
        }
        Write(NameResolver.Escape(generic.Identifier.Text));
    }

    // -------------------------------------------------------------------------------------------
    // Member access and invocation
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Emits a member that is itself a JavaScript global, dropping the containing type.
    /// <para>
    /// A C# static class is only a container — <c>setTimeout</c> lives on <c>globalThis</c>, not on
    /// anything called <c>Timers</c> — so a member-level <c>[JsGlobal]</c> replaces the whole access.
    /// </para>
    /// </summary>
    private bool TryEmitGlobalMember(ISymbol? symbol)
    {
        if (symbol is null || !symbol.IsStatic) return false;

        var attribute = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "JsGlobalAttribute");
        if (attribute is null) return false;

        var name = attribute.ConstructorArguments.Length > 0
                   && attribute.ConstructorArguments[0].Value is string s && s.Length > 0
            ? s
            : symbol.Name;

        Write(name);
        return true;
    }

    private void EmitMemberAccess(MemberAccessExpressionSyntax member)
    {
        var symbol = _ctx.SymbolFor(member);

        if (TryEmitGlobalMember(symbol)) return;

        // A tuple is emitted as an array, so a named element is really an index.
        if (symbol is IFieldSymbol { ContainingType.IsTupleType: true } element
            && TupleElementIndex(element) is { } tupleIndex)
        {
            EmitExpression(member.Expression);
            Write($"[{tupleIndex}]");
            return;
        }

        if (symbol is not null && GetJsTemplate(symbol) is { } inline)
        {
            ApplyTemplate(new JsTemplate(inline), () => EmitExpression(member.Expression),
                Array.Empty<Action>(), member);
            return;
        }

        // A mapped BCL member replaces the whole access, receiver included.
        if (symbol is not null && symbol is not IMethodSymbol && MemberMap.Find(symbol) is { } template)
        {
            ApplyTemplate(template, () => EmitExpression(member.Expression), Array.Empty<Action>(), member);
            return;
        }

        // An enum member is a property on the frozen enum object.
        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumMember)
        {
            Write($"{ReferenceToType(enumMember.ContainingType)}.{enumMember.Name}");
            return;
        }

        if (symbol is ITypeSymbol typeSymbol)
        {
            Write(typeSymbol is INamedTypeSymbol named ? ReferenceToType(named) : _ctx.Names.TypeName(typeSymbol));
            return;
        }

        // A static member reaches through the type name; namespaces do not exist in JavaScript,
        // so a namespace-qualified path collapses to just the type.
        if (symbol is { IsStatic: true } staticMember && staticMember.ContainingType is { } owner
            && member.Expression is not ThisExpressionSyntax)
        {
            Write($"{ReferenceToType(owner)}.{_ctx.Names.MemberName(staticMember)}");
            return;
        }

        if (_ctx.SymbolFor(member.Expression) is INamespaceSymbol)
        {
            EmitExpression(member.Name);
            return;
        }

        if (symbol is not null) ReportIfUnmapped(symbol, member);

        EmitExpression(member.Expression);
        Write(".");
        Write(symbol is null ? NameResolver.Escape(member.Name.Identifier.Text) : _ctx.Names.MemberName(symbol));
    }

    /// <summary>
    /// Position of a tuple element. A named element and its backing <c>ItemN</c> field are
    /// distinct symbols, so the position is taken from whichever one the model handed back.
    /// </summary>
    private static int? TupleElementIndex(IFieldSymbol element)
    {
        var backing = element.CorrespondingTupleField ?? element;

        if (backing.Name.StartsWith("Item", StringComparison.Ordinal)
            && int.TryParse(backing.Name.AsSpan(4), out var position))
        {
            return position - 1;
        }

        var elements = element.ContainingType.TupleElements;
        for (var i = 0; i < elements.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(elements[i], element)) return i;
        }

        return null;
    }

    private void EmitMemberBinding(MemberBindingExpressionSyntax binding)
    {
        var symbol = _ctx.SymbolFor(binding);
        Write("?.");
        Write(symbol is null ? NameResolver.Escape(binding.Name.Identifier.Text) : _ctx.Names.MemberName(symbol));
    }

    private void EmitElementBinding(ElementBindingExpressionSyntax binding)
    {
        Write("?.[");
        WriteJoined(binding.ArgumentList.Arguments, a => EmitExpression(a.Expression));
        Write("]");
    }

    private void EmitConditionalAccess(ConditionalAccessExpressionSyntax conditional)
    {
        EmitExpression(conditional.Expression);
        EmitExpression(conditional.WhenNotNull);
    }

    private void EmitInvocation(InvocationExpressionSyntax invocation)
    {
        // `nameof(x)` is a compile-time constant with no symbol of its own.
        if (invocation.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" }
            && _ctx.Model.GetConstantValue(invocation) is { HasValue: true, Value: string constantName })
        {
            Write(QuoteString(constantName));
            return;
        }

        var symbol = _ctx.SymbolFor(invocation) as IMethodSymbol;

        if (symbol is null)
        {
            // Unbound call: emit structurally and let the C# diagnostics explain why.
            EmitExpression(invocation.Expression);
            Write("(");
            EmitArgumentList(invocation.ArgumentList.Arguments);
            Write(")");
            return;
        }

        if (TryEmitSpecialInvocation(invocation, symbol)) return;
        if (TryEmitDelegateInvoke(invocation, symbol)) return;

        var receiver = GetReceiver(invocation.Expression);

        // A [JsTemplate] member rewrites its own call site, which is the only way a binding
        // type's members can work: no declaration is emitted for them to live on.
        if (GetJsTemplate(symbol) is { } inline)
        {
            ApplyTemplate(new JsTemplate(inline),
                receiver is null ? null : () => EmitExpression(receiver),
                BuildArgumentEmitters(invocation.ArgumentList.Arguments, symbol),
                invocation);
            return;
        }

        if (MemberMap.Find(symbol) is { } template)
        {
            var arguments = BuildArgumentEmitters(invocation.ArgumentList.Arguments, symbol);
            ApplyTemplate(template,
                receiver is null ? null : () => EmitExpression(receiver),
                arguments,
                invocation,
                receiverIsArray: receiver is not null && ProducesJsArray(receiver));
            return;
        }

        if (GetJsImport(symbol) is { } import)
        {
            var alias = _ctx.Names.MemberName(symbol);
            _ctx.UseImport(import.Module, import.IsDefault ? null : import.Export ?? symbol.Name, alias);
            Write(alias);
            Write("(");
            EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
            Write(")");
            return;
        }

        // An extension method is a static call with the receiver moved into first position.
        if (symbol.IsExtensionMethod && symbol.ReducedFrom is not null && receiver is not null)
        {
            Write($"{ReferenceToType(symbol.ContainingType)}.{_ctx.Names.MemberName(symbol)}(");
            EmitExpression(receiver);
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                Write(", ");
                EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
            }
            Write(")");
            return;
        }

        ReportIfUnmapped(symbol, invocation);

        EmitInvocationTarget(invocation.Expression, symbol);
        Write("(");
        EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
        Write(")");
    }

    /// <summary>
    /// Reports a framework member that reached emission without a mapping.
    /// <para>
    /// Such a member has no source to walk and no template to apply, so whatever gets emitted
    /// will fail at runtime. Saying so at build time is the difference between a fixable error
    /// and a mystery in the browser console.
    /// </para>
    /// </summary>
    private void ReportIfUnmapped(ISymbol symbol, SyntaxNode site)
    {
        if (!MemberMap.RequiresMapping(symbol)) return;

        var owner = symbol.ContainingType is { } type ? TypeMap.Unbound(type) : "";
        _ctx.Error(DiagnosticIds.UnsupportedMember,
            $"'{owner}.{symbol.Name}' has no JavaScript mapping and cannot be transpiled. " +
            "Use a supported member, or declare a binding with [JsGlobal] or [JsBody].",
            site);
    }

    private void EmitInvocationTarget(ExpressionSyntax target, IMethodSymbol symbol)
    {
        switch (target)
        {
            case MemberAccessExpressionSyntax member:
                if (TryEmitGlobalMember(symbol)) return;
                if (symbol.IsStatic && symbol.ContainingType is { } owner
                    && member.Expression is not ThisExpressionSyntax)
                {
                    Write($"{ReferenceToType(owner)}.{_ctx.Names.MemberName(symbol)}");
                    return;
                }
                EmitExpression(member.Expression);
                Write(".");
                Write(_ctx.Names.MemberName(symbol));
                return;

            case MemberBindingExpressionSyntax:
                Write("?.");
                Write(_ctx.Names.MemberName(symbol));
                return;

            case IdentifierNameSyntax or GenericNameSyntax:
                if (symbol.MethodKind == MethodKind.LocalFunction)
                {
                    Write(NameResolver.Escape(symbol.Name));
                    return;
                }
                if (symbol.IsStatic && symbol.ContainingType is { } staticOwner)
                {
                    Write($"{ReferenceToType(staticOwner)}.{_ctx.Names.MemberName(symbol)}");
                    return;
                }
                Write($"{_thisAlias ?? "this"}.{_ctx.Names.MemberName(symbol)}");
                return;

            default:
                EmitExpression(target);
                return;
        }
    }

    /// <summary>
    /// Rewrites <c>Invoke</c> on a delegate or event.
    /// <para>
    /// A plain delegate is already a JavaScript function, so <c>d.Invoke(x)</c> is just
    /// <c>d(x)</c>. An event is a handler array, so invoking it has to run every subscriber —
    /// which is why <c>Clicked?.Invoke(e)</c> becomes a <c>forEach</c> rather than a call.
    /// </para>
    /// </summary>
    private bool TryEmitDelegateInvoke(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.MethodKind != MethodKind.DelegateInvoke) return false;

        // `x?.Invoke(...)` parses as a conditional access whose receiver sits on the outer node.
        var isConditional = invocation.Expression is MemberBindingExpressionSyntax;

        // Two shapes reach here and they need opposite treatment. `d.Invoke(x)` names the
        // delegate's own Invoke method, so the delegate is the receiver and `.Invoke` is dropped.
        // `job.Task()` invokes a delegate held in a member, so the whole member access *is* the
        // delegate — stripping the last segment there emitted `job(x)`, which called the object.
        var invokedSymbol = _ctx.SymbolFor(invocation.Expression);
        var isExplicitInvoke = invokedSymbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke };

        ExpressionSyntax? receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member when isExplicitInvoke => member.Expression,
            MemberBindingExpressionSyntax => (invocation.Parent as ConditionalAccessExpressionSyntax)?.Expression
                                             ?? invocation.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
                                                 .FirstOrDefault()?.Expression,
            _ => invocation.Expression,
        };

        var targetSymbol = receiver is null ? null : _ctx.SymbolFor(receiver);

        if (targetSymbol is IEventSymbol)
        {
            // The conditional access already emitted the receiver, so only the tail goes here.
            if (isConditional) Write("?.");
            else
            {
                EmitExpression(receiver!);
                Write(".");
            }

            Write("forEach($h => $h(");
            EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
            Write("))");
            return true;
        }

        if (isConditional)
        {
            Write("?.(");
            EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
            Write(")");
            return true;
        }

        if (receiver is null) return false;

        EmitExpression(receiver);
        Write("(");
        EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
        Write(")");
        return true;
    }

    /// <summary>
    /// Handles the calls whose C# shape has no direct JavaScript equivalent, chiefly the
    /// <c>out</c>-parameter Try* pattern. These are common enough that refusing them would make
    /// ordinary dictionary and parsing code untranspilable.
    /// </summary>
    private bool TryEmitSpecialInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        var owner = symbol.ContainingType is null ? "" : TypeMap.Unbound(symbol.ContainingType);
        var arguments = invocation.ArgumentList.Arguments;

        // dictionary.TryGetValue(key, out value)
        if (owner is "System.Collections.Generic.Dictionary" or "System.Collections.Generic.IDictionary"
            && symbol.Name == "TryGetValue" && arguments.Count == 2)
        {
            var receiver = GetReceiver(invocation.Expression);
            Write("((");
            EmitOutTarget(arguments[1]);
            Write(" = ");
            if (receiver is not null) EmitExpression(receiver);
            Write(".get(");
            EmitExpression(arguments[0].Expression);
            Write(")) !== undefined)");
            return true;
        }

        // int.TryParse(text, out value) and double.TryParse(text, out value)
        if (symbol.Name == "TryParse" && arguments.Count == 2)
        {
            var parser = owner is "System.Double" or "System.Single" or "System.Decimal" ? "parseFloat" : "parseInt";
            var radix = parser == "parseInt" ? ", 10" : "";
            Write("(!Number.isNaN(");
            EmitOutTarget(arguments[1]);
            Write($" = {parser}(");
            EmitExpression(arguments[0].Expression);
            Write($"{radix})))");
            return true;
        }

        // A user-declared method with by-reference parameters cannot be modelled faithfully.
        if (symbol.Parameters.Any(p => p.RefKind is RefKind.Out or RefKind.Ref)
            && symbol.ContainingType?.ContainingAssembly?.Name != "CSJS.Interop")
        {
            _ctx.Error(DiagnosticIds.UnsupportedMember,
                $"'{symbol.Name}' uses ref/out parameters, which have no JavaScript equivalent. " +
                "Return a tuple or an object instead.", invocation);
            Write("undefined");
            return true;
        }

        return false;
    }

    private void EmitOutTarget(ArgumentSyntax argument)
    {
        // The binding was already hoisted by DeclareHoistedVariables; only the name is needed.
        if (argument.Expression is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single })
        {
            Write(NameResolver.Escape(single.Identifier.Text));
            return;
        }
        EmitExpression(argument.Expression);
    }

    /// <summary>
    /// True when an expression already evaluates to a real JavaScript array, so a LINQ template
    /// can operate on it directly instead of spreading it first.
    /// </summary>
    private bool ProducesJsArray(ExpressionSyntax expression)
    {
        var type = _ctx.TypeFor(expression);
        if (type is null) return false;
        if (type is IArrayTypeSymbol) return true;
        if (TypeMap.ShapeOf(type) != JsShape.Array) return false;

        // IEnumerable<T> is an array only when the value behind it is one. A method that yields
        // returns a generator, and interface-typed values could be either — so require a
        // concrete List<T> or an expression the emitter itself produced as an array.
        if (type is INamedTypeSymbol named)
        {
            var unbound = TypeMap.Unbound(named);
            if (unbound is "System.Collections.Generic.List"
                or "System.Collections.Generic.IReadOnlyList"
                or "System.Collections.Generic.IList")
            {
                return true;
            }

            // A LINQ call always hands back an array, so chains stay spread-free.
            if (expression is InvocationExpressionSyntax invocation
                && _ctx.SymbolFor(invocation) is IMethodSymbol { ContainingType: { } owner }
                && TypeMap.Unbound(owner) == "System.Linq.Enumerable")
            {
                return true;
            }
        }

        return false;
    }

    private ExpressionSyntax? GetReceiver(ExpressionSyntax invocationTarget) => invocationTarget switch
    {
        MemberAccessExpressionSyntax member => member.Expression,
        MemberBindingExpressionSyntax => null,
        _ => null,
    };

    private void EmitArgumentList(SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol? target = null)
    {
        var ordered = OrderArguments(arguments, target);
        var first = true;

        foreach (var (argument, parameter) in ordered)
        {
            if (!first) Write(", ");
            first = false;

            if (parameter?.IsParams == true && argument.Expression is not null
                && _ctx.TypeFor(argument.Expression) is IArrayTypeSymbol)
            {
                Write("...");
            }

            if (argument.Expression is null) Write(DefaultValueFor(parameter?.Type));
            else EmitExpressionAs(argument.Expression, parameter?.Type);
        }
    }

    /// <summary>
    /// Reorders named arguments into declaration order and fills omitted optional parameters.
    /// JavaScript is positional, so both have to be resolved before emitting.
    /// </summary>
    private List<(ArgumentSyntax Argument, IParameterSymbol? Parameter)> OrderArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol? target)
    {
        if (target is null || arguments.All(a => a.NameColon is null))
        {
            return arguments
                .Select((a, i) => (a, ParameterAt(target, i)))
                .ToList();
        }

        var parameters = target.Parameters;
        var slots = new ArgumentSyntax?[parameters.Length];

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument.NameColon is null)
            {
                if (i < slots.Length) slots[i] = argument;
                continue;
            }

            var name = argument.NameColon.Name.Identifier.Text;
            var index = parameters.IndexOf(parameters.FirstOrDefault(p => p.Name == name)!);
            if (index >= 0) slots[index] = argument;
        }

        // Trailing omitted optionals can be dropped entirely; JavaScript defaults handle them.
        var lastProvided = Array.FindLastIndex(slots, s => s is not null);
        var result = new List<(ArgumentSyntax, IParameterSymbol?)>();

        for (var i = 0; i <= lastProvided; i++)
        {
            var parameter = parameters[i];
            if (slots[i] is { } provided)
            {
                result.Add((provided, parameter));
            }
            else if (parameter.HasExplicitDefaultValue)
            {
                // A gap before a later named argument must be filled with the declared default.
                result.Add((SyntaxFactory.Argument(DefaultLiteralFor(parameter)), parameter));
            }
        }

        return result;
    }

    private static IParameterSymbol? ParameterAt(IMethodSymbol? target, int index)
    {
        if (target is null) return null;
        if (index < target.Parameters.Length) return target.Parameters[index];
        return target.Parameters.LastOrDefault(p => p.IsParams);
    }

    private static ExpressionSyntax DefaultLiteralFor(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;
        return value switch
        {
            null => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
            bool b => SyntaxFactory.LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
            string s => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(s)),
            int i => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(i)),
            double d => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(d)),
            _ => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
        };
    }

    private List<Action> BuildArgumentEmitters(SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol? target)
        => OrderArguments(arguments, target)
            .Select(pair => (Action)(() =>
            {
                if (pair.Argument.Expression is null) Write(DefaultValueFor(pair.Parameter?.Type));
                else EmitExpressionAs(pair.Argument.Expression, pair.Parameter?.Type);
            }))
            .ToList();

    /// <summary>
    /// Renders a <see cref="JsTemplate"/>, substituting the receiver and arguments.
    /// A template may reference a placeholder more than once (String.Substring does), so the
    /// emitters are callable rather than pre-rendered strings.
    /// </summary>
    private void ApplyTemplate(JsTemplate template, Action? emitThis, IReadOnlyList<Action> emitArgs, SyntaxNode site)
        => ApplyTemplate(template, emitThis, emitArgs, site, receiverIsArray: true);

    private void ApplyTemplate(
        JsTemplate template,
        Action? emitThis,
        IReadOnlyList<Action> emitArgs,
        SyntaxNode site,
        bool receiverIsArray)
    {
        if (template.Helper is not null)
            _ctx.UseHelper(template.Helper);

        var pattern = template.Pattern;
        var i = 0;

        while (i < pattern.Length)
        {
            if (pattern[i] != '{')
            {
                Write(pattern[i].ToString());
                i++;
                continue;
            }

            var close = pattern.IndexOf('}', i);
            if (close < 0)
            {
                Write(pattern[i].ToString());
                i++;
                continue;
            }

            var token = pattern.Substring(i + 1, close - i - 1);
            i = close + 1;

            if (token == "this")
            {
                if (emitThis is not null) emitThis();
                else Write(_thisAlias ?? "this");
                continue;
            }

            if (token == "seq")
            {
                // A Map, Set, or generator still needs materialising; an Array does not.
                if (!receiverIsArray) Write("[...");
                if (emitThis is not null) emitThis();
                else Write(_thisAlias ?? "this");
                if (!receiverIsArray) Write("]");
                continue;
            }

            if (token == "...")
            {
                WriteJoined(emitArgs, emit => emit());
                continue;
            }

            if (token.StartsWith("...", StringComparison.Ordinal)
                && int.TryParse(token.AsSpan(3), out var from))
            {
                WriteJoined(emitArgs.Skip(from), emit => emit());
                continue;
            }

            if (int.TryParse(token, out var index))
            {
                if (index < emitArgs.Count) emitArgs[index]();
                else Write("undefined");
                continue;
            }

            Write("{" + token + "}");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Object and collection construction
    // -------------------------------------------------------------------------------------------

    private void EmitObjectCreation(BaseObjectCreationExpressionSyntax creation, TypeSyntax? typeSyntax)
    {
        var symbol = _ctx.SymbolFor(creation) as IMethodSymbol;
        var type = symbol?.ContainingType ?? (typeSyntax is null ? null : _ctx.Model.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol);

        // A type marked [JsObjectLiteral] has no constructor at all — it is just a shape.
        if (type is not null && HasAttribute(type, "JsObjectLiteralAttribute"))
        {
            EmitObjectLiteralConstruction(creation);
            return;
        }

        var initializer = creation.Initializer;
        var shape = TypeMap.ShapeOf(type);

        // A collection initializer on a dictionary or set has to go inside the constructor call,
        // not after it, so these are built directly rather than chained onto an empty collection.
        if (initializer is not null
            && initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
            && initializer.Expressions.Count > 0)
        {
            switch (shape)
            {
                case JsShape.Map:
                    Write("new Map([");
                    WriteJoined(initializer.Expressions, EmitMapEntry);
                    Write("])");
                    return;

                case JsShape.Set:
                    Write("new Set([");
                    WriteJoined(initializer.Expressions, EmitExpression);
                    Write("])");
                    return;

                case JsShape.Array:
                    Write("[");
                    WriteJoined(initializer.Expressions, EmitExpression);
                    Write("]");
                    return;
            }
        }

        var hasObjectInitializer = initializer is not null
                                   && initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                                   && initializer.Expressions.Count > 0;

        // `new T { Member = { ... } }` adds to the existing member rather than assigning it, so
        // an object literal cannot express it. Those constructions get a small arrow instead.
        if (hasObjectInitializer && HasNestedInitializer(initializer!))
        {
            EmitConstructionWithNestedInitializer(creation, symbol, type, typeSyntax, initializer!);
            return;
        }

        // Object.assign has to wrap the construction, so the opening goes on before it.
        if (hasObjectInitializer) Write("Object.assign(");

        EmitConstruction(creation, symbol, type, typeSyntax);

        if (hasObjectInitializer)
        {
            Write(", ");
            EmitObjectInitializerLiteral(initializer!);
            Write(")");
            return;
        }

        // Any remaining collection initializer targets a user-declared collection type.
        if (initializer is not null && initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
            && initializer.Expressions.Count > 0)
        {
            Write(".concat([");
            WriteJoined(initializer.Expressions, EmitExpression);
            Write("])");
        }
    }

    private void EmitConstruction(
        BaseObjectCreationExpressionSyntax creation,
        IMethodSymbol? symbol,
        INamedTypeSymbol? type,
        TypeSyntax? typeSyntax)
    {
        if (type is not null && IsFrameworkException(type))
        {
            EmitFrameworkExceptionConstruction(creation, type);
            return;
        }

        if (symbol is not null && MemberMap.FindConstructor(symbol) is { } template)
        {
            var arguments = BuildArgumentEmitters(creation.ArgumentList?.Arguments ?? default, symbol);
            ApplyTemplate(template, null, arguments, creation);
            return;
        }

        // A binding type is an existing JavaScript class. JavaScript dispatches its constructor
        // arguments at runtime, so every overload is just `new T(...)` — the static-factory
        // rewrite would call a method the library does not have.
        var isBinding = type is not null
                        && (GetJsImport(type) is not null || NameResolver.JsGlobalName(type) is not null);

        // A secondary constructor became a static factory, so `new` must not appear.
        if (!isBinding && symbol is not null && _ctx.Names.MemberName(symbol) is var ctorName && ctorName != "constructor")
        {
            Write($"{ReferenceToType(symbol.ContainingType)}.{ctorName}(");
            EmitArgumentList(creation.ArgumentList?.Arguments ?? default, symbol);
            Write(")");
            return;
        }

        Write("new ");
        Write(type is not null ? ReferenceToType(type) : (typeSyntax?.ToString() ?? "Object"));
        Write("(");
        EmitArgumentList(creation.ArgumentList?.Arguments ?? default, symbol);
        Write(")");
    }

    private static bool HasNestedInitializer(InitializerExpressionSyntax initializer)
        => initializer.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(a => a.Right is InitializerExpressionSyntax);

    /// <summary>
    /// Emits a construction whose initializer writes into an existing member.
    /// <para>
    /// C# reads <c>new T { Items = { [0] = x } }</c> as "construct, then set <c>Items[0]</c>" —
    /// it never assigns <c>Items</c>. That is a sequence of statements, so it needs a scope to
    /// live in while staying an expression.
    /// </para>
    /// </summary>
    private void EmitConstructionWithNestedInitializer(
        BaseObjectCreationExpressionSyntax creation,
        IMethodSymbol? symbol,
        INamedTypeSymbol? type,
        TypeSyntax? typeSyntax,
        InitializerExpressionSyntax initializer)
    {
        var target = _ctx.NextTemporary("obj");

        Write($"(() => {{ const {target} = ");
        EmitConstruction(creation, symbol, type, typeSyntax);
        Write("; ");

        foreach (var expression in initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assignment) continue;

            var member = _ctx.SymbolFor(assignment.Left);
            var name = member is null ? assignment.Left.ToString() : _ctx.Names.MemberName(member);

            if (assignment.Right is InitializerExpressionSyntax nested)
            {
                foreach (var entry in nested.Expressions)
                {
                    if (entry is AssignmentExpressionSyntax indexed)
                    {
                        Write($"{target}.{name}");
                        EmitExpression(indexed.Left);
                        Write(" = ");
                        EmitExpression(indexed.Right);
                        Write("; ");
                    }
                    else
                    {
                        // A collection initializer on the member appends rather than assigns.
                        Write($"{target}.{name}.push(");
                        EmitExpression(entry);
                        Write("); ");
                    }
                }
                continue;
            }

            Write($"{target}.{name} = ");
            EmitExpression(assignment.Right);
            Write("; ");
        }

        Write($"return {target}; }})()");
    }

    private void EmitImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax creation)
        => EmitObjectCreation(creation, null);

    /// <summary>
    /// True for an exception type that comes from the framework rather than this compilation.
    /// User-declared exceptions are emitted as real classes extending <c>Error</c>, so
    /// <c>instanceof</c> already discriminates them.
    /// </summary>
    internal static bool IsFrameworkException(INamedTypeSymbol type)
        => InheritsFromException(type) && type.DeclaringSyntaxReferences.Length == 0;

    internal static bool InheritsFromException(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "System.Exception") return true;
        return false;
    }

    /// <summary>
    /// Constructs a framework exception as an Error carrying its C# type hierarchy.
    /// Without the hierarchy every exception would be indistinguishable from every other, and
    /// a typed <c>catch</c> would swallow whatever reached it first.
    /// </summary>
    private void EmitFrameworkExceptionConstruction(BaseObjectCreationExpressionSyntax creation, INamedTypeSymbol type)
    {
        _ctx.UseHelper("csjsError");

        var hierarchy = new List<string>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            hierarchy.Add(current.Name);
            if (current.ToDisplayString() == "System.Exception") break;
        }

        Write($"csjsError({QuoteString(type.Name)}, [");
        Write(string.Join(", ", hierarchy.Select(QuoteString)));
        Write("], ");

        var message = creation.ArgumentList?.Arguments.FirstOrDefault();
        if (message is not null) EmitExpression(message.Expression);
        else Write("null");

        Write(")");
    }

    private void EmitObjectLiteralConstruction(BaseObjectCreationExpressionSyntax creation)
    {
        if (creation.Initializer is null)
        {
            Write("{}");
            return;
        }
        EmitObjectInitializerLiteral(creation.Initializer);
    }

    private void EmitObjectInitializerLiteral(InitializerExpressionSyntax initializer)
    {
        Write("{ ");
        var first = true;
        foreach (var expression in initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assignment) continue;
            if (!first) Write(", ");
            first = false;

            var member = _ctx.SymbolFor(assignment.Left);
            var name = member is null ? assignment.Left.ToString() : _ctx.Names.MemberName(member);
            Write(NameResolver.IsValidPropertyKey(name) ? name : QuoteString(name));
            Write(": ");
            EmitExpression(assignment.Right);
        }
        Write(" }");
    }

    private void EmitMapEntry(ExpressionSyntax entry)
    {
        switch (entry)
        {
            case InitializerExpressionSyntax pair when pair.Expressions.Count == 2:
                Write("[");
                EmitExpression(pair.Expressions[0]);
                Write(", ");
                EmitExpression(pair.Expressions[1]);
                Write("]");
                return;

            case AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax key } assignment:
                Write("[");
                WriteJoined(key.ArgumentList.Arguments, a => EmitExpression(a.Expression));
                Write(", ");
                EmitExpression(assignment.Right);
                Write("]");
                return;

            default:
                EmitExpression(entry);
                return;
        }
    }

    private void EmitArrayCreation(ArrayCreationExpressionSyntax array)
    {
        if (array.Initializer is not null)
        {
            EmitInitializer(array.Initializer);
            return;
        }

        // `new int[n]` allocates and fills, because JavaScript's Array(n) leaves holes that
        // behave differently from C#'s zero-initialised array.
        var rank = array.Type.RankSpecifiers.FirstOrDefault();
        var size = rank?.Sizes.FirstOrDefault();
        var elementType = (_ctx.Model.GetTypeInfo(array.Type).Type as IArrayTypeSymbol)?.ElementType;

        if (size is null or OmittedArraySizeExpressionSyntax)
        {
            Write("[]");
            return;
        }

        Write("new Array(");
        EmitExpression(size);
        Write($").fill({DefaultValueFor(elementType)})");
    }

    private void EmitInitializer(InitializerExpressionSyntax initializer)
    {
        Write("[");
        WriteJoined(initializer.Expressions, EmitExpression);
        Write("]");
    }

    private void EmitCollectionExpression(CollectionExpressionSyntax collection)
    {
        Write("[");
        WriteJoined(collection.Elements, element =>
        {
            switch (element)
            {
                case ExpressionElementSyntax expressionElement:
                    EmitExpression(expressionElement.Expression);
                    break;
                case SpreadElementSyntax spread:
                    Write("...");
                    EmitExpression(spread.Expression);
                    break;
            }
        });
        Write("]");
    }

    private void EmitAnonymousObject(AnonymousObjectCreationExpressionSyntax anonymous)
    {
        Write("{ ");
        WriteJoined(anonymous.Initializers, initializer =>
        {
            var name = initializer.NameEquals?.Name.Identifier.Text
                       ?? (initializer.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text
                       ?? (initializer.Expression as IdentifierNameSyntax)?.Identifier.Text
                       ?? "value";

            Write(NameResolver.IsValidPropertyKey(name) ? name : QuoteString(name));
            Write(": ");
            EmitExpression(initializer.Expression);
        });
        Write(" }");
    }

    /// <summary>
    /// Emits <c>[i]</c> from an initializer's implicit element access, as in <c>{ [0] = x }</c>.
    /// The receiver is supplied by the initializer, which is why only the subscript appears here.
    /// </summary>
    private void EmitImplicitElementAccess(ImplicitElementAccessSyntax access)
    {
        Write("[");
        WriteJoined(access.ArgumentList.Arguments, a => EmitExpression(a.Expression));
        Write("]");
    }

    private void EmitTuple(TupleExpressionSyntax tuple)
    {
        // C# tuples become arrays, which destructure identically on the JavaScript side.
        Write("[");
        WriteJoined(tuple.Arguments, a => EmitExpression(a.Expression));
        Write("]");
    }

    // -------------------------------------------------------------------------------------------
    // Operators
    // -------------------------------------------------------------------------------------------

    private void EmitBinary(BinaryExpressionSyntax binary)
    {
        var symbol = _ctx.SymbolFor(binary) as IMethodSymbol;

        // A user-defined operator became a static method during declaration emission. Records
        // synthesise ==/!= that the compiler never asked the developer to write, and those must
        // fall through to the value-equality path instead.
        if (symbol is { MethodKind: MethodKind.UserDefinedOperator, IsImplicitlyDeclared: false }
            && symbol.DeclaringSyntaxReferences.Length > 0
            && symbol.ContainingType is { } owner
            && owner.ContainingAssembly?.Name is not "System.Runtime" and not "System.Private.CoreLib")
        {
            Write($"{ReferenceToType(owner)}.{OperatorMethodName(binary.OperatorToken.Kind())}(");
            EmitExpression(binary.Left);
            Write(", ");
            EmitExpression(binary.Right);
            Write(")");
            return;
        }

        switch (binary.Kind())
        {
            case SyntaxKind.DivideExpression when IsIntegerDivision(binary):
                // C# integer division truncates toward zero; JavaScript `/` produces a real number.
                Write("Math.trunc(");
                EmitExpression(binary.Left);
                Write(" / ");
                EmitExpression(binary.Right);
                Write(")");
                return;

            case SyntaxKind.AsExpression:
                EmitAsExpression(binary);
                return;

            case SyntaxKind.IsExpression:
                EmitIsTypeExpression(binary);
                return;

            case SyntaxKind.CoalesceExpression:
                EmitBinaryOperands(binary, "??");
                return;

            case SyntaxKind.EqualsExpression:
                EmitEquality(binary, negated: false);
                return;

            case SyntaxKind.NotEqualsExpression:
                EmitEquality(binary, negated: true);
                return;
        }

        var op = binary.Kind() switch
        {
            SyntaxKind.AddExpression => "+",
            SyntaxKind.SubtractExpression => "-",
            SyntaxKind.MultiplyExpression => "*",
            SyntaxKind.DivideExpression => "/",
            SyntaxKind.ModuloExpression => "%",
            SyntaxKind.LessThanExpression => "<",
            SyntaxKind.LessThanOrEqualExpression => "<=",
            SyntaxKind.GreaterThanExpression => ">",
            SyntaxKind.GreaterThanOrEqualExpression => ">=",
            SyntaxKind.LogicalAndExpression => "&&",
            SyntaxKind.LogicalOrExpression => "||",
            SyntaxKind.BitwiseAndExpression => "&",
            SyntaxKind.BitwiseOrExpression => "|",
            SyntaxKind.ExclusiveOrExpression => "^",
            SyntaxKind.LeftShiftExpression => "<<",
            SyntaxKind.RightShiftExpression => ">>",
            SyntaxKind.UnsignedRightShiftExpression => ">>>",
            _ => null,
        };

        if (op is null)
        {
            _ctx.Error(DiagnosticIds.UnsupportedSyntax, $"Unsupported operator: {binary.OperatorToken.Text}.", binary);
            Write("undefined");
            return;
        }

        EmitBinaryOperands(binary, op);
    }

    private void EmitBinaryOperands(BinaryExpressionSyntax binary, string op)
    {
        EmitExpression(binary.Left);
        Write($" {op} ");
        EmitExpression(binary.Right);
    }

    private void EmitEquality(BinaryExpressionSyntax binary, bool negated)
    {
        var leftType = _ctx.TypeFor(binary.Left);
        var rightType = _ctx.TypeFor(binary.Right);

        // Records and structs compare by value in C#. Reference types and primitives do not,
        // and for those `===` is exactly right.
        var needsValueEquality = IsValueEqualityType(leftType) || IsValueEqualityType(rightType);

        if (needsValueEquality)
        {
            _ctx.UseHelper("csjsEquals");
            if (negated) Write("!");
            Write("csjsEquals(");
            EmitExpression(binary.Left);
            Write(", ");
            EmitExpression(binary.Right);
            Write(")");
            return;
        }

        EmitBinaryOperands(binary, negated ? "!==" : "===");
    }

    private static bool IsValueEqualityType(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type.IsRecord) return true;
        // A DateTime compares by value in C# but by reference as a JavaScript Date.
        return type is INamedTypeSymbol named && TypeMap.Unbound(named)
            is "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan";
    }

    private bool IsIntegerDivision(BinaryExpressionSyntax binary)
        => TypeMap.IsIntegral(_ctx.TypeFor(binary.Left)) && TypeMap.IsIntegral(_ctx.TypeFor(binary.Right));

    private void EmitAsExpression(BinaryExpressionSyntax binary)
    {
        var target = _ctx.Model.GetTypeInfo(binary.Right).Type;

        if (target is not null && TypeMap.ShapeOf(target) is JsShape.Object && target.TypeKind == TypeKind.Class)
        {
            _ctx.UseHelper("csjsAs");
            Write("csjsAs(");
            EmitExpression(binary.Left);
            Write($", {ReferenceToType((INamedTypeSymbol)target)})");
            return;
        }

        // For everything else `as` is a no-op that yields null on mismatch, which the
        // surrounding null check already handles.
        EmitExpression(binary.Left);
    }

    private void EmitIsTypeExpression(BinaryExpressionSyntax binary)
    {
        var target = _ctx.Model.GetTypeInfo(binary.Right).Type;
        EmitTypeTest(() => EmitExpression(binary.Left), target, binary);
    }

    private void EmitTypeTest(Action emitValue, ITypeSymbol? target, SyntaxNode site)
    {
        if (target is null)
        {
            emitValue();
            Write(" !== null");
            return;
        }

        switch (TypeMap.ShapeOf(target))
        {
            case JsShape.String:
                Write("typeof ");
                emitValue();
                Write(" === 'string'");
                return;

            case JsShape.Number:
                Write("typeof ");
                emitValue();
                Write(" === 'number'");
                return;

            case JsShape.Boolean:
                Write("typeof ");
                emitValue();
                Write(" === 'boolean'");
                return;

            case JsShape.Array:
                Write("Array.isArray(");
                emitValue();
                Write(")");
                return;

            case JsShape.Any:
                emitValue();
                Write(" !== null && ");
                emitValue();
                Write(" !== undefined");
                return;
        }

        if (target is INamedTypeSymbol named && named.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            emitValue();
            Write($" instanceof {ReferenceToType(named)}");
            return;
        }

        if (target.TypeKind == TypeKind.Interface)
        {
            // Interfaces are erased, so there is no runtime witness to test against.
            _ctx.Warn(DiagnosticIds.UnsupportedPattern,
                $"'is {target.Name}' tests an interface, which is erased in JavaScript. " +
                "The test only checks for a non-null value.", site);
            emitValue();
            Write(" !== null");
            return;
        }

        emitValue();
        Write(" !== null");
    }

    private void EmitPrefixUnary(PrefixUnaryExpressionSyntax prefix)
    {
        var op = prefix.Kind() switch
        {
            SyntaxKind.UnaryPlusExpression => "+",
            SyntaxKind.UnaryMinusExpression => "-",
            SyntaxKind.LogicalNotExpression => "!",
            SyntaxKind.BitwiseNotExpression => "~",
            SyntaxKind.PreIncrementExpression => "++",
            SyntaxKind.PreDecrementExpression => "--",
            SyntaxKind.IndexExpression => null,
            _ => null,
        };

        if (prefix.IsKind(SyntaxKind.IndexExpression))
        {
            // `^n` is an index from the end; the element-access emitter consumes this.
            Write("-(");
            EmitExpression(prefix.Operand);
            Write(")");
            return;
        }

        if (op is null)
        {
            _ctx.Error(DiagnosticIds.UnsupportedSyntax, $"Unsupported unary operator: {prefix.OperatorToken.Text}.", prefix);
            Write("undefined");
            return;
        }

        Write(op);
        EmitExpression(prefix.Operand);
    }

    private void EmitPostfixUnary(PostfixUnaryExpressionSyntax postfix)
    {
        // `x!` is a compile-time assertion about nullability with no runtime effect.
        if (postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            EmitExpression(postfix.Operand);
            return;
        }

        EmitExpression(postfix.Operand);
        Write(postfix.IsKind(SyntaxKind.PostIncrementExpression) ? "++" : "--");
    }

    private void EmitAssignment(AssignmentExpressionSyntax assignment)
    {
        // An event is a handler array, so `+=` pushes and `-=` removes.
        if (_ctx.SymbolFor(assignment.Left) is IEventSymbol)
        {
            if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
            {
                _ctx.UseHelper("csjsRemove");
                Write("csjsRemove(");
                EmitExpression(assignment.Left);
                Write(", ");
                EmitExpression(assignment.Right);
                Write(")");
                return;
            }

            EmitExpression(assignment.Left);
            Write(".push(");
            EmitExpression(assignment.Right);
            Write(")");
            return;
        }

        // Assigning through a user-declared indexer routes to the $set method.
        if (assignment.Left is ElementAccessExpressionSyntax elementAccess
            && _ctx.SymbolFor(elementAccess) is IPropertySymbol { IsIndexer: true } indexer
            && !IsMappedIndexer(indexer))
        {
            EmitExpression(elementAccess.Expression);
            Write(".$set(");
            WriteJoined(elementAccess.ArgumentList.Arguments, a => EmitExpression(a.Expression));
            Write(", ");
            EmitExpression(assignment.Right);
            Write(")");
            return;
        }

        // Dictionary index assignment is Map.set, not property assignment.
        if (assignment.Left is ElementAccessExpressionSyntax mapAccess
            && TypeMap.ShapeOf(_ctx.TypeFor(mapAccess.Expression)) == JsShape.Map
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            EmitExpression(mapAccess.Expression);
            Write(".set(");
            WriteJoined(mapAccess.ArgumentList.Arguments, a => EmitExpression(a.Expression));
            Write(", ");
            EmitExpression(assignment.Right);
            Write(")");
            return;
        }

        var op = assignment.Kind() switch
        {
            SyntaxKind.SimpleAssignmentExpression => "=",
            SyntaxKind.AddAssignmentExpression => "+=",
            SyntaxKind.SubtractAssignmentExpression => "-=",
            SyntaxKind.MultiplyAssignmentExpression => "*=",
            SyntaxKind.DivideAssignmentExpression => "/=",
            SyntaxKind.ModuloAssignmentExpression => "%=",
            SyntaxKind.AndAssignmentExpression => "&=",
            SyntaxKind.OrAssignmentExpression => "|=",
            SyntaxKind.ExclusiveOrAssignmentExpression => "^=",
            SyntaxKind.LeftShiftAssignmentExpression => "<<=",
            SyntaxKind.RightShiftAssignmentExpression => ">>=",
            SyntaxKind.UnsignedRightShiftAssignmentExpression => ">>>=",
            SyntaxKind.CoalesceAssignmentExpression => "??=",
            _ => "=",
        };

        // `x /= y` on integers must still truncate.
        if (assignment.IsKind(SyntaxKind.DivideAssignmentExpression)
            && TypeMap.IsIntegral(_ctx.TypeFor(assignment.Left))
            && TypeMap.IsIntegral(_ctx.TypeFor(assignment.Right)))
        {
            EmitExpression(assignment.Left);
            Write(" = Math.trunc(");
            EmitExpression(assignment.Left);
            Write(" / ");
            EmitExpression(assignment.Right);
            Write(")");
            return;
        }

        EmitExpression(assignment.Left);
        Write($" {op} ");
        EmitExpressionAs(assignment.Right, _ctx.TypeFor(assignment.Left));
    }

    private void EmitTernary(ConditionalExpressionSyntax ternary)
    {
        EmitCondition(ternary.Condition);
        Write(" ? ");
        EmitExpression(ternary.WhenTrue);
        Write(" : ");
        EmitExpression(ternary.WhenFalse);
    }

    private void EmitParenthesized(ParenthesizedExpressionSyntax parenthesized)
    {
        Write("(");
        EmitExpression(parenthesized.Expression);
        Write(")");
    }

    private void EmitCast(CastExpressionSyntax cast)
    {
        var target = _ctx.Model.GetTypeInfo(cast.Type).Type;
        var source = _ctx.TypeFor(cast.Expression);

        // Narrowing a floating-point value to an integer truncates in C#.
        if (TypeMap.IsIntegral(target) && source is not null && !TypeMap.IsIntegral(source)
            && TypeMap.ShapeOf(source) == JsShape.Number)
        {
            Write("Math.trunc(");
            EmitExpression(cast.Expression);
            Write(")");
            return;
        }

        if (TypeMap.IsBigInt(target) && source is not null && !TypeMap.IsBigInt(source))
        {
            Write("BigInt(");
            EmitExpression(cast.Expression);
            Write(")");
            return;
        }

        if (!TypeMap.IsBigInt(target) && TypeMap.IsBigInt(source))
        {
            Write("Number(");
            EmitExpression(cast.Expression);
            Write(")");
            return;
        }

        if (target?.SpecialType == SpecialType.System_String && source?.SpecialType != SpecialType.System_String)
        {
            Write("String(");
            EmitExpression(cast.Expression);
            Write(")");
            return;
        }

        // Reference conversions carry no runtime cost in JavaScript.
        EmitExpression(cast.Expression);
    }

    private void EmitAwait(AwaitExpressionSyntax await)
    {
        Write("await ");
        EmitExpression(await.Expression);
    }

    private void EmitThrowExpression(ThrowExpressionSyntax throwExpression)
    {
        // JavaScript has no throw expression, so an IIFE keeps it in expression position.
        Write("(() => { throw ");
        EmitExpression(throwExpression.Expression);
        Write("; })()");
    }

    private void EmitTypeOf(TypeOfExpressionSyntax typeOf)
    {
        var type = _ctx.Model.GetTypeInfo(typeOf.Type).Type;
        if (type is INamedTypeSymbol named && named.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            Write(ReferenceToType(named));
            return;
        }
        Write(QuoteString(type?.Name ?? "Object"));
    }

    private void EmitWith(WithExpressionSyntax with)
    {
        // `record with { X = 1 }` is a shallow clone plus overrides.
        Write("Object.assign(Object.create(Object.getPrototypeOf(");
        EmitExpression(with.Expression);
        Write(")), ");
        EmitExpression(with.Expression);
        Write(", { ");
        var first = true;
        foreach (var expression in with.Initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assignment) continue;
            if (!first) Write(", ");
            first = false;
            var member = _ctx.SymbolFor(assignment.Left);
            Write(member is null ? assignment.Left.ToString() : _ctx.Names.MemberName(member));
            Write(": ");
            EmitExpression(assignment.Right);
        }
        Write(" })");
    }

    private void EmitRange(RangeExpressionSyntax range)
    {
        // A bare range only appears inside an element access, which handles it directly.
        _ctx.Warn(DiagnosticIds.UnsupportedSyntax,
            "Range expressions are only supported inside an indexer.", range);
        Write("undefined");
    }

    // -------------------------------------------------------------------------------------------
    // Element access
    // -------------------------------------------------------------------------------------------

    private void EmitElementAccess(ElementAccessExpressionSyntax access)
    {
        var receiverType = _ctx.TypeFor(access.Expression);
        var arguments = access.ArgumentList.Arguments;

        // Dictionary read: Map.get, not property access.
        if (TypeMap.ShapeOf(receiverType) == JsShape.Map)
        {
            EmitExpression(access.Expression);
            Write(".get(");
            WriteJoined(arguments, a => EmitExpression(a.Expression));
            Write(")");
            return;
        }

        // A user-declared indexer became $get during declaration emission.
        if (_ctx.SymbolFor(access) is IPropertySymbol { IsIndexer: true } indexer && !IsMappedIndexer(indexer))
        {
            EmitExpression(access.Expression);
            Write(".$get(");
            WriteJoined(arguments, a => EmitExpression(a.Expression));
            Write(")");
            return;
        }

        // `list[^1]` and `list[1..3]` map onto at() and slice().
        if (arguments.Count == 1)
        {
            switch (arguments[0].Expression)
            {
                case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.IndexExpression } fromEnd:
                    EmitExpression(access.Expression);
                    Write(".at(-");
                    EmitExpression(fromEnd.Operand);
                    Write(")");
                    return;

                case RangeExpressionSyntax range:
                    EmitExpression(access.Expression);
                    Write(".slice(");
                    if (range.LeftOperand is not null) EmitExpression(range.LeftOperand);
                    else Write("0");
                    if (range.RightOperand is not null)
                    {
                        Write(", ");
                        EmitExpression(range.RightOperand);
                    }
                    Write(")");
                    return;
            }
        }

        EmitExpression(access.Expression);
        Write("[");
        WriteJoined(arguments, a => EmitExpression(a.Expression));
        Write("]");
    }

    private static bool IsMappedIndexer(IPropertySymbol indexer)
    {
        if (indexer.ContainingType is not { } owner) return false;
        var name = TypeMap.Unbound(owner);
        return name.StartsWith("System.", StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------
    // Lambdas
    // -------------------------------------------------------------------------------------------

    private void EmitLambda(IReadOnlyList<ParameterSyntax> parameters, SyntaxNode? body, bool isAsync)
    {
        if (isAsync) Write("async ");

        Write("(");
        WriteJoined(parameters, p =>
        {
            Write(NameResolver.Escape(p.Identifier.Text));

            // C# 12 lets a lambda parameter carry a default, and JavaScript has the same syntax.
            if (p.Default is null) return;
            Write(" = ");
            EmitExpression(p.Default.Value);
        });
        Write(") => ");

        switch (body)
        {
            case BlockSyntax block:
                // A lambda body is inside an argument list, so it must not terminate the line —
                // the closing paren of the call still has to follow it.
                EmitBlockInline(block);
                break;

            case ExpressionSyntax expression:
                // An object literal in expression position needs parentheses so JavaScript
                // does not read it as a block.
                var needsParens = expression is AnonymousObjectCreationExpressionSyntax
                    or InitializerExpressionSyntax
                    or AssignmentExpressionSyntax;
                if (needsParens) Write("(");
                EmitExpression(expression);
                if (needsParens) Write(")");
                break;

            default:
                Write("{ }");
                break;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Patterns
    // -------------------------------------------------------------------------------------------

    private void EmitIsPattern(IsPatternExpressionSyntax isPattern)
        => EmitPatternTest(null, isPattern.Pattern, () => EmitExpression(isPattern.Expression));

    private void EmitPatternTest(string subject, PatternSyntax pattern)
        => EmitPatternTest(subject, pattern, () => Write(subject));

    private void EmitPatternTest(string? subject, PatternSyntax pattern, Action emitValue)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax constant:
                emitValue();
                Write(" === ");
                EmitExpression(constant.Expression);
                return;

            case DiscardPatternSyntax:
                Write("true");
                return;

            case VarPatternSyntax varPattern:
                EmitPatternBinding(varPattern.Designation, emitValue);
                return;

            case DeclarationPatternSyntax declaration:
            {
                var type = _ctx.Model.GetTypeInfo(declaration.Type).Type;
                Write("(");
                EmitTypeTest(emitValue, type, declaration);
                if (declaration.Designation is SingleVariableDesignationSyntax)
                {
                    Write(" && ");
                    EmitPatternBinding(declaration.Designation, emitValue);
                }
                Write(")");
                return;
            }

            case TypePatternSyntax typePattern:
                EmitTypeTest(emitValue, _ctx.Model.GetTypeInfo(typePattern.Type).Type, typePattern);
                return;

            case RelationalPatternSyntax relational:
            {
                var op = relational.OperatorToken.Kind() switch
                {
                    SyntaxKind.LessThanToken => "<",
                    SyntaxKind.LessThanEqualsToken => "<=",
                    SyntaxKind.GreaterThanToken => ">",
                    SyntaxKind.GreaterThanEqualsToken => ">=",
                    _ => "===",
                };
                emitValue();
                Write($" {op} ");
                EmitExpression(relational.Expression);
                return;
            }

            case UnaryPatternSyntax unary:
                Write("!(");
                EmitPatternTest(subject, unary.Pattern, emitValue);
                Write(")");
                return;

            case BinaryPatternSyntax binary:
            {
                var op = binary.IsKind(SyntaxKind.AndPattern) ? " && " : " || ";
                Write("(");
                EmitPatternTest(subject, binary.Left, emitValue);
                Write(op);
                EmitPatternTest(subject, binary.Right, emitValue);
                Write(")");
                return;
            }

            case ParenthesizedPatternSyntax parenthesized:
                EmitPatternTest(subject, parenthesized.Pattern, emitValue);
                return;

            case RecursivePatternSyntax recursive:
                EmitRecursivePattern(subject, recursive, emitValue);
                return;

            case ListPatternSyntax list:
                EmitListPattern(list, emitValue);
                return;

            default:
                _ctx.Warn(DiagnosticIds.UnsupportedPattern, $"Unsupported pattern: {pattern.Kind()}.", pattern);
                Write("false");
                return;
        }
    }

    private void EmitPatternBinding(VariableDesignationSyntax designation, Action emitValue)
    {
        if (designation is not SingleVariableDesignationSyntax single)
        {
            Write("true");
            return;
        }

        // The `let` binding was hoisted; the pattern only performs the assignment, and the
        // comma expression keeps the whole test truthy so `&&` chains still work.
        Write("(");
        Write(NameResolver.Escape(single.Identifier.Text));
        Write(" = ");
        emitValue();
        Write(", true)");
    }

    private void EmitRecursivePattern(string? subject, RecursivePatternSyntax recursive, Action emitValue)
    {
        var parts = new List<Action>();

        if (recursive.Type is not null)
        {
            var type = _ctx.Model.GetTypeInfo(recursive.Type).Type;
            parts.Add(() => EmitTypeTest(emitValue, type, recursive));
        }
        else
        {
            parts.Add(() =>
            {
                emitValue();
                Write(" != null");
            });
        }

        if (recursive.PropertyPatternClause is not null)
        {
            foreach (var subpattern in recursive.PropertyPatternClause.Subpatterns)
            {
                var name = subpattern.NameColon?.Name.Identifier.Text
                           ?? subpattern.ExpressionColon?.Expression.ToString();
                if (name is null) continue;

                var member = subpattern.NameColon is not null ? _ctx.SymbolFor(subpattern.NameColon.Name) : null;
                var jsName = member is null ? NameResolver.Escape(name) : _ctx.Names.MemberName(member);

                parts.Add(() => EmitPatternTest(subject, subpattern.Pattern, () =>
                {
                    emitValue();
                    Write($".{jsName}");
                }));
            }
        }

        if (recursive.PositionalPatternClause is not null)
        {
            var index = 0;
            foreach (var subpattern in recursive.PositionalPatternClause.Subpatterns)
            {
                var position = index++;
                parts.Add(() => EmitPatternTest(subject, subpattern.Pattern, () =>
                {
                    emitValue();
                    Write($"[{position}]");
                }));
            }
        }

        if (recursive.Designation is SingleVariableDesignationSyntax)
            parts.Add(() => EmitPatternBinding(recursive.Designation, emitValue));

        Write("(");
        WriteJoined(parts, part => part(), " && ");
        Write(")");
    }

    private void EmitListPattern(ListPatternSyntax list, Action emitValue)
    {
        Write("(Array.isArray(");
        emitValue();
        Write($") && ");
        emitValue();
        Write($".length === {list.Patterns.Count}");

        var index = 0;
        foreach (var pattern in list.Patterns)
        {
            var position = index++;
            Write(" && ");
            EmitPatternTest(null, pattern, () =>
            {
                emitValue();
                Write($"[{position}]");
            });
        }

        Write(")");
    }

    private void EmitSwitchExpression(SwitchExpressionSyntax switchExpression)
    {
        // A switch expression is a value, so it becomes a chain of conditionals rather than
        // a statement. An IIFE would also work but reads far worse in the output.
        var arms = switchExpression.Arms.ToList();
        if (arms.Count == 0)
        {
            Write("undefined");
            return;
        }

        if (!IsSimpleExpression(switchExpression.GoverningExpression))
        {
            // Each arm re-emits the subject, so a subject with side effects would run repeatedly.
            _ctx.Warn(DiagnosticIds.UnsupportedSyntax,
                "The subject of this switch expression is re-evaluated once per arm. " +
                "Assign it to a local first if evaluating it has side effects.",
                switchExpression.GoverningExpression);
        }

        Write("(");
        for (var i = 0; i < arms.Count; i++)
        {
            var arm = arms[i];
            var isLast = i == arms.Count - 1;

            if (isLast && arm.Pattern is DiscardPatternSyntax && arm.WhenClause is null)
            {
                EmitExpression(arm.Expression);
                break;
            }

            EmitPatternTest(null, arm.Pattern, () => EmitExpression(switchExpression.GoverningExpression));
            if (arm.WhenClause is not null)
            {
                Write(" && ");
                EmitCondition(arm.WhenClause.Condition);
            }

            Write(" ? ");
            EmitExpression(arm.Expression);
            Write(" : ");

            if (isLast)
            {
                // No discard arm: C# throws when nothing matches.
                Write("(() => { throw new Error('No switch arm matched'); })()");
            }
        }
        Write(")");
    }

    private static bool IsSimpleExpression(ExpressionSyntax expression) => expression is
        IdentifierNameSyntax or LiteralExpressionSyntax or ThisExpressionSyntax
        or MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax or ThisExpressionSyntax };

    // -------------------------------------------------------------------------------------------
    // Strings
    // -------------------------------------------------------------------------------------------

    private void EmitInterpolatedString(InterpolatedStringExpressionSyntax interpolated)
    {
        var isRaw = interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken)
                    || interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);

        Write("`");
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    Write(EscapeTemplateText(text.TextToken.ValueText));
                    break;

                case InterpolationSyntax interpolation:
                    Write("${");
                    if (interpolation.FormatClause is not null)
                    {
                        _ctx.UseHelper("csjsFormatValue");
                        Write("csjsFormatValue(");
                        EmitExpression(interpolation.Expression);
                        Write($", {QuoteString(interpolation.FormatClause.FormatStringToken.ValueText)})");
                    }
                    else
                    {
                        EmitStringified(interpolation.Expression);
                    }
                    Write("}");
                    break;
            }
        }
        Write("`");
    }

    /// <summary>
    /// Emits an expression in a position where C# would convert it to a string.
    /// An enum is a number at runtime, so interpolating one would print the ordinal where C#
    /// prints the member name. The reverse map on the frozen enum object recovers the name.
    /// </summary>
    private void EmitStringified(ExpressionSyntax expression)
    {
        var type = _ctx.TypeFor(expression);

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            Write($"{ReferenceToType(enumType)}[");
            EmitExpression(expression);
            Write("]");
            return;
        }

        EmitExpression(expression);
    }

    private static string EscapeTemplateText(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '`': sb.Append("\\`"); break;
                case '\\': sb.Append("\\\\"); break;
                case '$': sb.Append("\\$"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    internal static string QuoteString(string value)
    {
        var sb = new StringBuilder("'");
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\'': sb.Append("\\'"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('\'').ToString();
    }

    private void EmitDeclarationExpression(DeclarationExpressionSyntax declaration)
    {
        switch (declaration.Designation)
        {
            case SingleVariableDesignationSyntax single:
                Write(NameResolver.Escape(single.Identifier.Text));
                break;

            case ParenthesizedVariableDesignationSyntax parenthesized:
                Write("[");
                WriteJoined(parenthesized.Variables, v =>
                    Write(v is SingleVariableDesignationSyntax s ? NameResolver.Escape(s.Identifier.Text) : "_"));
                Write("]");
                break;

            default:
                Write("_");
                break;
        }
    }

    // -------------------------------------------------------------------------------------------
    // LINQ query syntax
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Lowers LINQ query syntax onto the same array operations method syntax uses.
    /// <para>
    /// Ordering has to wrap the chain built so far rather than append to it, because
    /// <c>csjsOrderBy</c> takes the sequence as its first argument. Emission therefore runs
    /// innermost-outward: each clause is a function that emits its source before wrapping it.
    /// </para>
    /// </summary>
    private void EmitQuery(QueryExpressionSyntax query)
    {
        var range = NameResolver.Escape(query.FromClause.Identifier.Text);
        Action emitSource = () => EmitExpression(query.FromClause.Expression);

        foreach (var clause in query.Body.Clauses)
        {
            var source = emitSource;
            switch (clause)
            {
                case WhereClauseSyntax where:
                    emitSource = () =>
                    {
                        source();
                        Write($".filter(({range}) => ");
                        EmitExpression(where.Condition);
                        Write(")");
                    };
                    break;

                case OrderByClauseSyntax orderBy:
                    foreach (var ordering in orderBy.Orderings)
                    {
                        var inner = emitSource;
                        var descending = ordering.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword);
                        emitSource = () =>
                        {
                            _ctx.UseHelper("csjsOrderBy");
                            Write("csjsOrderBy(");
                            inner();
                            Write($", ({range}) => ");
                            EmitExpression(ordering.Expression);
                            Write($", {(descending ? "true" : "false")})");
                        };
                    }
                    break;

                case LetClauseSyntax let:
                    // `let` introduces a new binding, which becomes a map producing an object
                    // carrying both the original element and the new value.
                    _ctx.Warn(DiagnosticIds.UnsupportedSyntax,
                        "`let` in query syntax is not supported. Rewrite the query using method syntax.", let);
                    break;

                case FromClauseSyntax from:
                    emitSource = () =>
                    {
                        source();
                        Write($".flatMap(({range}) => ");
                        EmitExpression(from.Expression);
                        Write(")");
                    };
                    break;

                default:
                    _ctx.Warn(DiagnosticIds.UnsupportedSyntax,
                        $"Query clause `{clause.Kind()}` is not supported. Rewrite the query using method syntax.",
                        clause);
                    break;
            }
        }

        switch (query.Body.SelectOrGroup)
        {
            case SelectClauseSyntax select:
                // `select x` where x is the range variable is an identity projection worth dropping.
                if (select.Expression is IdentifierNameSyntax id && NameResolver.Escape(id.Identifier.Text) == range)
                {
                    emitSource();
                    return;
                }
                emitSource();
                Write($".map(({range}) => ");
                EmitExpression(select.Expression);
                Write(")");
                return;

            case GroupClauseSyntax group:
                _ctx.UseHelper("csjsGroupBy");
                Write("csjsGroupBy(");
                emitSource();
                Write($", ({range}) => ");
                EmitExpression(group.ByExpression);
                Write(")");
                return;

            default:
                emitSource();
                return;
        }
    }
}
