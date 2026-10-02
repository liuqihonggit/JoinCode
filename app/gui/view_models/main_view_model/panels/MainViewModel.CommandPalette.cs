namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 命令面板 partial — Ctrl+Shift+P 快速访问所有命令,Fuzzy 搜索。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>命令面板是否打开</summary>
    [ObservableProperty]
    private bool _isCommandPaletteOpen;

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
        new() { Name = "视图: Mock 引擎", Icon = "Ⓘ", CommandId = "view.mock" },
        new() { Name = "会话: 切换会话栏", Icon = "💬", CommandId = "sidebar.sessions" },
        new() { Name = "会话: 切换目录树", Icon = "📁", CommandId = "sidebar.filetree" }
    ];

    /// <summary>过滤后的命令列表 — Fuzzy Contains 匹配</summary>
    public IReadOnlyList<CommandEntryVm> FilteredCommands =>
        string.IsNullOrWhiteSpace(CommandPaletteQuery)
            ? RegisteredCommands
            : RegisteredCommands
                .Where(c => c.Name.Contains(CommandPaletteQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

    /// <summary>打开命令面板</summary>
    [RelayCommand]
    private void OpenCommandPalette() {
        CommandPaletteQuery = "";
        IsCommandPaletteOpen = true;
    }

    /// <summary>执行命令 — 根据命令标识分发到对应命令</summary>
    [RelayCommand]
    private void ExecuteCommand(CommandEntryVm? entry) {
        if (entry is null)
            return;
        IsCommandPaletteOpen = false;
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
            case "view.mock":
                ToggleMockCommand.Execute(null);
                break;
            case "sidebar.sessions":
                ToggleSidePanelCommand.Execute(SidePanelKind.Sessions);
                break;
            case "sidebar.filetree":
                ToggleSidePanelCommand.Execute(SidePanelKind.FileTree);
                break;
        }
    }

    /// <summary>查询变更时刷新过滤列表</summary>
    partial void OnCommandPaletteQueryChanged(string value) {
        OnPropertyChanged(nameof(FilteredCommands));
    }
}
