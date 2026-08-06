using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Microsoft.AspNetCore.Components;

namespace CSJSGen.Services;

/// <summary>
/// Renders assistant messages from Markdown to HTML.
/// <para>
/// The spec asks for tables, media, and code blocks to render properly. Markdig handles the
/// grammar; this class adds what a chat transcript specifically needs — language-tagged code
/// blocks the UI can attach a copy button to, and bare media URLs turned into real players.
/// </para>
/// </summary>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // tables, task lists, footnotes, autolinks
            .UseEmojiAndSmiley()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();
    }

    /// <summary>Converts Markdown to HTML ready for insertion into the transcript.</summary>
    public MarkupString ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return new MarkupString("");

        var html = Markdown.ToHtml(markdown, _pipeline);

        html = TagCodeBlocks(html);
        html = EmbedMedia(html);
        html = OpenLinksExternally(html);

        return new MarkupString(html);
    }

    /// <summary>
    /// Marks up code blocks so the UI can show the language and a copy button.
    /// Markdig emits <c>class="language-csharp"</c>; the wrapper is what the toolbar hangs off.
    /// </summary>
    private static string TagCodeBlocks(string html)
    {
        return Regex.Replace(html,
            @"<pre><code class=""language-(?<lang>[^""]+)"">(?<body>.*?)</code></pre>",
            match =>
            {
                var language = match.Groups["lang"].Value;
                var body = match.Groups["body"].Value;
                var label = FriendlyLanguageName(language);

                return $"""
                    <div class="code-block" data-language="{language}">
                      <div class="code-head"><span class="code-lang">{label}</span>
                        <button class="code-copy" type="button" data-copy>Salin</button></div>
                      <pre><code class="language-{language}">{body}</code></pre>
                    </div>
                    """;
            },
            RegexOptions.Singleline);
    }

    private static string FriendlyLanguageName(string language) => language.ToLowerInvariant() switch
    {
        "csharp" or "cs" => "C#",
        "csjs" => "CSJS",
        "javascript" or "js" => "JavaScript",
        "typescript" or "ts" => "TypeScript",
        "html" => "HTML",
        "css" => "CSS",
        "json" => "JSON",
        "xml" => "XML",
        "bash" or "sh" or "shell" => "Shell",
        _ => language.ToUpperInvariant(),
    };

    /// <summary>
    /// Turns links that point at media into players.
    /// A bare .mp4 rendered as an anchor is useless in a chat transcript; a video element is not.
    /// </summary>
    private static string EmbedMedia(string html)
    {
        html = Regex.Replace(html,
            @"<a href=""(?<url>[^""]+\.(?:mp4|webm|ogv))""[^>]*>[^<]*</a>",
            m => $"""<video class="chat-media" controls preload="metadata" src="{m.Groups["url"].Value}"></video>""",
            RegexOptions.IgnoreCase);

        html = Regex.Replace(html,
            @"<a href=""(?<url>[^""]+\.(?:mp3|wav|ogg|m4a))""[^>]*>[^<]*</a>",
            m => $"""<audio class="chat-media" controls preload="metadata" src="{m.Groups["url"].Value}"></audio>""",
            RegexOptions.IgnoreCase);

        // Images already render as <img>; this only adds the class the stylesheet keys off.
        html = Regex.Replace(html, @"<img\s+(?![^>]*class=)", "<img class=\"chat-media\" ",
            RegexOptions.IgnoreCase);

        return html;
    }

    /// <summary>
    /// Makes external links open outside the embedded browser, and adds the rel attributes
    /// that stop a target=_blank link from reaching back into this window.
    /// </summary>
    private static string OpenLinksExternally(string html)
    {
        return Regex.Replace(html,
            @"<a href=""(?<url>https?://[^""]+)""",
            m => $"""<a target="_blank" rel="noopener noreferrer" href="{m.Groups["url"].Value}" """,
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Extracts fenced code blocks, so the editor can offer to insert generated code directly.
    /// </summary>
    public static IReadOnlyList<(string Language, string Code)> ExtractCodeBlocks(string markdown)
    {
        var document = Markdown.Parse(markdown);
        var blocks = new List<(string, string)>();

        foreach (var block in document.Descendants<FencedCodeBlock>())
        {
            var language = block.Info ?? "";
            var code = string.Join("\n", block.Lines.Lines
                .Take(block.Lines.Count)
                .Select(l => l.ToString()));

            if (code.Trim().Length > 0) blocks.Add((language, code));
        }

        return blocks;
    }

    /// <summary>
    /// The first block that looks like CSJS source, which is what "apply to editor" should use.
    /// </summary>
    public static string? FirstCsjsBlock(string markdown)
    {
        var blocks = ExtractCodeBlocks(markdown);

        var typed = blocks.FirstOrDefault(b =>
            b.Language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
            || b.Language.Equals("cs", StringComparison.OrdinalIgnoreCase)
            || b.Language.Equals("csjs", StringComparison.OrdinalIgnoreCase));

        if (typed.Code is { Length: > 0 }) return typed.Code;

        // An untagged block that declares a type is still almost certainly the code in question.
        var untagged = blocks.FirstOrDefault(b =>
            b.Code.Contains("class ", StringComparison.Ordinal)
            || b.Code.Contains("namespace ", StringComparison.Ordinal));

        return untagged.Code is { Length: > 0 } ? untagged.Code : null;
    }
}
