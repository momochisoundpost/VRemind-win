namespace VRemindWin.Core.Models;

public abstract record MarkdownBlock;

public sealed record HeadingBlock(int Level, string Text) : MarkdownBlock;

public sealed record ParagraphBlock(string Text) : MarkdownBlock;

public sealed record BulletBlock(string Text) : MarkdownBlock;

public sealed record NestedBulletBlock(int Indent, string Text) : MarkdownBlock;

public sealed record NumberedListBlock(int Index, string Text) : MarkdownBlock;

public sealed record TaskItemBlock(bool Checked, string Text) : MarkdownBlock;

public sealed record BlockquoteBlock(string Text) : MarkdownBlock;

public sealed record TableBlock(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;

public sealed record CodeBlockItem(string Code) : MarkdownBlock;

public sealed record DividerBlock() : MarkdownBlock;

public sealed record ImageBlock(string Url, string? Alt) : MarkdownBlock;

public sealed record VideoBlock(string Url, string? Alt) : MarkdownBlock;

public abstract record MarkdownInlineSpan;

public sealed record PlainTextSpan(string Text) : MarkdownInlineSpan;

public sealed record BoldSpan(string Text) : MarkdownInlineSpan;

public sealed record ItalicSpan(string Text) : MarkdownInlineSpan;

public sealed record InlineCodeSpan(string Text) : MarkdownInlineSpan;

public sealed record HyperlinkSpan(string Label, string Url) : MarkdownInlineSpan;

public sealed record LineBreakSpan() : MarkdownInlineSpan;
