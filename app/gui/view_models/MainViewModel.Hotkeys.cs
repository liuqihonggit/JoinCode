namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 快捷键管理 partial — 快捷键面板项、录制切换、重置、持久化。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>快捷键面板项列表（需求3）— 从 GuiPreferences 加载，录制后写回持久化</summary>
    public ObservableCollection<HotkeyItemVm> HotkeyItems { get; } = [];

    /// <summary>切换快捷键录制状态：同一时间只允许一个项录制中</summary>
    [RelayCommand]
    private void ToggleHotkeyRecording(HotkeyItemVm? item)
    {
        if (item is null)
            return;
        foreach (var h in HotkeyItems)
            h.IsRecording = h == item && !h.IsRecording;
    }

    /// <summary>恢复单个快捷键为默认值</summary>
    [RelayCommand]
    private void ResetHotkey(HotkeyItemVm? item)
    {
        if (item is null)
            return;
        item.Gesture = HotkeyDefaults.Get(item.ActionKey);
        SaveHotkeysToPreferences();
    }

    /// <summary>录制完成后由 View 层调用：设置键位并持久化</summary>
    public void ApplyRecordedHotkey(HotkeyItemVm item, string gesture)
    {
        item.Gesture = gesture;
        item.IsRecording = false;
        SaveHotkeysToPreferences();
    }

    /// <summary>从 HotkeyItems 获取指定动作的当前键位</summary>
    private string GetHotkeyGesture(string actionKey)
    {
        foreach (var h in HotkeyItems)
            if (h.ActionKey == actionKey)
                return h.Gesture;
        return HotkeyDefaults.Get(actionKey);
    }

    /// <summary>从 HotkeyItems 写回 GuiPreferences 并持久化</summary>
    private void SaveHotkeysToPreferences()
    {
        if (!_gate.PreferencesLoaded)
            return;
        try
        {
            var existing = _preferencesStore.Load();
            foreach (var h in HotkeyItems)
            {
                switch (h.ActionKey)
                {
                    case "Send": existing.HotkeySend = h.Gesture; break;
                    case "Newline": existing.HotkeyNewline = h.Gesture; break;
                    case "Stop": existing.HotkeyStop = h.Gesture; break;
                    case "NewSession": existing.HotkeyNewSession = h.Gesture; break;
                    case "ClearHistory": existing.HotkeyClearHistory = h.Gesture; break;
                    case "ToggleSettings": existing.HotkeyToggleSettings = h.Gesture; break;
                }
            }
            _preferencesStore.Save(existing);
        }
        catch (Exception ex)
        {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }
}
