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
///   <item><c>{0}</c>…<c>{n}</c> — positional arguments.</item>
///   <item><c>{...}</c> — every argument, comma-joined.</item>
///   <item><c>{...n}</c> — arguments from index <c>n</c> onward.</item>
/// </list>
/// </summary>
/// <param name="Pattern">The JavaScript to emit.</param>
/// <param name="Helper">
/// Name of a runtime helper the pattern calls. When set, the emitter adds a named import from the
/// runtime module. Helpers are imported one at a time so bundlers drop the ones nobody used.
/// </param>
public sealed record JsTemplate(string Pattern, string? Helper = null);

/// <summary>
/// Maps BCL members onto JavaScript. Anything absent here is either emitted structurally
/// (user-declared types) or reported as unsupported, which keeps the failure mode honest
/// instead of silently emitting broken JavaScript.
/// </summary>
public static class MemberMap
{
    /// <summary>Looks up a method. Returns null when the method has no mapping.</summary>
    public static JsTemplate? Find(IMethodSymbol method)
    {
        var target = method.ReducedFrom ?? method.OriginalDefinition;
        var argCount = target.IsExtensionMethod && method.ReducedFrom is not null
            ? target.Parameters.Length - 1
            : method.Parameters.Length;

        var key = $"{OwnerName(target)}.{target.Name}/{argCount}";
        if (Methods.TryGetValue(key, out var t)) return t;

        // Fall back to an arity-agnostic entry for params-style members such as console.log.
        return Methods.TryGetValue($"{OwnerName(target)}.{target.Name}/*", out var any) ? any : null;
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
        var owner = OwnerName(ctor.OriginalDefinition);
        var key = $"{owner}/{ctor.Parameters.Length}";
        if (Constructors.TryGetValue(key, out var t)) return t;
        return Constructors.TryGetValue($"{owner}/*", out var any) ? any : null;
    }

    private static string OwnerName(ISymbol member)
        => member.ContainingType is { } ct ? TypeMap.Unbound(ct) : "";

    /// <summary>
    /// Assemblies whose members need a mapping to survive transpilation. A member from one of
    /// these with no entry in this table cannot be emitted structurally — there is no source to
    /// walk — so the emitter reports it instead of writing a call that will fail at runtime.
    /// </summary>
    private static readonly HashSet<string> FrameworkAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Runtime", "System.Private.CoreLib", "System.Console", "System.Linq",
        "System.Collections", "System.Text.RegularExpressions", "netstandard", "mscorlib",
        "System.Runtime.Extensions", "System.Private.Uri", "System.Memory",
    };

    /// <summary>
    /// True when a symbol comes from the framework and therefore requires a mapping.
    /// Types the developer declared are emitted from their own source and need none, and
    /// <c>CSJS.Interop</c> declarations carry their own binding attributes.
    /// </summary>
    public static bool RequiresMapping(ISymbol symbol)
    {
        if (symbol.DeclaringSyntaxReferences.Length > 0) return false;

        var assembly = symbol.ContainingAssembly?.Name;
        if (assembly is null) return false;
        if (assembly == "CSJS.Interop") return false;

        return FrameworkAssemblies.Contains(assembly);
    }

    // ---------------------------------------------------------------------------------------
    // Methods
    // ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, JsTemplate> Methods = new(StringComparer.Ordinal)
    {
        // ---- Console -------------------------------------------------------------------
        ["System.Console.WriteLine/0"] = new("console.log()"),
        ["System.Console.WriteLine/1"] = new("console.log({0})"),
        ["System.Console.WriteLine/*"] = new("console.log({...})"),
        ["System.Console.Write/1"] = new("csjsWrite({0})", Helper: "csjsWrite"),
        ["System.Console.Error.WriteLine/1"] = new("console.error({0})"),

        // ---- String --------------------------------------------------------------------
        ["System.String.Substring/1"] = new("{this}.substring({0})"),
        ["System.String.Substring/2"] = new("{this}.substring({0}, {0} + {1})"),
        ["System.String.IndexOf/1"] = new("{this}.indexOf({0})"),
        ["System.String.IndexOf/2"] = new("{this}.indexOf({0}, {1})"),
        ["System.String.LastIndexOf/1"] = new("{this}.lastIndexOf({0})"),
        ["System.String.Contains/1"] = new("{this}.includes({0})"),
        ["System.String.StartsWith/1"] = new("{this}.startsWith({0})"),
        ["System.String.EndsWith/1"] = new("{this}.endsWith({0})"),
        ["System.String.ToUpper/0"] = new("{this}.toUpperCase()"),
        ["System.String.ToUpperInvariant/0"] = new("{this}.toUpperCase()"),
        ["System.String.ToLower/0"] = new("{this}.toLowerCase()"),
        ["System.String.ToLowerInvariant/0"] = new("{this}.toLowerCase()"),
        ["System.String.Trim/0"] = new("{this}.trim()"),
        ["System.String.TrimStart/0"] = new("{this}.trimStart()"),
        ["System.String.TrimEnd/0"] = new("{this}.trimEnd()"),
        ["System.String.Replace/2"] = new("{this}.replaceAll({0}, {1})"),
        ["System.String.Split/1"] = new("csjsSplit({this}, {0})", Helper: "csjsSplit"),
        ["System.String.Split/2"] = new("csjsSplit({this}, {0}, {1})", Helper: "csjsSplit"),
        ["System.String.Split/*"] = new("csjsSplit({this}, {0})", Helper: "csjsSplit"),
        ["System.String.PadLeft/2"] = new("{this}.padStart({0}, {1})"),
        ["System.String.PadLeft/1"] = new("{this}.padStart({0}, ' ')"),
        ["System.String.PadRight/2"] = new("{this}.padEnd({0}, {1})"),
        ["System.String.PadRight/1"] = new("{this}.padEnd({0}, ' ')"),
        ["System.String.ToString/0"] = new("{this}"),
        ["System.String.Concat/*"] = new("[{...}].join('')"),
        ["System.String.Join/2"] = new("[...{1}].join({0})"),
        ["System.String.IsNullOrEmpty/1"] = new("(!{0})"),
        ["System.String.IsNullOrWhiteSpace/1"] = new("(!{0} || !{0}.trim())"),
        ["System.String.Format/*"] = new("csjsFormat({...})", Helper: "csjsFormat"),
        ["System.String.Repeat/1"] = new("{this}.repeat({0})"),

        // ---- Object / conversion -------------------------------------------------------
        ["System.Object.ToString/0"] = new("csjsToString({this})", Helper: "csjsToString"),
        ["System.Object.Equals/1"] = new("csjsEquals({this}, {0})", Helper: "csjsEquals"),
        ["System.Object.GetHashCode/0"] = new("csjsHash({this})", Helper: "csjsHash"),
        ["System.IComparable.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.String.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.Int32.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.Double.CompareTo/1"] = new("csjsCompare({this}, {0})", Helper: "csjsCompare"),
        ["System.Int32.ToString/0"] = new("String({this})"),
        ["System.Double.ToString/0"] = new("String({this})"),
        ["System.Boolean.ToString/0"] = new("String({this})"),
        ["System.Int32.Parse/1"] = new("csjsParseInt({0})", Helper: "csjsParseInt"),
        ["System.Int32.TryParse/2"] = new("csjsTryParseInt({0})", Helper: "csjsTryParseInt"),
        ["System.Double.Parse/1"] = new("csjsParseFloat({0})", Helper: "csjsParseFloat"),
        ["System.Convert.ToInt32/1"] = new("csjsParseInt({0})", Helper: "csjsParseInt"),
        ["System.Convert.ToDouble/1"] = new("Number({0})"),
        ["System.Convert.ToString/1"] = new("String({0})"),
        ["System.Convert.ToBoolean/1"] = new("Boolean({0})"),
        ["System.Double.IsNaN/1"] = new("Number.isNaN({0})"),
        ["System.Double.IsInfinity/1"] = new("(!Number.isFinite({0}) && !Number.isNaN({0}))"),
        ["System.Double.IsFinite/1"] = new("Number.isFinite({0})"),
        ["System.Single.IsNaN/1"] = new("Number.isNaN({0})"),
        ["System.Double.TryParse/2"] = new("csjsTryParseFloat({0})", Helper: "csjsTryParseFloat"),
        ["System.Int64.Parse/1"] = new("BigInt({0})"),
        ["System.Double.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),
        ["System.Int32.ToString/1"] = new("csjsFormatValue({this}, {0})", Helper: "csjsFormatValue"),

        // ---- Char ----------------------------------------------------------------------
        // A C# char is a one-character string here, so these are string tests.
        ["System.Char.IsDigit/1"] = new("(/^[0-9]$/.test({0}))"),
        ["System.Char.IsLetter/1"] = new("(/^[a-zA-Z]$/.test({0}))"),
        ["System.Char.IsLetterOrDigit/1"] = new("(/^[a-zA-Z0-9]$/.test({0}))"),
        ["System.Char.IsWhiteSpace/1"] = new("(/^\\s$/.test({0}))"),
        ["System.Char.IsUpper/1"] = new("({0} === {0}.toUpperCase() && {0} !== {0}.toLowerCase())"),
        ["System.Char.IsLower/1"] = new("({0} === {0}.toLowerCase() && {0} !== {0}.toUpperCase())"),
        ["System.Char.ToUpper/1"] = new("{0}.toUpperCase()"),
        ["System.Char.ToLower/1"] = new("{0}.toLowerCase()"),
        ["System.Char.ToString/0"] = new("{this}"),

        // ---- Array -----------------------------------------------------------------------
        ["System.Array.IndexOf/2"] = new("{0}.indexOf({1})"),
        ["System.Array.Sort/1"] = new("csjsSort({0})", Helper: "csjsSort"),
        ["System.Array.Reverse/1"] = new("{0}.reverse()"),
        ["System.Array.Empty/0"] = new("[]"),

        // ---- Math ----------------------------------------------------------------------
        ["System.Math.Abs/1"] = new("Math.abs({0})"),
        ["System.Math.Ceiling/1"] = new("Math.ceil({0})"),
        ["System.Math.Floor/1"] = new("Math.floor({0})"),
        ["System.Math.Round/1"] = new("Math.round({0})"),
        ["System.Math.Round/2"] = new("csjsRound({0}, {1})", Helper: "csjsRound"),
        ["System.Math.Truncate/1"] = new("Math.trunc({0})"),
        ["System.Math.Sqrt/1"] = new("Math.sqrt({0})"),
        ["System.Math.Cbrt/1"] = new("Math.cbrt({0})"),
        ["System.Math.Pow/2"] = new("Math.pow({0}, {1})"),
        ["System.Math.Exp/1"] = new("Math.exp({0})"),
        ["System.Math.Log/1"] = new("Math.log({0})"),
        ["System.Math.Log10/1"] = new("Math.log10({0})"),
        ["System.Math.Log2/1"] = new("Math.log2({0})"),
        ["System.Math.Sin/1"] = new("Math.sin({0})"),
        ["System.Math.Cos/1"] = new("Math.cos({0})"),
        ["System.Math.Tan/1"] = new("Math.tan({0})"),
        ["System.Math.Asin/1"] = new("Math.asin({0})"),
        ["System.Math.Acos/1"] = new("Math.acos({0})"),
        ["System.Math.Atan/1"] = new("Math.atan({0})"),
        ["System.Math.Atan2/2"] = new("Math.atan2({0}, {1})"),
        ["System.Math.Min/2"] = new("Math.min({0}, {1})"),
        ["System.Math.Max/2"] = new("Math.max({0}, {1})"),
        ["System.Math.Sign/1"] = new("Math.sign({0})"),
        ["System.Math.Clamp/3"] = new("Math.min(Math.max({0}, {1}), {2})"),
        ["System.Random.NextDouble/0"] = new("Math.random()"),
        ["System.Random.Next/1"] = new("Math.floor(Math.random() * {0})"),
        ["System.Random.Next/2"] = new("({0} + Math.floor(Math.random() * ({1} - {0})))"),

        // ---- List<T> -> Array ----------------------------------------------------------
        ["System.Collections.Generic.List.Add/1"] = new("{this}.push({0})"),
        ["System.Collections.Generic.List.AddRange/1"] = new("{this}.push(...{0})"),
        ["System.Collections.Generic.List.Insert/2"] = new("{this}.splice({0}, 0, {1})"),
        ["System.Collections.Generic.List.RemoveAt/1"] = new("{this}.splice({0}, 1)"),
        ["System.Collections.Generic.List.Remove/1"] = new("csjsRemove({this}, {0})", Helper: "csjsRemove"),
        ["System.Collections.Generic.List.Clear/0"] = new("{this}.length = 0"),
        ["System.Collections.Generic.List.Contains/1"] = new("{this}.includes({0})"),
        ["System.Collections.Generic.List.IndexOf/1"] = new("{this}.indexOf({0})"),
        ["System.Collections.Generic.List.Sort/0"] = new("csjsSort({this})", Helper: "csjsSort"),
        ["System.Collections.Generic.List.Sort/1"] = new("{this}.sort({0})"),
        ["System.Collections.Generic.List.Reverse/0"] = new("{this}.reverse()"),
        ["System.Collections.Generic.List.ToArray/0"] = new("{seq}"),
        ["System.Collections.Generic.List.ForEach/1"] = new("{this}.forEach({0})"),
        ["System.Collections.Generic.List.Find/1"] = new("({this}.find({0}) ?? null)"),
        ["System.Collections.Generic.List.FindIndex/1"] = new("{this}.findIndex({0})"),
        ["System.Collections.Generic.List.Exists/1"] = new("{this}.some({0})"),

        // ---- Dictionary<K,V> -> Map ----------------------------------------------------
        ["System.Collections.Generic.Dictionary.Add/2"] = new("{this}.set({0}, {1})"),
        ["System.Collections.Generic.Dictionary.Remove/1"] = new("{this}.delete({0})"),
        ["System.Collections.Generic.Dictionary.ContainsKey/1"] = new("{this}.has({0})"),
        ["System.Collections.Generic.Dictionary.Clear/0"] = new("{this}.clear()"),
        ["System.Collections.Generic.Dictionary.TryGetValue/2"] = new("{this}.has({0})"),
        ["System.Collections.Generic.Dictionary.TryAdd/2"] = new("csjsTryAdd({this}, {0}, {1})", Helper: "csjsTryAdd"),

        // ---- HashSet<T> -> Set ---------------------------------------------------------
        ["System.Collections.Generic.HashSet.Add/1"] = new("csjsSetAdd({this}, {0})", Helper: "csjsSetAdd"),
        ["System.Collections.Generic.HashSet.Contains/1"] = new("{this}.has({0})"),
        ["System.Collections.Generic.HashSet.Remove/1"] = new("{this}.delete({0})"),
        ["System.Collections.Generic.HashSet.Clear/0"] = new("{this}.clear()"),

        // ---- LINQ ----------------------------------------------------------------------
        // Extension receivers arrive as {this}; the lambda is {0}.
        ["System.Linq.Enumerable.Select/1"] = new("{seq}.map({0})"),
        ["System.Linq.Enumerable.Where/1"] = new("{seq}.filter({0})"),
        ["System.Linq.Enumerable.Any/0"] = new("({seq}.length > 0)"),
        ["System.Linq.Enumerable.Any/1"] = new("{seq}.some({0})"),
        ["System.Linq.Enumerable.All/1"] = new("{seq}.every({0})"),
        ["System.Linq.Enumerable.Count/0"] = new("{seq}.length"),
        ["System.Linq.Enumerable.Count/1"] = new("{seq}.filter({0}).length"),
        ["System.Linq.Enumerable.First/0"] = new("csjsFirst({this})", Helper: "csjsFirst"),
        ["System.Linq.Enumerable.First/1"] = new("csjsFirst({this}, {0})", Helper: "csjsFirst"),
        ["System.Linq.Enumerable.FirstOrDefault/0"] = new("({seq}[0] ?? null)"),
        ["System.Linq.Enumerable.FirstOrDefault/1"] = new("({seq}.find({0}) ?? null)"),
        ["System.Linq.Enumerable.Last/0"] = new("csjsLast({this})", Helper: "csjsLast"),
        ["System.Linq.Enumerable.LastOrDefault/0"] = new("({seq}.at(-1) ?? null)"),
        ["System.Linq.Enumerable.Single/0"] = new("csjsSingle({this})", Helper: "csjsSingle"),
        ["System.Linq.Enumerable.ToList/0"] = new("{seq}"),
        ["System.Linq.Enumerable.ToArray/0"] = new("{seq}"),
        ["System.Linq.Enumerable.ToDictionary/2"] = new("csjsToDictionary({this}, {0}, {1})", Helper: "csjsToDictionary"),
        ["System.Linq.Enumerable.OrderBy/1"] = new("csjsOrderBy({this}, {0}, false)", Helper: "csjsOrderBy"),
        ["System.Linq.Enumerable.OrderByDescending/1"] = new("csjsOrderBy({this}, {0}, true)", Helper: "csjsOrderBy"),
        ["System.Linq.Enumerable.ThenBy/1"] = new("csjsOrderBy({this}, {0}, false)", Helper: "csjsOrderBy"),
        ["System.Linq.Enumerable.Sum/0"] = new("csjsSum({this})", Helper: "csjsSum"),
        ["System.Linq.Enumerable.Sum/1"] = new("csjsSum({this}, {0})", Helper: "csjsSum"),
        ["System.Linq.Enumerable.Average/0"] = new("csjsAverage({this})", Helper: "csjsAverage"),
        ["System.Linq.Enumerable.Average/1"] = new("csjsAverage({this}, {0})", Helper: "csjsAverage"),
        ["System.Linq.Enumerable.Min/0"] = new("Math.min(...{seq})"),
        ["System.Linq.Enumerable.Max/0"] = new("Math.max(...{seq})"),
        ["System.Linq.Enumerable.Take/1"] = new("{seq}.slice(0, {0})"),
        ["System.Linq.Enumerable.Skip/1"] = new("{seq}.slice({0})"),
        ["System.Linq.Enumerable.Reverse/0"] = new("{seq}.reverse()"),
        ["System.Linq.Enumerable.Distinct/0"] = new("[...new Set({seq})]"),
        ["System.Linq.Enumerable.Concat/1"] = new("[...{this}, ...{0}]"),
        ["System.Linq.Enumerable.SelectMany/1"] = new("{seq}.flatMap({0})"),
        ["System.Linq.Enumerable.Contains/1"] = new("{seq}.includes({0})"),
        ["System.Linq.Enumerable.GroupBy/1"] = new("csjsGroupBy({this}, {0})", Helper: "csjsGroupBy"),
        ["System.Linq.Enumerable.Range/2"] = new("csjsRange({0}, {1})", Helper: "csjsRange"),
        ["System.Linq.Enumerable.Empty/0"] = new("[]"),

        // ---- Task ----------------------------------------------------------------------
        ["System.Threading.Tasks.Task.Delay/1"] = new("csjsDelay({0})", Helper: "csjsDelay"),
        ["System.Threading.Tasks.Task.FromResult/1"] = new("Promise.resolve({0})"),
        ["System.Threading.Tasks.Task.WhenAll/*"] = new("Promise.all([{...}])"),
        ["System.Threading.Tasks.Task.WhenAny/*"] = new("Promise.race([{...}])"),
        ["System.Threading.Tasks.Task.Run/1"] = new("Promise.resolve().then({0})"),

        // ---- DateTime ------------------------------------------------------------------
        ["System.DateTime.ToString/0"] = new("{this}.toISOString()"),
        ["System.DateTime.AddDays/1"] = new("csjsAddMs({this}, {0} * 86400000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddHours/1"] = new("csjsAddMs({this}, {0} * 3600000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddMinutes/1"] = new("csjsAddMs({this}, {0} * 60000)", Helper: "csjsAddMs"),
        ["System.DateTime.AddSeconds/1"] = new("csjsAddMs({this}, {0} * 1000)", Helper: "csjsAddMs"),
    };

    // ---------------------------------------------------------------------------------------
    // Properties and fields
    // ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, JsTemplate> Properties = new(StringComparer.Ordinal)
    {
        ["System.String.Length"] = new("{this}.length"),
        ["System.String.Empty"] = new("''"),
        ["System.Array.Length"] = new("{this}.length"),
        ["System.Collections.Generic.List.Count"] = new("{this}.length"),
        ["System.Collections.Generic.Dictionary.Count"] = new("{this}.size"),
        ["System.Collections.Generic.Dictionary.Keys"] = new("[...{this}.keys()]"),
        ["System.Collections.Generic.Dictionary.Values"] = new("[...{this}.values()]"),
        ["System.Collections.Generic.HashSet.Count"] = new("{this}.size"),

        // Exceptions become JavaScript Errors, whose members are lowercase.
        ["System.Exception.Message"] = new("{this}.message"),
        ["System.Exception.StackTrace"] = new("{this}.stack"),
        ["System.Exception.InnerException"] = new("({this}.cause ?? null)"),

        ["System.Math.PI"] = new("Math.PI"),
        ["System.Math.E"] = new("Math.E"),
        ["System.Math.Tau"] = new("(Math.PI * 2)"),

        ["System.Int32.MaxValue"] = new("2147483647"),
        ["System.Int32.MinValue"] = new("(-2147483648)"),
        ["System.Double.MaxValue"] = new("Number.MAX_VALUE"),
        ["System.Double.MinValue"] = new("(-Number.MAX_VALUE)"),
        ["System.Double.PositiveInfinity"] = new("Infinity"),
        ["System.Double.NegativeInfinity"] = new("(-Infinity)"),
        ["System.Double.NaN"] = new("NaN"),

        ["System.DateTime.Now"] = new("new Date()"),
        ["System.DateTime.UtcNow"] = new("new Date()"),
        ["System.DateTime.Today"] = new("csjsToday()", Helper: "csjsToday"),
        ["System.DateTime.Year"] = new("{this}.getFullYear()"),
        ["System.DateTime.Month"] = new("({this}.getMonth() + 1)"),
        ["System.DateTime.Day"] = new("{this}.getDate()"),
        ["System.DateTime.Hour"] = new("{this}.getHours()"),
        ["System.DateTime.Minute"] = new("{this}.getMinutes()"),
        ["System.DateTime.Second"] = new("{this}.getSeconds()"),
        ["System.DateTime.DayOfWeek"] = new("{this}.getDay()"),

        // A group is an Array carrying its key, matching IGrouping<K,T>'s dual nature.
        ["System.Linq.IGrouping.Key"] = new("{this}.key"),

        // Map iteration yields [key, value] pairs, so KeyValuePair is just that array.
        // This is what lets `foreach (var kvp in dictionary)` emit a plain for..of loop.
        ["System.Collections.Generic.KeyValuePair.Key"] = new("{this}[0]"),
        ["System.Collections.Generic.KeyValuePair.Value"] = new("{this}[1]"),
    };

    // ---------------------------------------------------------------------------------------
    // Constructors
    // ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, JsTemplate> Constructors = new(StringComparer.Ordinal)
    {
        ["System.Collections.Generic.List/0"] = new("[]"),
        ["System.Collections.Generic.List/1"] = new("[...{0}]"),
        ["System.Collections.Generic.Dictionary/0"] = new("new Map()"),
        ["System.Collections.Generic.Dictionary/1"] = new("new Map()"),
        ["System.Collections.Generic.HashSet/0"] = new("new Set()"),
        ["System.Collections.Generic.HashSet/1"] = new("new Set({0})"),
        ["System.Text.StringBuilder/0"] = new("csjsStringBuilder()", Helper: "csjsStringBuilder"),
        ["System.Random/*"] = new("null"),
        ["System.DateTime/3"] = new("new Date({0}, {1} - 1, {2})"),
        ["System.DateTime/6"] = new("new Date({0}, {1} - 1, {2}, {3}, {4}, {5})"),
        // Exception construction is handled by the emitter rather than a template, because the
        // C# type hierarchy has to travel with the error for typed catch clauses to work.
    };

    /// <summary>StringBuilder members, resolved against the helper object the constructor returns.</summary>
    static MemberMap()
    {
        Methods["System.Text.StringBuilder.Append/1"] = new("{this}.append({0})");
        Methods["System.Text.StringBuilder.AppendLine/1"] = new("{this}.appendLine({0})");
        Methods["System.Text.StringBuilder.AppendLine/0"] = new("{this}.appendLine('')");
        Methods["System.Text.StringBuilder.ToString/0"] = new("{this}.toString()");
        Methods["System.Text.StringBuilder.Clear/0"] = new("{this}.clear()");
        Properties["System.Text.StringBuilder.Length"] = new("{this}.length()");
    }
}
