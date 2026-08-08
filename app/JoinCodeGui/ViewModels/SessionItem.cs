using CommunityToolkit.Mvvm.ComponentModel;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 侧边栏会话条目 — 占位阶段仅展示结构，P1 接入引擎后映射真实会话。
/// </summary>
public sealed partial class SessionItem : ObservableObject
{
    /// <summary>会话唯一 ID（持久化到 ~/.jcc/sessions/{Id}.json，用于恢复与删除）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>是否处于重命名编辑状态（双击会话条目触发）</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>重命名编辑中的草稿标题</summary>
    [ObservableProperty]
    private string _renameDraft = string.Empty;
}