using CSJS.Core.Bundling;
using Xunit;

namespace CSJS.Core.Tests;

/// <summary>
/// The minifier is only as safe as the tokenizer under it. These cases are the places a naive
/// one goes wrong: comment markers inside strings, templates with nested substitutions, regex
/// literals that contain slashes, and the newlines automatic semicolon insertion depends on.
/// </summary>
public class MinifierTests
{
    /// <summary>Token texts, comments excluded: what minification must leave identical.</summary>
    private static List<string> Significant(string source)
        => JsTokenizer.Tokenize(source).Where(t => !t.IsComment).Select(t => t.Text(source)).ToList();

    private static void AssertSameBehaviour(string source)
    {
        var minified = JsMinifier.Minify(source).JavaScript;
        Assert.Equal(Significant(source), Significant(minified));

        using var sandbox = new NodeSandbox();
        sandbox.Write("original.js", source);
        sandbox.Write("minified.js", minified);
        Assert.Equal(sandbox.RunOk("original.js"), sandbox.RunOk("minified.js"));
    }

    [Fact]
    public void CommentMarkersInsideStringsSurvive()
    {
        AssertSameBehaviour("""
            // a real comment
            const a = "not // a comment";   /* a real one */
            const b = 'nor /* this */ one';
            const c = "escaped \" quote // still string";
            console.log(a, b, c);
            """);
    }

    [Fact]
    public void TemplateLiteralsWithNestedSubstitutions()
    {
        AssertSameBehaviour("""
            const name = 'x';
            const obj = { k: 1 };
            const t = `outer ${ `inner ${name} // not comment` } and ${ { a: obj.k }.a } /* kept */ ${'}'}`;
            const multi = `line one
              line two   keeps   spacing
            ${ [1, 2].map((n) => `#${n}`).join(',') }`;
            console.log(t);
            console.log(multi);
            """);
    }

    [Fact]
    public void RegexLiteralsAreNotDivisionOrComments()
    {
        AssertSameBehaviour("""
            const path = 'a/b//c';
            const parts = path.split(/\/+/);
            const cls = /[/*]+/.test('/*');
            const half = 10 / 2 / 5;
            const re = (x) => x.replace(/\/\/.*$/g, '');
            const after = [1].map(() => /a/g.source);
            console.log(parts.join('|'), cls, half, re('keep // drop'), after[0]);
            """);
    }

    [Fact]
    public void OperatorsThatWouldFuseKeepTheirSpace()
    {
        var source = """
            let a = 1, b = 2;
            const c = a + +b;
            const d = a - -b;
            const e = a + ++b;
            const f = 1 .toString();
            const g = /x/g instanceof RegExp;
            const h = typeof a === 'number' ? .5 : 0;
            console.log(c, d, e, f, g, h);
            """;
        AssertSameBehaviour(source);

        var minified = JsMinifier.Minify(source).JavaScript;
        Assert.Contains("a+ +b", minified);
        Assert.Contains("a- -b", minified);
        Assert.Contains("1 .toString()", minified);
    }

    [Fact]
    public void NewlinesThatAutomaticSemicolonInsertionNeedsAreKept()
    {
        AssertSameBehaviour("""
            function f() {
              return
                42;
            }
            let i = 0
            const arr = [1, 2]
            ;[3].forEach((x) => console.log(x))
            i
            ++i
            console.log(f(), i, arr.length)
            """);
    }

    [Fact]
    public void TheRuntimeSurvivesMinification()
    {
        // The largest hand-written JavaScript in the project: every helper, regex, and template
        // in it has to come through token-for-token.
        var runtime = Transpiler.RuntimeSource.Value;
        var minified = JsMinifier.Minify(runtime).JavaScript;

        Assert.Equal(Significant(runtime), Significant(minified));
        Assert.True(minified.Length < runtime.Length * 0.8, $"{minified.Length} vs {runtime.Length}");

        using var sandbox = new NodeSandbox();
        sandbox.Write("runtime.js", minified);
        sandbox.Write("check.js", """
            import { csjsFormat, csjsSplit, csjsOrderBy } from './runtime.js';
            console.log(csjsFormat('{0:F2}|{1}', 3.14159, 'x'), csjsSplit('a,,b', [','], 1).join('/'),
              csjsOrderBy([3, 1, 2], (n) => n).join(''));
            """);
        Assert.Equal("3.14|x a/b 123", sandbox.RunOk("check.js"));
    }

    [Fact]
    public void PositionsTranslateForTheSourceMap()
    {
        var source = "// header\nfunction f() {\n    return   1 +   2;\n}\n";
        var result = JsMinifier.Minify(source);
        var lines = result.JavaScript.Split('\n');

        // `return` started at line 2, column 4 of the original.
        var at = result.Translate(2, 4);
        Assert.NotNull(at);
        Assert.StartsWith("return", lines[at!.Value.Line][at.Value.Column..]);
    }
}
