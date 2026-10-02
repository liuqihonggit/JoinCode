namespace JoinCode.Gui.Views;

/// <summary>
/// 代码编辑器窗口 — AvaloniaEdit.TextEditor,打开文件编辑保存。
/// 从目录树双击文件打开,独立窗口可多开。
/// </summary>
public sealed partial class EditorWindow : Window {
    private string? _filePath;

    /// <summary>初始化 EditorWindow 实例</summary>
    public EditorWindow() {
        InitializeComponent();
        Editor.TextChanged += OnTextChanged;
    }

    /// <summary>打开指定文件 — 读取内容到编辑器,设置标题和面包屑</summary>
    public void OpenFile(string filePath) {
        _filePath = filePath;
        FileNameText.Text = System.IO.Path.GetFileName(filePath);
        Title = $"{System.IO.Path.GetFileName(filePath)} — 编辑器";
        SetBreadcrumb(filePath);
        try {
            Editor.Text = System.IO.File.ReadAllText(filePath);
            ModifiedIndicator.IsVisible = false;
        } catch (Exception ex) {
            Editor.Text = $"无法读取文件: {ex.Message}";
        }
    }

    /// <summary>设置面包屑 — 从根目录到文件名的路径段</summary>
    private void SetBreadcrumb(string filePath) {
        var segments = new List<string>();
        var dir = System.IO.Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir)) {
            segments.Add(System.IO.Path.GetFileName(dir));
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (parent == dir)
                break;
            dir = parent;
        }
        segments.Reverse();
        segments.Add(System.IO.Path.GetFileName(filePath));
        BreadcrumbItems.ItemsSource = segments;
    }

    /// <summary>内容变更时显示修改指示器</summary>
    private void OnTextChanged(object? sender, EventArgs e) {
        ModifiedIndicator.IsVisible = true;
    }

    /// <summary>保存文件 — Ctrl+S 或点击保存按钮</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e) {
        SaveFile();
    }

    /// <summary>保存文件到磁盘</summary>
    private void SaveFile() {
        if (_filePath is null)
            return;
        try {
            System.IO.File.WriteAllText(_filePath, Editor.Text);
            ModifiedIndicator.IsVisible = false;
        } catch (Exception ex) {
            App.LogDiag($"[EditorWindow] Save failed: {ex.Message}");
        }
    }

    /// <summary>键盘快捷键 — Ctrl+S 保存</summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.S) {
            e.Handled = true;
            SaveFile();
        }
        base.OnKeyDown(e);
    }
}
