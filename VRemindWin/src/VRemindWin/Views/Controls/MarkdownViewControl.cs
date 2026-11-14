using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI;

namespace VRemindWin.Views.Controls;

/// <summary>
/// Renders GitHub-flavored Markdown release notes and inline MP4/MOV video blocks
/// using 100% native WinUI 3 XAML and <see cref="MediaPlayerElement"/> (zero WebView2 dependency).
/// </summary>
public sealed class MarkdownViewControl : UserControl
{
    private readonly StackPanel _rootStack;
    private readonly List<(MediaPlayerElement Element, MediaPlayer Player, MediaSource? Source)> _activePlayers = [];
    private Action<Uri>? _onOpenUri;

    public MarkdownViewControl()
    {
        _rootStack = new StackPanel
        {
            Spacing = 8
        };
        Content = _rootStack;
        Unloaded += (_, _) => CleanupMediaPlayers();
    }

    public void SetMarkdown(string? markdown, Action<Uri>? onOpenUri)
    {
        _onOpenUri = onOpenUri;
        CleanupMediaPlayers();
        _rootStack.Children.Clear();

        var blocks = MarkdownParser.Parse(markdown);
        foreach (var block in blocks)
        {
            var element = RenderBlock(block);
            if (element is not null)
            {
                _rootStack.Children.Add(element);
            }
        }
    }

    public void CleanupMediaPlayers()
    {
        foreach (var (element, player, source) in _activePlayers)
        {
            try
            {
                player.Pause();
                element.SetMediaPlayer(null);
                player.Source = null;
                source?.Dispose();
                player.Dispose();
            }
            catch
            {
                // Ignore teardown race errors on close
            }
        }
        _activePlayers.Clear();
    }

    private UIElement? RenderBlock(MarkdownBlock block)
    {
        return block switch
        {
            HeadingBlock h => RenderHeading(h),
            ParagraphBlock p => CreateRichText(p.Text, 12.5, FontWeights.Normal, false),
            BulletBlock b => RenderBulletItem("•", b.Text, 0),
            NestedBulletBlock nb => RenderBulletItem("◦", nb.Text, nb.Indent),
            NumberedListBlock nl => RenderBulletItem($"{nl.Index}.", nl.Text, 0),
            TaskItemBlock t => RenderTaskItem(t),
            BlockquoteBlock bq => RenderBlockquote(bq),
            TableBlock tbl => RenderTable(tbl),
            CodeBlockItem code => RenderCodeBlock(code),
            DividerBlock => RenderDivider(),
            ImageBlock img => RenderImage(img),
            VideoBlock vid => RenderVideo(vid),
            _ => null
        };
    }

    private UIElement RenderHeading(HeadingBlock heading)
    {
        double fontSize = heading.Level switch
        {
            1 => 16.0,
            2 => 14.0,
            3 => 13.0,
            _ => 12.5
        };

        var panel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, heading.Level <= 2 ? 6 : 3, 0, 2)
        };

        panel.Children.Add(CreateRichText(heading.Text, fontSize, FontWeights.SemiBold, false));

        if (heading.Level <= 2)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(40, 128, 128, 128))
            });
        }

        return panel;
    }

    private UIElement RenderBulletItem(string marker, string text, int indentLevel)
    {
        var grid = new Grid
        {
            ColumnSpacing = 6,
            Margin = new Thickness(indentLevel * 16, 0, 0, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var markerBlock = new TextBlock
        {
            Text = marker,
            FontSize = 12.5,
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(markerBlock, 0);

        var contentBlock = CreateRichText(text, 12.5, FontWeights.Normal, false);
        Grid.SetColumn(contentBlock, 1);

        grid.Children.Add(markerBlock);
        grid.Children.Add(contentBlock);
        return grid;
    }

    private UIElement RenderTaskItem(TaskItemBlock task)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Background = task.Checked
                ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
                : new SolidColorBrush(Colors.Transparent),
            BorderBrush = task.Checked
                ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
                : GetThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
            BorderThickness = new Thickness(1.5),
            Child = task.Checked
                ? new TextBlock
                {
                    Text = "✓",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : null
        };
        Grid.SetColumn(badge, 0);

        var contentBlock = CreateRichText(task.Text, 12.5, FontWeights.Normal, false);
        Grid.SetColumn(contentBlock, 1);

        grid.Children.Add(badge);
        grid.Children.Add(contentBlock);
        return grid;
    }

    private UIElement RenderBlockquote(BlockquoteBlock blockquote)
    {
        var innerStack = new StackPanel { Spacing = 6 };
        var paragraphs = blockquote.Text.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var para in paragraphs)
        {
            innerStack.Children.Add(CreateRichText(
                para,
                12.0,
                FontWeights.Normal,
                defaultItalic: true,
                secondaryForeground: true));
        }

        return new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 59, 130, 246)),
            Background = GetThemeBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(24, 128, 128, 128)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Child = innerStack
        };
    }

    private UIElement RenderTable(TableBlock table)
    {
        var grid = new Grid();
        int colCount = Math.Max(1, table.Headers.Count);
        for (int c = 0; c < colCount; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        int totalRows = 1 + table.Rows.Count;
        for (int r = 0; r < totalRows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int c = 0; c < table.Headers.Count; c++)
        {
            var headerCell = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Background = GetThemeBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(35, 128, 128, 128)),
                BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(40, 128, 128, 128)),
                BorderThickness = new Thickness(0, 0, c < colCount - 1 ? 1 : 0, 1),
                Child = CreateRichText(table.Headers[c], 11.5, FontWeights.SemiBold, false)
            };
            Grid.SetRow(headerCell, 0);
            Grid.SetColumn(headerCell, c);
            grid.Children.Add(headerCell);
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (int c = 0; c < Math.Min(colCount, row.Count); c++)
            {
                var cell = new Border
                {
                    Padding = new Thickness(8, 5, 8, 5),
                    Background = r % 2 == 1
                        ? GetThemeBrush("SubtleFillColorTertiaryBrush", Color.FromArgb(14, 128, 128, 128))
                        : new SolidColorBrush(Colors.Transparent),
                    BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(40, 128, 128, 128)),
                    BorderThickness = new Thickness(0, 0, c < colCount - 1 ? 1 : 0, r < table.Rows.Count - 1 ? 1 : 0),
                    Child = CreateRichText(row[c], 11.5, FontWeights.Normal, false)
                };
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(50, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    private UIElement RenderCodeBlock(CodeBlockItem codeBlock)
    {
        var text = new TextBlock
        {
            Text = codeBlock.Code,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            IsTextSelectionEnabled = true,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Colors.White)
        };

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Background = GetThemeBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(30, 128, 128, 128)),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(40, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = text
            }
        };
    }

    private static UIElement RenderDivider()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            Background = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(50, 128, 128, 128))
        };
    }

    private UIElement RenderImage(ImageBlock imageBlock)
    {
        if (!LinkSafetyValidator.TryValidateAllowedUri(imageBlock.Url, out var safeUri) || safeUri is null)
        {
            return CreateFallbackMediaButton(imageBlock.Alt ?? "Open Image in Browser", imageBlock.Url);
        }

        var container = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(40, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2)
        };

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            MaxHeight = 240,
            Source = new BitmapImage(safeUri)
        };

        image.ImageFailed += (_, _) =>
        {
            container.Child = CreateFallbackMediaButton(imageBlock.Alt ?? "Open Image in Browser", imageBlock.Url);
        };

        container.Child = image;
        return container;
    }

    private UIElement RenderVideo(VideoBlock videoBlock)
    {
        if (!LinkSafetyValidator.TryValidateAllowedUri(videoBlock.Url, out var safeUri) || safeUri is null)
        {
            return CreateFallbackMediaButton(videoBlock.Alt ?? "Open Video in Browser", videoBlock.Url);
        }

        var hostBorder = new Border
        {
            Height = 210,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 128)),
            BorderThickness = new Thickness(1)
        };

        try
        {
            var mediaSource = MediaSource.CreateFromUri(safeUri);
            var mediaPlayer = new MediaPlayer
            {
                Source = mediaSource,
                IsLoopingEnabled = true,
                IsMuted = true,
                AutoPlay = true
            };

            var playerElement = new MediaPlayerElement
            {
                AreTransportControlsEnabled = false,
                Stretch = Stretch.Uniform
            };
            playerElement.SetMediaPlayer(mediaPlayer);
            _activePlayers.Add((playerElement, mediaPlayer, mediaSource));

            var pausedBadge = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Glyph = "\uE768",
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Colors.White)
                }
            };

            var muteLabel = new TextBlock
            {
                Text = "Muted",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Colors.White)
            };

            var muteIcon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\uE74F",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White)
            };

            var mutePillContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5
            };
            mutePillContent.Children.Add(muteIcon);
            mutePillContent.Children.Add(muteLabel);

            var muteButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Content = mutePillContent
            };

            muteButton.Click += (_, _) =>
            {
                try
                {
                    mediaPlayer.IsMuted = !mediaPlayer.IsMuted;
                    muteLabel.Text = mediaPlayer.IsMuted ? "Muted" : "Unmuted";
                    muteIcon.Glyph = mediaPlayer.IsMuted ? "\uE74F" : "\uE767";
                }
                catch
                {
                    // Ignore if disposed
                }
            };

            var overlayGrid = new Grid
            {
                Background = new SolidColorBrush(Colors.Transparent)
            };
            overlayGrid.Children.Add(playerElement);
            overlayGrid.Children.Add(pausedBadge);
            overlayGrid.Children.Add(muteButton);

            bool isPaused = false;
            overlayGrid.Tapped += (sender, e) =>
            {
                if (e.OriginalSource is DependencyObject dep && IsInsideButton(dep))
                {
                    return;
                }

                try
                {
                    if (isPaused)
                    {
                        mediaPlayer.Play();
                        isPaused = false;
                        pausedBadge.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        mediaPlayer.Pause();
                        isPaused = true;
                        pausedBadge.Visibility = Visibility.Visible;
                    }
                }
                catch
                {
                    // Ignore playback state errors
                }
            };

            mediaPlayer.MediaFailed += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    hostBorder.Height = double.NaN;
                    hostBorder.Background = new SolidColorBrush(Colors.Transparent);
                    hostBorder.Child = CreateFallbackMediaButton(videoBlock.Alt ?? "Open Video in Browser", videoBlock.Url);
                });
            };

            hostBorder.Child = overlayGrid;
            return hostBorder;
        }
        catch
        {
            return CreateFallbackMediaButton(videoBlock.Alt ?? "Open Video in Browser", videoBlock.Url);
        }
    }

    private UIElement CreateFallbackMediaButton(string label, string rawUrl)
    {
        var button = new Button
        {
            Content = $"↗ {label}",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) =>
        {
            if (LinkSafetyValidator.TryValidateAllowedUri(rawUrl, out var safeUri) && safeUri is not null)
            {
                _onOpenUri?.Invoke(safeUri);
            }
        };
        return button;
    }

    private RichTextBlock CreateRichText(
        string text,
        double fontSize,
        Windows.UI.Text.FontWeight fontWeight,
        bool defaultItalic,
        bool secondaryForeground = false)
    {
        var richText = new RichTextBlock
        {
            FontSize = fontSize,
            FontWeight = fontWeight,
            FontStyle = defaultItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = secondaryForeground
                ? GetThemeBrush("TextFillColorSecondaryBrush", Colors.LightGray)
                : GetThemeBrush("TextFillColorPrimaryBrush", Colors.White)
        };

        var paragraph = new Paragraph();
        foreach (var span in MarkdownParser.ParseInline(text))
        {
            switch (span)
            {
                case PlainTextSpan plain:
                    paragraph.Inlines.Add(new Run { Text = plain.Text });
                    break;

                case BoldSpan bold:
                    var boldInline = new Bold();
                    boldInline.Inlines.Add(new Run { Text = bold.Text });
                    paragraph.Inlines.Add(boldInline);
                    break;

                case ItalicSpan italic:
                    var italicInline = new Italic();
                    italicInline.Inlines.Add(new Run { Text = italic.Text });
                    paragraph.Inlines.Add(italicInline);
                    break;

                case InlineCodeSpan code:
                    paragraph.Inlines.Add(new Run
                    {
                        Text = code.Text,
                        FontFamily = new FontFamily("Consolas")
                    });
                    break;

                case HyperlinkSpan link:
                    if (LinkSafetyValidator.TryValidateAllowedUri(link.Url, out var safeUri) && safeUri is not null)
                    {
                        var hyperlink = new Hyperlink
                        {
                            UnderlineStyle = UnderlineStyle.Single
                        };
                        hyperlink.Inlines.Add(new Run { Text = link.Label });
                        hyperlink.Click += (_, _) => _onOpenUri?.Invoke(safeUri);
                        paragraph.Inlines.Add(hyperlink);
                    }
                    else
                    {
                        paragraph.Inlines.Add(new Run { Text = link.Label });
                    }
                    break;

                case LineBreakSpan:
                    paragraph.Inlines.Add(new LineBreak());
                    break;
            }
        }

        richText.Blocks.Add(paragraph);
        return richText;
    }

    private static bool IsInsideButton(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static Brush GetThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources is { } res &&
            res.TryGetValue(key, out var val) &&
            val is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback);
    }
}
