using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 侧边栏会话条目 — 占位阶段仅展示结构，P1 接入引擎后映射真实会话。
/// 需求11：支持树形展示（主会话展开后显示子会话），ParentId=null 为顶层主会话。
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

    /// <summary>父会话 ID — null 为顶层主会话，非 null 为子会话（需求11 树形展示）</summary>
    public string? ParentId { get; set; }

    /// <summary>子会话列表 — 主会话展开后显示（需求11）；空列表表示无子会话</summary>
    public ObservableCollection<SessionItem> Children { get; } = [];

    /// <summary>是否为子会话（ParentId 非空）</summary>
    public bool IsSubSession => ParentId is not null;

    /// <summary>是否有子会话（驱动展开箭头可见性）</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>树形节点是否展开（需求11）</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>是否有 git worktree（子会话右键"打开文件夹"可见性，需求11）</summary>
    [ObservableProperty]
    private bool _hasWorktree;

    /// <summary>worktree 路径（HasWorktree=true 时填充，供资源管理器打开）</summary>
    [ObservableProperty]
    private string? _worktreePath;

    /// <summary>子会话生命周期状态 — Running/Completed/Merged/Cancelled/Failed（需求11，完成后灰色保留）</summary>
    [ObservableProperty]
    private string _subSessionState = "Running";

    /// <summary>子会话是否已结束（Completed/Merged/Cancelled/Failed）— 驱动灰色样式，不移除保留统计</summary>
    public bool IsSubSessionFinished => IsSubSession
        && SubSessionState is "Completed" or "Merged" or "Cancelled" or "Failed";

    /// <summary>子会话是否正在运行 — 驱动正常亮色</summary>
    public bool IsSubSessionRunning => IsSubSession && SubSessionState == "Running";
}