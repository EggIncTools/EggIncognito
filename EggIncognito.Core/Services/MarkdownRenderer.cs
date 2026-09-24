using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services;

public static partial class MarkdownRenderer {
    private static readonly Regex EscapeChars = EscapeCharsRegex();
    private static readonly Regex SafeScheme = MyRegex();
    private static readonly Regex SplitLines = SplitLinesRegex();

    private static readonly Regex InlineCode = InlineCodeRegex();
    private static readonly Regex InlineImg = InlineImgRegex();
    private static readonly Regex InlineLink = InlineLinkRegex();
    private static readonly Regex InlineBold = InlineBoldRegex();
    private static readonly Regex InlineItalic = InlineItalicRegex();

    private static readonly Regex Fence = FenceRegex();
    private static readonly Regex Rule = RuleRegex();
    private static readonly Regex Heading = HeadingRegex();

    private static readonly Regex Quote = QuoteRegex();
    private static readonly Regex Ul = UlRegex();
    private static readonly Regex Ol = OlRegex();

    private static readonly Regex ParaStop = ParaStopRegex();

    private static string EscapeHtml(string s) => EscapeChars.Replace(s, m => m.Value switch {
        "&" => "&amp;",
        "<" => "&lt;",
        ">" => "&gt;",
        "\"" => "&quot;",
        "'" => "&#39;",
        _ => m.Value
    });

    private static string SafeUrl(string url) {
        string u = url.Trim();
        return SafeScheme.IsMatch(u) ? u : "#";
    }

    private static string Inline(string text) {
        text = InlineCode.Replace(text, m => $"<code>{m.Groups[1].Value}</code>");
        text = InlineImg.Replace(text,
            m => $"<img src=\"{SafeUrl(m.Groups[2].Value)}\" alt=\"{m.Groups[1].Value}\" />");
        text = InlineLink.Replace(text,
            m =>
                $"<a href=\"{SafeUrl(m.Groups[2].Value)}\" target=\"_blank\" rel=\"noopener noreferrer\">{m.Groups[1].Value}</a>");
        text = InlineBold.Replace(text, "<strong>$1</strong>");
        text = InlineItalic.Replace(text, "$1<em>$2</em>");
        return text;
    }

    private static string FenceLanguage(string fenceLine) {
        string trimmed = fenceLine.Trim();
        int i = 0;
        while (i < trimmed.Length && trimmed[i] == '`') i++;
        string info = trimmed[i..].Trim();
        if (info.Length == 0) return "";
        string resolved = Syntax.SyntaxHighlighter.Resolve(info);
        return resolved == Syntax.SyntaxHighlighter.Fallback ? "" : resolved;
    }

    public static string Render(string? src) {
        string[] lines = SplitLines.Split(EscapeHtml(src ?? ""));
        var outLines = new List<string>();
        int i = 0;
        string? listType = null;

        void CloseList() {
            if (listType is not null) {
                outLines.Add($"</{listType}>");
                listType = null;
            }
        }

        while (i < lines.Length) {
            string line = lines[i];

            if (Fence.IsMatch(line.Trim())) {
                CloseList();
                string language = FenceLanguage(line);
                var body = new List<string>();
                i++;
                while (i < lines.Length && !Fence.IsMatch(lines[i].Trim())) {
                    body.Add(lines[i]);
                    i++;
                }

                i++;
                string codeOpen = language.Length == 0 ? "<code>" : $"<code class=\"lang-{language}\">";
                outLines.Add($"<pre class=\"md-code\">{codeOpen}{string.Join("\n", body)}</code></pre>");
                continue;
            }

            if (Rule.IsMatch(line)) {
                CloseList();
                outLines.Add("<hr class=\"md-rule\" />");
                i++;
                continue;
            }

            var h = Heading.Match(line);
            if (h.Success) {
                CloseList();
                int n = h.Groups[1].Value.Length;
                outLines.Add($"<h{n} class=\"md-h{n}\">{Inline(h.Groups[2].Value)}</h{n}>");
                i++;
                continue;
            }

            if (Quote.IsMatch(line)) {
                CloseList();
                var body = new List<string>();
                while (i < lines.Length && Quote.IsMatch(lines[i])) {
                    body.Add(Quote.Replace(lines[i], "", 1));
                    i++;
                }

                outLines.Add($"<blockquote class=\"md-quote\">{Inline(string.Join("<br/>", body))}</blockquote>");
                continue;
            }

            if (Ul.IsMatch(line)) {
                if (listType != "ul") {
                    CloseList();
                    outLines.Add("<ul class=\"md-list\">");
                    listType = "ul";
                }

                outLines.Add($"<li>{Inline(Ul.Replace(line, "", 1))}</li>");
                i++;
                continue;
            }

            if (Ol.IsMatch(line)) {
                if (listType != "ol") {
                    CloseList();
                    outLines.Add("<ol class=\"md-list\">");
                    listType = "ol";
                }

                outLines.Add($"<li>{Inline(Ol.Replace(line, "", 1))}</li>");
                i++;
                continue;
            }

            if (line.Trim() == "") {
                CloseList();
                i++;
                continue;
            }

            {
                CloseList();
                var body = new List<string> { line };
                i++;
                while (i < lines.Length && lines[i].Trim() != "" && !ParaStop.IsMatch(lines[i])) {
                    body.Add(lines[i]);
                    i++;
                }

                outLines.Add($"<p>{Inline(string.Join("<br/>", body))}</p>");
            }
        }

        CloseList();
        return string.Join("\n", outLines);
    }

    [GeneratedRegex("[&<>\"']", RegexOptions.Compiled)]
    private static partial Regex EscapeCharsRegex();

    [GeneratedRegex(@"^(https?://|/|\./|#)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"\r?\n", RegexOptions.Compiled)]
    private static partial Regex SplitLinesRegex();

    [GeneratedRegex(@"`([^`]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex InlineImgRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*", RegexOptions.Compiled)]
    private static partial Regex InlineBoldRegex();

    [GeneratedRegex(@"(^|[^*])\*([^*]+)\*", RegexOptions.Compiled)]
    private static partial Regex InlineItalicRegex();

    [GeneratedRegex(@"^```", RegexOptions.Compiled)]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^\s*---+\s*$", RegexOptions.Compiled)]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"^(#{1,3})\s+(.*)$", RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*&gt;\s?", RegexOptions.Compiled)]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^\s*[-*]\s+", RegexOptions.Compiled)]
    private static partial Regex UlRegex();

    [GeneratedRegex(@"^\s*\d+\.\s+", RegexOptions.Compiled)]
    private static partial Regex OlRegex();

    [GeneratedRegex(@"^(#{1,3}\s|\s*[-*]\s|\s*\d+\.\s|\s*&gt;|```|\s*---+\s*$)", RegexOptions.Compiled)]
    private static partial Regex ParaStopRegex();
}
