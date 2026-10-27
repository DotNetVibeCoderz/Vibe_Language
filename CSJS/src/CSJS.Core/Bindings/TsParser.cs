namespace CSJS.Core.Bindings;

/// <summary>
/// A recursive-descent parser for TypeScript declaration files.
/// <para>
/// It covers the declaration subset real typings use — classes, interfaces, functions,
/// variables, enums, type aliases, namespaces and ambient modules, imports and re-exports — and
/// parses the whole type grammar, including the parts no C# type can express. Those come back as
/// <see cref="TsUnsupportedType"/> rather than an error, because a library's single conditional
/// type should cost one member, not the whole file.
/// </para>
/// <para>
/// Recovery is per statement and per member: anything the parser cannot follow is skipped to the
/// next boundary and reported as a <see cref="TsSkippedDeclaration"/> or
/// <see cref="TsSkippedMember"/>, so the generator can write a <c>// skipped:</c> comment for it.
/// </para>
/// </summary>
public sealed class TsParser
{
    private readonly string _text;
    private readonly List<TsToken> _tokens;
    private int _pos;

    private TsParser(string text)
    {
        _text = text;
        _tokens = TsLexer.Tokenize(text);
    }

    /// <summary>Parses the text of one <c>.d.ts</c> file.</summary>
    public static TsSourceFile Parse(string text, string path = "index.d.ts")
    {
        var parser = new TsParser(text);
        var declarations = parser.ParseStatements(untilBrace: false);
        var isModule = declarations.Any(d => d is TsImportDeclaration or TsExportDeclaration
                                             or TsExportAssignment or TsExportAsNamespace
                                             || d.Exported);
        return new TsSourceFile(path, declarations) { IsModule = isModule };
    }

    /// <summary>Parses a standalone type expression, e.g. <c>Map&lt;string, number[]&gt;</c>.</summary>
    public static TsType ParseTypeText(string text) => new TsParser(text).ParseType();

    private sealed class ParseException(string message) : Exception(message);

    // -------------------------------------------------------------------------------------------
    // Token helpers
    // -------------------------------------------------------------------------------------------

    private TsToken Peek(int offset = 0)
    {
        var index = Math.Min(_pos + offset, _tokens.Count - 1);
        return _tokens[index];
    }

    private TsToken Next()
    {
        var token = _tokens[_pos];
        if (_pos < _tokens.Count - 1) _pos++;
        return token;
    }

    private bool Accept(string text)
    {
        if (!Peek().Is(text) || Peek().Kind is TsTokenKind.String or TsTokenKind.Template) return false;
        Next();
        return true;
    }

    private TsToken Expect(string text)
    {
        if (!Peek().Is(text) || Peek().Kind is TsTokenKind.String)
            throw new ParseException($"expected '{text}' but found '{Peek().Text}'");
        return Next();
    }

    private string ExpectIdentifier()
    {
        if (!Peek().IsIdentifier) throw new ParseException($"expected an identifier but found '{Peek().Text}'");
        return Next().Text;
    }

    private bool PeekIsPunct(string text, int offset = 0)
        => Peek(offset).Kind == TsTokenKind.Punctuation && Peek(offset).Text == text;

    private bool PeekIsWord(string text, int offset = 0)
        => Peek(offset).Kind == TsTokenKind.Identifier && Peek(offset).Text == text;

    /// <summary>The source text spanning the tokens from <paramref name="startToken"/> to the current position.</summary>
    private string TextFrom(int startToken)
    {
        if (startToken >= _pos) return Peek().Text;
        var start = _tokens[startToken].Position;
        var last = _tokens[_pos - 1];
        var end = last.Position + LengthOf(last);
        var raw = _text[start..Math.Min(end, _text.Length)];
        return string.Join(" ", raw.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
    }

    private int LengthOf(TsToken token)
    {
        // String tokens hold their unescaped value, so measure the raw source instead.
        if (token.Kind != TsTokenKind.String) return token.Text.Length;
        var quote = _text[token.Position];
        var i = token.Position + 1;
        while (i < _text.Length && _text[i] != quote)
        {
            if (_text[i] == '\\') i++;
            i++;
        }
        return i + 1 - token.Position;
    }

    /// <summary>Index of the token closing the bracket at <paramref name="openIndex"/>, or -1.</summary>
    private int MatchingClose(int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < _tokens.Count; i++)
        {
            var t = _tokens[i];
            if (t.Kind != TsTokenKind.Punctuation) continue;
            if (t.Text is "(" or "[" or "{") depth++;
            else if (t.Text is ")" or "]" or "}")
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private void SkipBalanced()
    {
        var close = MatchingClose(_pos);
        _pos = close < 0 ? _tokens.Count - 1 : Math.Min(close + 1, _tokens.Count - 1);
    }

    // -------------------------------------------------------------------------------------------
    // Statements
    // -------------------------------------------------------------------------------------------

    private List<TsDeclaration> ParseStatements(bool untilBrace)
    {
        var declarations = new List<TsDeclaration>();

        while (!Peek().IsEnd)
        {
            if (untilBrace && PeekIsPunct("}")) break;
            if (Accept(";")) continue;

            var start = _pos;
            try
            {
                var parsed = ParseStatement();
                if (parsed is not null) declarations.AddRange(parsed);
            }
            catch (ParseException ex)
            {
                _pos = start;
                var name = DescribeStatement();
                SkipStatement();
                declarations.Add(new TsSkippedDeclaration { Name = name, Reason = $"could not parse ({ex.Message})" });
            }

            // Guarantee progress, whatever happened above.
            if (_pos == start) Next();
        }

        return declarations;
    }

    private string DescribeStatement()
    {
        var words = new List<string>();
        for (var i = 0; i < 4 && !Peek(i).IsEnd; i++)
        {
            if (Peek(i).Kind != TsTokenKind.Identifier) break;
            words.Add(Peek(i).Text);
        }
        return words.Count > 0 ? string.Join(" ", words) : Peek().Text;
    }

    private static readonly HashSet<string> StatementStarts = new(StringComparer.Ordinal)
    {
        "export", "declare", "interface", "type", "class", "abstract", "function", "const", "let",
        "var", "enum", "namespace", "module", "import", "global",
    };

    /// <summary>Skips to the end of the current statement: a semicolon, a balanced block, or a new declaration line.</summary>
    private void SkipStatement()
    {
        var consumed = false;
        while (!Peek().IsEnd)
        {
            var token = Peek();
            if (consumed && token.NewLineBefore && token.IsIdentifier && StatementStarts.Contains(token.Text)) return;
            if (PeekIsPunct("}")) return;
            if (PeekIsPunct(";")) { Next(); return; }
            if (token.Kind == TsTokenKind.Punctuation && token.Text is "(" or "[" or "{")
            {
                var wasBrace = token.Text == "{";
                SkipBalanced();
                consumed = true;
                // A block-bodied declaration ends at its closing brace.
                if (wasBrace && (Peek().NewLineBefore || PeekIsPunct("}"))) return;
                continue;
            }
            Next();
            consumed = true;
        }
    }

    private IEnumerable<TsDeclaration>? ParseStatement()
    {
        var doc = Peek().Doc;
        var exported = false;
        var isDefault = false;

        if (PeekIsWord("import") && !PeekIsPunct("(", 1) && !PeekIsPunct(".", 1))
            return new[] { ParseImport() };

        if (PeekIsWord("export"))
        {
            Next();
            exported = true;

            if (Accept("="))
            {
                var target = ParseEntityName();
                Accept(";");
                return new[] { new TsExportAssignment { Name = target, Doc = doc } };
            }

            if (PeekIsWord("as") && PeekIsWord("namespace", 1))
            {
                Next(); Next();
                var name = ExpectIdentifier();
                Accept(";");
                return new[] { new TsExportAsNamespace { Name = name } };
            }

            if (PeekIsWord("type") && (PeekIsPunct("{", 1) || PeekIsPunct("*", 1)))
                Next(); // `export type { … }` is a type-only re-export; the same as far as bindings go.

            if (PeekIsPunct("*") || PeekIsPunct("{"))
                return new[] { ParseExportDeclaration() };

            if (PeekIsWord("import"))
            {
                // `export import X = A.B;` — an alias re-export.
                Next();
                var alias = ExpectIdentifier();
                Expect("=");
                var target = ParseEntityName();
                Accept(";");
                return new[] { new TsSkippedDeclaration { Name = alias, Reason = $"import alias of '{target}' is not supported" } };
            }

            if (PeekIsWord("default"))
            {
                Next();
                isDefault = true;

                // `export default Foo;` names an existing declaration.
                if (Peek().IsIdentifier && (PeekIsPunct(";", 1) || Peek(1).NewLineBefore || Peek(1).IsEnd)
                    && !StatementStarts.Contains(Peek().Text))
                {
                    var target = Next().Text;
                    Accept(";");
                    return new[]
                    {
                        new TsExportDeclaration { Specifiers = new[] { new TsExportSpecifier(target, "default") } },
                    };
                }
            }
        }

        if (PeekIsWord("declare")) Next();

        var token = Peek();
        if (!token.IsIdentifier)
            throw new ParseException($"unexpected '{token.Text}'");

        TsDeclaration? declaration = token.Text switch
        {
            "interface" => ParseInterface(),
            "class" => ParseClass(isAbstract: false),
            "abstract" when PeekIsWord("class", 1) => ParseAbstractClass(),
            "function" => ParseFunction(),
            "enum" => ParseEnum(isConst: false),
            "const" when PeekIsWord("enum", 1) => ParseConstEnum(),
            "type" when Peek(1).IsIdentifier => ParseTypeAlias(),
            "namespace" => ParseNamespace(),
            "module" => ParseNamespace(),
            "global" when PeekIsPunct("{", 1) => ParseGlobal(),
            _ => null,
        };

        if (declaration is not null)
        {
            if (declaration is TsNamespaceDeclaration { Kind: TsNamespaceKind.Namespace } ns)
                return new[] { WithCommon(ns, doc, exported, isDefault) };
            return new[] { WithCommon(declaration, doc, exported, isDefault) };
        }

        if (token.Text is "const" or "let" or "var")
            return ParseVariables().Select(v => WithCommon(v, doc, exported, isDefault)).ToList();

        throw new ParseException($"unsupported statement starting with '{token.Text}'");
    }

    private static TsDeclaration WithCommon(TsDeclaration declaration, string? doc, bool exported, bool isDefault)
        => declaration with
        {
            Doc = declaration.Doc ?? doc,
            Exported = declaration.Exported || exported,
            IsDefault = declaration.IsDefault || isDefault,
        };

    private string ParseEntityName()
    {
        var name = ExpectIdentifier();
        while (PeekIsPunct(".") && Peek(1).IsIdentifier)
        {
            Next();
            name += "." + Next().Text;
        }
        return name;
    }

    private TsDeclaration ParseImport()
    {
        Expect("import");
        if (PeekIsWord("type") && !PeekIsWord("from", 1) && !PeekIsPunct(",", 1) && !PeekIsPunct("=", 1)) Next();

        // Side-effect import: `import "m";`
        if (Peek().Kind == TsTokenKind.String)
        {
            var bare = Next().Text;
            Accept(";");
            return new TsImportDeclaration { Module = bare };
        }

        string? defaultName = null;
        string? namespaceName = null;
        var specifiers = new List<TsExportSpecifier>();

        if (Peek().IsIdentifier && !PeekIsWord("from"))
        {
            defaultName = Next().Text;

            // `import X = require("m");` or `import X = A.B;`
            if (Accept("="))
            {
                if (PeekIsWord("require"))
                {
                    Next();
                    Expect("(");
                    var required = Next().Text;
                    Expect(")");
                    Accept(";");
                    return new TsImportDeclaration { Module = required, NamespaceName = defaultName };
                }

                var target = ParseEntityName();
                Accept(";");
                return new TsSkippedDeclaration { Name = defaultName, Reason = $"import alias of '{target}' is not supported" };
            }

            Accept(",");
        }

        if (Accept("*"))
        {
            if (!PeekIsWord("as")) throw new ParseException("expected 'as'");
            Next();
            namespaceName = ExpectIdentifier();
        }
        else if (PeekIsPunct("{"))
        {
            specifiers.AddRange(ParseSpecifiers());
        }

        if (!PeekIsWord("from")) throw new ParseException("expected 'from'");
        Next();
        var module = Next().Text;
        SkipImportAttributes();
        Accept(";");

        return new TsImportDeclaration
        {
            Module = module,
            DefaultName = defaultName,
            NamespaceName = namespaceName,
            Specifiers = specifiers,
        };
    }

    private void SkipImportAttributes()
    {
        if ((PeekIsWord("with") || PeekIsWord("assert")) && PeekIsPunct("{", 1))
        {
            Next();
            SkipBalanced();
        }
    }

    private List<TsExportSpecifier> ParseSpecifiers()
    {
        Expect("{");
        var specifiers = new List<TsExportSpecifier>();
        while (!PeekIsPunct("}") && !Peek().IsEnd)
        {
            if (PeekIsWord("type") && (Peek(1).IsIdentifier || Peek(1).Kind == TsTokenKind.String)
                && !PeekIsWord("as", 1)) Next();

            var name = Peek().Kind == TsTokenKind.String ? Next().Text : ExpectIdentifier();
            var alias = name;
            if (PeekIsWord("as"))
            {
                Next();
                alias = Peek().Kind == TsTokenKind.String ? Next().Text : ExpectIdentifier();
            }
            specifiers.Add(new TsExportSpecifier(name, alias));
            if (!Accept(",")) break;
        }
        Expect("}");
        return specifiers;
    }

    private TsDeclaration ParseExportDeclaration()
    {
        if (Accept("*"))
        {
            string? alias = null;
            if (PeekIsWord("as"))
            {
                Next();
                alias = ExpectIdentifier();
            }
            if (!PeekIsWord("from")) throw new ParseException("expected 'from'");
            Next();
            var module = Next().Text;
            SkipImportAttributes();
            Accept(";");
            return new TsExportDeclaration { IsStar = true, FromModule = module, Name = alias ?? "" };
        }

        var specifiers = ParseSpecifiers();
        string? from = null;
        if (PeekIsWord("from"))
        {
            Next();
            from = Next().Text;
            SkipImportAttributes();
        }
        Accept(";");
        return new TsExportDeclaration { Specifiers = specifiers, FromModule = from };
    }

    private TsDeclaration ParseInterface()
    {
        Expect("interface");
        var name = ExpectIdentifier();
        var typeParameters = ParseTypeParametersOpt();
        var extends = new List<TsTypeReference>();
        if (PeekIsWord("extends"))
        {
            Next();
            do
            {
                if (ParseType() is TsTypeReference reference) extends.Add(reference);
            } while (Accept(","));
        }
        var members = ParseObjectMembers(isClass: false);
        return new TsInterfaceDeclaration { Name = name, TypeParameters = typeParameters, Extends = extends, Members = members };
    }

    private TsDeclaration ParseAbstractClass()
    {
        Expect("abstract");
        return ParseClass(isAbstract: true);
    }

    private TsDeclaration ParseClass(bool isAbstract)
    {
        Expect("class");
        // `export default class { }` may be anonymous.
        var name = Peek().IsIdentifier && !PeekIsWord("extends") && !PeekIsWord("implements") ? Next().Text : "default";
        var typeParameters = ParseTypeParametersOpt();
        TsTypeReference? extends = null;

        if (PeekIsWord("extends"))
        {
            Next();
            var baseType = ParseType();
            extends = baseType as TsTypeReference;
        }

        if (PeekIsWord("implements"))
        {
            // Interfaces are TypeScript-only; the C# class still gets every member it declares.
            Next();
            do { ParseType(); } while (Accept(","));
        }

        var members = ParseObjectMembers(isClass: true);
        return new TsClassDeclaration
        {
            Name = name,
            TypeParameters = typeParameters,
            Extends = extends,
            Members = members,
            Abstract = isAbstract,
        };
    }

    private TsDeclaration ParseFunction()
    {
        Expect("function");
        if (Accept("*")) { /* generator function — the signature is all that matters */ }
        var name = Peek().IsIdentifier ? Next().Text : "default";
        var typeParameters = ParseTypeParametersOpt();
        var parameters = ParseParameters();
        TsType returnType = new TsTypeReference("void", Array.Empty<TsType>());
        if (Accept(":")) returnType = ParseReturnType();
        Accept(";");
        return new TsFunctionDeclaration
        {
            Name = name,
            TypeParameters = typeParameters,
            Parameters = parameters,
            ReturnType = returnType,
        };
    }

    private List<TsDeclaration> ParseVariables()
    {
        var keyword = Next().Text;
        var result = new List<TsDeclaration>();

        do
        {
            var doc = Peek().Doc;
            if (PeekIsPunct("{") || PeekIsPunct("["))
                throw new ParseException("destructured variable declarations are not supported");

            var name = ExpectIdentifier();
            TsType? type = null;
            TsLiteralType? literal = null;
            if (Accept(":")) type = ParseType();
            if (Accept("="))
            {
                var start = _pos;
                var value = ParsePrimaryTypeSafe();
                literal = value as TsLiteralType;
                if (literal is null) _pos = start;
                SkipInitializer();
            }

            result.Add(new TsVariableDeclaration { Name = name, Doc = doc, Type = type, IsConst = keyword == "const", Literal = literal });
        } while (Accept(","));

        Accept(";");
        return result;
    }

    private TsType? ParsePrimaryTypeSafe()
    {
        try { return ParsePrimaryType(); }
        catch (ParseException) { return null; }
    }

    private void SkipInitializer()
    {
        while (!Peek().IsEnd && !PeekIsPunct(",") && !PeekIsPunct(";") && !PeekIsPunct("}"))
        {
            if (Peek().Kind == TsTokenKind.Punctuation && Peek().Text is "(" or "[" or "{") SkipBalanced();
            else if (Peek().NewLineBefore && _pos > 0 && Peek().IsIdentifier && StatementStarts.Contains(Peek().Text)) return;
            else Next();
        }
    }

    private TsDeclaration ParseConstEnum()
    {
        Expect("const");
        return ParseEnum(isConst: true);
    }

    private TsDeclaration ParseEnum(bool isConst)
    {
        Expect("enum");
        var name = ExpectIdentifier();
        Expect("{");
        var members = new List<TsEnumMember>();

        while (!PeekIsPunct("}") && !Peek().IsEnd)
        {
            var doc = Peek().Doc;
            var memberName = Peek().Kind == TsTokenKind.String ? Next().Text : ExpectIdentifier();
            string? value = null;
            var isString = false;

            if (Accept("="))
            {
                if (Peek().Kind == TsTokenKind.String && (PeekIsPunct(",", 1) || PeekIsPunct("}", 1)))
                {
                    value = Next().Text;
                    isString = true;
                }
                else
                {
                    var start = _pos;
                    while (!PeekIsPunct(",") && !PeekIsPunct("}") && !Peek().IsEnd)
                    {
                        if (PeekIsPunct("(")) SkipBalanced();
                        else Next();
                    }
                    value = TextFrom(start);
                }
            }

            members.Add(new TsEnumMember(memberName, doc, value, isString));
            if (!Accept(",")) break;
        }

        Expect("}");
        return new TsEnumDeclaration { Name = name, Members = members, IsConst = isConst };
    }

    private TsDeclaration ParseTypeAlias()
    {
        Expect("type");
        var name = ExpectIdentifier();
        var typeParameters = ParseTypeParametersOpt();
        Expect("=");
        var type = ParseType();
        Accept(";");
        return new TsTypeAliasDeclaration { Name = name, TypeParameters = typeParameters, Type = type };
    }

    private TsDeclaration ParseNamespace()
    {
        Next(); // namespace | module

        if (Peek().Kind == TsTokenKind.String)
        {
            var moduleName = Next().Text;
            if (!PeekIsPunct("{"))
            {
                // `declare module "x";` — a shorthand ambient module with no declarations.
                Accept(";");
                return new TsSkippedDeclaration { Name = moduleName, Reason = "shorthand ambient module declares nothing" };
            }

            Expect("{");
            var body = ParseStatements(untilBrace: true);
            Expect("}");
            return new TsNamespaceDeclaration { Name = moduleName, Kind = TsNamespaceKind.AmbientModule, Body = body };
        }

        // `namespace A.B.C { }` nests.
        var parts = new List<string> { ExpectIdentifier() };
        while (Accept(".")) parts.Add(ExpectIdentifier());

        Expect("{");
        var declarations = ParseStatements(untilBrace: true);
        Expect("}");

        TsNamespaceDeclaration result = new() { Name = parts[^1], Kind = TsNamespaceKind.Namespace, Body = declarations, Exported = true };
        for (var i = parts.Count - 2; i >= 0; i--)
            result = new TsNamespaceDeclaration { Name = parts[i], Kind = TsNamespaceKind.Namespace, Body = new[] { result }, Exported = true };
        return result with { Exported = false };
    }

    private TsDeclaration ParseGlobal()
    {
        Expect("global");
        Expect("{");
        var body = ParseStatements(untilBrace: true);
        Expect("}");
        return new TsNamespaceDeclaration { Name = "global", Kind = TsNamespaceKind.Global, Body = body };
    }

    // -------------------------------------------------------------------------------------------
    // Members
    // -------------------------------------------------------------------------------------------

    private static readonly HashSet<string> MemberModifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "static", "readonly", "abstract", "declare", "override", "accessor", "async",
    };

    private List<TsMember> ParseObjectMembers(bool isClass)
    {
        Expect("{");
        var members = new List<TsMember>();

        while (!PeekIsPunct("}") && !Peek().IsEnd)
        {
            if (Accept(";") || Accept(",")) continue;

            var start = _pos;
            try
            {
                var member = ParseMember(isClass);
                if (member is not null) members.Add(member);
                if (!Accept(";")) Accept(",");
            }
            catch (ParseException ex)
            {
                _pos = start;
                var name = Peek().Text;
                SkipMember();
                members.Add(new TsSkippedMember(name, $"could not parse ({ex.Message})"));
            }

            if (_pos == start) Next();
        }

        Expect("}");
        return members;
    }

    private void SkipMember()
    {
        var consumed = false;
        while (!Peek().IsEnd)
        {
            if (PeekIsPunct("}")) return;
            if (PeekIsPunct(";") || PeekIsPunct(",")) { Next(); return; }
            if (consumed && Peek().NewLineBefore && Peek().IsIdentifier
                && (PeekIsPunct(":", 1) || PeekIsPunct("?", 1) || PeekIsPunct("(", 1)))
            {
                var previous = _tokens[_pos - 1];
                if (previous.Kind != TsTokenKind.Punctuation || previous.Text is ")" or "]" or "}" or ">") return;
            }
            if (Peek().Kind == TsTokenKind.Punctuation && Peek().Text is "(" or "[" or "{") SkipBalanced();
            else Next();
            consumed = true;
        }
    }

    /// <summary>True when the word at the cursor is a modifier rather than a member called, say, <c>static</c>.</summary>
    private bool IsModifierHere()
    {
        var next = Peek(1);
        if (next.NewLineBefore && next.Kind == TsTokenKind.Punctuation) return false;
        return next.Kind is TsTokenKind.Identifier or TsTokenKind.String or TsTokenKind.Number
               || next.Is("[") || next.Is("#") || next.Is("*");
    }

    private TsMember? ParseMember(bool isClass)
    {
        var doc = Peek().Doc;
        var modifiers = TsModifiers.None;

        while (Peek().IsIdentifier && MemberModifiers.Contains(Peek().Text) && IsModifierHere())
        {
            switch (Next().Text)
            {
                case "static": modifiers |= TsModifiers.Static; break;
                case "readonly": modifiers |= TsModifiers.Readonly; break;
                case "abstract": modifiers |= TsModifiers.Abstract; break;
                case "protected": modifiers |= TsModifiers.Protected; break;
                case "private": modifiers |= TsModifiers.Private; break;
            }
        }

        // Accessors: `get name(): T` / `set name(value: T)`.
        if ((PeekIsWord("get") || PeekIsWord("set")) && IsModifierHere())
        {
            var isSetter = Next().Text == "set";
            var accessorName = ParsePropertyName(out var computed);
            if (computed) return SkipComputed(accessorName);
            var parameters = ParseParameters();
            TsType type = isSetter
                ? parameters.FirstOrDefault()?.Type ?? AnyType
                : Accept(":") ? ParseType() : AnyType;
            return new TsAccessor(accessorName, doc, modifiers, isSetter, type);
        }

        // Call and construct signatures.
        if (PeekIsPunct("(") || PeekIsPunct("<"))
        {
            var typeParameters = ParseTypeParametersOpt();
            var parameters = ParseParameters();
            var returnType = Accept(":") ? ParseReturnType() : AnyType;
            return new TsCallSignature(doc, typeParameters, parameters, returnType);
        }

        if (PeekIsWord("new") && (PeekIsPunct("(", 1) || PeekIsPunct("<", 1)))
        {
            Next();
            var typeParameters = ParseTypeParametersOpt();
            var parameters = ParseParameters();
            var returnType = Accept(":") ? ParseType() : null;
            return new TsConstructSignature(doc, modifiers, typeParameters, parameters, returnType);
        }

        if (isClass && PeekIsWord("constructor") && PeekIsPunct("(", 1))
        {
            Next();
            var parameters = ParseParameters();
            if (Accept(":")) ParseType();
            return new TsConstructSignature(doc, modifiers, Array.Empty<TsTypeParameter>(), parameters, null);
        }

        // Index signature: `[key: string]: T`.
        if (PeekIsPunct("[") && Peek(1).IsIdentifier && PeekIsPunct(":", 2))
        {
            Next();
            var keyName = Next().Text;
            Expect(":");
            var keyType = ParseType();
            Expect("]");
            if (Accept("?")) { }
            Expect(":");
            var valueType = ParseType();
            return new TsIndexSignature(doc, modifiers, keyName, keyType, valueType);
        }

        if (PeekIsPunct("#"))
        {
            // An ECMAScript private name is invisible from outside the class.
            SkipMember();
            return null;
        }

        var name = ParsePropertyName(out var isComputed);
        if (isComputed) return SkipComputed(name);

        if (Accept("?")) modifiers |= TsModifiers.Optional;
        Accept("!");

        if (PeekIsPunct("(") || PeekIsPunct("<"))
        {
            var typeParameters = ParseTypeParametersOpt();
            var parameters = ParseParameters();
            var returnType = Accept(":") ? ParseReturnType() : AnyType;
            return new TsMethodSignature(name, doc, modifiers, typeParameters, parameters, returnType);
        }

        var propertyType = Accept(":") ? ParseType() : AnyType;
        if (Accept("=")) SkipInitializer();
        return new TsPropertySignature(name, doc, modifiers, propertyType);
    }

    private TsMember SkipComputed(string name)
    {
        SkipMember();
        return new TsSkippedMember(name, "computed property names have no C# equivalent");
    }

    private string ParsePropertyName(out bool computed)
    {
        computed = false;
        var token = Peek();
        if (token.Kind is TsTokenKind.Identifier or TsTokenKind.String or TsTokenKind.Number)
            return Next().Text;

        if (PeekIsPunct("["))
        {
            var start = _pos;
            SkipBalanced();
            computed = true;
            return TextFrom(start);
        }

        throw new ParseException($"expected a member name but found '{token.Text}'");
    }

    private static TsType AnyType => new TsTypeReference("any", Array.Empty<TsType>());

    // -------------------------------------------------------------------------------------------
    // Parameters
    // -------------------------------------------------------------------------------------------

    private List<TsTypeParameter> ParseTypeParametersOpt()
    {
        var result = new List<TsTypeParameter>();
        if (!Accept("<")) return result;

        while (!PeekIsPunct(">") && !Peek().IsEnd)
        {
            while ((PeekIsWord("const") || PeekIsWord("in") || PeekIsWord("out")) && Peek(1).IsIdentifier) Next();
            var name = ExpectIdentifier();
            TsType? constraint = null;
            TsType? @default = null;
            if (PeekIsWord("extends"))
            {
                Next();
                constraint = ParseType();
            }
            if (Accept("=")) @default = ParseType();
            result.Add(new TsTypeParameter(name, constraint, @default));
            if (!Accept(",")) break;
        }

        Expect(">");
        return result;
    }

    private List<TsParameter> ParseParameters()
    {
        Expect("(");
        var result = new List<TsParameter>();
        var index = 0;

        while (!PeekIsPunct(")") && !Peek().IsEnd)
        {
            while (Peek().IsIdentifier && Peek().Text is "public" or "private" or "protected" or "readonly" or "override"
                   && (Peek(1).IsIdentifier || PeekIsPunct("{", 1) || PeekIsPunct("[", 1)))
            {
                Next();
            }

            var rest = Accept("...");
            string name;
            if (PeekIsPunct("{") || PeekIsPunct("["))
            {
                // A destructured parameter has no name of its own; invent a readable one.
                name = PeekIsPunct("{") ? "options" : "args";
                SkipBalanced();
            }
            else
            {
                name = ExpectIdentifier();
            }

            var optional = Accept("?");
            var type = Accept(":") ? ParseType() : (rest ? new TsArrayType(AnyType) : AnyType);
            if (Accept("="))
            {
                optional = true;
                SkipInitializer();
            }

            result.Add(new TsParameter(name, type, optional, rest));
            index++;
            if (!Accept(",")) break;
        }

        Expect(")");
        return result;
    }

    // -------------------------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------------------------

    private bool _noConditional;

    /// <summary>Parses a full type, including function and conditional types.</summary>
    private TsType ParseType()
    {
        if (IsStartOfFunctionType()) return ParseFunctionType();

        var start = _pos;
        var type = ParseUnionType();

        if (!_noConditional && PeekIsWord("extends") && !Peek().NewLineBefore)
        {
            Next();
            var saved = _noConditional;
            _noConditional = true;
            TsType extendsType;
            try { extendsType = ParseType(); }
            finally { _noConditional = saved; }

            Expect("?");
            var savedOuter = _noConditional;
            _noConditional = false;
            TsType whenTrue, whenFalse;
            try
            {
                whenTrue = ParseType();
                Expect(":");
                whenFalse = ParseType();
            }
            finally { _noConditional = savedOuter; }

            return new TsConditionalType(type, extendsType, whenTrue, whenFalse, TextFrom(start));
        }

        return type;
    }

    private TsType ParseReturnType()
    {
        // `asserts x` / `asserts x is T`
        if (PeekIsWord("asserts") && (Peek(1).IsIdentifier) && !PeekIsPunct(".", 1))
        {
            Next();
            var parameter = Next().Text;
            TsType? type = null;
            if (PeekIsWord("is"))
            {
                Next();
                type = ParseType();
            }
            return new TsPredicateType(parameter, type, Asserts: true);
        }

        // `x is T`
        if (Peek().IsIdentifier && PeekIsWord("is", 1) && !Peek(1).NewLineBefore)
        {
            var parameter = Next().Text;
            Next();
            var type = ParseType();
            return new TsPredicateType(parameter, type, Asserts: false);
        }

        return ParseType();
    }

    private bool IsStartOfFunctionType()
    {
        if (PeekIsPunct("<")) return true;
        if (PeekIsWord("new") && (PeekIsPunct("(", 1) || PeekIsPunct("<", 1))) return true;
        if (PeekIsWord("abstract") && PeekIsWord("new", 1)) return true;
        if (!PeekIsPunct("(")) return false;

        // A parenthesised type is never followed by an arrow; a parameter list always is.
        var close = MatchingClose(_pos);
        return close > 0 && close + 1 < _tokens.Count && _tokens[close + 1].Is("=>");
    }

    private TsType ParseFunctionType()
    {
        Accept("abstract");
        var isConstructor = Accept("new");
        var typeParameters = ParseTypeParametersOpt();
        var parameters = ParseParameters();
        Expect("=>");
        var returnType = ParseReturnType();
        return new TsFunctionType(typeParameters, parameters, returnType, isConstructor);
    }

    private TsType ParseUnionType()
    {
        Accept("|");
        var types = new List<TsType> { ParseIntersectionType() };
        while (PeekIsPunct("|"))
        {
            Next();
            types.Add(IsStartOfFunctionType() ? ParseFunctionType() : ParseIntersectionType());
        }
        return types.Count == 1 ? types[0] : new TsUnionType(types);
    }

    private TsType ParseIntersectionType()
    {
        Accept("&");
        var types = new List<TsType> { ParseTypeOperator() };
        while (PeekIsPunct("&"))
        {
            Next();
            types.Add(IsStartOfFunctionType() ? ParseFunctionType() : ParseTypeOperator());
        }
        return types.Count == 1 ? types[0] : new TsIntersectionType(types);
    }

    private TsType ParseTypeOperator()
    {
        var start = _pos;

        if (PeekIsWord("keyof") && !IsEndOfTypeAt(1))
        {
            Next();
            ParseTypeOperator();
            return new TsUnsupportedType(TextFrom(start), "keyof type");
        }

        if (PeekIsWord("unique") && PeekIsWord("symbol", 1))
        {
            Next(); Next();
            return new TsTypeReference("symbol", Array.Empty<TsType>());
        }

        if (PeekIsWord("readonly") && !IsEndOfTypeAt(1))
        {
            Next();
            var inner = ParseTypeOperator();
            return inner is TsArrayType array ? array with { Readonly = true } : inner;
        }

        if (PeekIsWord("infer") && Peek(1).IsIdentifier)
        {
            Next(); Next();
            if (PeekIsWord("extends") && _noConditional)
            {
                Next();
                ParseTypeOperator();
            }
            return new TsUnsupportedType(TextFrom(start), "infer type");
        }

        return ParsePostfixType();
    }

    /// <summary>True when the token at the offset cannot start a type, so the word before it is a name.</summary>
    private bool IsEndOfTypeAt(int offset)
    {
        var token = Peek(offset);
        return token.IsEnd || token.Kind == TsTokenKind.Punctuation && token.Text is ";" or "," or ")" or "]" or "}" or ">" or "=" or "|" or "&" or "?" or ":";
    }

    private TsType ParsePostfixType()
    {
        var start = _pos;
        var type = ParsePrimaryType();

        while (PeekIsPunct("[") && !Peek().NewLineBefore)
        {
            Next();
            if (Accept("]"))
            {
                type = new TsArrayType(type);
                continue;
            }

            var index = ParseType();
            Expect("]");
            type = new TsIndexedAccessType(type, index, TextFrom(start));
        }

        return type;
    }

    private TsType ParsePrimaryType()
    {
        var token = Peek();
        var start = _pos;

        switch (token.Kind)
        {
            case TsTokenKind.String:
                Next();
                return new TsLiteralType(TsLiteralKind.String, token.Text);

            case TsTokenKind.Number:
                Next();
                return token.Text.EndsWith('n')
                    ? new TsLiteralType(TsLiteralKind.BigInt, token.Text)
                    : new TsLiteralType(TsLiteralKind.Number, token.Text);

            case TsTokenKind.Template:
                // A template literal type is always some string.
                Next();
                return new TsTypeReference("string", Array.Empty<TsType>());
        }

        if (token.Kind == TsTokenKind.Punctuation)
        {
            switch (token.Text)
            {
                case "(":
                {
                    Next();
                    var inner = ParseType();
                    Expect(")");
                    return inner;
                }

                case "{":
                    return IsMappedType() ? SkipMappedType() : new TsObjectType(ParseObjectMembers(isClass: false));

                case "[":
                    return ParseTupleType();

                case "-" when Peek(1).Kind == TsTokenKind.Number:
                    Next();
                    return new TsLiteralType(TsLiteralKind.Number, "-" + Next().Text);

                case "<":
                    return ParseFunctionType();
            }

            throw new ParseException($"unexpected '{token.Text}' in a type");
        }

        switch (token.Text)
        {
            case "true":
            case "false":
                Next();
                return new TsLiteralType(TsLiteralKind.Boolean, token.Text);

            case "typeof":
            {
                Next();
                if (PeekIsWord("import"))
                {
                    ParseImportType();
                    if (PeekIsPunct("<") && !Peek().NewLineBefore) ParseTypeArguments();
                    return new TsUnsupportedType(TextFrom(start), "typeof type query");
                }
                var queried = ParseEntityName();
                if (PeekIsPunct("<") && !Peek().NewLineBefore) ParseTypeArguments();
                return new TsTypeQuery(queried, TextFrom(start));
            }

            case "import" when PeekIsPunct("(", 1):
                return ParseImportType();

            case "new":
            case "abstract":
                return ParseFunctionType();
        }

        var name = ParseEntityName();
        var arguments = PeekIsPunct("<") ? ParseTypeArguments() : (IReadOnlyList<TsType>)Array.Empty<TsType>();
        return new TsTypeReference(name, arguments);
    }

    /// <summary><c>import("m").Name&lt;T&gt;</c> — resolved by its trailing name.</summary>
    private TsType ParseImportType()
    {
        var start = _pos;
        Expect("import");
        Expect("(");
        Next();
        Expect(")");
        if (!Accept(".")) return new TsUnsupportedType(TextFrom(start), "import type of a whole module");
        var name = ParseEntityName();
        var arguments = PeekIsPunct("<") ? ParseTypeArguments() : (IReadOnlyList<TsType>)Array.Empty<TsType>();
        return new TsTypeReference(name, arguments);
    }

    private List<TsType> ParseTypeArguments()
    {
        Expect("<");
        var arguments = new List<TsType>();
        while (!PeekIsPunct(">") && !Peek().IsEnd)
        {
            arguments.Add(ParseType());
            if (!Accept(",")) break;
        }
        Expect(">");
        return arguments;
    }

    private bool IsMappedType()
    {
        var offset = 1;
        if (PeekIsWord("readonly", offset) || PeekIsPunct("+", offset) || PeekIsPunct("-", offset))
        {
            offset++;
            if (PeekIsWord("readonly", offset)) offset++;
        }
        return PeekIsPunct("[", offset) && Peek(offset + 1).IsIdentifier && PeekIsWord("in", offset + 2);
    }

    private TsType SkipMappedType()
    {
        var start = _pos;
        SkipBalanced();
        return new TsUnsupportedType(TextFrom(start), "mapped type");
    }

    private TsType ParseTupleType()
    {
        Expect("[");
        var elements = new List<TsTupleElement>();

        while (!PeekIsPunct("]") && !Peek().IsEnd)
        {
            var rest = Accept("...");
            string? name = null;
            var optional = false;

            if (Peek().IsIdentifier && (PeekIsPunct(":", 1) || (PeekIsPunct("?", 1) && PeekIsPunct(":", 2))))
            {
                name = Next().Text;
                optional = Accept("?");
                Expect(":");
            }

            var type = ParseType();
            if (Accept("?")) optional = true;
            elements.Add(new TsTupleElement(type, name, optional, rest));
            if (!Accept(",")) break;
        }

        Expect("]");
        return new TsTupleType(elements);
    }
}
