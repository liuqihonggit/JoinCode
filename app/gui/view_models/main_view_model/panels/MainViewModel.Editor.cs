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

    /// <summary>是否分屏编辑 — 左右两组独立标签页</summary>
    [ObservableProperty]
    private bool _isSplitEditor;

    /// <summary>第二编辑器组标签页列表</summary>
    public ObservableCollection<EditorTabVm> EditorTabs2 { get; } = [];

    /// <summary>第二编辑器组当前激活标签页</summary>
    [ObservableProperty]
    private EditorTabVm? _activeEditorTab2;

    /// <summary>第二编辑器组是否有标签页</summary>
    public bool HasEditorTabs2 => EditorTabs2.Count > 0;

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

    /// <summary>在编辑器中打开文件(预览模式) — 已打开则激活,预览标签被替换,否则新建预览标签</summary>
    public void OpenEditorFile(string filePath) {
        OpenEditorFileCore(filePath, isPreview: true);
    }

    /// <summary>在编辑器中打开文件(固定模式) — 已打开则激活并取消预览,否则新建固定标签</summary>
    public void OpenEditorFilePinned(string filePath) {
        OpenEditorFileCore(filePath, isPreview: false);
    }

    /// <summary>打开文件核心逻辑 — 预览模式下替换当前预览标签,固定模式下新建固定标签</summary>
    private void OpenEditorFileCore(string filePath, bool isPreview) {
        var existing = EditorTabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null) {
            if (!isPreview)
                existing.IsPreview = false;
            SetActiveEditorTab(existing);
        } else {
            if (isPreview && ActiveEditorTab is { IsPreview: true } previewTab) {
                var index = EditorTabs.IndexOf(previewTab);
                EditorTabs[index] = new EditorTabVm {
                    FilePath = filePath,
                    Content = ReadFileContent(filePath),
                    IsPreview = true
                };
                SetActiveEditorTab(EditorTabs[index]);
            } else {
                var tab = new EditorTabVm {
                    FilePath = filePath,
                    Content = ReadFileContent(filePath),
                    IsPreview = isPreview
                };
                EditorTabs.Add(tab);
                SetActiveEditorTab(tab);
            }
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

    /// <summary>关闭所有编辑器标签页 — 锁定标签保留</summary>
    [RelayCommand]
    private void CloseAllEditorTabs() {
        var toClose = EditorTabs.Where(t => !t.IsPinned).ToList();
        foreach (var tab in toClose)
            EditorTabs.Remove(tab);
        if (EditorTabs.Count == 0) {
            SetActiveEditorTab(null);
            ActiveMainArea = MainAreaKind.Messages;
        } else {
            SetActiveEditorTab(EditorTabs[0]);
        }
    }

    /// <summary>关闭除指定标签外的所有标签 — 锁定标签保留</summary>
    [RelayCommand]
    private void CloseOtherEditorTabs(EditorTabVm? keep) {
        if (keep is null)
            return;
        var toClose = EditorTabs.Where(t => t != keep && !t.IsPinned).ToList();
        foreach (var tab in toClose)
            EditorTabs.Remove(tab);
        SetActiveEditorTab(keep);
    }

    /// <summary>切换标签页锁定状态 — 锁定标签不可被关闭</summary>
    [RelayCommand]
    private void TogglePinTab(EditorTabVm? tab) {
        if (tab is not null)
            tab.IsPinned = !tab.IsPinned;
    }

    /// <summary>将标签页固定为非预览状态 — 双击标签时调用</summary>
    [RelayCommand]
    private void PinTab(EditorTabVm? tab) {
        if (tab is not null)
            tab.IsPreview = false;
    }

    /// <summary>切换分屏编辑 — 开关右侧第二编辑器组</summary>
    [RelayCommand]
    private void ToggleSplitEditor() {
        IsSplitEditor = !IsSplitEditor;
        if (IsSplitEditor && EditorTabs2.Count == 0 && ActiveEditorTab is not null) {
            var tab = new EditorTabVm {
                FilePath = ActiveEditorTab.FilePath,
                Content = ActiveEditorTab.Content
            };
            EditorTabs2.Add(tab);
            SetActiveEditorTab2(tab);
        }
    }

    /// <summary>在第二编辑器组中打开文件</summary>
    public void OpenEditorFileInGroup2(string filePath) {
        var existing = EditorTabs2.FirstOrDefault(t => t.FilePath == filePath);
        if (existing is not null) {
            SetActiveEditorTab2(existing);
        } else {
            var tab = new EditorTabVm {
                FilePath = filePath,
                Content = ReadFileContent(filePath)
            };
            EditorTabs2.Add(tab);
            SetActiveEditorTab2(tab);
        }
        IsSplitEditor = true;
        ActiveMainArea = MainAreaKind.Editor;
    }

    /// <summary>设置第二编辑器组激活标签页</summary>
    private void SetActiveEditorTab2(EditorTabVm? tab) {
        foreach (var t in EditorTabs2)
            t.IsActive = false;
        if (tab is not null)
            tab.IsActive = true;
        ActiveEditorTab2 = tab;
    }

    /// <summary>关闭第二编辑器组标签页</summary>
    [RelayCommand]
    private void CloseEditorTab2(EditorTabVm? tab) {
        if (tab is null)
            return;
        var index = EditorTabs2.IndexOf(tab);
        EditorTabs2.Remove(tab);
        if (ActiveEditorTab2 == tab) {
            if (EditorTabs2.Count == 0) {
                SetActiveEditorTab2(null);
            } else {
                var nextIndex = Math.Min(index, EditorTabs2.Count - 1);
                SetActiveEditorTab2(EditorTabs2[nextIndex]);
            }
        }
    }

    /// <summary>激活第二编辑器组标签页</summary>
    [RelayCommand]
    private void ActivateEditorTab2(EditorTabVm? tab) {
        if (tab is not null)
            SetActiveEditorTab2(tab);
    }

    /// <summary>移动标签页到另一组 — 从组1移到组2或反之</summary>
    [RelayCommand]
    private void MoveTabToOtherGroup(EditorTabVm? tab) {
        if (tab is null)
            return;
        if (EditorTabs.Contains(tab)) {
            EditorTabs.Remove(tab);
            EditorTabs2.Add(tab);
            if (ActiveEditorTab == tab) {
                SetActiveEditorTab(EditorTabs.Count > 0 ? EditorTabs[0] : null);
            }
            SetActiveEditorTab2(tab);
        } else if (EditorTabs2.Contains(tab)) {
            EditorTabs2.Remove(tab);
            EditorTabs.Add(tab);
            if (ActiveEditorTab2 == tab) {
                SetActiveEditorTab2(EditorTabs2.Count > 0 ? EditorTabs2[0] : null);
            }
            SetActiveEditorTab(tab);
        }
    }
}
