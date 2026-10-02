namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 编辑器标签页 ViewModel — 一个打开的文件对应一个标签页。
/// </summary>
public sealed partial class EditorTabVm : ViewModelBase {
    private bool _isActive;

    /// <summary>文件完整路径</summary>
    public required string FilePath { get; init; }

    /// <summary>文件名(标签显示)</summary>
    public string Name => System.IO.Path.GetFileName(FilePath);

    /// <summary>文件内容 — 编辑器读写,变更时触发 IsModified</summary>
    [ObservableProperty]
    private string _content = "";

    /// <summary>是否已修改(未保存)</summary>
    [ObservableProperty]
    private bool _isModified;

    /// <summary>是否锁定(锁定标签不可关闭,显示在左侧)</summary>
    [ObservableProperty]
    private bool _isPinned;

    /// <summary>是否为预览标签(单击打开=预览斜体,双击=固定)</summary>
    [ObservableProperty]
    private bool _isPreview;

    /// <summary>是否为当前激活标签</summary>
    public bool IsActive {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
