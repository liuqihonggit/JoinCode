namespace JoinCode.Gui.Markdown;

/// <summary>统一差异行着色，颜色读取当前主题的语义色。</summary>
internal sealed class UnifiedDiffColorizer : AvaloniaEdit.Rendering.DocumentColorizingTransformer {
    /// <summary>区分增删行、块头与文件头。</summary>
    internal static char Kind(string text) => text switch {
        var line when line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) => ' ',
        var line when line.StartsWith('+') => '+',
        var line when line.StartsWith('-') => '-',
        var line when line.StartsWith("@@", StringComparison.Ordinal) => '@',
        _ => ' '
    };

    /// <inheritdoc />
    protected override void ColorizeLine(AvaloniaEdit.Document.DocumentLine line) {
        var kind = Kind(CurrentContext.Document.GetText(line));
        if (kind == ' ') return;
        var palette = GuiPalette.Current;
        var foreground = GuiPalette.ToBrush(kind switch { '+' => palette.SuccessText, '-' => palette.ErrorText, _ => palette.AccentText });
        ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(foreground));
    }
}
