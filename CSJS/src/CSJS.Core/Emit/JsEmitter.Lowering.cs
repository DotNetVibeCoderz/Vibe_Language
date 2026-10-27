using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

/// <summary>
/// The constructs that need a real rewrite rather than a one-to-one translation: struct copies,
/// LINQ query syntax, the <c>Enum</c> and <c>JsonSerializer</c> APIs that need a type's shape at
/// runtime, and C# 14's <c>field</c> keyword and null-conditional assignment.
/// </summary>
public sealed partial class JsEmitter
{
    // -------------------------------------------------------------------------------------------
    // Struct copies
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Emits an expression whose value is being stored, copying it when it is a struct read from
    /// a variable, field, property, or element. A fresh value — a constructor call, a method
    /// result — is already a copy nobody else holds, so it passes through untouched.
    /// </summary>
    private bool TryEmitStructCopy(ExpressionSyntax expression, ITypeSymbol? target)
    {
        var type = _ctx.TypeFor(expression);
        if (!IsMutableStruct(type)) return false;

        if (expression is ConditionalExpressionSyntax conditional)
        {
            EmitCondition(conditional.Condition);
            Write(" ? ");
            EmitExpressionAs(conditional.WhenTrue, target);
            Write(" : ");
            EmitExpressionAs(conditional.WhenFalse, target);
            return true;
        }

        if (!ReadsStorage(expression)) return false;

        if (IsNullableValue(type!))
        {
            Write("(");
            EmitExpression(expression);
            Write("?.$clone() ?? null)");
            return true;
        }

        EmitExpression(expression);
        Write(".$clone()");
        return true;
    }

    private bool ReadsStorage(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => ReadsStorage(parenthesized.Expression),
        ThisExpressionSyntax or ElementAccessExpressionSyntax => true,
        IdentifierNameSyntax or MemberAccessExpressionSyntax
            => _ctx.SymbolFor(expression) is ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol,
        _ => false,
    };

    /// <summary>Emits a value headed into storage with no particular declared type.</summary>
    private void EmitValue(ExpressionSyntax expression) => EmitExpressionAs(expression, null);

    // -------------------------------------------------------------------------------------------
    // C# 14: the field keyword and null-conditional assignment
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>field</c> inside a property accessor names the property's compiler-synthesised storage,
    /// which <see cref="EmitProperty"/> declared beside the accessors.
    /// </summary>
    private void EmitFieldKeyword(ExpressionSyntax expression)
    {
        var property = expression.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (property is null || _ctx.Model.GetDeclaredSymbol(property) is not { } symbol)
        {
            Write("undefined");
            return;
        }

        var owner = symbol.IsStatic ? _ctx.Names.TypeName(symbol.ContainingType) : (_thisAlias ?? "this");
        Write($"{owner}.{FieldKeywordStorage(symbol)}");
    }

    /// <summary>
    /// <c>a?.B = c</c> assigns only when <c>a</c> is not null. JavaScript has no optional-chained
    /// assignment, so the test is spelled out; the receiver is evaluated once.
    /// </summary>
    private bool TryEmitNullConditionalAssignment(ConditionalAccessExpressionSyntax conditional)
    {
        if (conditional.WhenNotNull is not AssignmentExpressionSyntax assignment) return false;

        var receiver = _ctx.NextTemporary("o");
        Write($"(({receiver}) => {receiver} == null ? null : (");
        Write(receiver);
        EmitAssignmentTarget(assignment.Left);
        Write($" {AssignmentOperator(assignment)} ");
        EmitExpressionAs(assignment.Right, _ctx.TypeFor(assignment.Left));
        Write("))(");
        EmitExpression(conditional.Expression);
        Write(")");
        return true;
    }

    /// <summary>The tail of a null-conditional assignment target: <c>.Name</c> or <c>[i]</c>.</summary>
    private void EmitAssignmentTarget(ExpressionSyntax left)
    {
        switch (left)
        {
            case MemberBindingExpressionSyntax binding:
                var symbol = _ctx.SymbolFor(binding);
                Write(".");
                Write(symbol is null ? NameResolver.Escape(binding.Name.Identifier.Text) : _ctx.Names.MemberName(symbol));
                return;

            case ElementBindingExpressionSyntax element:
                Write("[");
                WriteJoined(element.ArgumentList.Arguments, a => EmitExpression(a.Expression));
                Write("]");
                return;

            case MemberAccessExpressionSyntax access:
                EmitAssignmentTarget(access.Expression);
                Write(".");
                Write(_ctx.SymbolFor(access) is { } member ? _ctx.Names.MemberName(member) : NameResolver.Escape(access.Name.Identifier.Text));
                return;

            default:
                EmitExpression(left);
                return;
        }
    }

    private static string AssignmentOperator(AssignmentExpressionSyntax assignment) => assignment.Kind() switch
    {
        SyntaxKind.AddAssignmentExpression => "+=",
        SyntaxKind.SubtractAssignmentExpression => "-=",
        SyntaxKind.MultiplyAssignmentExpression => "*=",
        SyntaxKind.DivideAssignmentExpression => "/=",
        SyntaxKind.ModuloAssignmentExpression => "%=",
        SyntaxKind.AndAssignmentExpression => "&=",
        SyntaxKind.OrAssignmentExpression => "|=",
        SyntaxKind.ExclusiveOrAssignmentExpression => "^=",
        SyntaxKind.CoalesceAssignmentExpression => "??=",
        _ => "=",
    };

    // -------------------------------------------------------------------------------------------
    // C# 14: extension members
    // -------------------------------------------------------------------------------------------

    /// <summary>The receiver parameter of the extension block being emitted, for implicit uses.</summary>
    private string? _extensionReceiver;

    /// <summary>
    /// Emits an <c>extension(T receiver) { … }</c> block as the static methods the compiler
    /// itself lowers it to: each instance member takes the receiver as its first parameter, a
    /// property becomes <c>get_Name(receiver)</c>, and static members stay as they are.
    /// </summary>
    private void EmitExtensionBlock(ExtensionBlockDeclarationSyntax block)
    {
        if (_ctx.Model.GetDeclaredSymbol(block) is not INamedTypeSymbol { IsExtension: true } extension) return;

        var receiver = extension.ExtensionParameter is { } parameter ? NameResolver.Escape(parameter.Name) : null;
        var previousReceiver = _extensionReceiver;
        _extensionReceiver = receiver;

        foreach (var member in block.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method
                    when _ctx.Model.GetDeclaredSymbol(method) is { AssociatedExtensionImplementation: { } implementation } symbol:
                {
                    Out.WriteLine();
                    WriteDocComment(method);
                    Out.MapTo(method.Identifier.GetLocation());
                    Write($"static {(symbol.IsAsync ? "async " : "")}{(IsIterator(method) ? "*" : "")}{_ctx.Names.MemberName(implementation)}(");
                    var hasReceiver = !symbol.IsStatic && receiver is not null;
                    if (hasReceiver) Write(receiver!);
                    if (hasReceiver && method.ParameterList.Parameters.Any(p => !p.Modifiers.Any(SyntaxKind.OutKeyword))) Write(", ");
                    EmitParameterList(method.ParameterList);
                    Out.WriteLine(") {");
                    var previousIterator = _inIterator;
                    _inIterator = IsIterator(method);
                    Out.InBlock(() => EmitFunctionBody(symbol, method.Body, method.ExpressionBody));
                    _inIterator = previousIterator;
                    Out.WriteLine("}");
                    break;
                }

                case PropertyDeclarationSyntax property when _ctx.Model.GetDeclaredSymbol(property) is { } symbol:
                {
                    var hasReceiver = !symbol.IsStatic && receiver is not null;
                    var prefix = hasReceiver ? receiver! : "";

                    if (symbol.GetMethod?.AssociatedExtensionImplementation is { } getter)
                    {
                        Out.WriteLine();
                        WriteDocComment(property);
                        Out.MapTo(property.Identifier.GetLocation());
                        Write($"static {_ctx.Names.MemberName(getter)}({prefix}) ");
                        var accessor = property.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                        if (property.ExpressionBody is not null)
                        {
                            Write("{ return ");
                            EmitExpression(property.ExpressionBody.Expression);
                            Out.WriteLine("; }");
                        }
                        else if (accessor is not null) EmitAccessorBody(accessor, isGetter: true);
                        else Out.WriteLine("{ }");
                    }

                    if (symbol.SetMethod?.AssociatedExtensionImplementation is { } setter
                        && property.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) is { } setAccessor)
                    {
                        Out.WriteLine();
                        Write($"static {_ctx.Names.MemberName(setter)}({(hasReceiver ? prefix + ", " : "")}value) ");
                        EmitAccessorBody(setAccessor, isGetter: false);
                    }
                    break;
                }
            }
        }

        _extensionReceiver = previousReceiver;
    }

    /// <summary>
    /// Calls an extension member through its static implementation, moving the receiver into the
    /// first argument. An unqualified call inside the block itself uses the block's receiver.
    /// </summary>
    private bool TryEmitExtensionMemberInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.ContainingType is not { IsExtension: true }
            || symbol.OriginalDefinition.AssociatedExtensionImplementation is not { } implementation)
        {
            return false;
        }

        Write($"{ReferenceToType(implementation.ContainingType)}.{_ctx.Names.MemberName(implementation)}(");
        var hasReceiver = !symbol.IsStatic;
        if (hasReceiver) EmitExtensionReceiver(GetReceiver(invocation.Expression));
        if (hasReceiver && invocation.ArgumentList.Arguments.Count > 0) Write(", ");
        EmitArgumentList(invocation.ArgumentList.Arguments, symbol, skipOut: true);
        Write(")");
        return true;
    }

    /// <summary>Reads an extension property through its static getter.</summary>
    private bool TryEmitExtensionPropertyRead(ExpressionSyntax site, ISymbol? symbol, ExpressionSyntax? receiver)
    {
        if (symbol is not IPropertySymbol { ContainingType.IsExtension: true } property
            || property.OriginalDefinition.GetMethod?.AssociatedExtensionImplementation is not { } getter)
        {
            return false;
        }

        Write($"{ReferenceToType(getter.ContainingType)}.{_ctx.Names.MemberName(getter)}(");
        if (!property.IsStatic) EmitExtensionReceiver(receiver);
        Write(")");
        return true;
    }

    private void EmitExtensionReceiver(ExpressionSyntax? receiver)
    {
        if (receiver is not null) EmitExpression(receiver);
        else Write(_extensionReceiver ?? "undefined");
    }

    // -------------------------------------------------------------------------------------------
    // Enum APIs
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>ToString</c>, <c>HasFlag</c>, and the static <c>Enum</c> helpers. Each needs the enum's
    /// names or values, which the emitter knows at compile time and writes out directly.
    /// </summary>
    private bool TryEmitEnumInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol, string owner)
    {
        var receiver = GetReceiver(invocation.Expression);
        var receiverType = receiver is null ? null : _ctx.TypeFor(receiver);
        var arguments = invocation.ArgumentList.Arguments;

        if (receiverType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumReceiver && receiver is not null)
        {
            switch (symbol.Name)
            {
                case "ToString" when arguments.Count == 0:
                    EmitEnumName(enumReceiver, () => EmitExpression(receiver));
                    return true;

                case "HasFlag" when arguments.Count == 1:
                    Write("((");
                    EmitExpression(receiver);
                    Write(" & ");
                    EmitExpression(arguments[0].Expression);
                    Write(") === ");
                    EmitExpression(arguments[0].Expression);
                    Write(")");
                    return true;

                case "CompareTo" when arguments.Count == 1:
                    _ctx.UseHelper("csjsCompare");
                    Write("csjsCompare(");
                    EmitExpression(receiver);
                    Write(", ");
                    EmitExpression(arguments[0].Expression);
                    Write(")");
                    return true;

                case "Equals" when arguments.Count == 1:
                    Write("(");
                    EmitExpression(receiver);
                    Write(" === ");
                    EmitExpression(arguments[0].Expression);
                    Write(")");
                    return true;

                case "GetHashCode" when arguments.Count == 0:
                    EmitExpression(receiver);
                    return true;
            }
        }

        if (owner != "System.Enum") return false;

        // Enum.X<T>(...) names T as a type argument; Enum.X(typeof(T), ...) passes it as a value.
        var enumType = symbol.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        var valueArguments = arguments.ToList();
        if (enumType is null && valueArguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf)
        {
            enumType = _ctx.Model.GetTypeInfo(typeOf.Type).Type as INamedTypeSymbol;
            valueArguments.RemoveAt(0);
        }

        if (enumType is not { TypeKind: TypeKind.Enum }) return false;

        var members = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();
        string Values() => "[" + string.Join(", ", members.Select(m => Convert.ToInt64(m.ConstantValue)).Distinct().OrderBy(v => v)) + "]";
        string Names() => "[" + string.Join(", ", members.OrderBy(m => Convert.ToInt64(m.ConstantValue)).Select(m => QuoteString(m.Name))) + "]";
        string Map() => "{ " + string.Join(", ", members.Select(m => $"{m.Name}: {Convert.ToInt64(m.ConstantValue)}")) + " }";

        switch (symbol.Name)
        {
            case "GetValues":
            case "GetValuesAsUnderlyingType":
                Write(Values());
                return true;

            case "GetNames":
                Write(Names());
                return true;

            case "GetName" when valueArguments.Count == 1:
                Write("(");
                EmitEnumName(enumType, () => EmitExpression(valueArguments[0].Expression));
                Write(" ?? null)");
                return true;

            case "IsDefined" when valueArguments.Count == 1:
                Write($"{Values()}.includes(");
                EmitExpression(valueArguments[0].Expression);
                Write(")");
                return true;

            case "Parse" when valueArguments.Count is 1 or 2:
                _ctx.UseHelper("csjsEnumParse");
                Write($"csjsEnumParse({Map()}, ");
                EmitExpression(valueArguments[0].Expression);
                Write(", ");
                if (valueArguments.Count == 2) EmitExpression(valueArguments[1].Expression);
                else Write("false");
                Write($", {QuoteString(enumType.Name)})");
                return true;

            case "TryParse" when valueArguments.Count is 2 or 3:
            {
                var outArgument = valueArguments[^1];
                var parsed = _ctx.NextTemporary("e");
                _ctx.UseHelper("csjsEnumTryParse");
                Write($"(({parsed}) => {parsed} === null ? (");
                EmitOutTarget(outArgument);
                Write($" = 0, false) : (");
                EmitOutTarget(outArgument);
                Write($" = {parsed}, true))(csjsEnumTryParse({Map()}, ");
                EmitExpression(valueArguments[0].Expression);
                Write(", ");
                if (valueArguments.Count == 3) EmitExpression(valueArguments[1].Expression);
                else Write("false");
                Write("))");
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------------------------------------------
    // System.Text.Json
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>JsonSerializer.Serialize</c> and <c>Deserialize&lt;T&gt;</c>. Both carry a description of
    /// the type, built here from the semantic model, so the runtime can produce real class
    /// instances, Maps, and Dates rather than bare JSON objects — and can write computed
    /// properties that JSON.stringify would never see.
    /// </summary>
    private bool TryEmitJsonInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol, string owner)
    {
        if (owner != "System.Text.Json.JsonSerializer") return false;

        var ordered = OrderArguments(invocation.ArgumentList.Arguments, symbol);
        var arguments = ordered.Where(pair => pair.Argument.Expression is not null).ToList();

        switch (symbol.Name)
        {
            case "Serialize" when symbol.IsGenericMethod && arguments.Count >= 1:
            {
                _ctx.UseHelper("csjsJsonSerialize");
                Write("csjsJsonSerialize(");
                EmitExpression(arguments[0].Argument.Expression);
                Write($", {DescribeForJson(symbol.TypeArguments[0], 0)}");
                EmitJsonOptions(arguments.Skip(1));
                Write(")");
                return true;
            }

            case "Serialize" when !symbol.IsGenericMethod && arguments.Count >= 1:
            {
                // Serialize(object, Type, options): the static type is whatever the argument is.
                var valueType = _ctx.TypeFor(arguments[0].Argument.Expression);
                _ctx.UseHelper("csjsJsonSerialize");
                Write("csjsJsonSerialize(");
                EmitExpression(arguments[0].Argument.Expression);
                Write($", {DescribeForJson(valueType, 0)}");
                EmitJsonOptions(arguments.Skip(1).Where(a => a.Parameter?.Type.Name == "JsonSerializerOptions"));
                Write(")");
                return true;
            }

            case "Deserialize" when symbol.IsGenericMethod && arguments.Count >= 1
                                    && arguments[0].Parameter?.Type.SpecialType == SpecialType.System_String:
            {
                _ctx.UseHelper("csjsJsonDeserialize");
                Write("csjsJsonDeserialize(");
                EmitExpression(arguments[0].Argument.Expression);
                Write($", {DescribeForJson(symbol.TypeArguments[0], 0)}");
                EmitJsonOptions(arguments.Skip(1));
                Write(")");
                return true;
            }
        }

        return false;
    }

    private void EmitJsonOptions(IEnumerable<(ArgumentSyntax Argument, IParameterSymbol? Parameter)> rest)
    {
        var options = rest.FirstOrDefault(a => a.Parameter?.Type.Name == "JsonSerializerOptions");
        if (options.Argument?.Expression is null) return;
        Write(", ");
        EmitExpression(options.Argument.Expression);
    }

    /// <summary>
    /// The runtime's type description, as a JavaScript literal. See the JSON section of
    /// <c>csjs-runtime.js</c> for the shape. Recursion stops at a fixed depth, so a type that
    /// refers to itself describes a few levels and treats the rest as plain JSON.
    /// </summary>
    private string DescribeForJson(ITypeSymbol? type, int depth)
    {
        if (type is null || depth > 6) return "null";

        if (type is INamedTypeSymbol nullable && TypeMap.IsNullableValueType(nullable, out var underlying))
            return DescribeForJson(underlying, depth);

        if (type is IArrayTypeSymbol array)
            return $"{{ a: {DescribeForJson(array.ElementType, depth + 1)} }}";

        if (type is not INamedTypeSymbol named) return "null";

        switch (TypeMap.Unbound(named))
        {
            case "System.DateTime" or "System.DateTimeOffset":
                return "{ d: 1 }";

            case "System.Collections.Generic.List" or "System.Collections.Generic.IList"
                or "System.Collections.Generic.IEnumerable" or "System.Collections.Generic.IReadOnlyList"
                or "System.Collections.Generic.ICollection" or "System.Collections.Generic.IReadOnlyCollection"
                or "System.Collections.Generic.Queue" or "System.Collections.Generic.Stack":
                return named.TypeArguments.Length == 1 ? $"{{ a: {DescribeForJson(named.TypeArguments[0], depth + 1)} }}" : "{ a: null }";

            case "System.Collections.Generic.Dictionary" or "System.Collections.Generic.IDictionary"
                or "System.Collections.Generic.IReadOnlyDictionary":
                return named.TypeArguments.Length == 2 ? $"{{ m: {DescribeForJson(named.TypeArguments[1], depth + 1)} }}" : "{ m: null }";

            case "System.Collections.Generic.HashSet" or "System.Collections.Generic.ISet":
                return named.TypeArguments.Length == 1 ? $"{{ s: {DescribeForJson(named.TypeArguments[0], depth + 1)} }}" : "{ s: null }";
        }

        // Only types this program declares have a constructor and properties worth describing.
        // An anonymous type is emitted as an object literal whose keys are already the property
        // names, and it has no constructor to name — it is plain JSON as it stands.
        if (named.DeclaringSyntaxReferences.Length == 0 || named.IsAnonymousType || named.IsTupleType
            || named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return "null";
        if (HasAttribute(named, "JsObjectLiteralAttribute")) return "null";

        var properties = new List<string>();
        for (var current = named; current is not null && current.DeclaringSyntaxReferences.Length > 0; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || property.GetMethod is null) continue;
                if (property.DeclaredAccessibility != Accessibility.Public) continue;
                if (property.Name == "EqualityContract") continue;
                if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonIgnoreAttribute")) continue;

                var jsonName = property.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "JsonPropertyNameAttribute")?
                    .ConstructorArguments.FirstOrDefault().Value as string;
                var readOnly = property.SetMethod is null || property.SetMethod.DeclaredAccessibility != Accessibility.Public;

                properties.Add($"[{QuoteString(_ctx.Names.MemberName(property))}, {DescribeForJson(property.Type, depth + 1)}, " +
                               $"{(jsonName is null ? "null" : QuoteString(jsonName))}, {(readOnly ? 1 : 0)}]");
            }
        }

        return $"{{ c: {ReferenceToType(named)}, p: [{string.Join(", ", properties)}] }}";
    }

    // -------------------------------------------------------------------------------------------
    // LINQ query syntax
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Lowers LINQ query syntax onto the same operations method syntax uses.
    /// <para>
    /// Once a query has more than one range variable in scope — after a second <c>from</c>, a
    /// <c>let</c>, or a <c>join</c> — each element becomes an object carrying all of them, which is
    /// what the C# compiler's transparent identifiers are. Every lambda then destructures that
    /// object, so <c>({ a, b }) =&gt; a.X + b.Y</c> reads the same as the query did.
    /// </para>
    /// <para>
    /// Ordering has to wrap the chain built so far rather than append to it, because
    /// <c>csjsOrderBy</c> takes the sequence as its first argument. Emission therefore runs
    /// innermost-outward: each clause is a function that emits its source before wrapping it.
    /// </para>
    /// </summary>
    private void EmitQuery(QueryExpressionSyntax query)
    {
        var from = query.FromClause;
        var vars = new List<string> { NameResolver.Escape(from.Identifier.Text) };
        var sourceIsArray = ProducesJsArray(from.Expression);

        Action source = () =>
        {
            if (sourceIsArray) EmitExpression(from.Expression);
            else
            {
                Write("[...");
                EmitExpression(from.Expression);
                Write("]");
            }
        };

        EmitQueryBody(source, vars, query.Body);
    }

    private static string QueryParameter(IReadOnlyList<string> vars)
        => vars.Count == 1 ? $"({vars[0]})" : $"({{ {string.Join(", ", vars)} }})";

    private static string QueryParameterPattern(IReadOnlyList<string> vars)
        => vars.Count == 1 ? vars[0] : $"{{ {string.Join(", ", vars)} }}";

    private void EmitQueryBody(Action source, List<string> vars, QueryBodySyntax body)
    {
        foreach (var clause in body.Clauses)
        {
            var inner = source;
            var scope = vars.ToList();
            var parameter = QueryParameter(scope);

            switch (clause)
            {
                case WhereClauseSyntax where:
                    source = () =>
                    {
                        inner();
                        Write($".filter({parameter} => ");
                        EmitExpression(where.Condition);
                        Write(")");
                    };
                    break;

                case LetClauseSyntax let:
                {
                    var name = NameResolver.Escape(let.Identifier.Text);
                    source = () =>
                    {
                        inner();
                        Write($".map({parameter} => ({{ {string.Join(", ", scope)}, {name}: ");
                        EmitExpression(let.Expression);
                        Write(" }))");
                    };
                    vars.Add(name);
                    break;
                }

                case FromClauseSyntax from:
                {
                    var name = NameResolver.Escape(from.Identifier.Text);
                    source = () =>
                    {
                        _ctx.UseHelper("csjsSelectMany");
                        Write("csjsSelectMany(");
                        inner();
                        Write($", {parameter} => ");
                        EmitExpression(from.Expression);
                        Write($", ({QueryParameterPattern(scope)}, {name}) => ({{ {string.Join(", ", scope)}, {name} }}))");
                    };
                    vars.Add(name);
                    break;
                }

                case JoinClauseSyntax join:
                {
                    var name = NameResolver.Escape(join.Identifier.Text);
                    var into = join.Into is null ? null : NameResolver.Escape(join.Into.Identifier.Text);
                    var helper = into is null ? "csjsInnerJoin" : "csjsGroupJoin";
                    var added = into ?? name;

                    source = () =>
                    {
                        _ctx.UseHelper(helper);
                        Write($"{helper}(");
                        inner();
                        Write(", ");
                        EmitExpression(join.InExpression);
                        Write($", {parameter} => ");
                        EmitExpression(join.LeftExpression);
                        Write($", ({name}) => ");
                        EmitExpression(join.RightExpression);
                        Write($", ({QueryParameterPattern(scope)}, {added}) => ({{ {string.Join(", ", scope)}, {added} }}))");
                    };
                    vars.Add(added);
                    break;
                }

                case OrderByClauseSyntax orderBy:
                {
                    var first = true;
                    foreach (var ordering in orderBy.Orderings)
                    {
                        var before = source;
                        var helper = first ? "csjsOrderBy" : "csjsThenBy";
                        first = false;
                        var descending = ordering.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword);
                        source = () =>
                        {
                            _ctx.UseHelper(helper);
                            Write($"{helper}(");
                            before();
                            Write($", {parameter} => ");
                            EmitExpression(ordering.Expression);
                            Write($", {(descending ? "true" : "false")})");
                        };
                    }
                    break;
                }

                default:
                    _ctx.Error(DiagnosticIds.UnsupportedSyntax,
                        $"Query clause `{clause.Kind()}` is not supported. Rewrite the query using method syntax.", clause);
                    break;
            }
        }

        var finalParameter = QueryParameter(vars);
        Action result;

        switch (body.SelectOrGroup)
        {
            case SelectClauseSyntax select:
            {
                var before = source;
                // `select x` where x is the only range variable is an identity worth dropping.
                var identity = vars.Count == 1 && select.Expression is IdentifierNameSyntax id
                               && NameResolver.Escape(id.Identifier.Text) == vars[0];
                result = identity
                    ? before
                    : () =>
                    {
                        before();
                        Write($".map({finalParameter} => ");
                        EmitQueryProjection(select.Expression);
                        Write(")");
                    };
                break;
            }

            case GroupClauseSyntax group:
            {
                var before = source;
                var identity = vars.Count == 1 && group.GroupExpression is IdentifierNameSyntax id
                               && NameResolver.Escape(id.Identifier.Text) == vars[0];
                result = () =>
                {
                    _ctx.UseHelper("csjsGroupBy");
                    Write("csjsGroupBy(");
                    before();
                    Write($", {finalParameter} => ");
                    EmitExpression(group.ByExpression);
                    if (!identity)
                    {
                        Write($", {finalParameter} => ");
                        EmitQueryProjection(group.GroupExpression);
                    }
                    Write(")");
                };
                break;
            }

            default:
                result = source;
                break;
        }

        // `select … into g` / `group … into g` continues the query with g as the only variable.
        if (body.Continuation is { } continuation)
        {
            EmitQueryBody(result, new List<string> { NameResolver.Escape(continuation.Identifier.Text) }, continuation.Body);
            return;
        }

        result();
    }

    /// <summary>An object literal in arrow-body position needs parentheses, or it reads as a block.</summary>
    private void EmitQueryProjection(ExpressionSyntax expression)
    {
        var needsParens = expression is AnonymousObjectCreationExpressionSyntax;
        if (needsParens) Write("(");
        EmitValue(expression);
        if (needsParens) Write(")");
    }
}
