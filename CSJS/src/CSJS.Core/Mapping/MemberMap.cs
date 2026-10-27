using Microsoft.CodeAnalysis;

namespace CSJS.Core.Mapping;

/// <summary>
/// An output pattern for a mapped BCL member.
/// <para>Placeholders:</para>
/// <list type="bullet">
///   <item><c>{this}</c> — the receiver, emitted as written.</item>
///   <item><c>{seq}</c> — the receiver as a JavaScript array. A spread is inserted only when
///     the receiver is not already one, which keeps chained LINQ from stacking up
///     <c>[...[...x]]</c> for no reason.</item>
///   <item><c>{copy}</c> — the receiver as a <em>new</em> array. A LINQ call already hands back a
///     fresh one; anything else is spread, because <c>ToList()</c> in C# never aliases.</item>
///   <item><c>{0}</c>…<c>{n}</c> — positional arguments.</item>
///   <item><c>{...}</c> — every argument, comma-joined.</item>
///   <item><c>{...n}</c> — arguments from index <c>n</c> onward.</item>
///   <item><c>{default}</c> — the C# default of the member's result type: <c>0</c> for an
///     <c>int</c>, <c>null</c> for a reference. <c>FirstOrDefault</c> on an empty
///     <c>List&lt;int&gt;</c> is 0, not null.</item>
/// </list>
/// </summary>
/// <param name="Pattern">The JavaScript to emit.</param>
/// <param name="Helper">
/// Name of a runtime helper the pattern calls. When set, the emitter adds a named import from the
/// runtime module. Helpers are imported one at a time so bundlers drop the ones nobody used.
/// </param>
/// <param name="StringifyArguments">
/// The arguments are converted to text the way C# would convert them — an enum prints its member
/// name and a <c>bool</c> prints <c>True</c>. <c>Console.WriteLine</c> needs this; JavaScript's
/// <c>console.log</c> would print the ordinal and <c>true</c>.
/// </param>
public sealed record JsTemplate(string Pattern, string? Helper = null, bool StringifyArguments = false);

/// <summary>
/// Maps BCL members onto JavaScript. Anything absent here is either emitted structurally
/// (user-declared types) or reported as unsupported, which keeps the failure mode honest
/// instead of silently emitting broken JavaScript.
/// <para>
/// Keys come in two forms. <c>Owner.Name/arity</c> covers a member whose overloads all behave
/// alike. <c>Owner.Name(type,type)</c> pins one overload by its parameter types, for the cases
/// where arity is not enough: <c>IndexOf(string, int)</c> and
/// <c>IndexOf(string, StringComparison)</c> share an arity and mean different things. The exact
/// signature is tried first.
/// </para>
/// </summary>
public static class MemberMap
{
    /// <summary>
    /// How a parameter type is spelled in a signature key: namespace-qualified, special types as
    /// keywords (<c>string</c>, <c>int</c>), generic parameters by name, and no spaces.
    /// </summary>
    private static readonly SymbolDisplayFormat SignatureFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>Looks up a method. Returns null when the method has no mapping.</summary>
    public static JsTemplate? Find(IMethodSymbol method)
    {
        var target = method.ReducedFrom ?? method.OriginalDefinition;
        var isReducedExtension = target.IsExtensionMethod && method.ReducedFrom is not null;
        var argCount = isReducedExtension ? target.Parameters.Length - 1 : method.Parameters.Length;
        var owner = OwnerName(target);

        if (Methods.TryGetValue($"{owner}.{target.Name}{Signature(target, skipFirst: isReducedExtension)}", out var exact))
            return exact;

        if (Methods.TryGetValue($"{owner}.{target.Name}/{argCount}", out var t)) return t;

        // Fall back to an arity-agnostic entry for params-style members such as console.log.
        return Methods.TryGetValue($"{owner}.{target.Name}/*", out var any) ? any : null;
    }

    /// <summary>Looks up a property or field.</summary>
    public static JsTemplate? Find(ISymbol member)
    {
        if (member is IMethodSymbol m) return Find(m);
        var key = $"{OwnerName(member)}.{member.Name}";
        return Properties.TryGetValue(key, out var t) ? t : null;
    }

    /// <summary>Constructor mapping, e.g. <c>new List&lt;T&gt;()</c> to <c>[]</c>.</summary>
    public static JsTemplate? FindConstructor(IMethodSymbol ctor)
    {
        var definition = ctor.OriginalDefinition;
        var owner = OwnerName(definition);
        if (Constructors.TryGetValue(owner + Signature(definition, skipFirst: false), out var exact)) return exact;

        var key = $"{owner}/{ctor.Parameters.Length}";
        if (Constructors.TryGetValue(key, out var t)) return t;
        return Constructors.TryGetValue($"{owner}/*", out var any) ? any : null;
    }

    /// <summary>
    /// The template any kind of member maps to — constructor, method, property, or field — or
    /// null when it has none. A read-only convenience for tooling (hover shows what a member
    /// emits); the emitter keeps calling the specific lookups above.
    /// </summary>
    public static JsTemplate? Lookup(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } ctor => FindConstructor(ctor),
        IMethodSymbol method => Find(method),
        IPropertySymbol or IFieldSymbol => Find(symbol),
        _ => null,
    };

    /// <summary>
    /// Every key in the table, so a test can prove each one names a member that exists. A typo in
    /// a signature key would otherwise fail silently — the lookup simply never matches.
    /// </summary>
    public static IEnumerable<string> AllKeys
        => Methods.Keys.Concat(Properties.Keys).Concat(Constructors.Keys.Select(k => "ctor:" + k));

    /// <summary>The signature suffix a key uses for an exact overload, e.g. <c>(string,int)</c>.</summary>
    public static string Signature(IMethodSymbol method, bool skipFirst)
    {
        var parameters = method.Parameters.Skip(skipFirst ? 1 : 0)
            .Select(p => p.Type.ToDisplayString(SignatureFormat).Replace(" ", ""));
        return "(" + string.Join(",", parameters) + ")";
    }

    private static string OwnerName(ISymbol member)
        => member.ContainingType is { } ct ? TypeMap.Unbound(ct) : "";

    /// <summary>
    /// True when a symbol comes from the framework and therefore requires a mapping.
    /// Types the developer declared are emitted from their own source and need none, and
    /// <c>CSJS.Interop</c> declarations carry their own binding attributes.
    /// <para>
    /// Every <c>System.*</c> and <c>Microsoft.*</c> assembly counts. An allow-list of a dozen names
    /// let <c>System.Text.Json</c> and <c>System.Net.Http</c> through unchecked, and their members
    /// were emitted verbatim — exactly the silent failure this check exists to prevent.
    /// </para>
    /// </summary>
    public static bool RequiresMapping(ISymbol symbol)
    {
        if (symbol.DeclaringSyntaxReferences.Length > 0) return false;

        var assembly = symbol.ContainingAssembly?.Name;
        if (assembly is null) return false;
        if (assembly == "CSJS.Interop") return false;

        return IsFrameworkAssembly(assembly);
    }

    /// <summary>True for an assembly that ships with .NET rather than with the program.</summary>
    public static bool IsFrameworkAssembly(string assembly)
        => assembly is "netstandard" or "mscorlib"
           || assembly.StartsWith("System.", StringComparison.Ordinal)
           || assembly.StartsWith("Microsoft.", StringComparison.Ordinal);

    // ---------------------------------------------------------------------------------------
    // Methods
    // ---------------------------------------------------------------------------------------

    private const string Linq = "System.Linq.Enumerable";
    private const string List = "System.Collections.Generic.List";
    private const string Dictionary = "System.Collections.Generic.Dictionary";
    private const string HashSet = "System.Collections.Generic.HashSet";
    private const string Regex = "System.Text.RegularExpressions.Regex";

    private static readonly Dictionary<string, JsTemplate> Methods = new(StringComparer.Ordinal)
    {
        // ---- Console -------------------------------------------------------------------
        ["System.Console.WriteLine/0"] = new("console.log()"),
        ["System.Console.WriteLine/1"] = new("console.log({0})", StringifyArguments: true),
        ["System.Console.WriteLine/*"] = new("console.log(csjsFormat({...}))", Helper: "csjsFormat", StringifyArguments: true),
        ["System.Console.Write/1"] = new("csjsWrite({0})", Helper: "csjsWrite", StringifyArguments: true),
        ["System.Console.Write/*"] = new("csjsWrite(csjsFormat({...}))", Helper: "csjsWrite", StringifyArguments: true),
        ["System.IO.TextWriter.WriteLine/1"] = new("console.error({0})", StringifyArguments: true),

        // ---- String --------------------------------------------------------------------
        ["System.String.Substring/1"] = new("{this}.substring({0})"),
        ["System.String.Substring/2"] = new("{this}.substring({0}, {0} + {1})"),
        ["System.String.IndexOf/1"] = new("{this}.indexOf({0})"),
        ["System.String.IndexOf(string,int)"] = new("{this}.indexOf({0}, {1})"),
        ["System.String.IndexOf(char,int)"] = new("{this}.indexOf({0}, {1})"),
        ["System.String.IndexOf(string,System.StringComparison)"] = new("csjsIndexOf({this}, {0}, {1})", Helper: "csjsIndexOf"),
        ["System.String.IndexOfAny/1"] = new("csjsIndexOfAny({this}, {0})", Helper: "csjsIndexOfAny"),
        ["System.String.LastIndexOf/1"] = new("{this}.lastIndexOf({0})"),
        ["System.String.LastIndexOf(string,int)"] = new("{this}.lastIndexOf({0}, {1})"),
        ["System.String.LastIndexOf(char,int)"] = new("{this}.lastIndexOf({0}, {1})"),
        ["System.String.Contains/1"] = new("{this}.includes({0})"),
        ["System.String.Contains(string,System.StringComparison)"] = new("(csjsIndexOf({this}, {0}, {1}) >= 0)", Helper: "csjsIndexOf"),
        ["System.String.StartsWith/1"] = new("{this}.startsWith({0})"),
        ["System.String.StartsWith(string,System.StringComparison)"] = new("csjsStartsWith({this}, {0}, {1})", Helper: "csjsStartsWith"),
        ["System.String.EndsWith/1"] = new("{this}.endsWith({0})"),
        ["System.String.EndsWith(string,System.StringComparison)"] = new("csjsEndsWith({this}, {0}, {1})", Helper: "csjsEndsWith"),
        ["System.String.Equals(string)"] = new("({this} === {0})"),
        ["System.String.Equals(object)"] = new("({this} === {0})"),
        ["System.String.Equals(string,string)"] = new("({0} === {1})"),
        ["System.String.Equals(string,System.StringComparison)"] = new("csjsStringEquals({this}, {0}, {1})", Helper: "csjsStringEquals"),
        ["System.String.Equals(string,string,System.StringComparison)"] = new("csjsStringEquals({0}, {1}, {2})", Helper: "csjsStringEquals"),
        ["System.String.Compare(string,string)"] = new("csjsStringCompare({0}, {1}, 0)", Helper: "csjsStringCompare"),
        ["System.String.Compare(string,string,bool)"] = new("csjsStringCompare({0}, {1}, {2} ? 1 : 0)", Helper: "csjsStringCompare"),
        ["System.String.Compare(string,string,System.StringComparison)"] = new("csjsStringCompare({0}, {1}, {2})", Helper: "csjsStringCompare"),
        ["System.String.CompareOrdinal/2"] = new("csjsCompare({0}, {1})", Helper: "csjsCompare"),
        ["System.String.ToUpper/0"] = new("{this}.toUpperCase()"),
        ["System.String.ToUpperInvariant/0"] = new("{this}.toUpperCase()"),
        ["System.String.ToLower/0"] = new("{this}.toLowerCase()"),
        ["System.String.ToLowerInvariant/0"] = new("{this}.toLowerCase()"),
        ["System.String.Trim/0"] = new("{this}.trim()"),
        ["System.String.TrimStart/0"] = new("{this}.trimStart()"),
        ["System.String.TrimEnd/0"] = new("{this}.trimEnd()"),
        ["System.String.Trim/*"] = new("csjsTrim({this}, [{...}], 3)", Helper: "csjsTrim"),
        ["System.String.TrimStart/*"] = new("csjsTrim({this}, [{...}], 1)", Helper: "csjsTrim"),
        ["System.String.TrimEnd/*"] = new("csjsTrim({this}, [{...}], 2)", Helper: "csjsTrim"),
        ["System.String.Replace/2"] = new("{this}.replaceAll({0}, {1})"),
        ["System.String.Insert/2"] = new("csjsInsert({this}, {0}, {1})", Helper: "csjsInsert"),
        ["System.String.Remove/1"] = new("{this}.slice(0, {0})"),
        ["System.String.Remove/2"] = new("csjsRemoveAt({this}, {0}, {1})", Helper: "csjsRemoveAt"),
        ["System.String.Split/1"] = new("csjsSplit({this}, {0})", Helper: "csjsSplit"),
        ["System.String.Split/2"] = new("csjsSplit({this}, {0}, {1})", Helper: "csjsSplit"),
        ["System.String.Split/*"] = new("csjsSplit({this}, {0})", Helper: "csjsSplit"),
        ["System.String.PadLeft/2"] = new("{this}.padStart({0}, {1})"),
        ["System.String.PadLeft/1"] = new("{this}.padStart({0}, ' ')"),
        ["System.String.PadRight/2"] = new("{this}.padEnd({0}, {1})"),
        ["System.String.PadRight/1"] = new("{this}.padEnd({0}, ' ')"),
        ["System.String.ToString/0"] = new("{this}"),
        ["System.String.ToCharArray/0"] = new("{this}.split('')"),
        ["System.String.GetHashCode/0"] = new("csjsHash({this})", Helper: "csjsHash"),
        ["System.String.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.String.Concat/*"] = new("[{...}].join('')", StringifyArguments: true),
        // A single IEnumerable<string> argument concatenates its elements, not the sequence.
        ["System.String.Concat(System.Collections.Generic.IEnumerable<string>)"] = new("[...{0}].join('')"),
        ["System.String.Join(string,System.Collections.Generic.IEnumerable<string>)"] = new("[...{1}].join({0})"),
        ["System.String.Join(string,System.Collections.Generic.IEnumerable<T>)"] = new("csjsJoin({0}, {1})", Helper: "csjsJoin"),
        ["System.String.Join(char,System.Collections.Generic.IEnumerable<T>)"] = new("csjsJoin({0}, {1})", Helper: "csjsJoin"),
        ["System.String.Join/2"] = new("csjsJoin({0}, {1})", Helper: "csjsJoin"),
        ["System.String.Join/*"] = new("csjsJoin({0}, [{...1}])", Helper: "csjsJoin"),
        ["System.String.IsNullOrEmpty/1"] = new("(!{0})"),
        ["System.String.IsNullOrWhiteSpace/1"] = new("(!{0} || !{0}.trim())"),
        ["System.String.Format/*"] = new("csjsFormat({...})", Helper: "csjsFormat"),

        // ---- Object / conversion -------------------------------------------------------
        ["System.Object.ToString/0"] = new("csjsToString({this})", Helper: "csjsToString"),
        ["System.Object.Equals/1"] = new("csjsEquals({this}, {0})", Helper: "csjsEquals"),
        ["System.Object.Equals/2"] = new("csjsEquals({0}, {1})", Helper: "csjsEquals"),
        ["System.Object.ReferenceEquals/2"] = new("({0} === {1})"),
        // A type is its constructor at runtime; its Name is the constructor's name.
        ["System.Object.GetType/0"] = new("{this}.constructor"),
        ["System.Object.GetHashCode/0"] = new("csjsHash({this})", Helper: "csjsHash"),
        ["System.ValueType.Equals/1"] = new("csjsEquals({this}, {0})", Helper: "csjsEquals"),
        ["System.ValueType.ToString/0"] = new("csjsToString({this})", Helper: "csjsToString"),
        ["System.IComparable.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.Collections.Generic.IComparer.Compare/2"] = new("{this}.Compare({0}, {1})"),
        ["System.Int32.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.Double.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.Int32.Equals/1"] = new("({this} === {0})"),
        ["System.Double.Equals/1"] = new("({this} === {0})"),
        ["System.Int32.GetHashCode/0"] = new("{this}"),
        ["System.Int32.ToString/0"] = new("String({this})"),
        ["System.Int64.ToString/0"] = new("String({this})"),
        ["System.Double.ToString/0"] = new("String({this})"),
        ["System.Single.ToString/0"] = new("String({this})"),
        ["System.Decimal.ToString/0"] = new("String({this})"),
        // C# capitalises booleans when it converts them to text.
        ["System.Boolean.ToString/0"] = new("({this} ? 'True' : 'False')"),
        ["System.Boolean.Parse/1"] = new("csjsParseBool({0})", Helper: "csjsParseBool"),
        ["System.Int32.Parse/1"] = new("csjsParseInt({0})", Helper: "csjsParseInt"),
        ["System.Int32.TryParse/2"] = new("csjsTryParseInt({0})", Helper: "csjsTryParseInt"),
        ["System.Double.Parse/1"] = new("csjsParseFloat({0})", Helper: "csjsParseFloat"),
        ["System.Single.Parse/1"] = new("csjsParseFloat({0})", Helper: "csjsParseFloat"),
        ["System.Decimal.Parse/1"] = new("csjsParseFloat({0})", Helper: "csjsParseFloat"),
        ["System.Int64.Parse/1"] = new("BigInt({0})"),
        ["System.Double.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),
        ["System.Single.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),
        ["System.Decimal.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),
        ["System.Int32.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),
        ["System.Double.IsNaN/1"] = new("Number.isNaN({0})"),
        ["System.Double.IsInfinity/1"] = new("(!Number.isFinite({0}) && !Number.isNaN({0}))"),
        ["System.Double.IsPositiveInfinity/1"] = new("({0} === Infinity)"),
        ["System.Double.IsNegativeInfinity/1"] = new("({0} === -Infinity)"),
        ["System.Double.IsFinite/1"] = new("Number.isFinite({0})"),
        ["System.Single.IsNaN/1"] = new("Number.isNaN({0})"),
        ["System.Double.TryParse/2"] = new("csjsTryParseFloat({0})", Helper: "csjsTryParseFloat"),
        ["System.Int32.Max/2"] = new("Math.max({0}, {1})"),
        ["System.Int32.Min/2"] = new("Math.min({0}, {1})"),
        ["System.Int32.Abs/1"] = new("Math.abs({0})"),
        ["System.Int32.IsEvenInteger/1"] = new("({0} % 2 === 0)"),
        ["System.Int32.IsOddInteger/1"] = new("({0} % 2 !== 0)"),

        // Convert rounds to nearest-even where a cast would truncate; the two are easy to confuse.
        ["System.Convert.ToInt32(double)"] = new("csjsRoundEven({0})", Helper: "csjsRoundEven"),
        ["System.Convert.ToInt32(float)"] = new("csjsRoundEven({0})", Helper: "csjsRoundEven"),
        ["System.Convert.ToInt32(decimal)"] = new("csjsRoundEven({0})", Helper: "csjsRoundEven"),
        ["System.Convert.ToInt32(int)"] = new("{0}"),
        ["System.Convert.ToInt32(long)"] = new("Number({0})"),
        ["System.Convert.ToInt32(bool)"] = new("({0} ? 1 : 0)"),
        ["System.Convert.ToInt32(char)"] = new("{0}.charCodeAt(0)"),
        ["System.Convert.ToInt32(string,int)"] = new("csjsParseInt({0}, {1})", Helper: "csjsParseInt"),
        ["System.Convert.ToInt32/1"] = new("csjsParseInt({0})", Helper: "csjsParseInt"),
        ["System.Convert.ToDouble/1"] = new("Number({0})"),
        ["System.Convert.ToString(int,int)"] = new("{0}.toString({1})"),
        ["System.Convert.ToString(bool)"] = new("({0} ? 'True' : 'False')"),
        ["System.Convert.ToString/1"] = new("csjsToString({0})", Helper: "csjsToString"),
        ["System.Convert.ToBoolean/1"] = new("Boolean({0})"),
        ["System.Convert.ToChar(int)"] = new("String.fromCharCode({0})"),
        ["System.Convert.ToBase64String/1"] = new("csjsToBase64({0})", Helper: "csjsToBase64"),
        ["System.Convert.FromBase64String/1"] = new("csjsFromBase64({0})", Helper: "csjsFromBase64"),

        // ---- Char ----------------------------------------------------------------------
        // A C# char is a one-character string here, so these are string tests. Unicode
        // property escapes match what char.IsLetter means, not just ASCII.
        ["System.Char.IsDigit/1"] = new("(/^\\p{Nd}$/u.test({0}))"),
        ["System.Char.IsNumber/1"] = new("(/^\\p{N}$/u.test({0}))"),
        ["System.Char.IsLetter/1"] = new("(/^\\p{L}$/u.test({0}))"),
        ["System.Char.IsLetterOrDigit/1"] = new("(/^[\\p{L}\\p{Nd}]$/u.test({0}))"),
        ["System.Char.IsWhiteSpace/1"] = new("(/^\\s$/.test({0}))"),
        ["System.Char.IsPunctuation/1"] = new("(/^\\p{P}$/u.test({0}))"),
        ["System.Char.IsSymbol/1"] = new("(/^\\p{S}$/u.test({0}))"),
        ["System.Char.IsControl/1"] = new("(/^\\p{Cc}$/u.test({0}))"),
        ["System.Char.IsUpper/1"] = new("({0} === {0}.toUpperCase() && {0} !== {0}.toLowerCase())"),
        ["System.Char.IsLower/1"] = new("({0} === {0}.toLowerCase() && {0} !== {0}.toUpperCase())"),
        ["System.Char.ToUpper/1"] = new("{0}.toUpperCase()"),
        ["System.Char.ToLower/1"] = new("{0}.toLowerCase()"),
        ["System.Char.ToUpperInvariant/1"] = new("{0}.toUpperCase()"),
        ["System.Char.ToLowerInvariant/1"] = new("{0}.toLowerCase()"),
        ["System.Char.ToString/0"] = new("{this}"),
        ["System.Char.ToString/1"] = new("{0}"),
        ["System.Char.GetNumericValue/1"] = new("csjsNumericValue({0})", Helper: "csjsNumericValue"),
        ["System.Char.IsAsciiDigit/1"] = new("(/^[0-9]$/.test({0}))"),
        ["System.Char.IsAsciiLetter/1"] = new("(/^[A-Za-z]$/.test({0}))"),

        // ---- Array -----------------------------------------------------------------------
        ["System.Array.IndexOf/2"] = new("{0}.indexOf({1})"),
        ["System.Array.LastIndexOf/2"] = new("{0}.lastIndexOf({1})"),
        ["System.Array.Sort/1"] = new("csjsSort({0})", Helper: "csjsSort"),
        ["System.Array.Sort(T[],System.Comparison<T>)"] = new("{0}.sort({1})"),
        ["System.Array.Reverse/1"] = new("{0}.reverse()"),
        ["System.Array.Empty/0"] = new("[]"),
        ["System.Array.Fill/2"] = new("{0}.fill({1})"),
        ["System.Array.Exists/2"] = new("{0}.some({1})"),
        ["System.Array.TrueForAll/2"] = new("{0}.every({1})"),
        ["System.Array.Find/2"] = new("({0}.find({1}) ?? {default})"),
        ["System.Array.FindIndex/2"] = new("{0}.findIndex({1})"),
        ["System.Array.FindAll/2"] = new("{0}.filter({1})"),
        ["System.Array.ConvertAll/2"] = new("{0}.map({1})"),
        ["System.Array.Copy(System.Array,System.Array,int)"] = new("csjsArrayCopy({0}, 0, {1}, 0, {2})", Helper: "csjsArrayCopy"),
        ["System.Array.Copy(System.Array,int,System.Array,int,int)"] = new("csjsArrayCopy({0}, {1}, {2}, {3}, {4})", Helper: "csjsArrayCopy"),
        ["System.Array.Clone/0"] = new("[...{this}]"),
        ["System.Array.BinarySearch/2"] = new("csjsBinarySearch({0}, {1})", Helper: "csjsBinarySearch"),

        // ---- Math ----------------------------------------------------------------------
        ["System.Math.Abs/1"] = new("Math.abs({0})"),
        ["System.Math.Ceiling/1"] = new("Math.ceil({0})"),
        ["System.Math.Floor/1"] = new("Math.floor({0})"),
        // Math.Round rounds half to even ("banker's rounding"); Math.round rounds half up.
        ["System.Math.Round/1"] = new("csjsRoundEven({0})", Helper: "csjsRoundEven"),
        ["System.Math.Round(double,int)"] = new("csjsRound({0}, {1})", Helper: "csjsRound"),
        ["System.Math.Round(decimal,int)"] = new("csjsRound({0}, {1})", Helper: "csjsRound"),
        ["System.Math.Round(double,System.MidpointRounding)"] = new("csjsRound({0}, 0, {1})", Helper: "csjsRound"),
        ["System.Math.Round(double,int,System.MidpointRounding)"] = new("csjsRound({0}, {1}, {2})", Helper: "csjsRound"),
        ["System.Math.Truncate/1"] = new("Math.trunc({0})"),
        ["System.Math.Sqrt/1"] = new("Math.sqrt({0})"),
        ["System.Math.Cbrt/1"] = new("Math.cbrt({0})"),
        ["System.Math.Pow/2"] = new("Math.pow({0}, {1})"),
        ["System.Math.Exp/1"] = new("Math.exp({0})"),
        ["System.Math.Log/1"] = new("Math.log({0})"),
        ["System.Math.Log/2"] = new("(Math.log({0}) / Math.log({1}))"),
        ["System.Math.Log10/1"] = new("Math.log10({0})"),
        ["System.Math.Log2/1"] = new("Math.log2({0})"),
        ["System.Math.Sin/1"] = new("Math.sin({0})"),
        ["System.Math.Cos/1"] = new("Math.cos({0})"),
        ["System.Math.Tan/1"] = new("Math.tan({0})"),
        ["System.Math.Asin/1"] = new("Math.asin({0})"),
        ["System.Math.Acos/1"] = new("Math.acos({0})"),
        ["System.Math.Atan/1"] = new("Math.atan({0})"),
        ["System.Math.Atan2/2"] = new("Math.atan2({0}, {1})"),
        ["System.Math.Sinh/1"] = new("Math.sinh({0})"),
        ["System.Math.Cosh/1"] = new("Math.cosh({0})"),
        ["System.Math.Tanh/1"] = new("Math.tanh({0})"),
        ["System.Math.Min/2"] = new("Math.min({0}, {1})"),
        ["System.Math.Max/2"] = new("Math.max({0}, {1})"),
        ["System.Math.Sign/1"] = new("Math.sign({0})"),
        ["System.Math.Clamp/3"] = new("Math.min(Math.max({0}, {1}), {2})"),
        ["System.Math.DivRem/2"] = new("[Math.trunc({0} / {1}), {0} % {1}]"),
        ["System.Math.FusedMultiplyAdd/3"] = new("({0} * {1} + {2})"),
        ["System.Random.NextDouble/0"] = new("Math.random()"),
        ["System.Random.Next/0"] = new("Math.floor(Math.random() * 2147483647)"),
        ["System.Random.Next/1"] = new("Math.floor(Math.random() * {0})"),
        ["System.Random.Next/2"] = new("({0} + Math.floor(Math.random() * ({1} - {0})))"),
        ["System.Random.Shuffle/1"] = new("csjsShuffle({0})", Helper: "csjsShuffle"),

        // ---- Guid ----------------------------------------------------------------------
        ["System.Guid.NewGuid/0"] = new("crypto.randomUUID()"),
        ["System.Guid.ToString/0"] = new("{this}"),
        ["System.Guid.Parse/1"] = new("{0}.toLowerCase()"),

        // ---- List<T> -> Array ----------------------------------------------------------
        [$"{List}.Add/1"] = new("{this}.push({0})"),
        [$"{List}.AddRange/1"] = new("{this}.push(...{0})"),
        [$"{List}.Insert/2"] = new("{this}.splice({0}, 0, {1})"),
        [$"{List}.InsertRange/2"] = new("{this}.splice({0}, 0, ...{1})"),
        [$"{List}.RemoveAt/1"] = new("{this}.splice({0}, 1)"),
        [$"{List}.RemoveRange/2"] = new("{this}.splice({0}, {1})"),
        [$"{List}.Remove/1"] = new("csjsRemove({this}, {0})", Helper: "csjsRemove"),
        [$"{List}.RemoveAll/1"] = new("csjsRemoveAll({this}, {0})", Helper: "csjsRemoveAll"),
        [$"{List}.Clear/0"] = new("{this}.length = 0"),
        [$"{List}.Contains/1"] = new("{this}.includes({0})"),
        [$"{List}.IndexOf/1"] = new("{this}.indexOf({0})"),
        [$"{List}.LastIndexOf/1"] = new("{this}.lastIndexOf({0})"),
        [$"{List}.Sort/0"] = new("csjsSort({this})", Helper: "csjsSort"),
        [$"{List}.Sort(System.Comparison<T>)"] = new("{this}.sort({0})"),
        [$"{List}.Sort(System.Collections.Generic.IComparer<T>)"] = new("csjsSortWith({this}, {0})", Helper: "csjsSortWith"),
        [$"{List}.Reverse/0"] = new("{this}.reverse()"),
        [$"{List}.ToArray/0"] = new("[...{this}]"),
        [$"{List}.ForEach/1"] = new("{this}.forEach({0})"),
        [$"{List}.Find/1"] = new("({this}.find({0}) ?? {default})"),
        [$"{List}.FindLast/1"] = new("({this}.findLast({0}) ?? {default})"),
        [$"{List}.FindIndex/1"] = new("{this}.findIndex({0})"),
        [$"{List}.FindLastIndex/1"] = new("{this}.findLastIndex({0})"),
        [$"{List}.FindAll/1"] = new("{this}.filter({0})"),
        [$"{List}.Exists/1"] = new("{this}.some({0})"),
        [$"{List}.TrueForAll/1"] = new("{this}.every({0})"),
        [$"{List}.ConvertAll/1"] = new("{this}.map({0})"),
        [$"{List}.GetRange/2"] = new("{this}.slice({0}, {0} + {1})"),
        [$"{List}.AsReadOnly/0"] = new("{this}"),
        [$"{List}.BinarySearch/1"] = new("csjsBinarySearch({this}, {0})", Helper: "csjsBinarySearch"),

        // ---- Queue<T> / Stack<T> -> Array ----------------------------------------------
        // A stack keeps its top at index 0, because C# enumerates a Stack<T> top-first and an
        // Array enumerates from index 0. Pushing to the end would reverse every foreach.
        ["System.Collections.Generic.Queue.Enqueue/1"] = new("{this}.push({0})"),
        ["System.Collections.Generic.Queue.Dequeue/0"] = new("csjsTake({this}, 'Queue empty.')", Helper: "csjsTake"),
        ["System.Collections.Generic.Queue.Peek/0"] = new("csjsPeek({this}, 'Queue empty.')", Helper: "csjsPeek"),
        ["System.Collections.Generic.Queue.Contains/1"] = new("{this}.includes({0})"),
        ["System.Collections.Generic.Queue.Clear/0"] = new("{this}.length = 0"),
        ["System.Collections.Generic.Queue.ToArray/0"] = new("[...{this}]"),
        ["System.Collections.Generic.Stack.Push/1"] = new("{this}.unshift({0})"),
        ["System.Collections.Generic.Stack.Pop/0"] = new("csjsTake({this}, 'Stack empty.')", Helper: "csjsTake"),
        ["System.Collections.Generic.Stack.Peek/0"] = new("csjsPeek({this}, 'Stack empty.')", Helper: "csjsPeek"),
        ["System.Collections.Generic.Stack.Contains/1"] = new("{this}.includes({0})"),
        ["System.Collections.Generic.Stack.Clear/0"] = new("{this}.length = 0"),
        ["System.Collections.Generic.Stack.ToArray/0"] = new("[...{this}]"),

        // ---- Dictionary<K,V> -> Map ----------------------------------------------------
        [$"{Dictionary}.Add/2"] = new("csjsMapAdd({this}, {0}, {1})", Helper: "csjsMapAdd"),
        [$"{Dictionary}.Remove/1"] = new("{this}.delete({0})"),
        [$"{Dictionary}.ContainsKey/1"] = new("{this}.has({0})"),
        [$"{Dictionary}.ContainsValue/1"] = new("[...{this}.values()].includes({0})"),
        [$"{Dictionary}.Clear/0"] = new("{this}.clear()"),
        [$"{Dictionary}.TryGetValue/2"] = new("{this}.has({0})"),
        [$"{Dictionary}.TryAdd/2"] = new("csjsTryAdd({this}, {0}, {1})", Helper: "csjsTryAdd"),
        ["System.Collections.Generic.CollectionExtensions.GetValueOrDefault/1"] = new("({this}.get({0}) ?? {default})"),
        ["System.Collections.Generic.CollectionExtensions.GetValueOrDefault/2"] = new("({this}.has({0}) ? {this}.get({0}) : {1})"),
        ["System.Collections.Generic.CollectionExtensions.TryAdd/2"] = new("csjsTryAdd({this}, {0}, {1})", Helper: "csjsTryAdd"),
        ["System.Collections.Generic.IDictionary.ContainsKey/1"] = new("{this}.has({0})"),
        ["System.Collections.Generic.IDictionary.Add/2"] = new("csjsMapAdd({this}, {0}, {1})", Helper: "csjsMapAdd"),
        ["System.Collections.Generic.IDictionary.Remove/1"] = new("{this}.delete({0})"),
        ["System.Collections.Generic.IReadOnlyDictionary.ContainsKey/1"] = new("{this}.has({0})"),

        // ---- HashSet<T> -> Set ---------------------------------------------------------
        [$"{HashSet}.Add/1"] = new("csjsSetAdd({this}, {0})", Helper: "csjsSetAdd"),
        [$"{HashSet}.Contains/1"] = new("{this}.has({0})"),
        [$"{HashSet}.Remove/1"] = new("{this}.delete({0})"),
        [$"{HashSet}.Clear/0"] = new("{this}.clear()"),
        [$"{HashSet}.UnionWith/1"] = new("csjsUnionWith({this}, {0})", Helper: "csjsUnionWith"),
        [$"{HashSet}.IntersectWith/1"] = new("csjsIntersectWith({this}, {0})", Helper: "csjsIntersectWith"),
        [$"{HashSet}.ExceptWith/1"] = new("csjsExceptWith({this}, {0})", Helper: "csjsExceptWith"),
        [$"{HashSet}.IsSubsetOf/1"] = new("csjsIsSubsetOf({this}, {0})", Helper: "csjsIsSubsetOf"),
        [$"{HashSet}.IsSupersetOf/1"] = new("csjsIsSubsetOf(new Set({0}), {this})", Helper: "csjsIsSubsetOf"),
        [$"{HashSet}.Overlaps/1"] = new("[...{0}].some((x) => {this}.has(x))"),
        [$"{HashSet}.SetEquals/1"] = new("csjsSetEquals({this}, {0})", Helper: "csjsSetEquals"),
        [$"{HashSet}.RemoveWhere/1"] = new("csjsRemoveWhere({this}, {0})", Helper: "csjsRemoveWhere"),

        // ---- LINQ ----------------------------------------------------------------------
        // Extension receivers arrive as {this}; the lambda is {0}. JavaScript's array methods
        // pass the index as a second argument, so the indexed overloads come for free.
        [$"{Linq}.Select/1"] = new("{seq}.map({0})"),
        [$"{Linq}.Where/1"] = new("{seq}.filter({0})"),
        [$"{Linq}.Any/0"] = new("({seq}.length > 0)"),
        [$"{Linq}.Any/1"] = new("{seq}.some({0})"),
        [$"{Linq}.All/1"] = new("{seq}.every({0})"),
        [$"{Linq}.Count/0"] = new("{seq}.length"),
        [$"{Linq}.Count/1"] = new("{seq}.filter({0}).length"),
        [$"{Linq}.LongCount/0"] = new("BigInt({seq}.length)"),
        [$"{Linq}.First/0"] = new("csjsFirst({this})", Helper: "csjsFirst"),
        [$"{Linq}.First/1"] = new("csjsFirst({this}, {0})", Helper: "csjsFirst"),
        [$"{Linq}.FirstOrDefault/0"] = new("({seq}[0] ?? {default})"),
        [$"{Linq}.FirstOrDefault(System.Func<TSource,bool>)"] = new("({seq}.find({0}) ?? {default})"),
        [$"{Linq}.FirstOrDefault(TSource)"] = new("({seq}[0] ?? {0})"),
        [$"{Linq}.FirstOrDefault(System.Func<TSource,bool>,TSource)"] = new("({seq}.find({0}) ?? {1})"),
        [$"{Linq}.Last/0"] = new("csjsLast({this})", Helper: "csjsLast"),
        [$"{Linq}.Last/1"] = new("csjsLast({this}, {0})", Helper: "csjsLast"),
        [$"{Linq}.LastOrDefault/0"] = new("({seq}.at(-1) ?? {default})"),
        [$"{Linq}.LastOrDefault(System.Func<TSource,bool>)"] = new("({seq}.findLast({0}) ?? {default})"),
        [$"{Linq}.Single/0"] = new("csjsSingle({this})", Helper: "csjsSingle"),
        [$"{Linq}.Single/1"] = new("csjsSingle({this}, {0})", Helper: "csjsSingle"),
        [$"{Linq}.SingleOrDefault/0"] = new("(csjsSingleOrNull({this}) ?? {default})", Helper: "csjsSingleOrNull"),
        [$"{Linq}.SingleOrDefault(System.Func<TSource,bool>)"] = new("(csjsSingleOrNull({this}, {0}) ?? {default})", Helper: "csjsSingleOrNull"),
        [$"{Linq}.ElementAt/1"] = new("csjsElementAt({this}, {0})", Helper: "csjsElementAt"),
        [$"{Linq}.ElementAtOrDefault/1"] = new("({seq}[{0}] ?? {default})"),
        [$"{Linq}.ToList/0"] = new("{copy}"),
        [$"{Linq}.ToArray/0"] = new("{copy}"),
        [$"{Linq}.AsEnumerable/0"] = new("{this}"),
        [$"{Linq}.Cast/0"] = new("{seq}"),
        [$"{Linq}.ToHashSet/0"] = new("new Set({this})"),
        [$"{Linq}.ToDictionary/1"] = new("csjsToDictionary({this}, {0})", Helper: "csjsToDictionary"),
        [$"{Linq}.ToDictionary/2"] = new("csjsToDictionary({this}, {0}, {1})", Helper: "csjsToDictionary"),
        [$"{Linq}.OrderBy/1"] = new("csjsOrderBy({this}, {0}, false)", Helper: "csjsOrderBy"),
        [$"{Linq}.OrderByDescending/1"] = new("csjsOrderBy({this}, {0}, true)", Helper: "csjsOrderBy"),
        [$"{Linq}.Order/0"] = new("csjsOrderBy({this}, (x) => x, false)", Helper: "csjsOrderBy"),
        [$"{Linq}.OrderDescending/0"] = new("csjsOrderBy({this}, (x) => x, true)", Helper: "csjsOrderBy"),
        // ThenBy refines the ordering already established; it must not become the primary key.
        [$"{Linq}.ThenBy/1"] = new("csjsThenBy({this}, {0}, false)", Helper: "csjsThenBy"),
        [$"{Linq}.ThenByDescending/1"] = new("csjsThenBy({this}, {0}, true)", Helper: "csjsThenBy"),
        [$"{Linq}.Sum/0"] = new("csjsSum({this})", Helper: "csjsSum"),
        [$"{Linq}.Sum/1"] = new("csjsSum({this}, {0})", Helper: "csjsSum"),
        [$"{Linq}.Average/0"] = new("csjsAverage({this})", Helper: "csjsAverage"),
        [$"{Linq}.Average/1"] = new("csjsAverage({this}, {0})", Helper: "csjsAverage"),
        // Math.min over an empty spread is Infinity; C# throws. The helper keeps the throw.
        [$"{Linq}.Min/0"] = new("csjsMin({this})", Helper: "csjsMin"),
        [$"{Linq}.Max/0"] = new("csjsMax({this})", Helper: "csjsMax"),
        [$"{Linq}.Min/1"] = new("csjsMin({this}, {0})", Helper: "csjsMin"),
        [$"{Linq}.Max/1"] = new("csjsMax({this}, {0})", Helper: "csjsMax"),
        [$"{Linq}.MinBy/1"] = new("csjsMinBy({this}, {0})", Helper: "csjsMinBy"),
        [$"{Linq}.MaxBy/1"] = new("csjsMaxBy({this}, {0})", Helper: "csjsMaxBy"),
        [$"{Linq}.Take/1"] = new("{seq}.slice(0, Math.max(0, {0}))"),
        [$"{Linq}.Skip/1"] = new("{seq}.slice(Math.max(0, {0}))"),
        [$"{Linq}.TakeLast/1"] = new("csjsTakeLast({this}, {0})", Helper: "csjsTakeLast"),
        [$"{Linq}.SkipLast/1"] = new("csjsSkipLast({this}, {0})", Helper: "csjsSkipLast"),
        [$"{Linq}.TakeWhile/1"] = new("csjsTakeWhile({this}, {0})", Helper: "csjsTakeWhile"),
        [$"{Linq}.SkipWhile/1"] = new("csjsSkipWhile({this}, {0})", Helper: "csjsSkipWhile"),
        // Reverse() is a new sequence in LINQ; Array.prototype.reverse would mutate the source.
        [$"{Linq}.Reverse/0"] = new("[...{this}].reverse()"),
        [$"{Linq}.Distinct/0"] = new("[...new Set({this})]"),
        [$"{Linq}.DistinctBy/1"] = new("csjsDistinctBy({this}, {0})", Helper: "csjsDistinctBy"),
        [$"{Linq}.Concat/1"] = new("[...{this}, ...{0}]"),
        [$"{Linq}.Union/1"] = new("[...new Set([...{this}, ...{0}])]"),
        [$"{Linq}.Intersect/1"] = new("csjsIntersect({this}, {0})", Helper: "csjsIntersect"),
        [$"{Linq}.Except/1"] = new("csjsExcept({this}, {0})", Helper: "csjsExcept"),
        [$"{Linq}.UnionBy/2"] = new("csjsDistinctBy([...{this}, ...{0}], {1})", Helper: "csjsDistinctBy"),
        [$"{Linq}.SelectMany/1"] = new("csjsSelectMany({this}, {0})", Helper: "csjsSelectMany"),
        [$"{Linq}.SelectMany/2"] = new("csjsSelectMany({this}, {0}, {1})", Helper: "csjsSelectMany"),
        [$"{Linq}.Contains/1"] = new("{seq}.includes({0})"),
        [$"{Linq}.GroupBy/1"] = new("csjsGroupBy({this}, {0})", Helper: "csjsGroupBy"),
        [$"{Linq}.GroupBy(System.Func<TSource,TKey>,System.Func<TSource,TElement>)"] = new("csjsGroupBy({this}, {0}, {1})", Helper: "csjsGroupBy"),
        [$"{Linq}.GroupBy(System.Func<TSource,TKey>,System.Func<TKey,System.Collections.Generic.IEnumerable<TSource>,TResult>)"] = new("csjsGroupBy({this}, {0}).map((g) => ({1})(g.key, g))", Helper: "csjsGroupBy"),
        [$"{Linq}.CountBy/2"] = new("csjsCountBy({this}, {0})", Helper: "csjsCountBy"),
        [$"{Linq}.Join/4"] = new("csjsInnerJoin({this}, {0}, {1}, {2}, {3})", Helper: "csjsInnerJoin"),
        [$"{Linq}.GroupJoin/4"] = new("csjsGroupJoin({this}, {0}, {1}, {2}, {3})", Helper: "csjsGroupJoin"),
        [$"{Linq}.Zip(System.Collections.Generic.IEnumerable<TSecond>)"] = new("csjsZip({this}, {0})", Helper: "csjsZip"),
        [$"{Linq}.Zip(System.Collections.Generic.IEnumerable<TSecond>,System.Func<TFirst,TSecond,TResult>)"] = new("csjsZip({this}, {0}, {1})", Helper: "csjsZip"),
        [$"{Linq}.Aggregate/1"] = new("csjsAggregate({this}, {0})", Helper: "csjsAggregate"),
        [$"{Linq}.Aggregate/2"] = new("{seq}.reduce({1}, {0})"),
        [$"{Linq}.Aggregate/3"] = new("({2})({seq}.reduce({1}, {0}))"),
        [$"{Linq}.SequenceEqual/1"] = new("csjsSequenceEqual({this}, {0})", Helper: "csjsSequenceEqual"),
        [$"{Linq}.Chunk/1"] = new("csjsChunk({this}, {0})", Helper: "csjsChunk"),
        [$"{Linq}.Append/1"] = new("[...{this}, {0}]"),
        [$"{Linq}.Prepend/1"] = new("[{0}, ...{this}]"),
        [$"{Linq}.DefaultIfEmpty/0"] = new("csjsDefaultIfEmpty({this}, {default})", Helper: "csjsDefaultIfEmpty"),
        [$"{Linq}.Range/2"] = new("csjsRange({0}, {1})", Helper: "csjsRange"),
        [$"{Linq}.Repeat/2"] = new("new Array({1}).fill({0})"),
        [$"{Linq}.Empty/0"] = new("[]"),
        [$"{Linq}.Index/0"] = new("{seq}.map((x, i) => [i, x])"),

        // ---- MemoryExtensions ----------------------------------------------------------
        // C# 14 converts arrays to spans implicitly, so `array.Contains(x)` and friends now bind
        // here rather than to LINQ. A span over an array is that array in JavaScript.
        ["System.MemoryExtensions.Contains/1"] = new("{this}.includes({0})"),
        ["System.MemoryExtensions.IndexOf/1"] = new("{this}.indexOf({0})"),
        ["System.MemoryExtensions.LastIndexOf/1"] = new("{this}.lastIndexOf({0})"),
        ["System.MemoryExtensions.SequenceEqual/1"] = new("csjsSequenceEqual({this}, {0})", Helper: "csjsSequenceEqual"),
        ["System.MemoryExtensions.StartsWith/1"] = new("csjsSpanStartsWith({this}, {0})", Helper: "csjsSpanStartsWith"),
        ["System.MemoryExtensions.EndsWith/1"] = new("csjsSpanEndsWith({this}, {0})", Helper: "csjsSpanEndsWith"),
        // Span.Reverse reverses in place, unlike LINQ's Reverse.
        ["System.MemoryExtensions.Reverse/0"] = new("{this}.reverse()"),
        ["System.MemoryExtensions.Sort/0"] = new("csjsSort({this})", Helper: "csjsSort"),
        ["System.MemoryExtensions.ContainsAny/2"] = new("({this}.includes({0}) || {this}.includes({1}))"),
        ["System.MemoryExtensions.Count/1"] = new("{this}.filter((x) => csjsEquals(x, {0})).length", Helper: "csjsEquals"),

        // ---- Task ----------------------------------------------------------------------
        ["System.Threading.Tasks.Task.Delay/1"] = new("csjsDelay({0})", Helper: "csjsDelay"),
        ["System.Threading.Tasks.Task.FromResult/1"] = new("Promise.resolve({0})"),
        ["System.Threading.Tasks.Task.WhenAll(System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task>)"] = new("Promise.all({0})"),
        ["System.Threading.Tasks.Task.WhenAll(System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<TResult>>)"] = new("Promise.all({0})"),
        ["System.Threading.Tasks.Task.WhenAll/*"] = new("Promise.all([{...}])"),
        ["System.Threading.Tasks.Task.WhenAny/*"] = new("Promise.race([{...}])"),
        ["System.Threading.Tasks.Task.Run/1"] = new("Promise.resolve().then({0})"),
        ["System.Threading.Tasks.Task.Yield/0"] = new("csjsDelay(0)", Helper: "csjsDelay"),

        // ---- DateTime ------------------------------------------------------------------
        ["System.DateTime.ToString/0"] = new("{this}.toISOString()"),
        ["System.DateTime.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),
        ["System.DateTime.ToShortDateString/0"] = new("csjsFormatValue({this}, 'd')", Helper: "csjsFormatValue"),
        ["System.DateTime.ToLongDateString/0"] = new("csjsFormatValue({this}, 'D')", Helper: "csjsFormatValue"),
        ["System.DateTime.ToShortTimeString/0"] = new("csjsFormatValue({this}, 't')", Helper: "csjsFormatValue"),
        ["System.DateTime.AddDays/1"] = new("csjsAddMs({this}, {0} * 86400000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddHours/1"] = new("csjsAddMs({this}, {0} * 3600000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddMinutes/1"] = new("csjsAddMs({this}, {0} * 60000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddSeconds/1"] = new("csjsAddMs({this}, {0} * 1000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddMilliseconds/1"] = new("csjsAddMs({this}, {0})", Helper: "csjsAddMs"),
        ["System.DateTime.AddMonths/1"] = new("csjsAddMonths({this}, {0})", Helper: "csjsAddMonths"),
        ["System.DateTime.AddYears/1"] = new("csjsAddMonths({this}, {0} * 12)", Helper: "csjsAddMonths"),
        ["System.DateTime.Add/1"] = new("csjsAddMs({this}, {0})", Helper: "csjsAddMs"),
        ["System.DateTime.Subtract(System.DateTime)"] = new("({this} - {0})"),
        ["System.DateTime.Subtract(System.TimeSpan)"] = new("csjsAddMs({this}, -({0}))", Helper: "csjsAddMs"),
        ["System.DateTime.CompareTo/1"] = new("csjsCompare({this}.getTime(), {0}.getTime())", Helper: "csjsCompare"),
        ["System.DateTime.Equals/1"] = new("csjsEquals({this}, {0})", Helper: "csjsEquals"),
        ["System.DateTime.ToUniversalTime/0"] = new("{this}"),
        ["System.DateTime.ToLocalTime/0"] = new("{this}"),
        ["System.DateTime.Parse/1"] = new("csjsParseDate({0})", Helper: "csjsParseDate"),
        ["System.DateTime.IsLeapYear/1"] = new("csjsIsLeapYear({0})", Helper: "csjsIsLeapYear"),
        ["System.DateTime.DaysInMonth/2"] = new("new Date({0}, {1}, 0).getDate()"),
        // Operators on the framework's DateTime: JavaScript would concatenate a Date and a number.
        ["System.DateTime.op_Subtraction(System.DateTime,System.DateTime)"] = new("({0} - {1})"),
        ["System.DateTime.op_Subtraction(System.DateTime,System.TimeSpan)"] = new("csjsAddMs({0}, -({1}))", Helper: "csjsAddMs"),
        ["System.DateTime.op_Addition(System.DateTime,System.TimeSpan)"] = new("csjsAddMs({0}, {1})", Helper: "csjsAddMs"),

        // ---- TimeSpan -> milliseconds --------------------------------------------------
        // A TimeSpan is a number of milliseconds. That is what subtracting two Dates yields in
        // JavaScript, so `end - start` needs no conversion at all.
        // .NET 9 added integer overloads (FromMinutes(long)), whose argument arrives as a BigInt.
        ["System.TimeSpan.FromMilliseconds/1"] = new("Number({0})"),
        ["System.TimeSpan.FromSeconds/1"] = new("(Number({0}) * 1000)"),
        ["System.TimeSpan.FromMinutes/1"] = new("(Number({0}) * 60000)"),
        ["System.TimeSpan.FromHours/1"] = new("(Number({0}) * 3600000)"),
        ["System.TimeSpan.FromDays/1"] = new("(Number({0}) * 86400000)"),
        ["System.TimeSpan.FromTicks/1"] = new("(Number({0}) / 10000)"),
        ["System.TimeSpan.Add/1"] = new("({this} + {0})"),
        ["System.TimeSpan.Subtract/1"] = new("({this} - {0})"),
        ["System.TimeSpan.Negate/0"] = new("(-{this})"),
        ["System.TimeSpan.Duration/0"] = new("Math.abs({this})"),
        ["System.TimeSpan.ToString/0"] = new("csjsTimeSpanToString({this})", Helper: "csjsTimeSpanToString"),
        ["System.TimeSpan.ToString/1"] = new("csjsTimeSpanToString({this}, {0})", Helper: "csjsTimeSpanToString"),
        ["System.TimeSpan.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.TimeSpan.Equals/1"] = new("({this} === {0})"),

        // ---- Regex ---------------------------------------------------------------------
        // A Regex is a RegExp without the global flag; operations that scan the whole input make
        // a global copy, so `lastIndex` state never leaks between calls the way it would if the
        // shared object carried /g.
        [$"{Regex}.IsMatch(string)"] = new("{this}.test({0})"),
        [$"{Regex}.IsMatch(string,string)"] = new("csjsRegex({1}).test({0})", Helper: "csjsRegex"),
        [$"{Regex}.IsMatch(string,string,System.Text.RegularExpressions.RegexOptions)"] = new("csjsRegex({1}, {2}).test({0})", Helper: "csjsRegex"),
        [$"{Regex}.Match(string)"] = new("csjsMatch({this}, {0})", Helper: "csjsMatch"),
        [$"{Regex}.Match(string,string)"] = new("csjsMatch(csjsRegex({1}), {0})", Helper: "csjsMatch"),
        [$"{Regex}.Match(string,string,System.Text.RegularExpressions.RegexOptions)"] = new("csjsMatch(csjsRegex({1}, {2}), {0})", Helper: "csjsMatch"),
        [$"{Regex}.Matches(string)"] = new("csjsMatches({this}, {0})", Helper: "csjsMatches"),
        [$"{Regex}.Matches(string,string)"] = new("csjsMatches(csjsRegex({1}), {0})", Helper: "csjsMatches"),
        [$"{Regex}.Matches(string,string,System.Text.RegularExpressions.RegexOptions)"] = new("csjsMatches(csjsRegex({1}, {2}), {0})", Helper: "csjsMatches"),
        [$"{Regex}.Replace(string,string)"] = new("csjsRegexReplace({this}, {0}, {1})", Helper: "csjsRegexReplace"),
        [$"{Regex}.Replace(string,System.Text.RegularExpressions.MatchEvaluator)"] = new("csjsRegexReplace({this}, {0}, {1})", Helper: "csjsRegexReplace"),
        [$"{Regex}.Replace(string,string,string)"] = new("csjsRegexReplace(csjsRegex({1}), {0}, {2})", Helper: "csjsRegexReplace"),
        [$"{Regex}.Replace(string,string,System.Text.RegularExpressions.MatchEvaluator)"] = new("csjsRegexReplace(csjsRegex({1}), {0}, {2})", Helper: "csjsRegexReplace"),
        [$"{Regex}.Replace(string,string,string,System.Text.RegularExpressions.RegexOptions)"] = new("csjsRegexReplace(csjsRegex({1}, {3}), {0}, {2})", Helper: "csjsRegexReplace"),
        [$"{Regex}.Split(string)"] = new("{0}.split({this})"),
        [$"{Regex}.Split(string,string)"] = new("{0}.split(csjsRegex({1}))", Helper: "csjsRegex"),
        [$"{Regex}.Escape/1"] = new("csjsRegexEscape({0})", Helper: "csjsRegexEscape"),
        [$"{Regex}.ToString/0"] = new("{this}.source"),
        ["System.Text.RegularExpressions.Match.NextMatch/0"] = new("{this}.nextMatch()"),
        ["System.Text.RegularExpressions.Capture.ToString/0"] = new("{this}.value"),
        ["System.Text.RegularExpressions.Match.Result/1"] = new("{this}.result({0})"),

        // ---- System.Text.Json ----------------------------------------------------------
        // Serialize and Deserialize<T> are handled by the emitter, which passes a description
        // of T so the result has real class instances and real Maps, not bare objects.

        // ---- Environment ---------------------------------------------------------------
        ["System.Environment.GetEnvironmentVariable/1"] = new("(globalThis.process?.env?.[{0}] ?? null)"),
    };

    // ---------------------------------------------------------------------------------------
    // Properties and fields
    // ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, JsTemplate> Properties = new(StringComparer.Ordinal)
    {
        ["System.String.Length"] = new("{this}.length"),
        ["System.String.Empty"] = new("''"),
        ["System.Array.Length"] = new("{this}.length"),
        [$"{List}.Count"] = new("{this}.length"),
        ["System.Collections.Generic.Queue.Count"] = new("{this}.length"),
        ["System.Collections.Generic.Stack.Count"] = new("{this}.length"),
        ["System.Collections.Generic.ICollection.Count"] = new("csjsCount({this})", Helper: "csjsCount"),
        ["System.Collections.Generic.IReadOnlyCollection.Count"] = new("csjsCount({this})", Helper: "csjsCount"),
        [$"{Dictionary}.Count"] = new("{this}.size"),
        [$"{Dictionary}.Keys"] = new("[...{this}.keys()]"),
        [$"{Dictionary}.Values"] = new("[...{this}.values()]"),
        ["System.Collections.Generic.IDictionary.Keys"] = new("[...{this}.keys()]"),
        ["System.Collections.Generic.IDictionary.Values"] = new("[...{this}.values()]"),
        ["System.Collections.Generic.IReadOnlyDictionary.Keys"] = new("[...{this}.keys()]"),
        ["System.Collections.Generic.IReadOnlyDictionary.Values"] = new("[...{this}.values()]"),
        [$"{HashSet}.Count"] = new("{this}.size"),
        ["System.Environment.NewLine"] = new("'\\n'"),
        ["System.Reflection.MemberInfo.Name"] = new("{this}.name"),
        ["System.Type.FullName"] = new("{this}.name"),

        // Exceptions become JavaScript Errors, whose members are lowercase.
        ["System.Exception.Message"] = new("{this}.message"),
        ["System.Exception.StackTrace"] = new("{this}.stack"),
        ["System.Exception.InnerException"] = new("({this}.cause ?? null)"),

        ["System.Math.PI"] = new("Math.PI"),
        ["System.Math.E"] = new("Math.E"),
        ["System.Math.Tau"] = new("(Math.PI * 2)"),

        ["System.Int32.MaxValue"] = new("2147483647"),
        ["System.Int32.MinValue"] = new("(-2147483648)"),
        ["System.UInt32.MaxValue"] = new("4294967295"),
        ["System.Int16.MaxValue"] = new("32767"),
        ["System.Int16.MinValue"] = new("(-32768)"),
        ["System.Byte.MaxValue"] = new("255"),
        ["System.Int64.MaxValue"] = new("9223372036854775807n"),
        ["System.Int64.MinValue"] = new("(-9223372036854775808n)"),
        ["System.Double.MaxValue"] = new("Number.MAX_VALUE"),
        ["System.Double.MinValue"] = new("(-Number.MAX_VALUE)"),
        ["System.Double.Epsilon"] = new("Number.MIN_VALUE"),
        ["System.Double.PositiveInfinity"] = new("Infinity"),
        ["System.Double.NegativeInfinity"] = new("(-Infinity)"),
        ["System.Double.NaN"] = new("NaN"),

        ["System.Guid.Empty"] = new("'00000000-0000-0000-0000-000000000000'"),

        ["System.DateTime.Now"] = new("new Date()"),
        ["System.DateTime.UtcNow"] = new("new Date()"),
        ["System.DateTime.Today"] = new("csjsToday()", Helper: "csjsToday"),
        ["System.DateTime.MinValue"] = new("new Date(-62135596800000)"),
        ["System.DateTime.MaxValue"] = new("new Date(253402300799999)"),
        ["System.DateTime.Year"] = new("{this}.getFullYear()"),
        ["System.DateTime.Month"] = new("({this}.getMonth() + 1)"),
        ["System.DateTime.Day"] = new("{this}.getDate()"),
        ["System.DateTime.Hour"] = new("{this}.getHours()"),
        ["System.DateTime.Minute"] = new("{this}.getMinutes()"),
        ["System.DateTime.Second"] = new("{this}.getSeconds()"),
        ["System.DateTime.Millisecond"] = new("{this}.getMilliseconds()"),
        ["System.DateTime.DayOfWeek"] = new("{this}.getDay()"),
        ["System.DateTime.DayOfYear"] = new("csjsDayOfYear({this})", Helper: "csjsDayOfYear"),
        ["System.DateTime.Date"] = new("csjsDateOnly({this})", Helper: "csjsDateOnly"),
        ["System.DateTime.TimeOfDay"] = new("({this} - csjsDateOnly({this}))", Helper: "csjsDateOnly"),
        ["System.DateTime.Ticks"] = new("(BigInt({this}.getTime()) * 10000n + 621355968000000000n)"),

        ["System.TimeSpan.Zero"] = new("0"),
        ["System.TimeSpan.MaxValue"] = new("Number.MAX_SAFE_INTEGER"),
        ["System.TimeSpan.MinValue"] = new("Number.MIN_SAFE_INTEGER"),
        ["System.TimeSpan.TotalMilliseconds"] = new("{this}"),
        ["System.TimeSpan.TotalSeconds"] = new("({this} / 1000)"),
        ["System.TimeSpan.TotalMinutes"] = new("({this} / 60000)"),
        ["System.TimeSpan.TotalHours"] = new("({this} / 3600000)"),
        ["System.TimeSpan.TotalDays"] = new("({this} / 86400000)"),
        ["System.TimeSpan.Milliseconds"] = new("(Math.trunc({this}) % 1000)"),
        ["System.TimeSpan.Seconds"] = new("(Math.trunc({this} / 1000) % 60)"),
        ["System.TimeSpan.Minutes"] = new("(Math.trunc({this} / 60000) % 60)"),
        ["System.TimeSpan.Hours"] = new("(Math.trunc({this} / 3600000) % 24)"),
        ["System.TimeSpan.Days"] = new("Math.trunc({this} / 86400000)"),
        ["System.TimeSpan.Ticks"] = new("BigInt(Math.round({this} * 10000))"),

        // A group is an Array carrying its key, matching IGrouping<K,T>'s dual nature.
        ["System.Linq.IGrouping.Key"] = new("{this}.key"),

        // Map iteration yields [key, value] pairs, so KeyValuePair is just that array.
        // This is what lets `foreach (var kvp in dictionary)` emit a plain for..of loop.
        ["System.Collections.Generic.KeyValuePair.Key"] = new("{this}[0]"),
        ["System.Collections.Generic.KeyValuePair.Value"] = new("{this}[1]"),

        // Regex results are plain objects built by csjsMatch; their members are lowercase.
        ["System.Text.RegularExpressions.Capture.Value"] = new("{this}.value"),
        ["System.Text.RegularExpressions.Capture.Index"] = new("{this}.index"),
        ["System.Text.RegularExpressions.Capture.Length"] = new("{this}.length"),
        ["System.Text.RegularExpressions.Group.Success"] = new("{this}.success"),
        ["System.Text.RegularExpressions.Group.Name"] = new("{this}.name"),
        ["System.Text.RegularExpressions.Match.Groups"] = new("{this}.groups"),
        ["System.Text.RegularExpressions.Match.Empty"] = new("csjsMatch(/(?!)/, '')", Helper: "csjsMatch"),
        ["System.Text.RegularExpressions.GroupCollection.Count"] = new("{this}.length"),
        ["System.Text.RegularExpressions.MatchCollection.Count"] = new("{this}.length"),

        ["System.Text.Json.JsonNamingPolicy.CamelCase"] = new("'camelCase'"),
        ["System.Text.Json.JsonNamingPolicy.SnakeCaseLower"] = new("'snake_case_lower'"),
        ["System.Text.Json.JsonNamingPolicy.KebabCaseLower"] = new("'kebab-case-lower'"),
    };

    // ---------------------------------------------------------------------------------------
    // Constructors
    // ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, JsTemplate> Constructors = new(StringComparer.Ordinal)
    {
        [$"{List}/0"] = new("[]"),
        // A capacity is a hint in C#; spreading a number would throw.
        [$"{List}(int)"] = new("[]"),
        [$"{List}/1"] = new("[...{0}]"),
        ["System.Collections.Generic.Queue/0"] = new("[]"),
        ["System.Collections.Generic.Queue(int)"] = new("[]"),
        ["System.Collections.Generic.Queue/1"] = new("[...{0}]"),
        ["System.Collections.Generic.Stack/0"] = new("[]"),
        ["System.Collections.Generic.Stack(int)"] = new("[]"),
        // new Stack<T>(items) pushes each in turn, so the last item ends on top.
        ["System.Collections.Generic.Stack/1"] = new("[...{0}].reverse()"),
        [$"{Dictionary}/0"] = new("new Map()"),
        [$"{Dictionary}(System.Collections.Generic.IDictionary<TKey,TValue>)"] = new("new Map({0})"),
        [$"{Dictionary}(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<TKey,TValue>>)"] = new("new Map({0})"),
        [$"{Dictionary}/1"] = new("new Map()"),
        [$"{HashSet}/0"] = new("new Set()"),
        [$"{HashSet}(int)"] = new("new Set()"),
        [$"{HashSet}(System.Collections.Generic.IEnumerable<T>)"] = new("new Set({0})"),
        [$"{HashSet}/1"] = new("new Set()"),
        ["System.String(char,int)"] = new("{0}.repeat({1})"),
        ["System.String(char[])"] = new("{0}.join('')"),
        ["System.Text.StringBuilder/0"] = new("csjsStringBuilder()", Helper: "csjsStringBuilder"),
        ["System.Text.StringBuilder(string)"] = new("csjsStringBuilder({0})", Helper: "csjsStringBuilder"),
        ["System.Text.StringBuilder(int)"] = new("csjsStringBuilder()", Helper: "csjsStringBuilder"),
        ["System.Random/*"] = new("null"),
        ["System.DateTime/3"] = new("new Date({0}, {1} - 1, {2})"),
        ["System.DateTime/6"] = new("new Date({0}, {1} - 1, {2}, {3}, {4}, {5})"),
        ["System.DateTime/7"] = new("new Date({0}, {1} - 1, {2}, {3}, {4}, {5}, {6})"),
        ["System.TimeSpan(int,int,int)"] = new("(({0} * 3600 + {1} * 60 + {2}) * 1000)"),
        ["System.TimeSpan(int,int,int,int)"] = new("((({0} * 24 + {1}) * 3600 + {2} * 60 + {3}) * 1000)"),
        ["System.TimeSpan(int,int,int,int,int)"] = new("((({0} * 24 + {1}) * 3600 + {2} * 60 + {3}) * 1000 + {4})"),
        ["System.TimeSpan(long)"] = new("(Number({0}) / 10000)"),
        [$"{Regex}(string)"] = new("csjsRegex({0})", Helper: "csjsRegex"),
        [$"{Regex}(string,System.Text.RegularExpressions.RegexOptions)"] = new("csjsRegex({0}, {1})", Helper: "csjsRegex"),
        ["System.Text.Json.JsonSerializerOptions/0"] = new("{}"),
        ["System.Guid(string)"] = new("{0}.toLowerCase()"),
        // Exception construction is handled by the emitter rather than a template, because the
        // C# type hierarchy has to travel with the error for typed catch clauses to work.
    };

    /// <summary>StringBuilder members, resolved against the helper object the constructor returns.</summary>
    static MemberMap()
    {
        const string sb = "System.Text.StringBuilder";
        Methods[$"{sb}.Append/1"] = new("{this}.append({0})", StringifyArguments: true);
        Methods[$"{sb}.Append(char,int)"] = new("{this}.append({0}.repeat({1}))");
        Methods[$"{sb}.AppendLine/1"] = new("{this}.appendLine({0})", StringifyArguments: true);
        Methods[$"{sb}.AppendLine/0"] = new("{this}.appendLine('')");
        Methods[$"{sb}.AppendFormat/*"] = new("{this}.append(csjsFormat({...}))", Helper: "csjsFormat");
        Methods[$"{sb}.AppendJoin/*"] = new("{this}.append(csjsJoin({0}, [{...1}]))", Helper: "csjsJoin");
        Methods[$"{sb}.AppendJoin(string,System.Collections.Generic.IEnumerable<T>)"] = new("{this}.append(csjsJoin({0}, {1}))", Helper: "csjsJoin");
        Methods[$"{sb}.Insert/2"] = new("{this}.insert({0}, {1})", StringifyArguments: true);
        Methods[$"{sb}.Replace/2"] = new("{this}.replace({0}, {1})");
        Methods[$"{sb}.Remove/2"] = new("{this}.remove({0}, {1})");
        Methods[$"{sb}.ToString/0"] = new("{this}.toString()");
        Methods[$"{sb}.Clear/0"] = new("{this}.clear()");
        Properties[$"{sb}.Length"] = new("{this}.length()");

        // MathF mirrors Math; the float/double distinction does not survive into JavaScript.
        foreach (var name in new[] { "Abs", "Ceiling", "Floor", "Sqrt", "Pow", "Sin", "Cos", "Tan", "Atan2",
                     "Min", "Max", "Sign", "Exp", "Log", "Truncate", "Round", "Asin", "Acos", "Atan" })
        {
            foreach (var arity in new[] { 1, 2, 3 })
            {
                if (Methods.TryGetValue($"System.Math.{name}/{arity}", out var template))
                    Methods[$"System.MathF.{name}/{arity}"] = template;
            }
        }
        Methods["System.MathF.Round(float,int)"] = Methods["System.Math.Round(double,int)"];
        Properties["System.MathF.PI"] = Properties["System.Math.PI"];
        Properties["System.MathF.E"] = Properties["System.Math.E"];
        Properties["System.Single.MaxValue"] = new("3.4028234663852886e38");
        Properties["System.Single.MinValue"] = new("(-3.4028234663852886e38)");
        Properties["System.Single.NaN"] = new("NaN");
        Properties["System.Single.PositiveInfinity"] = new("Infinity");
        Properties["System.Single.NegativeInfinity"] = new("(-Infinity)");
        Properties["System.Decimal.Zero"] = new("0");
        Properties["System.Decimal.One"] = new("1");

        // The other integral parse/format entry points share int's behaviour.
        foreach (var type in new[] { "Int16", "Byte", "SByte", "UInt16", "UInt32" })
        {
            Methods[$"System.{type}.Parse/1"] = Methods["System.Int32.Parse/1"];
            Methods[$"System.{type}.ToString/0"] = Methods["System.Int32.ToString/0"];
            Methods[$"System.{type}.ToString/1"] = Methods["System.Int32.ToString/1"];
            Methods[$"System.{type}.CompareTo/1"] = Methods["System.Int32.CompareTo/1"];
        }
        Properties["System.UInt16.MaxValue"] = new("65535");
        Properties["System.SByte.MaxValue"] = new("127");
        Properties["System.SByte.MinValue"] = new("(-128)");
        Properties["System.Byte.MinValue"] = new("0");
        Properties["System.UInt32.MinValue"] = new("0");
    }
}
