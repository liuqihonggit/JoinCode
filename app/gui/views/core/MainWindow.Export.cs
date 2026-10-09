namespace JoinCode.Gui.Views;

/// <summary>通过系统保存对话框写入 Markdown，路径选择属于视图层。</summary>
public sealed partial class MainWindow {
    /// <summary>选择保存位置并导出 UTF-8 Markdown，取消时无副作用。</summary>
    private void OnSaveSessionMarkdown(object? sender, RoutedEventArgs e) => _ = SaveSessionMarkdownAsync();

    /// <summary>异步导出实现，捕获保存失败并显示可操作提示。</summary>
    private async Task SaveSessionMarkdownAsync() {
        if (_vm is null || !_vm.HasMessages) return;
        var snapshot = _vm.ExportSessionMarkdown;
        try {
            using var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions {
                Title = "导出会话为 Markdown",
                SuggestedFileName = $"JoinCode-{DateTime.Now:yyyyMMdd-HHmmss}.md",
                DefaultExtension = "md",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("Markdown") { Patterns = ["*.md"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(snapshot);
            _vm.StatusText = "Markdown 已导出";
        } catch (Exception ex) {
            _vm.StatusText = $"导出失败: {ex.Message}。请重新选择有写入权限的文件位置。";
        }
    }
}
