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
        // The stand-in for a rewritten conditional access's receiver is just its parameter.
        if (_ctx.StandInName(expression) is { } standIn)
        {
            Write(standIn);
            return;
        }

        // A char is a one-character string here, but C# promotes it to its UTF-16 code wherever
        // it takes part in arithmetic or comparison: `c - '0'` is 7 for '7', not NaN.
        if (IsCharPromotedToNumber(expression))
        {
            if (expression is LiteralExpressionSyntax { Token.Value: char literal })
            {
                Write(((int)literal).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            var simple = expression is IdentifierNameSyntax or MemberAccessExpressionSyntax
                or ElementAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax;
            if (!simple) Write("(");
            EmitExpressionCore(expression);
            if (!simple) Write(")");
            Write(".charCodeAt(0)");
            return;
        }

        EmitExpressionCore(expression);
    }

    private bool IsCharPromotedToNumber(ExpressionSyntax expression)
    {
        // A parenthesised char promotes at its outermost position only.
        if (expression.Parent is ParenthesizedExpressionSyntax) return false;

        var info = _ctx.Model.GetTypeInfo(expression);
        if (info.Type?.SpecialType != SpecialType.System_Char) return false;
        return info.ConvertedType is { } converted
               && converted.SpecialType != SpecialType.System_Char
               && TypeMap.ShapeOf(converted) is JsShape.Number or JsShape.BigInt;
    }

    private void EmitExpressionCore(ExpressionSyntax expression)
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
            case ExpressionSyntax fieldKeyword when fieldKeyword.IsKind(SyntaxKind.FieldExpression): EmitFieldKeyword(fieldKeyword); break;

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

        if (TryEmitStructCopy(expression, target)) return;

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
            if (TryEmitExtensionPropertyRead(identifier, symbol, null)) return;

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

        if (TryEmitImportedMember(symbol)) return true;

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

    /// <summary>
    /// Emits a static member that is itself a module export — a <c>const</c>, or a function
    /// referenced as a value — as its import binding.
    /// <para>
    /// Invocations already import through <see cref="EmitInvocation"/>; this covers every other
    /// way of reaching the member. Without it, <c>Lib.Version</c> would emit as a property of a
    /// container class that does not exist in JavaScript.
    /// </para>
    /// </summary>
    private bool TryEmitImportedMember(ISymbol symbol)
    {
        if (symbol is not (IPropertySymbol or IFieldSymbol or IMethodSymbol)) return false;
        if (GetJsImport(symbol.OriginalDefinition) is not { } import) return false;

        Write(UseMemberImport(symbol.OriginalDefinition, import));
        return true;
    }

    /// <summary>Registers a member-level import and returns the local alias to emit.</summary>
    private string UseMemberImport(ISymbol symbol, JsImportInfo import)
    {
        // The alias is a bare identifier, so unlike a member name it must not be a keyword.
        var alias = NameResolver.LocalName(_ctx.Names.MemberName(symbol));
        _ctx.UseImport(import.Module, import.IsDefault ? null : import.Export ?? symbol.Name, alias);
        return alias;
    }

    private void EmitMemberAccess(MemberAccessExpressionSyntax member)
    {
        var symbol = _ctx.SymbolFor(member);

        if (TryEmitGlobalMember(symbol)) return;
        if (TryEmitExtensionPropertyRead(member, symbol, member.Expression)) return;

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
            ApplyTemplate(template, () => EmitExpression(member.Expression), Array.Empty<Action>(), member,
                receiverIsArray: true, receiverIsFresh: false, resultType: _ctx.TypeFor(member));
            return;
        }

        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumMember)
        {
            EmitEnumMember(enumMember);
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

    /// <summary>
    /// The receiver of the conditional access being emitted, when it was rewritten into a
    /// parameter (see <see cref="EmitConditionalAccess"/>). Its bindings read from this instead of
    /// writing JavaScript's own <c>?.</c>.
    /// </summary>
    private ExpressionSyntax? _conditionalReceiver;

    private void EmitMemberBinding(MemberBindingExpressionSyntax binding)
    {
        var symbol = _ctx.SymbolFor(binding);

        if (_conditionalReceiver is { } receiver)
        {
            if (TryEmitExtensionPropertyRead(binding, symbol, receiver)) return;

            if (symbol is IFieldSymbol { ContainingType.IsTupleType: true } element && TupleElementIndex(element) is { } index)
            {
                EmitExpression(receiver);
                Write($"[{index}]");
                return;
            }

            var template = symbol is null ? null
                : GetJsTemplate(symbol) is { } inline ? new JsTemplate(inline)
                : symbol is not IMethodSymbol ? MemberMap.Find(symbol) : null;
            if (template is not null)
            {
                ApplyTemplate(template, () => EmitExpression(receiver), Array.Empty<Action>(), binding,
                    receiverIsArray: true, receiverIsFresh: false, resultType: _ctx.TypeFor(binding));
                return;
            }

            if (symbol is not null && symbol is not IMethodSymbol) ReportIfUnmapped(symbol, binding);
            EmitExpression(receiver);
            Write(".");
            Write(symbol is null ? NameResolver.Escape(binding.Name.Identifier.Text) : _ctx.Names.MemberName(symbol));
            return;
        }

        Write("?.");
        Write(symbol is null ? NameResolver.Escape(binding.Name.Identifier.Text) : _ctx.Names.MemberName(symbol));
    }

    private void EmitElementBinding(ElementBindingExpressionSyntax binding)
    {
        if (_conditionalReceiver is { } receiver)
        {
            EmitElementAccessOn(receiver, binding.ArgumentList.Arguments, binding);
            return;
        }

        Write("?.[");
        WriteJoined(binding.ArgumentList.Arguments, a => EmitExpression(a.Expression));
        Write("]");
    }

    /// <summary>
    /// <c>a?.B</c>. JavaScript's own <c>?.</c> is used whenever the member exists on the JavaScript
    /// object under its emitted name. When it does not — a mapped BCL member, an extension method,
    /// a dictionary index — the member is really a rewrite around its receiver, so the receiver
    /// becomes a parameter: <c>((r) =&gt; r == null ? null : Ext.M(r))(a)</c>. That evaluates
    /// <c>a</c> once and yields C#'s null, not undefined, when it is null.
    /// </summary>
    private void EmitConditionalAccess(ConditionalAccessExpressionSyntax conditional)
    {
        if (TryEmitNullConditionalAssignment(conditional)) return;

        var previous = _conditionalReceiver;

        if (!NeedsReceiverRewrite(conditional))
        {
            // The receiver belongs to whatever encloses this access; the bindings here are native.
            EmitExpression(conditional.Expression);
            _conditionalReceiver = null;
            EmitExpression(conditional.WhenNotNull);
            _conditionalReceiver = previous;
            return;
        }

        var name = _ctx.NextTemporary("r");
        Write($"(({name}) => {name} == null ? null : ");
        _conditionalReceiver = _ctx.CreateStandIn(conditional.Expression, name);
        var needsParens = conditional.WhenNotNull is AssignmentExpressionSyntax;
        if (needsParens) Write("(");
        EmitExpression(conditional.WhenNotNull);
        if (needsParens) Write(")");
        _conditionalReceiver = previous;
        Write(")(");
        EmitExpression(conditional.Expression);
        Write(")");
    }

    /// <summary>True when the member right after <c>?.</c> has no native JavaScript counterpart.</summary>
    private bool NeedsReceiverRewrite(ConditionalAccessExpressionSyntax conditional)
    {
        ExpressionSyntax? node = conditional.WhenNotNull;
        while (true)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation: node = invocation.Expression; continue;
                case MemberAccessExpressionSyntax access: node = access.Expression; continue;
                case ElementAccessExpressionSyntax element: node = element.Expression; continue;
                case PostfixUnaryExpressionSyntax postfix: node = postfix.Operand; continue;
            }
            break;
        }

        switch (node)
        {
            case MemberBindingExpressionSyntax binding:
                return _ctx.SymbolFor(binding) switch
                {
                    IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } => false,
                    IMethodSymbol method => method.ReducedFrom is not null
                                            || method.ContainingType?.IsExtension == true
                                            || MemberMap.RequiresMapping(method.ReducedFrom ?? method)
                                            || GetJsTemplate(method) is not null,
                    ISymbol member when member is IPropertySymbol or IFieldSymbol => member.ContainingType?.IsExtension == true
                                            || MemberMap.Find(member) is not null
                                            || GetJsTemplate(member) is not null
                                            || member is IFieldSymbol { ContainingType.IsTupleType: true },
                    _ => false,
                };

            case ElementBindingExpressionSyntax element:
                var receiverType = _ctx.TypeFor(conditional.Expression);
                return TypeMap.ShapeOf(receiverType) == JsShape.Map
                       || _ctx.SymbolFor(element) is IPropertySymbol { IsIndexer: true } indexer && !IsMappedIndexer(indexer)
                       || element.ArgumentList.Arguments.Any(a => a.Expression is RangeExpressionSyntax
                           or PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.IndexExpression });

            default:
                return false;
        }
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
        if (TryEmitExtensionMemberInvocation(invocation, symbol)) return;
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
            var arguments = BuildArgumentEmitters(invocation.ArgumentList.Arguments, symbol, template.StringifyArguments,
                spreadParams: template.Pattern.Contains("{...", StringComparison.Ordinal));
            ApplyTemplate(template,
                receiver is null ? null : () => EmitExpression(receiver),
                arguments,
                invocation,
                receiverIsArray: receiver is not null && ProducesJsArray(receiver),
                receiverIsFresh: receiver is not null && ProducesFreshArray(receiver),
                resultType: symbol.ReturnType);
            return;
        }

        if (GetJsImport(symbol.OriginalDefinition) is { } import)
        {
            Write(UseMemberImport(symbol.OriginalDefinition, import));
            Write("(");
            EmitArgumentList(invocation.ArgumentList.Arguments, symbol);
            Write(")");
            return;
        }

        // An extension method is a static call with the receiver moved into first position.
        if (symbol.IsExtensionMethod && symbol.ReducedFrom is not null && receiver is not null)
        {
            // A framework extension with no mapping has no class to call in JavaScript.
            ReportIfUnmapped(symbol.ReducedFrom, invocation);

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
                if (_conditionalReceiver is { } receiver)
                {
                    EmitExpression(receiver);
                    Write(".");
                }
                else Write("?.");
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
        if (symbol.Name == "TryParse" && arguments.Count == 2 && MemberMap.RequiresMapping(symbol)
            && owner is "System.Int32" or "System.Int16" or "System.Byte" or "System.Double"
                or "System.Single" or "System.Decimal" or "System.UInt32")
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

        // queue.TryDequeue(out x), stack.TryPop(out x), and their Peek variants.
        if (owner is "System.Collections.Generic.Queue" or "System.Collections.Generic.Stack"
            && symbol.Name is "TryDequeue" or "TryPop" or "TryPeek" && arguments.Count == 1)
        {
            var receiver = GetReceiver(invocation.Expression);
            if (receiver is null) return false;
            Write("(");
            EmitExpression(receiver);
            Write(".length > 0 ? (");
            EmitOutTarget(arguments[0]);
            Write(" = ");
            EmitExpression(receiver);
            Write(symbol.Name == "TryPeek" ? "[0]" : ".shift()");
            Write(", true) : (");
            EmitOutTarget(arguments[0]);
            Write($" = {DefaultValueFor(symbol.Parameters[0].Type)}, false))");
            return true;
        }

        if (TryEmitEnumInvocation(invocation, symbol, owner)) return true;
        if (TryEmitJsonInvocation(invocation, symbol, owner)) return true;

        // OfType<T>() filters by a runtime type test, which needs T — a template cannot see it.
        if (owner == "System.Linq.Enumerable" && symbol.Name == "OfType" && symbol.TypeArguments.Length == 1)
        {
            var receiver = GetReceiver(invocation.Expression) ?? arguments.FirstOrDefault()?.Expression;
            if (receiver is null) return false;
            var item = _ctx.NextTemporary("x");
            if (ProducesJsArray(receiver)) EmitExpression(receiver);
            else
            {
                Write("[...");
                EmitExpression(receiver);
                Write("]");
            }
            Write($".filter(({item}) => ");
            EmitTypeTest(() => Write(item), symbol.TypeArguments[0], invocation);
            Write(")");
            return true;
        }

        if (symbol.Parameters.Any(p => p.RefKind is RefKind.Out or RefKind.Ref && !IsInterpolatedStringHandler(p.Type))
            && symbol.ContainingType?.ContainingAssembly?.Name != "CSJS.Interop")
        {
            // A method declared in this program hands its ref/out values back in its result.
            if (symbol.DeclaringSyntaxReferences.Length > 0
                && symbol.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction)
            {
                EmitByRefCall(invocation, symbol);
                return true;
            }

            _ctx.Error(DiagnosticIds.UnsupportedMember,
                $"'{symbol.Name}' uses ref/out parameters and has no JavaScript mapping. " +
                "Methods declared in your own code support ref and out; framework ones need a mapping.",
                invocation);
            Write("undefined");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calls a method rewritten by <see cref="EmitFunctionBody"/>: the result is a tuple whose
    /// first element is the return value and whose rest are the final ref/out values, which are
    /// written back into the variables the caller passed.
    /// </summary>
    private void EmitByRefCall(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        var result = _ctx.NextTemporary("r");
        var ordered = OrderArguments(invocation.ArgumentList.Arguments, symbol);

        Write($"(({result}) => (");
        var position = 1;
        foreach (var parameter in symbol.Parameters.Where(p => p.RefKind is RefKind.Ref or RefKind.Out))
        {
            var slot = position++;
            var argument = ordered.FirstOrDefault(pair => SymbolEqualityComparer.Default.Equals(pair.Parameter, parameter)).Argument;
            if (argument?.Expression is null || IsDiscard(argument.Expression)) continue;

            EmitOutTarget(argument);
            Write($" = {result}[{slot}], ");
        }
        Write($"{result}[0]))(");

        var receiver = GetReceiver(invocation.Expression);
        if (symbol.IsExtensionMethod && symbol.ReducedFrom is not null && receiver is not null)
        {
            Write($"{ReferenceToType(symbol.ContainingType)}.{_ctx.Names.MemberName(symbol)}(");
            EmitExpression(receiver);
            if (invocation.ArgumentList.Arguments.Count > 0) Write(", ");
        }
        else
        {
            EmitInvocationTarget(invocation.Expression, symbol);
            Write("(");
        }

        EmitArgumentList(invocation.ArgumentList.Arguments, symbol, skipOut: true);
        Write("))");
    }

    /// <summary>
    /// An interpolated string handler parameter (<c>ref AppendInterpolatedStringHandler</c>) is
    /// passed by reference in C#, but its argument is an interpolated string — a template literal
    /// here — so nothing is written back and the call is an ordinary one.
    /// </summary>
    private static bool IsInterpolatedStringHandler(ITypeSymbol type)
        => type.GetAttributes().Any(a => a.AttributeClass?.Name == "InterpolatedStringHandlerAttribute");

    private bool IsDiscard(ExpressionSyntax expression) => expression switch
    {
        DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax } => true,
        IdentifierNameSyntax { Identifier.Text: "_" } => _ctx.SymbolFor(expression) is null or IDiscardSymbol,
        _ => false,
    };

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
        // Inside a rewritten `a?.M()`, the binding's receiver is the stand-in for `a`.
        MemberBindingExpressionSyntax => _conditionalReceiver,
        _ => null,
    };

    private void EmitArgumentList(
        SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol? target = null, bool skipOut = false)
    {
        var ordered = OrderArguments(arguments, target);
        var first = true;

        foreach (var (argument, parameter) in ordered)
        {
            if (skipOut && parameter?.RefKind == RefKind.Out) continue;

            if (!first) Write(", ");
            first = false;

            if (parameter?.IsParams == true && argument.Expression is not null
                && _ctx.TypeFor(argument.Expression) is IArrayTypeSymbol)
            {
                Write("...");
            }

            if (argument.Expression is null) Write(DefaultValueFor(parameter?.Type));
            // By reference means the same storage, so a struct must not be copied on the way in.
            else if (argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.InKeyword
                     || parameter?.RefKind is RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter)
                EmitExpression(argument.Expression);
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

    /// <summary>
    /// Argument emitters whose expression binds more loosely than a member access or a call — a
    /// conditional, a binary operator, an assignment. Substituted next to an operator in a template
    /// (<c>{0} ?? {1}</c>, <c>{0}.repeat(…)</c>) such an argument needs parentheses, or JavaScript's
    /// precedence regroups it: <c>x ?? a > 0 ? b : c</c> is <c>(x ?? a > 0) ? b : c</c>.
    /// </summary>
    private readonly HashSet<Action> _looseArguments = new();

    private static bool BindsLoosely(ExpressionSyntax? expression) => expression is
        ConditionalExpressionSyntax or BinaryExpressionSyntax or AssignmentExpressionSyntax
        or PrefixUnaryExpressionSyntax or AwaitExpressionSyntax or CastExpressionSyntax
        or IsPatternExpressionSyntax or ConditionalAccessExpressionSyntax
        or AnonymousFunctionExpressionSyntax or ThrowExpressionSyntax;

    private List<Action> BuildArgumentEmitters(
        SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol? target, bool stringify = false, bool spreadParams = false)
        => OrderArguments(arguments, target)
            .Select(pair =>
            {
                Action emit = () => EmitTemplateArgument(pair, arguments, target, stringify, spreadParams);
                if (BindsLoosely(pair.Argument.Expression)) _looseArguments.Add(emit);
                return emit;
            })
            .ToList();

    private void EmitTemplateArgument(
        (ArgumentSyntax Argument, IParameterSymbol? Parameter) pair, SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? target, bool stringify, bool spreadParams)
    {
        // An array handed to a params parameter in non-expanded form is the whole
        // argument list, so a `[{...}]` template must receive its elements, not the array.
        var spread = spreadParams && pair.Parameter?.IsParams == true && pair.Argument.Expression is not null
                     && _ctx.TypeFor(pair.Argument.Expression) is IArrayTypeSymbol
                     && arguments.Count == target!.Parameters.Length;
        if (spread) Write("...");

        if (pair.Argument.Expression is null) Write(DefaultValueFor(pair.Parameter?.Type));
        // A spread array is the argument list itself; its elements are converted where used.
        else if (stringify && !spread) EmitStringified(pair.Argument.Expression);
        else EmitExpressionAs(pair.Argument.Expression, pair.Parameter?.Type);
    }

    /// <summary>
    /// True when an expression evaluates to an array nothing else holds a reference to, so a
    /// template that must hand back a copy (<c>ToList</c>) can use it as it is. Every LINQ call
    /// builds a new array, except the pass-throughs that return their receiver.
    /// </summary>
    /// <summary>
    /// True when a placeholder stands alone as an argument or element — bounded by <c>(</c>,
    /// <c>,</c>, or <c>[</c> before and <c>)</c>, <c>,</c>, or <c>]</c> after — so whatever is
    /// substituted cannot be regrouped by the operators around it.
    /// </summary>
    private static bool IsIsolated(string pattern, int open, int afterClose)
    {
        var before = open - 1;
        while (before >= 0 && pattern[before] == ' ') before--;
        var after = afterClose;
        while (after < pattern.Length && pattern[after] == ' ') after++;

        var openOk = before < 0 || pattern[before] is '(' or ',' or '[';
        var closeOk = after >= pattern.Length || pattern[after] is ')' or ',' or ']';
        return openOk && closeOk;
    }

    private static readonly System.Text.RegularExpressions.Regex HelperReference =
        new(@"\bcsjs[A-Z]\w*(?=\()", System.Text.RegularExpressions.RegexOptions.Compiled);

    private bool ProducesFreshArray(ExpressionSyntax expression)
        => expression is InvocationExpressionSyntax invocation
           && _ctx.SymbolFor(invocation) is IMethodSymbol { ContainingType: { } owner } method
           && TypeMap.Unbound(owner) == "System.Linq.Enumerable"
           && method.Name is not ("AsEnumerable" or "Cast");

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
        bool receiverIsArray,
        bool receiverIsFresh = false,
        ITypeSymbol? resultType = null)
    {
        if (template.Helper is not null)
            _ctx.UseHelper(template.Helper);

        // A pattern may call more than the one helper it names — csjsRegexReplace(csjsRegex(…))
        // — and every one of them needs an import, or the module throws a ReferenceError.
        foreach (System.Text.RegularExpressions.Match helper in HelperReference.Matches(template.Pattern))
            _ctx.UseHelper(helper.Value);

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
            var open = i;
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

            if (token == "copy")
            {
                if (!receiverIsFresh) Write("[...");
                if (emitThis is not null) emitThis();
                else Write(_thisAlias ?? "this");
                if (!receiverIsFresh) Write("]");
                continue;
            }

            if (token == "default")
            {
                Write(DefaultValueFor(resultType));
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
                if (index >= emitArgs.Count)
                {
                    Write("undefined");
                    continue;
                }

                var wrap = _looseArguments.Contains(emitArgs[index]) && !IsIsolated(pattern, open, i);
                if (wrap) Write("(");
                emitArgs[index]();
                if (wrap) Write(")");
                continue;
            }

            // Not a placeholder: a literal brace, as in an object literal `{ a: {0} }`. Only the
            // brace itself is written, so placeholders inside it are still substituted.
            Write("{");
            i = open + 1;
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
                    WriteJoined(initializer.Expressions, EmitValue);
                    Write("]");
                    return;
            }
        }

        // `new Dictionary<K,V> { ["a"] = 1 }` is an object initializer in C#'s grammar — index
        // initializers are — but for a Map every entry is a key, not a property.
        if (shape == JsShape.Map
            && initializer is { RawKind: (int)SyntaxKind.ObjectInitializerExpression, Expressions.Count: > 0 }
            && initializer.Expressions.All(e => e is AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax }))
        {
            Write("new Map([");
            WriteJoined(initializer.Expressions, EmitMapEntry);
            Write("])");
            return;
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

        // `new S()` with no constructor of S's own is default(S): every field zeroed.
        if (symbol is { IsImplicitlyDeclared: true, Parameters.Length: 0 } && IsDeclaredStruct(type))
        {
            Write($"{ReferenceToType(type!)}.$default()");
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
            EmitExpressionAs(assignment.Right, _ctx.TypeFor(assignment.Left));
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
                EmitValue(pair.Expressions[1]);
                Write("]");
                return;

            case AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax key } assignment:
                Write("[");
                WriteJoined(key.ArgumentList.Arguments, a => EmitExpression(a.Expression));
                Write(", ");
                EmitValue(assignment.Right);
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

        // fill() would put one shared instance in every slot; each struct element is its own value.
        if (IsDeclaredStruct(elementType))
        {
            Write("Array.from({ length: ");
            EmitExpression(size);
            Write($" }}, () => {DefaultValueFor(elementType)})");
            return;
        }

        Write("new Array(");
        EmitExpression(size);
        Write($").fill({DefaultValueFor(elementType)})");
    }

    private void EmitInitializer(InitializerExpressionSyntax initializer)
    {
        Write("[");
        WriteJoined(initializer.Expressions, EmitValue);
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
                    EmitValue(expressionElement.Expression);
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
            EmitValue(initializer.Expression);
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
        WriteJoined(tuple.Arguments, a => EmitValue(a.Expression));
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

        // A framework operator with a mapping (DateTime - DateTime, DateTime + TimeSpan) has no
        // JavaScript operator that means the same thing: Date + number concatenates.
        if (symbol is { MethodKind: MethodKind.UserDefinedOperator } frameworkOperator
            && MemberMap.Find(frameworkOperator) is { } operatorTemplate)
        {
            ApplyTemplate(operatorTemplate, null,
                new Action[] { () => EmitExpression(binary.Left), () => EmitExpression(binary.Right) }, binary);
            return;
        }

        if (binary.IsKind(SyntaxKind.AddExpression)
            && _ctx.TypeFor(binary)?.SpecialType == SpecialType.System_String)
        {
            EmitStringConcatenation(binary);
            return;
        }

        if (TryEmitOverflowAwareArithmetic(binary)) return;

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

    /// <summary>
    /// String concatenation converts each operand the way C# does: an enum by name, a bool as
    /// <c>True</c>, null as nothing. JavaScript's <c>+</c> would write <c>1</c>, <c>true</c>, and
    /// <c>null</c> into the middle of the string.
    /// </summary>
    private void EmitStringConcatenation(BinaryExpressionSyntax binary)
    {
        EmitStringified(binary.Left);
        Write(" + ");
        EmitStringified(binary.Right);
    }

    private enum OverflowMode { Default, Checked, Unchecked }

    /// <summary>
    /// The overflow context of an operation: the nearest enclosing <c>checked</c> or
    /// <c>unchecked</c> expression or block, else the project default.
    /// </summary>
    private OverflowMode OverflowContextOf(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case CheckedExpressionSyntax expression:
                    return expression.IsKind(SyntaxKind.CheckedExpression) ? OverflowMode.Checked : OverflowMode.Unchecked;
                case CheckedStatementSyntax statement:
                    return statement.IsKind(SyntaxKind.CheckedStatement) ? OverflowMode.Checked : OverflowMode.Unchecked;
                case MemberDeclarationSyntax:
                    return _ctx.Options.CheckOverflow ? OverflowMode.Checked : OverflowMode.Default;
            }
        }
        return _ctx.Options.CheckOverflow ? OverflowMode.Checked : OverflowMode.Default;
    }

    /// <summary>The <c>csjsChecked</c> range name for an integral type, or null for any other.</summary>
    private static string? IntegralKind(ITypeSymbol? type) => type?.SpecialType switch
    {
        SpecialType.System_SByte => "sbyte",
        SpecialType.System_Byte => "byte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long",
        SpecialType.System_UInt64 => "ulong",
        _ => null,
    };

    /// <summary>
    /// Integer <c>+</c>, <c>-</c>, and <c>*</c> inside a <c>checked</c> or <c>unchecked</c> context.
    /// <para>
    /// <c>checked</c> throws <c>OverflowException</c> when the result leaves the type's range.
    /// <c>unchecked</c> wraps to the type's width, as C# does: <c>int.MaxValue + 1</c> is
    /// <c>int.MinValue</c>. Outside either, arithmetic is emitted as written — doubles hold every
    /// 32-bit result exactly, and wrapping every addition would bury the output in <c>| 0</c>.
    /// </para>
    /// </summary>
    private bool TryEmitOverflowAwareArithmetic(BinaryExpressionSyntax binary)
    {
        var op = binary.Kind() switch
        {
            SyntaxKind.AddExpression => "+",
            SyntaxKind.SubtractExpression => "-",
            SyntaxKind.MultiplyExpression => "*",
            _ => null,
        };
        if (op is null) return false;

        var kind = IntegralKind(_ctx.TypeFor(binary));
        if (kind is null) return false;

        switch (OverflowContextOf(binary))
        {
            case OverflowMode.Checked:
                _ctx.UseHelper("csjsChecked");
                Write("csjsChecked(");
                EmitBinaryOperands(binary, op);
                Write($", '{kind}')");
                return true;

            case OverflowMode.Unchecked:
                EmitWrapped(kind, () =>
                {
                    // Math.imul multiplies as 32-bit integers; a double product of two large ints
                    // has already lost the low bits that wrapping needs.
                    if (op == "*" && kind is "int" or "uint")
                    {
                        Write("Math.imul(");
                        EmitExpression(binary.Left);
                        Write(", ");
                        EmitExpression(binary.Right);
                        Write(")");
                    }
                    else
                    {
                        EmitBinaryOperands(binary, op);
                    }
                });
                return true;

            default:
                return false;
        }
    }

    /// <summary>Wraps a value to the width of an integral type, as unchecked C# arithmetic does.</summary>
    private void EmitWrapped(string kind, Action emitValue)
    {
        switch (kind)
        {
            case "int":
                Write("((");
                emitValue();
                Write(") | 0)");
                return;
            case "uint":
                Write("((");
                emitValue();
                Write(") >>> 0)");
                return;
            case "short":
                Write("((");
                emitValue();
                Write(") << 16 >> 16)");
                return;
            case "ushort":
                Write("((");
                emitValue();
                Write(") & 0xFFFF)");
                return;
            case "sbyte":
                Write("((");
                emitValue();
                Write(") << 24 >> 24)");
                return;
            case "byte":
                Write("((");
                emitValue();
                Write(") & 0xFF)");
                return;
            case "long":
                Write("BigInt.asIntN(64, ");
                emitValue();
                Write(")");
                return;
            case "ulong":
                Write("BigInt.asUintN(64, ");
                emitValue();
                Write(")");
                return;
            default:
                emitValue();
                return;
        }
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
            is "System.DateTime" or "System.DateTimeOffset";
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
            EmitExpressionAs(assignment.Right, _ctx.TypeFor(assignment.Left));
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

        // char <-> number: a char is a one-character string here, so the conversion is explicit.
        if (target?.SpecialType == SpecialType.System_Char && source is not null
            && source.SpecialType != SpecialType.System_Char && TypeMap.ShapeOf(source) is JsShape.Number or JsShape.BigInt)
        {
            Write("String.fromCharCode(");
            if (TypeMap.IsBigInt(source)) Write("Number(");
            EmitExpression(cast.Expression);
            if (TypeMap.IsBigInt(source)) Write(")");
            Write(")");
            return;
        }

        if (source?.SpecialType == SpecialType.System_Char && target is not null
            && target.SpecialType != SpecialType.System_Char && TypeMap.ShapeOf(target) is JsShape.Number or JsShape.BigInt)
        {
            if (TypeMap.IsBigInt(target)) Write("BigInt(");
            EmitCharCode(cast.Expression);
            if (TypeMap.IsBigInt(target)) Write(")");
            return;
        }

        if (TryEmitIntegralConversion(cast, source, target)) return;

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

    private void EmitCharCode(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax { Token.Value: char literal })
        {
            Write(((int)literal).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        var simple = expression is IdentifierNameSyntax or MemberAccessExpressionSyntax
            or ElementAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax;
        if (!simple) Write("(");
        EmitExpression(expression);
        if (!simple) Write(")");
        Write(".charCodeAt(0)");
    }

    /// <summary>
    /// An explicit integral conversion that can lose information: <c>(byte)300</c> is 44 in C#.
    /// Under <c>checked</c> it throws instead. Conversions that always fit cost nothing.
    /// </summary>
    private bool TryEmitIntegralConversion(CastExpressionSyntax cast, ITypeSymbol? source, ITypeSymbol? target)
    {
        var targetKind = IntegralKind(target);
        if (targetKind is null || source is null || target is null) return false;

        var sourceIsIntegral = IntegralKind(source) is not null || source.TypeKind == TypeKind.Enum;
        var sourceIsFloating = source.SpecialType is SpecialType.System_Double or SpecialType.System_Single
            or SpecialType.System_Decimal;
        if (!sourceIsIntegral && !sourceIsFloating) return false;

        var mode = OverflowContextOf(cast);
        var sourceIsBig = TypeMap.IsBigInt(source);
        var targetIsBig = TypeMap.IsBigInt(target);

        void EmitConvertedValue()
        {
            // Cross the Number/BigInt boundary first; truncate a fraction, as every cast does.
            if (targetIsBig && !sourceIsBig)
            {
                Write("BigInt(");
                if (sourceIsFloating) Write("Math.trunc(");
                EmitExpression(cast.Expression);
                if (sourceIsFloating) Write(")");
                Write(")");
            }
            else if (sourceIsFloating)
            {
                Write("Math.trunc(");
                EmitExpression(cast.Expression);
                Write(")");
            }
            else
            {
                EmitExpression(cast.Expression);
            }
        }

        if (mode == OverflowMode.Checked)
        {
            if (FitsWithin(source, target)) return false;
            _ctx.UseHelper("csjsChecked");
            if (sourceIsBig && !targetIsBig)
            {
                // Range-check as BigInt so a huge long is caught before Number() can round it.
                Write("Number(csjsChecked(");
                EmitExpression(cast.Expression);
                Write($", '{targetKind}'))");
                return true;
            }
            Write("csjsChecked(");
            EmitConvertedValue();
            Write($", '{targetKind}')");
            return true;
        }

        if (FitsWithin(source, target) || sourceIsFloating) return false;

        // BigInt narrows with asIntN; a Number narrows with bit operations.
        if (sourceIsBig && !targetIsBig)
        {
            var bits = targetKind switch { "sbyte" or "byte" => 8, "short" or "ushort" => 16, _ => 32 };
            var signed = targetKind is "sbyte" or "short" or "int";
            Write($"Number(BigInt.as{(signed ? "Int" : "Uint")}N({bits}, ");
            EmitExpression(cast.Expression);
            Write("))");
            return true;
        }

        EmitWrapped(targetKind, EmitConvertedValue);
        return true;
    }

    /// <summary>True when every value of <paramref name="source"/> is representable in <paramref name="target"/>.</summary>
    private static bool FitsWithin(ITypeSymbol source, ITypeSymbol target)
    {
        static (decimal Min, decimal Max)? RangeOf(ITypeSymbol type) => type.SpecialType switch
        {
            SpecialType.System_SByte => (sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => (byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => (short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 => (ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Int32 => (int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 => (uint.MinValue, uint.MaxValue),
            SpecialType.System_Int64 => (long.MinValue, long.MaxValue),
            SpecialType.System_UInt64 => (ulong.MinValue, ulong.MaxValue),
            _ => null,
        };

        var underlying = source is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } e } ? e : source;
        if (RangeOf(underlying) is not { } s || RangeOf(target) is not { } t) return false;
        return s.Min >= t.Min && s.Max <= t.Max;
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
        => EmitElementAccessOn(access.Expression, access.ArgumentList.Arguments, access);

    /// <summary>Element access on any receiver — a real expression, or a conditional-access stand-in.</summary>
    private void EmitElementAccessOn(ExpressionSyntax receiver, SeparatedSyntaxList<ArgumentSyntax> arguments, ExpressionSyntax access)
    {
        var receiverType = _ctx.TypeFor(receiver);

        // Dictionary read: Map.get, not property access.
        if (TypeMap.ShapeOf(receiverType) == JsShape.Map)
        {
            EmitExpression(receiver);
            Write(".get(");
            WriteJoined(arguments, a => EmitExpression(a.Expression));
            Write(")");
            return;
        }

        // A user-declared indexer became $get during declaration emission.
        if (_ctx.SymbolFor(access) is IPropertySymbol { IsIndexer: true } indexer && !IsMappedIndexer(indexer))
        {
            EmitExpression(receiver);
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
                    EmitExpression(receiver);
                    Write(".at(-");
                    EmitExpression(fromEnd.Operand);
                    Write(")");
                    return;

                case RangeExpressionSyntax range:
                    EmitExpression(receiver);
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

        EmitExpression(receiver);
        Write("[");
        WriteJoined(arguments, a => EmitExpression(a.Expression));
        Write("]");
    }

    private static bool IsMappedIndexer(IPropertySymbol indexer)
    {
        if (indexer.ContainingType is not { } owner) return false;

        // A binding's indexer describes JavaScript's own bracket access — there is no $get on
        // a library object, so it emits as plain `obj[key]`.
        if (NameResolver.IsBindingMember(indexer)) return true;

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

        // The lambda is a function of its own: an enclosing method's ref/out rewrite stops here.
        var previousByRef = _byRefReturn;
        _byRefReturn = null;
        EmitLambdaBody(body);
        _byRefReturn = previousByRef;
    }

    private void EmitLambdaBody(SyntaxNode? body)
    {
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
            EmitEnumName(enumType, () => EmitExpression(expression));
            return;
        }

        if (type is INamedTypeSymbol nullable && TypeMap.IsNullableValueType(nullable, out var underlying)
            && underlying is INamedTypeSymbol { TypeKind: TypeKind.Enum } nullableEnum)
        {
            // A null enum prints as nothing; a value prints its name.
            _ctx.UseHelper("csjsToString");
            Write("csjsToString(");
            EmitEnumName(nullableEnum, () => EmitExpression(expression));
            Write(")");
            return;
        }

        switch (type)
        {
            // `True`, not `true`: C# capitalises booleans when it turns them into text.
            case { SpecialType: SpecialType.System_Boolean }:
                if (expression is LiteralExpressionSyntax)
                {
                    Write(expression.IsKind(SyntaxKind.TrueLiteralExpression) ? "'True'" : "'False'");
                    return;
                }
                Write("(");
                EmitExpression(expression);
                Write(" ? 'True' : 'False')");
                return;

            case INamedTypeSymbol named when TypeMap.Unbound(named) == "System.TimeSpan":
                _ctx.UseHelper("csjsTimeSpanToString");
                Write("csjsTimeSpanToString(");
                EmitExpression(expression);
                Write(")");
                return;

            case INamedTypeSymbol named when TypeMap.Unbound(named) == "System.DateTime":
                _ctx.UseHelper("csjsFormatValue");
                Write("csjsFormatValue(");
                EmitExpression(expression);
                Write(", 'G')");
                return;
        }

        // C# turns null into "" when it builds a string; JavaScript writes "null". Only values
        // that can actually be null pay for the guard. An object always goes through it too:
        // console.log would print its inspected fields, where C# calls ToString().
        if (CanBeNull(expression, type)
            || TypeMap.ShapeOf(type) is JsShape.Object or JsShape.Any or JsShape.Array or JsShape.Map or JsShape.Set)
        {
            _ctx.UseHelper("csjsToString");
            Write("csjsToString(");
            EmitExpression(expression);
            Write(")");
            return;
        }

        EmitExpression(expression);
    }

    private bool CanBeNull(ExpressionSyntax expression, ITypeSymbol? type)
    {
        if (type is null) return false;
        if (expression is LiteralExpressionSyntax or InterpolatedStringExpressionSyntax) return expression.IsKind(SyntaxKind.NullLiteralExpression);
        if (type is INamedTypeSymbol named && TypeMap.IsNullableValueType(named, out _)) return true;
        if (type.IsValueType) return false;
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.TypeParameter) return true;

        var flow = _ctx.Model.GetTypeInfo(expression).Nullability.FlowState;
        return flow == NullableFlowState.MaybeNull;
    }

    /// <summary>
    /// Emits the member name of an enum value. A declared enum carries a reverse map on its
    /// frozen object; a framework enum has no object at all, so the names are inlined.
    /// </summary>
    private void EmitEnumName(INamedTypeSymbol enumType, Action emitValue)
    {
        // A value with no member of its own still has text in C#: the number, or for a [Flags]
        // enum the set flags joined by ", ". Indexing the reverse map alone would give undefined.
        _ctx.UseHelper("csjsEnumName");
        Write("csjsEnumName(");

        if (enumType.DeclaringSyntaxReferences.Length > 0 && !IsInlinedEnum(enumType))
        {
            Write(ReferenceToType(enumType));
        }
        else
        {
            var entries = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .DistinctBy(f => Convert.ToInt64(f.ConstantValue))
                .Select(f => $"{Convert.ToInt64(f.ConstantValue)}: '{f.Name}'");
            Write($"{{ {string.Join(", ", entries)} }}");
        }

        Write(", ");
        emitValue();
        if (enumType.GetAttributes().Any(a => a.AttributeClass?.Name == "FlagsAttribute")) Write(", true");
        Write(")");
    }

    /// <summary>
    /// Emits an enum member. A declared enum's member is a property on its frozen object; a
    /// framework enum (<c>StringSplitOptions</c>, <c>RegexOptions</c>) does not exist in
    /// JavaScript, so its value is written as the number it is, with the name alongside.
    /// </summary>
    private void EmitEnumMember(IFieldSymbol member)
    {
        if (member.DeclaringSyntaxReferences.Length == 0 || IsInlinedEnum(member.ContainingType))
        {
            Write($"{Convert.ToInt64(member.ConstantValue)} /* {member.ContainingType.Name}.{member.Name} */");
            return;
        }

        Write($"{ReferenceToType(member.ContainingType)}.{member.Name}");
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
}
