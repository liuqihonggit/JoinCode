namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 磁盘保护项 — 单个盘号的保护开关（ADR 0123）。
/// <para>
/// 扫描盘号列表的每一项，DisplayText 显示盘号+卷标，IsProtected 驱动 checkbox 勾选状态。
/// 默认 IsProtected=true（保护开启），用户可取消勾选放行该盘。
/// </para>
/// </summary>
public sealed class DriveProtectionItem : ViewModelBase {
    /// <summary>盘号字母（如 "C:"）— 传递到引擎的标识</summary>
    public string DriveLetter { get; }

    /// <summary>显示文本（如 "C: 本地磁盘" 或 "D:"）— 供 GUI checkbox Content 绑定</summary>
    public string DisplayText { get; }

    private bool _isProtected = true;
    /// <summary>是否受保护（checkbox 勾选态）— 默认 true，变更时通知 MainViewModel 重新应用</summary>
    public bool IsProtected {
        get => _isProtected;
        set {
            if (_isProtected == value)
                return;
            _isProtected = value;
            OnPropertyChanged(nameof(IsProtected));
            ProtectionChanged?.Invoke(this);
        }
    }

    /// <summary>保护开关变更事件 — 通知父 ViewModel 重新应用到引擎</summary>
    public event Action<DriveProtectionItem>? ProtectionChanged;

    /// <summary>初始化 DriveProtectionItem 实例</summary>
    public DriveProtectionItem(string driveLetter, string displayText) {
        DriveLetter = driveLetter;
        DisplayText = displayText;
    }
}
