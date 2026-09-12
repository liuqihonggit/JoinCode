namespace JoinCode.Gui.Markdown;

/// <summary>
/// Diff 渲染控件 — 接收 <see cref="StructuredPatchHunk"/> 数组，渲染为带行号与增删行高亮的控件树。
/// 颜色全部取自 <see cref="GuiPalette.Current"/>，随主题实时切换。
/// 每行前缀对齐 git diff：添加行 '+'、删除行 '-'、上下文行 ' '。
/// </summary>
public sealed class DiffViewer : StackPanel
{
    /// <summary>待渲染的 Hunk 数组；变化时重建子控件树</summary>
    public static readonly StyledProperty<StructuredPatchHunk[]?> HunksProperty =
        AvaloniaProperty.Register<DiffViewer, StructuredPatchHunk[]?>(nameof(Hunks));

    private static readonly FontFamily MonoFont = new("Consolas");

    public DiffViewer()
    {
        Spacing = 4;
    }

    /// <summary>待渲染的 Hunk 数组；变化时重建子控件树</summary>
    public StructuredPatchHunk[]? Hunks
    {
        get => GetValue(HunksProperty);
        set => SetValue(HunksProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HunksProperty)
        {
            Rebuild();
        }
    }

    /// <summary>根据当前 Hunks 重建子控件树</summary>
    public void Rebuild()
    {
        Children.Clear();
        var hunks = Hunks;
        if (hunks is null || hunks.Length == 0)
            return;

        foreach (var hunk in hunks)
        {
            if (BuildHunk(hunk) is { } control)
            {
                Children.Add(control);
            }
        }
    }

    /// <summary>渲染单个 Hunk：header + 行列表</summary>
    private static Control BuildHunk(StructuredPatchHunk hunk)
    {
        var scheme = GuiPalette.Current;
        var stack = new StackPanel { Spacing = 0 };

        // Hunk header（如 @@ -1,3 +1,4 @@）
        var headerText = string.IsNullOrEmpty(hunk.Header)
            ? $"@@ -{hunk.OldStart},{hunk.OldLines} +{hunk.NewStart},{hunk.NewLines} @@"
            : hunk.Header;
        var header = new SelectableTextBlock
        {
            Text = headerText,
            SelectionBrush = ToBrush("#6680c0"),
            FontFamily = MonoFont,
            FontSize = 11,
            Foreground = ToBrush(scheme.AccentText),
            Background = ToBrush(scheme.BubbleToolCall),
            Padding = new Thickness(6, 2),
            TextWrapping = TextWrapping.NoWrap
        };
        stack.Children.Add(header);

        // Diff 行
        foreach (var line in hunk.Lines)
        {
            stack.Children.Add(BuildDiffLine(line, scheme));
        }

        return new Border
        {
            BorderBrush = ToBrush(scheme.Divider),
            BorderThickness = new Thickness(0, 0, 0, 1),
            ClipToBounds = true,
            Child = stack
        };
    }

    /// <summary>渲染单行 Diff：旧行号 + 新行号 + 前缀 + 内容，按增删类型着色</summary>
    private static Control BuildDiffLine(PatchLine line, GuiPalette.Scheme scheme)
    {
        var (prefix, foreground, background) = line.Type switch
        {
            PatchLineType.Added => ("+", ToBrush(scheme.SuccessText), ToBrush(scheme.DiffAddedBackground)),
            PatchLineType.Removed => ("-", ToBrush(scheme.ErrorText), ToBrush(scheme.DiffRemovedBackground)),
            _ => (" ", ToBrush(scheme.SecondaryText), Brushes.Transparent)
        };

        var oldLineNum = line.OldLineNumber?.ToString() ?? "";
        var newLineNum = line.NewLineNumber?.ToString() ?? "";

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            Background = background
        };

        // 旧行号（左列）
        var oldNumBlock = BuildLineNumberBlock(oldLineNum, scheme);
        Grid.SetColumn(oldNumBlock, 0);
        row.Children.Add(oldNumBlock);

        // 新行号（右列）
        var newNumBlock = BuildLineNumberBlock(newLineNum, scheme);
        Grid.SetColumn(newNumBlock, 1);
        row.Children.Add(newNumBlock);

        // 前缀 (+/-/space)
        var prefixBlock = new SelectableTextBlock
        {
            Text = prefix,
            SelectionBrush = ToBrush("#6680c0"),
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = foreground,
            Padding = new Thickness(2, 1, 2, 1),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(prefixBlock, 2);
        row.Children.Add(prefixBlock);

        // 内容
        var contentBlock = new SelectableTextBlock
        {
            Text = line.Content,
            SelectionBrush = ToBrush("#6680c0"),
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(2, 1, 4, 1),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(contentBlock, 3);
        row.Children.Add(contentBlock);

        return row;
    }

    /// <summary>构建行号单元格</summary>
    private static SelectableTextBlock BuildLineNumberBlock(string text, GuiPalette.Scheme scheme)
        => new()
        {
            Text = text,
            SelectionBrush = ToBrush("#6680c0"),
            FontFamily = MonoFont,
            FontSize = 11,
            Foreground = ToBrush(scheme.MutedText),
            Padding = new Thickness(4, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Top
        };

    private static ISolidColorBrush ToBrush(string hex) => GuiPalette.ToBrush(hex);
}
