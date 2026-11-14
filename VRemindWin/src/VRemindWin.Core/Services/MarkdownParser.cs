using System.Text.RegularExpressions;
using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public static partial class MarkdownParser
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v"
    };

    [GeneratedRegex(@"^\[!\[([^\]]*)\]\((https?://[^\s\)]+)(?:\s+""[^""]*"")?\)\]\((https?://[^\s\)]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex LinkedImageRegex();

    [GeneratedRegex(@"^!\[([^\]]*)\]\((https?://[^\s\)]+)(?:\s+""[^""]*"")?\)$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"^\[([^\]]*)\]\((https?://[^\s\)]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"<img\b[^>]*\bsrc\s*=\s*[""'](https?://[^""']+)[""'][^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlImgSrcRegex();

    [GeneratedRegex(@"\balt\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlImgAltRegex();

    [GeneratedRegex(@"<(?:video|source)\b[^>]*\bsrc\s*=\s*[""'](https?://[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlVideoSrcRegex();

    [GeneratedRegex(@"^\|?[\s:\-]+\|[\s\|:\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"^([0-9]+)\.\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"(<br\s*/?>)|(`[^`]+`)|(\*\*[^\*]+\*\*)|(\*[^\*]+\*)|(\[[^\]]+\]\(https?://[^\s\)]+\))|(https?://[^\s<>\)\]]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineTokenRegex();

    [GeneratedRegex(@"^\[([^\]]+)\]\((https?://[^\s\)]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex InlineLinkCaptureRegex();

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<MarkdownBlock>();
        var paragraphBuffer = new List<string>();
        var blockquoteParagraphs = new List<string>();
        var blockquoteCurrentLines = new List<string>();
        var inCodeBlock = false;
        var codeBuffer = new List<string>();

        void FlushParagraph()
        {
            if (paragraphBuffer.Count == 0)
            {
                return;
            }
            var joined = string.Join(" ", paragraphBuffer).Trim();
            if (joined.Length > 0)
            {
                blocks.Add(new ParagraphBlock(joined));
            }
            paragraphBuffer.Clear();
        }

        void FlushBlockquote()
        {
            if (blockquoteCurrentLines.Count > 0)
            {
                var joined = string.Join(" ", blockquoteCurrentLines).Trim();
                if (joined.Length > 0)
                {
                    blockquoteParagraphs.Add(joined);
                }
                blockquoteCurrentLines.Clear();
            }
            if (blockquoteParagraphs.Count == 0)
            {
                return;
            }
            blocks.Add(new BlockquoteBlock(string.Join("\n\n", blockquoteParagraphs)));
            blockquoteParagraphs.Clear();
        }

        int index = 0;
        while (index < lines.Length)
        {
            var rawLine = lines[index];
            var trimmed = rawLine.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushBlockquote();
                if (inCodeBlock)
                {
                    blocks.Add(new CodeBlockItem(string.Join("\n", codeBuffer)));
                    codeBuffer.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    FlushParagraph();
                    inCodeBlock = true;
                }
                index++;
                continue;
            }

            if (inCodeBlock)
            {
                codeBuffer.Add(rawLine);
                index++;
                continue;
            }

            if (trimmed.StartsWith("<video", StringComparison.OrdinalIgnoreCase))
            {
                FlushParagraph();
                FlushBlockquote();
                var videoLines = new List<string> { trimmed };
                while (!videoLines[^1].Contains("</video>", StringComparison.OrdinalIgnoreCase) &&
                       !videoLines[^1].Contains("/>", StringComparison.Ordinal) &&
                       index + 1 < lines.Length &&
                       lines[index + 1].Trim().Length > 0)
                {
                    index++;
                    videoLines.Add(lines[index].Trim());
                }
                var combined = string.Join(" ", videoLines);
                var videoBlock = ParseVideoHtml(combined);
                if (videoBlock is not null)
                {
                    blocks.Add(videoBlock);
                }
                else
                {
                    paragraphBuffer.Add(combined);
                }
                index++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var after = trimmed[1..];
                if (after.StartsWith(' '))
                {
                    after = after[1..];
                }
                var content = after.Trim();
                if (content.Length == 0)
                {
                    if (blockquoteCurrentLines.Count > 0)
                    {
                        blockquoteParagraphs.Add(string.Join(" ", blockquoteCurrentLines));
                        blockquoteCurrentLines.Clear();
                    }
                }
                else
                {
                    blockquoteCurrentLines.Add(content);
                }
                index++;
                continue;
            }
            else
            {
                FlushBlockquote();
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                index++;
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new DividerBlock());
                index++;
                continue;
            }

            if (trimmed.Contains('|') &&
                index + 1 < lines.Length &&
                TableSeparatorRegex().IsMatch(lines[index + 1].Trim()))
            {
                FlushParagraph();
                var headers = ParseTableRow(trimmed);
                var rows = new List<IReadOnlyList<string>>();
                index += 2;
                while (index < lines.Length)
                {
                    var rowLine = lines[index].Trim();
                    if (rowLine.Length == 0 || !rowLine.Contains('|'))
                    {
                        break;
                    }
                    var cells = ParseTableRow(rowLine);
                    if (cells.Count < headers.Count)
                    {
                        var padded = new List<string>(cells);
                        while (padded.Count < headers.Count)
                        {
                            padded.Add(string.Empty);
                        }
                        cells = padded;
                    }
                    else if (cells.Count > headers.Count)
                    {
                        cells = cells.Take(headers.Count).ToList();
                    }
                    rows.Add(cells);
                    index++;
                }
                blocks.Add(new TableBlock(headers, rows));
                continue;
            }

            var mediaBlock = ParseMediaOrVideoLink(trimmed);
            if (mediaBlock is not null)
            {
                FlushParagraph();
                blocks.Add(mediaBlock);
                index++;
                continue;
            }

            var imageBlock = ParseImage(trimmed);
            if (imageBlock is not null)
            {
                FlushParagraph();
                blocks.Add(imageBlock);
                index++;
                continue;
            }

            var headingBlock = ParseHeading(trimmed);
            if (headingBlock is not null)
            {
                FlushParagraph();
                blocks.Add(headingBlock);
                index++;
                continue;
            }

            var taskBlock = ParseTaskItem(trimmed);
            if (taskBlock is not null)
            {
                FlushParagraph();
                blocks.Add(taskBlock);
                index++;
                continue;
            }

            var bulletBlock = ParseBullet(rawLine);
            if (bulletBlock is not null)
            {
                FlushParagraph();
                blocks.Add(bulletBlock);
                index++;
                continue;
            }

            var numberedBlock = ParseNumberedList(trimmed);
            if (numberedBlock is not null)
            {
                FlushParagraph();
                blocks.Add(numberedBlock);
                index++;
                continue;
            }

            paragraphBuffer.Add(trimmed);
            index++;
        }

        if (inCodeBlock && codeBuffer.Count > 0)
        {
            blocks.Add(new CodeBlockItem(string.Join("\n", codeBuffer)));
        }
        FlushBlockquote();
        FlushParagraph();

        return blocks;
    }

    public static IReadOnlyList<MarkdownInlineSpan> ParseInline(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var spans = new List<MarkdownInlineSpan>();
        int lastIndex = 0;

        foreach (Match match in InlineTokenRegex().Matches(text))
        {
            if (match.Index > lastIndex)
            {
                spans.Add(new PlainTextSpan(text[lastIndex..match.Index]));
            }

            var token = match.Value;
            if (token.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
            {
                spans.Add(new LineBreakSpan());
            }
            else if (token.StartsWith('`') && token.EndsWith('`') && token.Length >= 2)
            {
                spans.Add(new InlineCodeSpan(token[1..^1]));
            }
            else if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal) && token.Length >= 4)
            {
                spans.Add(new BoldSpan(token[2..^2]));
            }
            else if (token.StartsWith('*') && token.EndsWith('*') && token.Length >= 2)
            {
                spans.Add(new ItalicSpan(token[1..^1]));
            }
            else if (token.StartsWith('['))
            {
                var linkMatch = InlineLinkCaptureRegex().Match(token);
                if (linkMatch.Success)
                {
                    spans.Add(new HyperlinkSpan(linkMatch.Groups[1].Value, linkMatch.Groups[2].Value));
                }
                else
                {
                    spans.Add(new PlainTextSpan(token));
                }
            }
            else if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var cleanUrl = token.TrimEnd('.', ',', ';', '!', '?');
                spans.Add(new HyperlinkSpan(cleanUrl, cleanUrl));
                if (cleanUrl.Length < token.Length)
                {
                    spans.Add(new PlainTextSpan(token[cleanUrl.Length..]));
                }
            }
            else
            {
                spans.Add(new PlainTextSpan(token));
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            spans.Add(new PlainTextSpan(text[lastIndex..]));
        }

        return spans;
    }

    private static HeadingBlock? ParseHeading(string line)
    {
        int level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }
        if (level is < 1 or > 6 || level >= line.Length || line[level] != ' ')
        {
            return null;
        }
        var text = line[(level + 1)..].Trim();
        return text.Length > 0 ? new HeadingBlock(level, text) : null;
    }

    private static TaskItemBlock? ParseTaskItem(string line)
    {
        if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("* [x] ", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskItemBlock(true, line[6..].Trim());
        }
        if (line.StartsWith("- [ ] ", StringComparison.Ordinal) ||
            line.StartsWith("* [ ] ", StringComparison.Ordinal))
        {
            return new TaskItemBlock(false, line[6..].Trim());
        }
        return null;
    }

    private static MarkdownBlock? ParseBullet(string rawLine)
    {
        int leadingSpaces = 0;
        while (leadingSpaces < rawLine.Length && (rawLine[leadingSpaces] == ' ' || rawLine[leadingSpaces] == '\t'))
        {
            leadingSpaces += rawLine[leadingSpaces] == '\t' ? 2 : 1;
        }

        var trimmed = rawLine.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            var content = trimmed[2..].Trim();
            if (content.Length == 0)
            {
                return null;
            }
            if (leadingSpaces >= 2)
            {
                return new NestedBulletBlock(Math.Max(1, leadingSpaces / 2), content);
            }
            return new BulletBlock(content);
        }
        return null;
    }

    private static NumberedListBlock? ParseNumberedList(string line)
    {
        var match = OrderedListRegex().Match(line);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index))
        {
            return null;
        }
        return new NumberedListBlock(index, match.Groups[2].Value.Trim());
    }

    private static IReadOnlyList<string> ParseTableRow(string line)
    {
        var working = line.Trim();
        if (working.StartsWith('|'))
        {
            working = working[1..];
        }
        if (working.EndsWith('|'))
        {
            working = working[..^1];
        }
        return working.Split('|').Select(c => c.Trim()).ToList();
    }

    private static VideoBlock? ParseVideoHtml(string html)
    {
        var match = HtmlVideoSrcRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }
        var url = match.Groups[1].Value;
        if (IsWebmUrl(url))
        {
            return null;
        }
        var altMatch = HtmlImgAltRegex().Match(html);
        var alt = altMatch.Success && !string.IsNullOrWhiteSpace(altMatch.Groups[1].Value)
            ? altMatch.Groups[1].Value
            : null;
        return new VideoBlock(url, alt);
    }

    private static VideoBlock? ParseMediaOrVideoLink(string line)
    {
        var htmlVideo = ParseVideoHtml(line);
        if (htmlVideo is not null)
        {
            return htmlVideo;
        }

        var imgMatch = MarkdownImageRegex().Match(line);
        if (imgMatch.Success)
        {
            var alt = imgMatch.Groups[1].Value;
            var url = imgMatch.Groups[2].Value;
            if (HasNativeVideoExtension(url))
            {
                return new VideoBlock(url, string.IsNullOrWhiteSpace(alt) ? null : alt);
            }
        }

        var linkMatch = MarkdownLinkRegex().Match(line);
        if (linkMatch.Success)
        {
            var label = linkMatch.Groups[1].Value;
            var url = linkMatch.Groups[2].Value;
            if (HasNativeVideoExtension(url))
            {
                return new VideoBlock(url, string.IsNullOrWhiteSpace(label) ? null : label);
            }
        }

        return null;
    }

    private static ImageBlock? ParseImage(string line)
    {
        var linkedMatch = LinkedImageRegex().Match(line);
        if (linkedMatch.Success)
        {
            var alt = linkedMatch.Groups[1].Value;
            var imgUrl = linkedMatch.Groups[2].Value;
            return new ImageBlock(imgUrl, string.IsNullOrWhiteSpace(alt) ? null : alt);
        }

        var mdMatch = MarkdownImageRegex().Match(line);
        if (mdMatch.Success)
        {
            var alt = mdMatch.Groups[1].Value;
            var imgUrl = mdMatch.Groups[2].Value;
            return new ImageBlock(imgUrl, string.IsNullOrWhiteSpace(alt) ? null : alt);
        }

        var htmlMatch = HtmlImgSrcRegex().Match(line);
        if (htmlMatch.Success)
        {
            var imgUrl = htmlMatch.Groups[1].Value;
            var altMatch = HtmlImgAltRegex().Match(line);
            var alt = altMatch.Success && !string.IsNullOrWhiteSpace(altMatch.Groups[1].Value)
                ? altMatch.Groups[1].Value
                : null;
            return new ImageBlock(imgUrl, alt);
        }

        return null;
    }

    private static bool HasNativeVideoExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        var ext = Path.GetExtension(uri.AbsolutePath);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }

    private static bool IsWebmUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               string.Equals(Path.GetExtension(uri.AbsolutePath), ".webm", StringComparison.OrdinalIgnoreCase);
    }
}
