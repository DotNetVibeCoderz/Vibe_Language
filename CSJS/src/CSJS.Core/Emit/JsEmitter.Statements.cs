using CSJS.Core.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.Core.Emit;

public sealed partial class JsEmitter
{
    private void EmitStatement(StatementSyntax statement)
    {
        Out.MapTo(statement.GetLocation());

        switch (statement)
        {
            case BlockSyntax block: EmitBlock(block); break;
            case LocalDeclarationStatementSyntax local: EmitLocalDeclaration(local); break;
            case ExpressionStatementSyntax expr: EmitExpressionStatement(expr); break;
            case IfStatementSyntax ifStatement: EmitIf(ifStatement); break;
            case WhileStatementSyntax whileStatement: EmitWhile(whileStatement); break;
            case DoStatementSyntax doStatement: EmitDo(doStatement); break;
            case ForStatementSyntax forStatement: EmitFor(forStatement); break;
            case ForEachStatementSyntax forEach: EmitForEach(forEach); break;
            case ForEachVariableStatementSyntax forEachVar: EmitForEachVariable(forEachVar); break;
            case SwitchStatementSyntax switchStatement: EmitSwitch(switchStatement); break;
            case ReturnStatementSyntax returnStatement: EmitReturn(returnStatement); break;
            case BreakStatementSyntax: Out.WriteLine("break;"); break;
            case ContinueStatementSyntax: Out.WriteLine("continue;"); break;
            case ThrowStatementSyntax throwStatement: EmitThrow(throwStatement); break;
            case TryStatementSyntax tryStatement: EmitTry(tryStatement); break;
            case UsingStatementSyntax usingStatement: EmitUsing(usingStatement); break;
            case YieldStatementSyntax yieldStatement: EmitYield(yieldStatement); break;
            case LocalFunctionStatementSyntax localFunction: EmitLocalFunction(localFunction); break;
            case LabeledStatementSyntax labeled: EmitLabeled(labeled); break;
            case EmptyStatementSyntax: Out.WriteLine(";"); break;

            case LockStatementSyntax lockStatement:
                // JavaScript is single-threaded; the lock has no meaning but the body still runs.
                _ctx.Warn(DiagnosticIds.LockIgnored,
                    "`lock` has no effect in JavaScript. The body is emitted as a plain block.", lockStatement);
                EmitStatement(lockStatement.Statement);
                break;

            case CheckedStatementSyntax checkedStatement:
                EmitStatement(checkedStatement.Block);
                break;

            case UnsafeStatementSyntax or FixedStatementSyntax:
                _ctx.Error(DiagnosticIds.PointerUnsupported,
                    "Pointers and unsafe code cannot be transpiled to JavaScript.", statement);
                break;

            default:
                _ctx.Error(DiagnosticIds.UnsupportedSyntax,
                    $"Unsupported statement: {statement.Kind()}.", statement);
                break;
        }
    }

    private void EmitBlock(BlockSyntax block)
    {
        EmitBlockInline(block);
        Out.WriteLine();
    }

    /// <summary>
    /// Emits a block without terminating the line, so a caller can continue on the same line.
    /// Lambda bodies need this: the call's closing paren has to follow the closing brace.
    /// </summary>
    private void EmitBlockInline(BlockSyntax block)
    {
        Out.WriteLine("{");
        Out.InBlock(() =>
        {
            DeclareHoistedVariables(block);
            foreach (var statement in block.Statements)
                EmitStatement(statement);
        });
        Write("}");
    }

    /// <summary>
    /// Emits a statement in a position that expects one, wrapping bare statements in braces.
    /// C# allows <c>if (x) DoThing();</c>; always bracing avoids the dangling-else hazard.
    /// </summary>
    private void EmitEmbeddedStatement(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            EmitBlock(block);
            return;
        }

        Out.WriteLine("{");
        Out.InBlock(() => EmitStatement(statement));
        Out.WriteLine("}");
    }

    /// <summary>
    /// Hoists variables that C# introduces mid-expression to the top of the block.
    /// <para>
    /// Both <c>out var x</c> and pattern designations (<c>if (o is Foo f)</c>) declare a binding
    /// inside an expression, which JavaScript has no syntax for. The emitter rewrites those into
    /// plain assignments, and those assignments need a binding that already exists.
    /// </para>
    /// </summary>
    private void DeclareHoistedVariables(SyntaxNode scope)
    {
        bool Descend(SyntaxNode node) =>
            node == scope || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BlockSyntax);

        var nodes = scope.DescendantNodes(descendIntoChildren: Descend).ToList();

        var fromOutArguments = nodes
            .OfType<DeclarationExpressionSyntax>()
            .Where(d => d.Parent is ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword })
            .Select(d => d.Designation);

        var fromPatterns = nodes
            .OfType<PatternSyntax>()
            .Select(p => p switch
            {
                DeclarationPatternSyntax declaration => declaration.Designation,
                VarPatternSyntax varPattern => varPattern.Designation,
                RecursivePatternSyntax recursive => recursive.Designation,
                _ => null,
            })
            .Where(d => d is not null)!;

        var names = fromOutArguments.Concat(fromPatterns!)
            .OfType<SingleVariableDesignationSyntax>()
            .Select(d => NameResolver.Escape(d.Identifier.Text))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Emitted modules are ESM and therefore strict, so an undeclared assignment is a
        // ReferenceError rather than an implicit global.
        if (names.Count == 0) return;

        foreach (var name in names)
            Out.WriteLine($"let {name};");
    }

    private void EmitLocalDeclaration(LocalDeclarationStatementSyntax declaration)
    {
        foreach (var variable in declaration.Declaration.Variables)
        {
            // `const` communicates intent and lets bundlers reason about the binding, so it is
            // used whenever the variable is initialised and never written again.
            var isConst = variable.Initializer is not null
                          && (declaration.IsConst || !IsReassigned(variable));

            Out.MapTo(variable.GetLocation());
            Write($"{(isConst ? "const" : "let")} {NameResolver.Escape(variable.Identifier.Text)}");

            if (variable.Initializer is not null)
            {
                Write(" = ");
                var target = _ctx.Model.GetDeclaredSymbol(variable) as ILocalSymbol;
                EmitExpressionAs(variable.Initializer.Value, target?.Type);
            }

            Out.WriteLine(";");
        }
    }

    private bool IsReassigned(VariableDeclaratorSyntax variable)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(variable);
        if (symbol is null) return true;

        var scope = variable.Ancestors().OfType<BlockSyntax>().FirstOrDefault()
                    ?? variable.Ancestors().OfType<SyntaxNode>().LastOrDefault();
        if (scope is null) return true;

        foreach (var node in scope.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment
                    when SymbolEqualityComparer.Default.Equals(_ctx.SymbolFor(assignment.Left), symbol):
                    return true;

                case PrefixUnaryExpressionSyntax prefix
                    when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                    if (SymbolEqualityComparer.Default.Equals(_ctx.SymbolFor(prefix.Operand), symbol)) return true;
                    break;

                case PostfixUnaryExpressionSyntax postfix:
                    if (SymbolEqualityComparer.Default.Equals(_ctx.SymbolFor(postfix.Operand), symbol)) return true;
                    break;

                case ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword or (int)SyntaxKind.RefKeyword } arg
                    when SymbolEqualityComparer.Default.Equals(_ctx.SymbolFor(arg.Expression), symbol):
                    return true;
            }
        }

        return false;
    }

    private void EmitExpressionStatement(ExpressionStatementSyntax statement)
    {
        // `var (a, b) = tuple;` is an assignment in C#'s grammar, but the left side declares
        // new variables, so JavaScript needs a declaration keyword in front of the pattern.
        if (statement.Expression is AssignmentExpressionSyntax
            { Left: DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax } } deconstruction)
        {
            Write("const ");
            EmitExpression(deconstruction.Left);
            Write(" = ");
            EmitExpression(deconstruction.Right);
            Out.WriteLine(";");
            return;
        }

        EmitExpression(statement.Expression);
        Out.WriteLine(";");
    }

    private void EmitIf(IfStatementSyntax statement)
    {
        Write("if (");
        EmitCondition(statement.Condition);
        Write(") ");
        EmitEmbeddedStatement(statement.Statement);

        if (statement.Else is null) return;

        if (statement.Else.Statement is IfStatementSyntax nested)
        {
            Write("else ");
            EmitIf(nested);
            return;
        }

        Write("else ");
        EmitEmbeddedStatement(statement.Else.Statement);
    }

    private void EmitWhile(WhileStatementSyntax statement)
    {
        Write("while (");
        EmitCondition(statement.Condition);
        Write(") ");
        EmitEmbeddedStatement(statement.Statement);
    }

    private void EmitDo(DoStatementSyntax statement)
    {
        Write("do ");
        EmitEmbeddedStatement(statement.Statement);
        Write("while (");
        EmitCondition(statement.Condition);
        Out.WriteLine(");");
    }

    private void EmitFor(ForStatementSyntax statement)
    {
        Write("for (");

        if (statement.Declaration is not null)
        {
            Write("let ");
            WriteJoined(statement.Declaration.Variables, variable =>
            {
                Write(NameResolver.Escape(variable.Identifier.Text));
                if (variable.Initializer is null) return;
                Write(" = ");
                EmitExpression(variable.Initializer.Value);
            });
        }
        else
        {
            WriteJoined(statement.Initializers, EmitExpression);
        }

        Write("; ");
        if (statement.Condition is not null) EmitCondition(statement.Condition);
        Write("; ");
        WriteJoined(statement.Incrementors, EmitExpression);
        Write(") ");
        EmitEmbeddedStatement(statement.Statement);
    }

    private void EmitForEach(ForEachStatementSyntax statement)
    {
        var name = NameResolver.Escape(statement.Identifier.Text);
        Write($"for (const {name} of ");
        EmitEnumerableSource(statement.Expression);
        Write(") ");
        EmitEmbeddedStatement(statement.Statement);
    }

    private void EmitForEachVariable(ForEachVariableStatementSyntax statement)
    {
        // `foreach (var (a, b) in pairs)` destructures directly, which is exactly how
        // Map iteration and tuple arrays already arrive on the JavaScript side.
        Write("for (const ");
        EmitExpression(statement.Variable);
        Write(" of ");
        EmitEnumerableSource(statement.Expression);
        Write(") ");
        EmitEmbeddedStatement(statement.Statement);
    }

    /// <summary>
    /// Emits the source of a for..of loop. Dictionaries iterate as <c>[key, value]</c> pairs,
    /// which is why KeyValuePair.Key/.Value map to index 0 and 1.
    /// </summary>
    private void EmitEnumerableSource(ExpressionSyntax expression)
    {
        var type = _ctx.TypeFor(expression);

        // A string iterates by code point in JavaScript but by UTF-16 char in C#. Neither is
        // wrong, but char-by-char is what C# code expects, so keep the indexable form.
        if (type?.SpecialType == SpecialType.System_String)
        {
            EmitExpression(expression);
            return;
        }

        EmitExpression(expression);
    }

    private void EmitSwitch(SwitchStatementSyntax statement)
    {
        // A switch whose sections use patterns cannot become a JavaScript switch, because
        // JavaScript compares with ===. Those become an if/else chain instead.
        if (statement.Sections.SelectMany(s => s.Labels).Any(l => l is CasePatternSwitchLabelSyntax))
        {
            EmitPatternSwitch(statement);
            return;
        }

        Write("switch (");
        EmitExpression(statement.Expression);
        Out.WriteLine(") {");

        Out.InBlock(() =>
        {
            foreach (var section in statement.Sections)
            {
                foreach (var label in section.Labels)
                {
                    switch (label)
                    {
                        case CaseSwitchLabelSyntax caseLabel:
                            Write("case ");
                            EmitExpression(caseLabel.Value);
                            Out.WriteLine(":");
                            break;
                        case DefaultSwitchLabelSyntax:
                            Out.WriteLine("default:");
                            break;
                    }
                }

                Out.InBlock(() =>
                {
                    foreach (var sectionStatement in section.Statements)
                        EmitStatement(sectionStatement);

                    // C# forbids fallthrough, so every section ends in break/return/throw.
                    // If the last statement is not one of those, add the break JavaScript needs.
                    var last = section.Statements.LastOrDefault();
                    if (last is not (BreakStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax
                        or ContinueStatementSyntax or GotoStatementSyntax))
                    {
                        Out.WriteLine("break;");
                    }
                });
            }
        });

        Out.WriteLine("}");
    }

    private void EmitPatternSwitch(SwitchStatementSyntax statement)
    {
        var subject = _ctx.NextTemporary("switch");
        Write($"const {subject} = ");
        EmitExpression(statement.Expression);
        Out.WriteLine(";");

        var first = true;
        SwitchSectionSyntax? defaultSection = null;

        foreach (var section in statement.Sections)
        {
            if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
            {
                defaultSection = section;
                continue;
            }

            Write(first ? "if (" : "else if (");
            first = false;

            WriteJoined(section.Labels, label =>
            {
                switch (label)
                {
                    case CasePatternSwitchLabelSyntax patternLabel:
                        EmitPatternTest(subject, patternLabel.Pattern);
                        if (patternLabel.WhenClause is not null)
                        {
                            Write(" && ");
                            EmitCondition(patternLabel.WhenClause.Condition);
                        }
                        break;
                    case CaseSwitchLabelSyntax caseLabel:
                        Write($"{subject} === ");
                        EmitExpression(caseLabel.Value);
                        break;
                }
            }, " || ");

            Out.WriteLine(") {");
            Out.InBlock(() => EmitSectionStatements(section));
            Out.WriteLine("}");
        }

        if (defaultSection is null) return;

        Write(first ? "" : "else ");
        Out.WriteLine("{");
        Out.InBlock(() => EmitSectionStatements(defaultSection));
        Out.WriteLine("}");
    }

    private void EmitSectionStatements(SwitchSectionSyntax section)
    {
        foreach (var statement in section.Statements)
        {
            // `break` inside the rewritten if/else chain would escape the wrong construct.
            if (statement is BreakStatementSyntax) continue;
            EmitStatement(statement);
        }
    }

    private void EmitReturn(ReturnStatementSyntax statement)
    {
        // Inside a function with ref/out parameters, every exit hands those values back too.
        var tail = _byRefReturn is { Count: > 0 } byRef ? ", " + string.Join(", ", byRef) : null;

        if (statement.Expression is null)
        {
            Out.WriteLine(tail is null ? "return;" : $"return [undefined{tail}];");
            return;
        }

        Write(tail is null ? "return " : "return [");
        EmitExpressionAs(statement.Expression, UnwrapTask(ReturnTypeOf(statement)));
        Out.WriteLine(tail is null ? ";" : $"{tail}];");
    }

    /// <summary>The declared return type of whatever function a return statement exits.</summary>
    private ITypeSymbol? ReturnTypeOf(ReturnStatementSyntax statement)
    {
        foreach (var ancestor in statement.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    return (_ctx.Model.GetSymbolInfo(lambda).Symbol as IMethodSymbol)?.ReturnType;
                case LocalFunctionStatementSyntax local:
                    return (_ctx.Model.GetDeclaredSymbol(local) as IMethodSymbol)?.ReturnType;
                case BaseMethodDeclarationSyntax or AccessorDeclarationSyntax:
                    return (_ctx.Model.GetDeclaredSymbol(ancestor) as IMethodSymbol)?.ReturnType;
                case PropertyDeclarationSyntax or IndexerDeclarationSyntax:
                    return (_ctx.Model.GetDeclaredSymbol(ancestor) as IPropertySymbol)?.Type;
            }
        }
        return null;
    }

    private static ITypeSymbol? UnwrapTask(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { TypeArguments.Length: 1 } named
            && TypeMap.Unbound(named) is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
        {
            return named.TypeArguments[0];
        }
        return type;
    }

    private void EmitThrow(ThrowStatementSyntax statement)
    {
        if (statement.Expression is null)
        {
            // Bare `throw;` rethrows the caught exception.
            Out.WriteLine($"throw {_currentCatchVariable ?? "$error"};");
            return;
        }

        Write("throw ");
        EmitExpression(statement.Expression);
        Out.WriteLine(";");
    }

    private string? _currentCatchVariable;

    private void EmitTry(TryStatementSyntax statement)
    {
        Write("try ");
        EmitBlock(statement.Block);

        var catches = statement.Catches;
        if (catches.Count > 0)
        {
            var variable = catches
                .Select(c => c.Declaration?.Identifier.Text)
                .FirstOrDefault(t => !string.IsNullOrEmpty(t)) is { } named
                ? NameResolver.Escape(named)
                : _ctx.NextTemporary("error");

            Write($"catch ({variable}) ");
            Out.WriteLine("{");

            var previous = _currentCatchVariable;
            _currentCatchVariable = variable;

            Out.InBlock(() =>
            {
                // JavaScript has one catch clause, so typed catches become a filter chain.
                var typed = catches.Where(c => c.Declaration?.Type is not null && !IsCatchAll(c)).ToList();
                var catchAll = catches.FirstOrDefault(IsCatchAll);

                var first = true;
                foreach (var clause in typed)
                {
                    Write(first ? "if (" : "else if (");
                    first = false;
                    EmitExceptionTest(variable, clause);
                    Out.WriteLine(") {");
                    Out.InBlock(() => EmitCatchBody(clause, variable));
                    Out.WriteLine("}");
                }

                if (catchAll is not null)
                {
                    if (!first) Write("else ");
                    Out.WriteLine("{");
                    Out.InBlock(() => EmitCatchBody(catchAll, variable));
                    Out.WriteLine("}");
                }
                else if (!first)
                {
                    // No catch-all: anything the filters missed must keep propagating.
                    Out.WriteLine($"else {{ throw {variable}; }}");
                }
            });

            _currentCatchVariable = previous;
            Out.WriteLine("}");
        }

        if (statement.Finally is not null)
        {
            Write("finally ");
            EmitBlock(statement.Finally.Block);
        }
    }

    private void EmitCatchBody(CatchClauseSyntax clause, string variable)
    {
        var declared = clause.Declaration?.Identifier.Text;
        if (!string.IsNullOrEmpty(declared) && NameResolver.Escape(declared) != variable)
            Out.WriteLine($"const {NameResolver.Escape(declared)} = {variable};");

        foreach (var statement in clause.Block.Statements)
            EmitStatement(statement);
    }

    private bool IsCatchAll(CatchClauseSyntax clause)
    {
        // A clause with a `when` filter never catches unconditionally, whatever its type.
        if (clause.Filter is not null) return false;
        if (clause.Declaration?.Type is null) return true;
        var type = _ctx.Model.GetTypeInfo(clause.Declaration.Type).Type;
        return type?.ToDisplayString() == "System.Exception";
    }

    private void EmitExceptionTest(string variable, CatchClauseSyntax clause)
    {
        var type = clause.Declaration?.Type is null
            ? null
            : _ctx.Model.GetTypeInfo(clause.Declaration.Type).Type as INamedTypeSymbol;

        if (type is not null && JsEmitter.IsFrameworkException(type))
        {
            // Framework exceptions all collapse onto Error, so the C# type travels on the value.
            _ctx.UseHelper("csjsCatches");
            Write($"csjsCatches({variable}, {QuoteString(type.Name)})");
        }
        else if (type is not null)
        {
            // A user-declared exception is a real class extending Error.
            Write($"{variable} instanceof {ReferenceToType(type)}");
        }
        else
        {
            Write($"{variable} instanceof Error");
        }

        if (clause.Filter is not null)
        {
            Write(" && ");
            EmitCondition(clause.Filter.FilterExpression);
        }
    }

    private void EmitUsing(UsingStatementSyntax statement)
    {
        // `using` is try/finally plus Dispose. JavaScript's `using` declaration is too new to
        // assume, so the desugared form is emitted instead.
        var resources = new List<string>();

        Out.WriteLine("{");
        Out.InBlock(() =>
        {
            if (statement.Declaration is not null)
            {
                foreach (var variable in statement.Declaration.Variables)
                {
                    var name = NameResolver.Escape(variable.Identifier.Text);
                    resources.Add(name);
                    Write($"const {name} = ");
                    if (variable.Initializer is not null) EmitExpression(variable.Initializer.Value);
                    else Write("null");
                    Out.WriteLine(";");
                }
            }
            else if (statement.Expression is not null)
            {
                var name = _ctx.NextTemporary("resource");
                resources.Add(name);
                Write($"const {name} = ");
                EmitExpression(statement.Expression);
                Out.WriteLine(";");
            }

            Write("try ");
            EmitEmbeddedStatement(statement.Statement);
            Out.WriteLine("finally {");
            Out.InBlock(() =>
            {
                foreach (var resource in Enumerable.Reverse(resources))
                    Out.WriteLine($"{resource}?.Dispose?.();");
            });
            Out.WriteLine("}");
        });
        Out.WriteLine("}");
    }

    private void EmitYield(YieldStatementSyntax statement)
    {
        if (statement.IsKind(SyntaxKind.YieldBreakStatement))
        {
            Out.WriteLine("return;");
            return;
        }

        Write("yield ");
        if (statement.Expression is not null) EmitValue(statement.Expression);
        Out.WriteLine(";");
    }

    private void EmitLocalFunction(LocalFunctionStatementSyntax function)
    {
        var symbol = _ctx.Model.GetDeclaredSymbol(function);
        var isIterator = function.Body is not null
                         && function.Body.DescendantNodes().Any(n => n is YieldStatementSyntax);

        if (symbol?.IsAsync == true) Write("async ");
        Write("function");
        if (isIterator) Write("*");
        Write($" {NameResolver.Escape(function.Identifier.Text)}(");
        EmitParameterList(function.ParameterList);
        Out.WriteLine(") {");

        var previous = _inIterator;
        _inIterator = isIterator;
        Out.InBlock(() =>
        {
            if (symbol is not null) EmitFunctionBody(symbol, function.Body, function.ExpressionBody);
            else EmitStatementsOf(function.Body, function.ExpressionBody);
        });
        _inIterator = previous;

        Out.WriteLine("}");
    }

    private void EmitLabeled(LabeledStatementSyntax statement)
    {
        Out.WriteLine($"{NameResolver.Escape(statement.Identifier.Text)}:");
        EmitStatement(statement.Statement);
    }

    /// <summary>
    /// Emits an expression used as a condition, adding a truthiness guard when the C# value is
    /// not already boolean. C# only accepts bool here, but a mapped member can yield a
    /// JavaScript value whose falsiness differs from C#'s (0 and "" are notable).
    /// </summary>
    private void EmitCondition(ExpressionSyntax condition) => EmitExpression(condition);
}
