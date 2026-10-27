using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSJS.LanguageService;

/// <summary>
/// Signature help from the semantic model.
/// <para>
/// Roslyn's own signature-help service is internal to its IDE layer, so this walks to the
/// enclosing argument list and asks the model for the member group instead. That is all the
/// service does too; what it adds over this is presentation.
/// </para>
/// </summary>
internal static class SignatureHelpBuilder
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    public static SignatureHelpResult? Build(SemanticModel model, int position, CancellationToken cancellationToken)
    {
        var root = model.SyntaxTree.GetRoot(cancellationToken);
        // The character before the caret decides where it is: just after `(` or `,` the caret
        // sits on whatever token follows, which may be outside the call entirely.
        var token = root.FindToken(Math.Clamp(position - 1, 0, Math.Max(0, root.FullSpan.End - 1)));

        foreach (var node in token.Parent?.AncestorsAndSelf() ?? Enumerable.Empty<SyntaxNode>())
        {
            // A call never encloses a member, type, or lambda body boundary it started outside of.
            if (node is MemberDeclarationSyntax or BlockSyntax) break;

            switch (node)
            {
                case ArgumentListSyntax list when Inside(list.OpenParenToken, list.CloseParenToken, position):
                    return ForArguments(model, list.Parent, list.Arguments, position, cancellationToken);

                case AttributeArgumentListSyntax list when Inside(list.OpenParenToken, list.CloseParenToken, position):
                    return ForArguments(model, list.Parent, list.Arguments, position, cancellationToken);
            }
        }

        return null;
    }

    private static bool Inside(SyntaxToken open, SyntaxToken close, int position)
        => position > open.SpanStart && (close.IsMissing || position <= close.SpanStart);

    private static SignatureHelpResult? ForArguments<T>(
        SemanticModel model,
        SyntaxNode? owner,
        SeparatedSyntaxList<T> arguments,
        int position,
        CancellationToken cancellationToken)
        where T : SyntaxNode
    {
        if (owner is null) return null;

        IEnumerable<IMethodSymbol> candidates = owner switch
        {
            InvocationExpressionSyntax invocation =>
                model.GetMemberGroup(invocation.Expression, cancellationToken).OfType<IMethodSymbol>(),
            BaseObjectCreationExpressionSyntax creation =>
                Constructors(model, model.GetTypeInfo(creation, cancellationToken).Type, creation.SpanStart),
            ConstructorInitializerSyntax initializer =>
                Constructors(model, model.GetSymbolInfo(initializer, cancellationToken) is { } info
                    ? (info.Symbol ?? info.CandidateSymbols.FirstOrDefault())?.ContainingType
                    : null, initializer.SpanStart),
            AttributeSyntax attribute =>
                Constructors(model, model.GetTypeInfo(attribute, cancellationToken).Type, attribute.SpanStart),
            PrimaryConstructorBaseTypeSyntax baseType =>
                Constructors(model, model.GetTypeInfo(baseType.Type, cancellationToken).Type, baseType.SpanStart),
            _ => Enumerable.Empty<IMethodSymbol>(),
        };

        var methods = candidates
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            .ToList();
        if (methods.Count == 0) return null;

        var activeParameter = arguments.GetSeparators().Count(s => s.SpanStart < position);

        // Prefer what the compiler actually bound. Mid-edit the call rarely binds, and then the
        // first overload with room for the argument being typed is the useful guess.
        var bound = model.GetSymbolInfo(owner, cancellationToken).Symbol as IMethodSymbol;

        var activeSignature = bound is null ? -1 : methods.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, bound)
                                                                       || SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, bound.OriginalDefinition));
        if (activeSignature < 0)
            activeSignature = Math.Max(0, methods.FindIndex(m => m.Parameters.Length > activeParameter || IsParams(m)));

        var signatures = methods.Select(m => Describe(m, model, position)).ToList();
        return new SignatureHelpResult(signatures, activeSignature, activeParameter);
    }

    private static bool IsParams(IMethodSymbol method)
        => method.Parameters.Length > 0 && method.Parameters[^1].IsParams;

    private static IEnumerable<IMethodSymbol> Constructors(SemanticModel model, ITypeSymbol? type, int position)
        => type is INamedTypeSymbol named
            ? named.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length == 0)
                .Where(c => model.IsAccessible(position, c))
            : Enumerable.Empty<IMethodSymbol>();

    private static SignatureEntry Describe(IMethodSymbol method, SemanticModel model, int position)
    {
        var label = new StringBuilder();
        if (method.MethodKind == MethodKind.Constructor)
        {
            label.Append(method.ContainingType.ToMinimalDisplayString(model, position, TypeFormat));
        }
        else
        {
            label.Append(method.ReturnType.ToMinimalDisplayString(model, position, TypeFormat)).Append(' ');
            if (method.ContainingType is { } owner)
                label.Append(owner.ToMinimalDisplayString(model, position, TypeFormat)).Append('.');
            label.Append(method.Name);
            if (method.TypeArguments.Length > 0)
                label.Append('<').Append(string.Join(", ", method.TypeArguments.Select(t => t.ToMinimalDisplayString(model, position, TypeFormat)))).Append('>');
        }

        label.Append('(');

        var docs = Documentation(method);
        var parameters = new List<SignatureParameter>();
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) label.Append(", ");

            var parameter = method.Parameters[i];
            var text = new StringBuilder();
            if (parameter.IsParams) text.Append("params ");
            text.Append(parameter.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => "",
            });
            text.Append(parameter.Type.ToMinimalDisplayString(model, position, TypeFormat)).Append(' ').Append(parameter.Name);
            if (parameter.HasExplicitDefaultValue)
                text.Append(" = ").Append(DefaultValue(parameter.ExplicitDefaultValue));

            var start = label.Length;
            label.Append(text);
            docs.Parameters.TryGetValue(parameter.Name, out var parameterDoc);
            parameters.Add(new SignatureParameter(text.ToString(), start, label.Length, parameterDoc));
        }

        label.Append(')');
        return new SignatureEntry(label.ToString(), docs.Summary, parameters);
    }

    private static string DefaultValue(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>Summary and parameter text from the XML documentation, when the symbol has any.</summary>
    private static (string? Summary, Dictionary<string, string> Parameters) Documentation(IMethodSymbol method)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var xml = method.GetDocumentationCommentXml(expandIncludes: true);
        if (string.IsNullOrWhiteSpace(xml)) return (null, parameters);

        try
        {
            var element = XElement.Parse(xml);
            foreach (var param in element.Elements("param"))
            {
                if (param.Attribute("name")?.Value is { } name)
                    parameters[name] = Flatten(param);
            }

            var summary = element.Element("summary");
            return (summary is null ? null : Flatten(summary), parameters);
        }
        catch (System.Xml.XmlException)
        {
            return (null, parameters);
        }
    }

    /// <summary>Reads doc-comment XML as plain text, keeping the names inside <c>see</c> tags.</summary>
    private static string Flatten(XElement element)
    {
        var text = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Name.LocalName is "see" or "seealso":
                    var target = e.Attribute("cref")?.Value ?? e.Attribute("langword")?.Value ?? e.Value;
                    text.Append(target.Contains(':') ? target[(target.IndexOf(':') + 1)..] : target);
                    break;
                case XElement e when e.Name.LocalName is "paramref" or "typeparamref":
                    text.Append(e.Attribute("name")?.Value);
                    break;
                case XElement e:
                    text.Append(Flatten(e));
                    break;
            }
        }

        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
