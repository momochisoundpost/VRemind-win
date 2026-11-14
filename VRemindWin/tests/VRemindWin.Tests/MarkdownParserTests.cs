using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class MarkdownParserTests
{
    [Fact]
    public void Parse_Blockquotes_StripsGreaterThanAndJoinsParagraphs()
    {
        const string md = """
            > *I am thrilled and humbled to announce*
            > that blockquotes render properly!
            >
            > Second paragraph inside quote.
            """;

        var blocks = MarkdownParser.Parse(md);
        var quote = Assert.Single(blocks);
        var bq = Assert.IsType<BlockquoteBlock>(quote);
        Assert.DoesNotContain(">", bq.Text);
        Assert.Contains("*I am thrilled and humbled to announce* that blockquotes render properly!", bq.Text);
        Assert.Contains("\n\nSecond paragraph inside quote.", bq.Text);
    }

    [Fact]
    public void Parse_ListsAndTasks_ParsesBulletsNestedBulletsNumberedAndChecklists()
    {
        const string md = """
            # Release 1.3.1
            - Top bullet
              - Sub bullet
            1. First step
            2. Second step
            - [x] Completed task
            - [ ] Pending task
            ---
            """;

        var blocks = MarkdownParser.Parse(md);
        Assert.Collection(
            blocks,
            b =>
            {
                var h = Assert.IsType<HeadingBlock>(b);
                Assert.Equal(1, h.Level);
                Assert.Equal("Release 1.3.1", h.Text);
            },
            b => Assert.Equal(new BulletBlock("Top bullet"), b),
            b => Assert.Equal(new NestedBulletBlock(1, "Sub bullet"), b),
            b => Assert.Equal(new NumberedListBlock(1, "First step"), b),
            b => Assert.Equal(new NumberedListBlock(2, "Second step"), b),
            b => Assert.Equal(new TaskItemBlock(true, "Completed task"), b),
            b => Assert.Equal(new TaskItemBlock(false, "Pending task"), b),
            b => Assert.IsType<DividerBlock>(b));
    }

    [Fact]
    public void Parse_TablesAndCodeBlocks_ParsesGfmTableAndFencedCode()
    {
        const string md = """
            | Platform | Status |
            | --- | --- |
            | YouTube | Live |
            | Twitch | Live |

            ```powershell
            .\VRemindWin.exe
            ```
            """;

        var blocks = MarkdownParser.Parse(md);
        Assert.Equal(2, blocks.Count);

        var table = Assert.IsType<TableBlock>(blocks[0]);
        Assert.Equal(["Platform", "Status"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["YouTube", "Live"], table.Rows[0]);
        Assert.Equal(["Twitch", "Live"], table.Rows[1]);

        var code = Assert.IsType<CodeBlockItem>(blocks[1]);
        Assert.Equal(@".\VRemindWin.exe", code.Code);
    }

    [Fact]
    public void Parse_Media_DetectsHtmlVideoAndMp4Links_LeavesWebmAsParagraph()
    {
        const string md = """
            <video src="https://github.com/user-attachments/assets/1234-5678"></video>

            [Demo Clip](https://example.com/demo.mp4)

            ![Screenshot](https://example.com/screen.png)

            [WebM Clip](https://example.com/clip.webm)
            """;

        var blocks = MarkdownParser.Parse(md);
        Assert.Collection(
            blocks,
            b => Assert.Equal(new VideoBlock("https://github.com/user-attachments/assets/1234-5678", null), b),
            b => Assert.Equal(new VideoBlock("https://example.com/demo.mp4", "Demo Clip"), b),
            b => Assert.Equal(new ImageBlock("https://example.com/screen.png", "Screenshot"), b),
            b => Assert.Equal(new ParagraphBlock("[WebM Clip](https://example.com/clip.webm)"), b));
    }

    [Fact]
    public void ParseInline_ParsesBoldItalicCodeLinksBareUrlsAndBrTags()
    {
        const string line = "Hello **bold** and *italic* with `code`, [GitHub](https://github.com)<br>and https://example.com!";
        var spans = MarkdownParser.ParseInline(line);

        Assert.Contains(spans, s => s is BoldSpan { Text: "bold" });
        Assert.Contains(spans, s => s is ItalicSpan { Text: "italic" });
        Assert.Contains(spans, s => s is InlineCodeSpan { Text: "code" });
        Assert.Contains(spans, s => s is HyperlinkSpan { Label: "GitHub", Url: "https://github.com" });
        Assert.Contains(spans, s => s is LineBreakSpan);
        Assert.Contains(spans, s => s is HyperlinkSpan { Label: "https://example.com", Url: "https://example.com" });
    }

    [Fact]
    public void Parse_SelfClosingVideoTag_DoesNotSwallowFollowingBlocks()
    {
        const string md = """
            <video src="https://github.com/user-attachments/assets/self-closing.mp4" />

            ### After Video
            - Item one
            """;

        var blocks = MarkdownParser.Parse(md);
        Assert.Collection(
            blocks,
            b => Assert.Equal(new VideoBlock("https://github.com/user-attachments/assets/self-closing.mp4", null), b),
            b => Assert.Equal(new HeadingBlock(3, "After Video"), b),
            b => Assert.Equal(new BulletBlock("Item one"), b));
    }
}
