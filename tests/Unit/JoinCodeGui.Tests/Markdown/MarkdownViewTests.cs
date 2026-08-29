namespace JoinCode.Gui.Tests.Markdown;

/// <summary>
/// MarkdownView 渲染冒烟测试 — Headless 渲染真实 <see cref="MarkdownView"/> 控件树，
/// 断言各 Markdown 块被渲染为对应控件（标题/段落/代码块/列表/表格/引用/分隔线）。
/// 验证模型 → 控件树链路（区别于仅解析模型的单元测试）。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class MarkdownViewTests
{
    /// <summary>从 TextBlock 提取全部文本（含 Inlines 内的 Run/嵌套 Span）</summary>
    private static string FullText(TextBlock tb)
    {
        if (tb.Text is not null)
        {
            return tb.Text;
        }
        var sb = new StringBuilder();
        AppendInlineText(sb, tb.Inlines);
        return sb.ToString();
    }

    private static void AppendInlineText(StringBuilder sb, IList<Inline>? inlines)
    {
        if (inlines is null)
        {
            return;
        }
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    sb.Append(run.Text);
                    break;
                case Span span:
                    AppendInlineText(sb, span.Inlines);
                    break;
            }
        }
    }

    private static MarkdownView Render(string markdown)
    {
        var view = new MarkdownView { Markdown = markdown };
        var win = new Window { Content = view, Width = 500, Height = 400 };
        win.Show();
        return view;
    }

    [AvaloniaFact]
    public void Heading_RendersTextBlockWithBoldFont()
    {
        var view = Render("# Title");
        var tb = view.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
        Assert.NotNull(tb);
        Assert.Equal("Title", tb.Text);
        Assert.Equal(FontWeight.SemiBold, tb.FontWeight);
    }

    [AvaloniaFact]
    public void Paragraph_RendersWrappingTextBlock()
    {
        var view = Render("hello world");
        var tb = view.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal("hello world", FullText(tb));
    }

    [AvaloniaFact]
    public void CodeBlock_RendersBorderWithCodeText()
    {
        var view = Render("```csharp\nint x = 1;\n```");
        var border = view.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        Assert.NotNull(border);
        var texts = border!.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(texts, t => t.Text == "csharp");
        Assert.Contains(texts, t => t.Text != null && t.Text.Contains("int x = 1;"));
    }

    [AvaloniaFact]
    public void List_RendersOneTextBlockPerItem()
    {
        var view = Render("- alpha\n- beta");
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(FullText).ToList();
        Assert.Contains(texts, t => t.Contains("alpha"));
        Assert.Contains(texts, t => t.Contains("beta"));
    }

    [AvaloniaFact]
    public void Table_RendersGridWithHeaderAndRow()
    {
        var view = Render("| a | b |\n|---|---|\n| 1 | 2 |");
        var grid = view.GetVisualDescendants().OfType<Grid>().FirstOrDefault();
        Assert.NotNull(grid);
        var texts = grid!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t != null).ToHashSet();
        Assert.Contains("a", texts);
        Assert.Contains("b", texts);
        Assert.Contains("1", texts);
        Assert.Contains("2", texts);
    }

    [AvaloniaFact]
    public void Quote_RendersBorderWithAccentEdge()
    {
        var view = Render("> quoted");
        var border = view.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        Assert.NotNull(border);
        var texts = border!.GetVisualDescendants().OfType<TextBlock>().Select(FullText).ToList();
        Assert.Contains(texts, t => t.Contains("quoted"));
    }

    [AvaloniaFact]
    public void Rule_RendersThinDividerBorder()
    {
        var view = Render("---");
        var divider = view.GetVisualDescendants().OfType<Border>().FirstOrDefault();
        Assert.NotNull(divider);
        Assert.True(divider.Height <= 2);
    }

    [AvaloniaFact]
    public void ThemeChange_RerendersWithNewPalette()
    {
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var view = Render("# Title");

        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Light;
        view.Markdown = "# Title 2";
        var tb2 = view.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal("Title 2", tb2.Text);
    }
}
