namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 命令面板 partial — Ctrl+Shift+P 快速访问所有命令,Fuzzy 搜索。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>命令面板是否打开</summary>
    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    /// <summary>文件搜索模式(Ctrl+P) vs 命令搜索模式(Ctrl+Shift+P)</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCommands))]
    private bool _isFileSearchMode;

    /// <summary>命令面板搜索查询</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCommands))]
    private string _commandPaletteQuery = "";

    /// <summary>已注册命令列表</summary>
    public IReadOnlyList<CommandEntryVm> RegisteredCommands { get; } = [
        new() { Name = "文件: 新建会话", Icon = "📝", CommandId = "file.newSession", Shortcut = "Ctrl+N" },
        new() { Name = "文件: 打开文件变更", Icon = "📂", CommandId = "file.openFiles" },
        new() { Name = "文件: 模型管理", Icon = "🔧", CommandId = "file.openManagement" },
        new() { Name = "编辑: 重新生成", Icon = "↻", CommandId = "edit.regenerate" },
        new() { Name = "编辑: 清空当前会话", Icon = "🗑", CommandId = "edit.clearHistory" },
        new() { Name = "编辑: 全部重置", Icon = "⚡", CommandId = "edit.clearAll" },
        new() { Name = "视图: 切换主题", Icon = "🎨", CommandId = "view.toggleTheme" },
        new() { Name = "视图: 设置面板", Icon = "⚙", CommandId = "view.settings" },
        new() { Name = "视图: 底部面板", Icon = "▾", CommandId = "view.panel" },
        new() { Name = "视图: 面板位置→底部", Icon = "⤓", CommandId = "view.panelBottom" },
        new() { Name = "视图: 面板位置→右侧", Icon = "⇥", CommandId = "view.panelRight" },
        new() { Name = "视图: 面板位置→左侧", Icon = "⇤", CommandId = "view.panelLeft" },
        new() { Name = "视图: 面板位置→顶部", Icon = "⤒", CommandId = "view.panelTop" },
        new() { Name = "视图: 代码编辑器", Icon = "📝", CommandId = "view.editor" },
        new() { Name = "视图: 对话区", Icon = "💬", CommandId = "view.messages" },
        new() { Name = "视图: Mock 引擎", Icon = "Ⓘ", CommandId = "view.mock" },
        new() { Name = "会话: 切换会话栏", Icon = "💬", CommandId = "sidebar.sessions" },
        new() { Name = "会话: 切换目录树", Icon = "📁", CommandId = "sidebar.filetree" },
        new() { Name = "视图: Secondary Side Bar", Icon = "⇥", CommandId = "view.secondarySidebar" },
        new() { Name = "视图: Zen Mode", Icon = "🔮", CommandId = "view.zenMode", Shortcut = "Ctrl+Shift+Z" },
        new() { Name = "视图: 居中布局", Icon = "⬌", CommandId = "view.centeredLayout" }
    ];

    /// <summary>过滤后的命令列表 — 文件模式搜索文件,命令模式搜索命令</summary>
    public IReadOnlyList<CommandEntryVm> FilteredCommands =>
        IsFileSearchMode ? SearchFiles() : SearchCommands();

    /// <summary>搜索命令 — Fuzzy Contains 匹配</summary>
    private IReadOnlyList<CommandEntryVm> SearchCommands() =>
        string.IsNullOrWhiteSpace(CommandPaletteQuery)
            ? RegisteredCommands
            : RegisteredCommands
                .Where(c => c.Name.Contains(CommandPaletteQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

    /// <summary>搜索文件 — 从当前工作目录递归搜索,限 50 条</summary>
    private IReadOnlyList<CommandEntryVm> SearchFiles() {
        if (string.IsNullOrEmpty(FileTreeRootPath))
            return [];
        try {
            var query = CommandPaletteQuery;
            var files = System.IO.Directory.EnumerateFiles(FileTreeRootPath, "*", System.IO.SearchOption.AllDirectories)
                .Where(p => !IsHiddenOrIgnored(p))
                .Where(p => string.IsNullOrWhiteSpace(query)
                            || System.IO.Path.GetFileName(p).Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .Select(p => new CommandEntryVm {
                    Name = System.IO.Path.GetFileName(p),
                    Icon = "📄",
                    CommandId = $"file.open:{p}"
                })
                .ToList();
            return files;
        } catch {
            return [];
        }
    }

    /// <summary>打开命令面板(Ctrl+Shift+P) — 命令搜索模式</summary>
    [RelayCommand]
    private void OpenCommandPalette() {
        CommandPaletteQuery = "";
        IsFileSearchMode = false;
        IsCommandPaletteOpen = true;
    }

    /// <summary>打开快速打开(Ctrl+P) — 文件搜索模式</summary>
    [RelayCommand]
    private void OpenQuickOpen() {
        CommandPaletteQuery = "";
        IsFileSearchMode = true;
        IsCommandPaletteOpen = true;
    }

    /// <summary>打开文件回调 — MainWindow 设置,ViewModel 调用以在内嵌编辑器中打开文件</summary>
    public Action<string>? OpenFileCallback { get; set; }

    /// <summary>执行命令 — 根据命令标识分发到对应命令</summary>
    [RelayCommand]
    private void ExecuteCommand(CommandEntryVm? entry) {
        if (entry is null)
            return;
        IsCommandPaletteOpen = false;
        if (entry.CommandId.StartsWith("file.open:")) {
            var path = entry.CommandId["file.open:".Length..];
            OpenFileCallback?.Invoke(path);
            return;
        }
        switch (entry.CommandId) {
            case "file.newSession":
                NewConversationCommand.Execute(null);
                break;
            case "edit.regenerate":
                if (CanRegenerate)
                    RegenerateLastReplyCommand.Execute(null);
                break;
            case "edit.clearHistory":
                ClearHistoryCommand.Execute(null);
                break;
            case "edit.clearAll":
                ClearAllSessionsCommand.Execute(null);
                break;
            case "view.toggleTheme":
                ToggleThemeCommand.Execute(null);
                break;
            case "view.settings":
                ToggleSettingsPanelCommand.Execute(null);
                break;
            case "view.panel":
                TogglePanelCommand.Execute(null);
                break;
            case "view.panelBottom":
                SetPanelPositionCommand.Execute(PanelPosition.Bottom);
                break;
            case "view.panelRight":
                SetPanelPositionCommand.Execute(PanelPosition.Right);
                break;
            case "view.panelLeft":
                SetPanelPositionCommand.Execute(PanelPosition.Left);
                break;
            case "view.panelTop":
                SetPanelPositionCommand.Execute(PanelPosition.Top);
                break;
            case "view.editor":
                ShowEditorViewCommand.Execute(null);
                break;
            case "view.messages":
                ShowMessagesViewCommand.Execute(null);
                break;
            case "view.mock":
                ToggleMockCommand.Execute(null);
                break;
            case "sidebar.sessions":
                ToggleSidePanelCommand.Execute(SidePanelKind.Sessions);
                break;
            case "sidebar.filetree":
                ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
                break;
            case "view.secondarySidebar":
                ToggleSecondarySideBarCommand.Execute(null);
                break;
            case "view.zenMode":
                ToggleZenModeCommand.Execute(null);
                break;
            case "view.centeredLayout":
                ToggleCenteredLayoutCommand.Execute(null);
                break;
        }
    }

    /// <summary>查询变更时刷新过滤列表</summary>
    partial void OnCommandPaletteQueryChanged(string value) {
        OnPropertyChanged(nameof(FilteredCommands));
    }
}
