namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 命令面板条目 — 命令名称+图标+快捷键+执行回调。
/// </summary>
public sealed class CommandEntryVm {
    /// <summary>命令显示名称</summary>
    public required string Name { get; init; }

    /// <summary>命令图标(emoji)</summary>
    public string Icon { get; init; } = "▸";

    /// <summary>快捷键文本(如 "Ctrl+Shift+P")</summary>
    public string? Shortcut { get; init; }

    /// <summary>命令标识(用于执行分发)</summary>
    public required string CommandId { get; init; }
}
