namespace JoinCode.Gui.ViewModels;

/// <summary>
/// Side Bar 位置 — 左侧(默认)或右侧。
/// </summary>
public enum SideBarPosition {
    /// <summary>左侧(默认,VSCode 风格)</summary>
    Left,
    /// <summary>右侧</summary>
    Right
}

/// <summary>
/// MainViewModel 布局管理 partial — Zen Mode 全屏专注 + 居中布局 + Side Bar 位置切换。
/// Zen Mode: 隐藏所有 UI(菜单栏/侧边栏/面板/状态栏),只留编辑器,Esc 退出。
/// 居中布局: 编辑器居中显示,限制最大宽度。
/// Side Bar 位置: 左侧(默认)或右侧,切换时 Activity Bar 跟随移动。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>Zen Mode 是否激活 — 隐藏所有 UI 只留编辑器</summary>
    [ObservableProperty]
    private bool _isZenMode;

    /// <summary>居中布局是否激活 — 编辑器居中显示</summary>
    [ObservableProperty]
    private bool _isCenteredLayout;

    /// <summary>Minimap 是否可见 — 代码缩略图</summary>
    [ObservableProperty]
    private bool _isMinimapVisible = true;

    /// <summary>Primary Side Bar 位置 — 左侧(默认)或右侧</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimarySideBarLeft))]
    [NotifyPropertyChangedFor(nameof(IsPrimarySideBarRight))]
    private SideBarPosition _primarySideBarPosition = SideBarPosition.Left;

    /// <summary>紧凑布局(窄屏) — 宽屏左右分栏,窄屏垂直上下分栏(openCode 风格)</summary>
    [ObservableProperty]
    private bool _isCompactLayout;

    /// <summary>输入栏文本区高度(可拖拽调节) — 默认三行约 72px,最小 38px,最大 240px</summary>
    [ObservableProperty]
    private double _inputAreaHeight = 72;

    /// <summary>系统日志是否展开(▲ 向上展开显示完整日志,默认收起避免遮挡消息区)</summary>
    [ObservableProperty]
    private bool _isStatusLogExpanded = false;

    /// <summary>系统日志是否全屏显示(占满主窗口高度)</summary>
    [ObservableProperty]
    private bool _isStatusLogFullscreen;

    /// <summary>系统日志字体大小(可放大缩小,默认10,范围8~20)</summary>
    [ObservableProperty]
    private double _statusLogFontSize = 10;

    /// <summary>系统日志 ScrollViewer 最大高度(全屏600,非全屏72)</summary>
    public double StatusLogScrollHeight => IsStatusLogFullscreen ? 600 : 72;

    /// <summary>放大日志字体</summary>
    [RelayCommand]
    private void EnlargeLogFont() {
        if (StatusLogFontSize < 20)
            StatusLogFontSize += 1;
    }

    /// <summary>缩小日志字体</summary>
    [RelayCommand]
    private void ShrinkLogFont() {
        if (StatusLogFontSize > 8)
            StatusLogFontSize -= 1;
    }

    /// <summary>切换日志全屏/非全屏</summary>
    [RelayCommand]
    private void ToggleLogFullscreen() {
        IsStatusLogFullscreen = !IsStatusLogFullscreen;
        OnPropertyChanged(nameof(StatusLogScrollHeight));
    }

    partial void OnIsStatusLogFullscreenChanged(bool value)
        => OnPropertyChanged(nameof(StatusLogScrollHeight));

    /// <summary>系统日志条目(最近的状态变化/错误/切换记录,最多保留 50 条)</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> StatusLogEntries { get; } = new();

    /// <summary>追加系统日志条目(倒序:旧的在上新的在下,最多保留 50 条)</summary>
    public void AddStatusLog(string entry) {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {entry}";
        StatusLogEntries.Add(stamped);
        while (StatusLogEntries.Count > 50)
            StatusLogEntries.RemoveAt(0);
    }

    /// <summary>切换系统日志展开/收起</summary>
    [RelayCommand]
    private void ToggleStatusLog() => IsStatusLogExpanded = !IsStatusLogExpanded;

    /// <summary>紧凑布局阈值 — 窗口宽度低于此值切换到垂直布局</summary>
    public const double CompactLayoutThreshold = 700;

    /// <summary>居中布局最大宽度 — 编辑器内容最大宽度限制</summary>
    private const double CenteredLayoutMaxWidth = 1200;

    /// <summary>居中布局最大宽度值</summary>
    public double CenteredMaxWidth => IsCenteredLayout ? CenteredLayoutMaxWidth : double.PositiveInfinity;

    /// <summary>切换 Zen Mode — 进入时切到编辑器区并隐藏面板/侧边栏,退出时恢复</summary>
    [RelayCommand]
    private void ToggleZenMode() {
        if (!IsZenMode) {
            IsZenMode = true;
            ActiveMainArea = MainAreaKind.Editor;
            IsPanelOpen = false;
            IsSecondarySideBarOpen = false;
        } else {
            IsZenMode = false;
        }
    }

    /// <summary>切换居中布局</summary>
    [RelayCommand]
    private void ToggleCenteredLayout() {
        IsCenteredLayout = !IsCenteredLayout;
    }

    /// <summary>切换 Minimap 可见性</summary>
    [RelayCommand]
    private void ToggleMinimap() {
        IsMinimapVisible = !IsMinimapVisible;
    }

    /// <summary>切换 Side Bar 位置 — 左侧↔右侧</summary>
    [RelayCommand]
    private void ToggleSideBarPosition() {
        PrimarySideBarPosition = PrimarySideBarPosition == SideBarPosition.Left
            ? SideBarPosition.Right
            : SideBarPosition.Left;
    }

    /// <summary>Primary Side Bar 在左侧</summary>
    public bool IsPrimarySideBarLeft => PrimarySideBarPosition == SideBarPosition.Left;

    /// <summary>Primary Side Bar 在右侧</summary>
    public bool IsPrimarySideBarRight => PrimarySideBarPosition == SideBarPosition.Right;

    /// <summary>Zen Mode 属性变化时联动 — 进入时切到编辑器,退出时恢复消息区</summary>
    partial void OnIsZenModeChanged(bool value) {
        if (value) {
            ActiveMainArea = MainAreaKind.Editor;
        }
        OnPropertyChanged(nameof(IsNotZenMode));
    }

    /// <summary>非 Zen Mode — 用于 XAML 绑定 UI 可见性</summary>
    public bool IsNotZenMode => !IsZenMode;

    /// <summary>居中布局变化时刷新 CenteredMaxWidth</summary>
    partial void OnIsCenteredLayoutChanged(bool value) {
        OnPropertyChanged(nameof(CenteredMaxWidth));
    }
}
