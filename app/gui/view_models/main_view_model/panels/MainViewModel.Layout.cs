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
