namespace JoinCode.Gui.ViewModels;

/// <summary>
/// Ask 模式灯色等级 — 对标五色灯中的绿/黄/红灯，区分不同危险等级的询问确认。
/// <para>绿灯=可撤回操作(LightValidation)，黄灯=未知命令(Unknown)，红灯=不可撤回操作(Execution)。</para>
/// </summary>
public enum AskLightLevel {
    /// <summary>绿灯 — 可撤回操作需确认（如 file_write）</summary>
    Green = 0,
    /// <summary>黄灯 — 未知命令需确认（如未识别的 shell 命令）</summary>
    Yellow = 1,
    /// <summary>红灯 — 不可撤回操作需确认（如 file_delete、git push）</summary>
    Red = 2,
}
