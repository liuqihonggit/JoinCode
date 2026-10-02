namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 主区视图类型 — 消息对话区或代码编辑器区。
/// </summary>
public enum MainAreaKind {
    /// <summary>消息对话区(AI 聊天)</summary>
    Messages,
    /// <summary>代码编辑器区(多标签页)</summary>
    Editor
}

/// <summary>
/// MainViewModel 编辑器 partial — 内嵌代码编辑器(非弹窗),多标签页管理。
/// 主区在消息对话区和编辑器区之间切换,Activity Bar 编辑器图标控制。
/// 从目录树双击文件或快速打开(Ctrl+P)选择文件时,自动切换到编辑器区并打开标签页。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>当前主区视图 — 消息区或编辑器区</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMessagesViewActive))]
    [NotifyPropertyChangedFor(nameof(IsEditorViewActive))]
    private MainAreaKind _activeMainArea = MainAreaKind.Messages;

    /// <summary>消息区是否激活</summary>
    public bool IsMessagesViewActive => ActiveMainArea == MainAreaKind.Messages;

    /// <summary>编辑器区是否激活</summary>
    public bool IsEditorViewActive => ActiveMainArea == MainAreaKind.Editor;

    /// <summary>编辑器标签页列表 — 每个打开的文件一个标签</summary>
    public ObservableCollection<EditorTabVm> EditorTabs { get; } = [];

    /// <summary>当前激活的编辑器标签页 — null 表示无标签页</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorTabs))]
    private EditorTabVm? _activeEditorTab;

    /// <summary>是否有打开的编辑器标签页</summary>
    public bool HasEditorTabs => EditorTabs.Count > 0;

    /// <summary>切换主区视图 — 在消息区和编辑器区之间切换</summary>
    [RelayCommand]
    private void ToggleEditorView() {
        ActiveMainArea = ActiveMainArea == MainAreaKind.Editor
            ? MainAreaKind.Messages
            : MainAreaKind.Editor;
    }

    /// <summary>切换到消息区视图</summary>
    [RelayCommand]
    private void ShowMessagesView() {
        ActiveMainArea = MainAreaKind.Messages;
    }

    /// <summary>切换到编辑器视图</summary>
    [RelayCommand]
    private void ShowEditorView() {
        ActiveMainArea = MainAreaKind.Editor;
    }

    /// <summary>在编辑器中打开文件 — 已打开则激活对应标签,否则新建标签并切换到编辑器区</summary>
    public void OpenEditorFile(string filePath) {
        var existing = EditorTabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null) {
            SetActiveEditorTab(existing);
        } else {
            var tab = new EditorTabVm {
                FilePath = filePath,
                Content = ReadFileContent(filePath)
            };
            EditorTabs.Add(tab);
            SetActiveEditorTab(tab);
        }
        ActiveMainArea = MainAreaKind.Editor;
    }

    /// <summary>读取文件内容 — 失败返回错误提示文本</summary>
    private static string ReadFileContent(string path) {
        try {
            return System.IO.File.ReadAllText(path);
        } catch (Exception ex) {
            return $"无法读取文件: {ex.Message}";
        }
    }

    /// <summary>设置激活标签页 — 取消其他标签的 IsActive,设置目标标签 IsActive</summary>
    private void SetActiveEditorTab(EditorTabVm? tab) {
        foreach (var t in EditorTabs)
            t.IsActive = false;
        if (tab is not null)
            tab.IsActive = true;
        ActiveEditorTab = tab;
    }

    /// <summary>关闭编辑器标签页 — 关闭激活标签时自动激活相邻标签,无标签时切回消息区</summary>
    [RelayCommand]
    private void CloseEditorTab(EditorTabVm? tab) {
        if (tab is null)
            return;
        var index = EditorTabs.IndexOf(tab);
        EditorTabs.Remove(tab);
        if (ActiveEditorTab == tab) {
            if (EditorTabs.Count == 0) {
                SetActiveEditorTab(null);
                ActiveMainArea = MainAreaKind.Messages;
            } else {
                var nextIndex = Math.Min(index, EditorTabs.Count - 1);
                SetActiveEditorTab(EditorTabs[nextIndex]);
            }
        }
    }

    /// <summary>激活编辑器标签页 — 点击标签栏时调用</summary>
    [RelayCommand]
    private void ActivateEditorTab(EditorTabVm? tab) {
        if (tab is null)
            return;
        SetActiveEditorTab(tab);
    }

    /// <summary>保存当前激活标签页文件到磁盘 — Ctrl+S 触发</summary>
    [RelayCommand]
    private void SaveActiveTab() {
        if (ActiveEditorTab is not { } tab)
            return;
        try {
            System.IO.File.WriteAllText(tab.FilePath, tab.Content);
            tab.IsModified = false;
        } catch (Exception ex) {
            App.LogDiag($"[Editor] Save failed: {ex.Message}");
        }
    }
}
