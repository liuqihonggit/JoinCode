namespace JoinCode.Gui.ViewModels;

/// <summary>快捷键项 VM — 快捷键面板中每行一个可配置快捷键（需求3）</summary>
public sealed class HotkeyItemVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>动作显示名（如"发送消息"）</summary>
    public string ActionLabel { get; }

    /// <summary>动作键（对应 GuiPreferences.HotkeyXxx 字段名）</summary>
    public string ActionKey { get; }

    private string _gesture;
    /// <summary>当前键位（如"Ctrl+Enter"）</summary>
    public string Gesture
    {
        get => _gesture;
        set { if (_gesture != value) { _gesture = value; Raise(nameof(Gesture)); } }
    }

    private bool _isRecording;
    /// <summary>是否正在录制键位</summary>
    public bool IsRecording
    {
        get => _isRecording;
        set { if (_isRecording != value) { _isRecording = value; Raise(nameof(IsRecording)); } }
    }

    public HotkeyItemVm(string actionLabel, string actionKey, string gesture)
    {
        ActionLabel = actionLabel;
        ActionKey = actionKey;
        _gesture = gesture;
    }
}
