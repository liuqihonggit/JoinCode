namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 偏好持久化数据 — 存储用户在设置面板调整的 UI 偏好与采样参数，
/// 写入 ~/.jcc/gui-preferences.json，使 GUI 重启后保留上次显示的内容。
/// 与引擎 settings.json 解耦：settings.json 管引擎配置（与 CLI 共享），
/// gui-preferences.json 管 GUI 专属偏好（CLI 不消费，避免破坏 CLI "温度不持久化" 语义）。
/// </summary>
public sealed class GuiPreferences
{
    /// <summary>采样温度（设置面板滑块，默认 0.7）</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>最大输出 token（设置面板滑块，默认 4096）</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>系统提示词（设置面板输入框）</summary>
    public string SystemPrompt { get; set; } = "你是 JoinCode 助手，请用简洁清晰的中文回答。";

    /// <summary>消息区字号（设置面板滑块，默认 14）</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>流式输出开关（设置面板，默认 true）</summary>
    public bool StreamingEnabled { get; set; } = true;
}
