using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

using JoinCode.Gui.Theming;

namespace JoinCode.Gui.Markdown;

/// <summary>
/// Markdown 渲染控件 — 接收 Markdown 文本，内部用 <see cref="MarkdownParser"/> 解析为模型，
/// 再构建为控件树（标题/段落/代码块/列表/表格/引用/分隔线）。
/// 颜色全部取自 <see cref="GuiPalette.Current"/>，随主题实时切换。
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<double> BaseFontSizeProperty =
        AvaloniaProperty.Register<MarkdownView, double>(nameof(BaseFontSize), 14);

    public MarkdownView()
    {
        Spacing = 6;
    }

    /// <summary>待渲染的 Markdown 文本；变化时重建子控件树</summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>正文字号（用户消息字号设置）；标题字号按比例放大</summary>
    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            Rebuild();
        }
    }

    /// <summary>根据当前 Markdown 重建子控件树</summary>
    public void Rebuild()
    {
        Children.Clear();
        foreach (var block in MarkdownParser.Parse(Markdown ?? string.Empty))
        {
            if (BuildBlock(block) is { } control)
            {
                Children.Add(control);
            }
        }
    }

    private static readonly FontFamily MonoFont = new("Consolas");

    /// <summary>把块模型渲染为控件；不支持的块返回 null</summary>
    private Control? BuildBlock(MarkdownBlock block)
    {
        var scheme = GuiPalette.Current;
        switch (block)
        {
            case MarkdownHeading heading:
                return new SelectableTextBlock
                {
                    Text = PlainText(heading.Inlines),
                    FontSize = HeadingFontSize(heading.Level, BaseFontSize),
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ToBrush(scheme.PrimaryText),
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
            case MarkdownParagraph paragraph:
                return BuildInlineText(paragraph.Inlines, scheme, BaseFontSize, wrap: true);
            case MarkdownCodeBlock code:
                return BuildCodeBlock(code, scheme);
            case MarkdownList list:
                return BuildList(list, scheme, BaseFontSize);
            case MarkdownTable table:
                return BuildTable(table, scheme);
            case MarkdownQuote quote:
                return BuildQuote(quote, scheme, BaseFontSize);
            case MarkdownRule:
                return new Border
                {
                    Height = 1,
                    Background = ToBrush(scheme.Divider),
                    Margin = new Thickness(0, 6)
                };
            default:
                return null;
        }
    }

    private static double HeadingFontSize(int level, double baseSize) => level switch
    {
        1 => baseSize * 1.43,
        2 => baseSize * 1.21,
        3 => baseSize * 1.07,
        _ => baseSize
    };

    /// <summary>段落/内联构建为 TextBlock（含粗体/斜体/行内代码/链接/删除线 Run）</summary>
    private static TextBlock BuildInlineText(IReadOnlyList<MarkdownInline> inlines, GuiPalette.Scheme scheme, double baseSize, bool wrap)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Foreground = ToBrush(scheme.PrimaryText),
            FontSize = baseSize,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            MaxLines = wrap ? 0 : 1
        };
        var collection = new InlineCollection();
        foreach (var inline in inlines)
        {
            AppendInline(collection, inline, scheme);
        }
        tb.Inlines = collection;
        return tb;
    }

    /// <summary>把单个内联元素递归追加到 InlineCollection</summary>
    private static void AppendInline(InlineCollection collection, MarkdownInline inline, GuiPalette.Scheme scheme)
    {
        switch (inline)
        {
            case MarkdownText text:
                collection.Add(new Run(text.Text));
                break;
            case MarkdownBold bold:
                collection.Add(new Run(PlainText(bold.Children)) { FontWeight = FontWeight.Bold });
                break;
            case MarkdownItalic italic:
                collection.Add(new Run(PlainText(italic.Children)) { FontStyle = FontStyle.Italic });
                break;
            case MarkdownCode code:
                collection.Add(new Run(code.Code)
                {
                    FontFamily = MonoFont,
                    Foreground = ToBrush(scheme.AccentText)
                });
                break;
            case MarkdownLink link:
                collection.Add(new Run(PlainText(link.Children))
                {
                    Foreground = ToBrush(scheme.AccentText),
                    TextDecorations = TextDecorations.Underline
                });
                break;
            case MarkdownStrikethrough strike:
                collection.Add(new Run(PlainText(strike.Children))
                {
                    TextDecorations = TextDecorations.Strikethrough
                });
                break;
        }
    }

    /// <summary>代码块 → 深色底 Border + 语言标识 + 等宽正文</summary>
    private static Control BuildCodeBlock(MarkdownCodeBlock code, GuiPalette.Scheme scheme)
    {
        var lang = string.IsNullOrWhiteSpace(code.Language) ? "code" : code.Language;
        var header = new SelectableTextBlock
        {
            Text = lang,
            FontSize = 11,
            FontFamily = MonoFont,
            Foreground = ToBrush(scheme.MutedText),
            Margin = new Thickness(8, 6, 8, 0)
        };
        var body = new SelectableTextBlock
        {
            Text = code.Code.TrimEnd('\n'),
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = ToBrush(scheme.PrimaryText),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 4, 8, 8)
        };
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(header);
        stack.Children.Add(body);
        return new Border
        {
            Background = ToBrush(scheme.CodeBlockBackground),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = stack
        };
    }

    /// <summary>列表 → 每项一行（无序 • / 有序 N.）</summary>
    private static Control BuildList(MarkdownList list, GuiPalette.Scheme scheme, double baseSize)
    {
        var stack = new StackPanel { Spacing = 2 };
        for (int i = 0; i < list.Items.Count; i++)
        {
            var prefix = list.Ordered ? $"{i + 1}. " : "• ";
            var item = new SelectableTextBlock
            {
                FontSize = baseSize,
                Foreground = ToBrush(scheme.PrimaryText),
                TextWrapping = TextWrapping.Wrap
            };
            var collection = new InlineCollection { new Run(prefix) };
            foreach (var inline in list.Items[i])
            {
                AppendInline(collection, inline, scheme);
            }
            item.Inlines = collection;
            stack.Children.Add(item);
        }
        return stack;
    }

    /// <summary>表格 → Grid（列 Auto + 表头加粗，首行分隔线）</summary>
    private static Control BuildTable(MarkdownTable table, GuiPalette.Scheme scheme)
    {
        if (table.Header.Count == 0)
        {
            return null!;
        }

        var colCount = table.Header.Count;
        var grid = new Grid();
        var defs = new ColumnDefinitions();
        for (int c = 0; c < colCount; c++)
        {
            defs.Add(new ColumnDefinition(GridLength.Auto));
        }
        grid.ColumnDefinitions = defs;
        grid.RowDefinitions = new RowDefinitions(
            string.Join(",", Enumerable.Repeat("Auto", table.Rows.Count + 1)));

        for (int c = 0; c < colCount; c++)
        {
            var cell = new SelectableTextBlock
            {
                Text = table.Header[c],
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = ToBrush(scheme.PrimaryText),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 4)
            };
            Grid.SetColumn(cell, c);
            Grid.SetRow(cell, 0);
            grid.Children.Add(cell);
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            for (int c = 0; c < Math.Min(colCount, table.Rows[r].Count); c++)
            {
                var cell = new SelectableTextBlock
                {
                    Text = table.Rows[r][c],
                    FontSize = 12,
                    Foreground = ToBrush(scheme.SecondaryText),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 4)
                };
                Grid.SetColumn(cell, c);
                Grid.SetRow(cell, r + 1);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            Background = ToBrush(scheme.SearchBarBackground),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = grid
        };
    }

    /// <summary>引用块 → 左侧竖条 + 次级文本</summary>
    private static Control BuildQuote(MarkdownQuote quote, GuiPalette.Scheme scheme, double baseSize)
    {
        var text = new SelectableTextBlock
        {
            Inlines = BuildInlines(quote.Inlines, scheme),
            FontSize = baseSize,
            Foreground = ToBrush(scheme.SecondaryText),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 6)
        };
        return new Border
        {
            Background = ToBrush(scheme.BubbleThinking),
            BorderBrush = ToBrush(scheme.AccentText),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = text
        };
    }

    /// <summary>内联列表 → InlineCollection（供 TextBlock.Inlines 赋值）</summary>
    private static InlineCollection BuildInlines(IReadOnlyList<MarkdownInline> inlines, GuiPalette.Scheme scheme)
    {
        var collection = new InlineCollection();
        foreach (var inline in inlines)
        {
            AppendInline(collection, inline, scheme);
        }
        return collection;
    }

    /// <summary>把内联列表拍平为纯文本（标题/粗体等取文本用）</summary>
    private static string PlainText(IReadOnlyList<MarkdownInline> inlines)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownText t:
                    sb.Append(t.Text);
                    break;
                case MarkdownBold b:
                    sb.Append(PlainText(b.Children));
                    break;
                case MarkdownItalic i:
                    sb.Append(PlainText(i.Children));
                    break;
                case MarkdownLink l:
                    sb.Append(PlainText(l.Children));
                    break;
                case MarkdownStrikethrough s:
                    sb.Append(PlainText(s.Children));
                    break;
                case MarkdownCode c:
                    sb.Append(c.Code);
                    break;
            }
        }
        return sb.ToString();
    }

    private static ISolidColorBrush ToBrush(string hex) => GuiPalette.ToBrush(hex);
}
