namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 错误 toast partial — 错误弹出提示文案、复制、关闭。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>错误 toast 文案（非空时显示错误弹出提示）</summary>
    [ObservableProperty]
    private string? _errorToastText;

    /// <summary>是否显示错误 toast</summary>
    public bool HasErrorToast => ErrorToastText is not null;

    partial void OnErrorToastTextChanged(string? value)
        => OnPropertyChanged(nameof(HasErrorToast));

    /// <summary>复制错误 toast 时待写入剪贴板的文本（View 层消费后清空）</summary>
    [ObservableProperty]
    private string? _errorToastCopy;

    /// <summary>复制错误内容到剪贴板并关闭 toast</summary>
    [RelayCommand]
    private void CopyErrorToast()
    {
        if (string.IsNullOrEmpty(ErrorToastText))
            return;
        ErrorToastCopy = ErrorToastText;
        CopiedMessage = ErrorToastText.GetHashCode();
        ErrorToastText = null;
    }

    /// <summary>手动关闭错误 toast</summary>
    [RelayCommand]
    private void DismissErrorToast() => ErrorToastText = null;

    /// <summary>View 层消费完剪贴板文本后调用，清空待复制状态</summary>
    public void ClearErrorToastCopy() => ErrorToastCopy = null;
}
