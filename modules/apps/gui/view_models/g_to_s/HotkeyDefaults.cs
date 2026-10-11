namespace JoinCode.Gui.ViewModels;

/// <summary>快捷键默认值表 — 集中管理各动作的默认键位</summary>
internal static class HotkeyDefaults {
    /// <summary>按动作键返回默认快捷键手势</summary>
    public static string Get(string actionKey) => actionKey switch {
        "Send" => "Ctrl+Enter",
        "Newline" => "Enter",
        "Stop" => "Double+Escape",
        "NewSession" => "Ctrl+N",
        "ClearHistory" => "Ctrl+L",
        "ToggleSettings" => "Ctrl+OemComma",
        _ => string.Empty
    };
}