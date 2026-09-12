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

    /// <summary>Enter 直接发送（F3 快捷键面板；默认 false → Ctrl+Enter 发送、Enter 换行）</summary>
    public bool EnterSends { get; set; } = false;

    /// <summary>双击 ESC 终止当前对话手势开关（默认开启）</summary>
    public bool DoubleEscStop { get; set; } = true;

    /// <summary>快捷键：发送消息（默认 Ctrl+Enter）— 需求3 快捷键面板</summary>
    public string HotkeySend { get; set; } = "Ctrl+Enter";

    /// <summary>快捷键：换行（默认 Enter）</summary>
    public string HotkeyNewline { get; set; } = "Enter";

    /// <summary>快捷键：终止对话（默认 Double+Escape）</summary>
    public string HotkeyStop { get; set; } = "Double+Escape";

    /// <summary>快捷键：新建会话（默认 Ctrl+N）</summary>
    public string HotkeyNewSession { get; set; } = "Ctrl+N";

    /// <summary>快捷键：清空对话（默认 Ctrl+L）</summary>
    public string HotkeyClearHistory { get; set; } = "Ctrl+L";

    /// <summary>快捷键：打开/收起设置（默认 Ctrl+Comma）</summary>
    public string HotkeyToggleSettings { get; set; } = "Ctrl+OemComma";

    /// <summary>网络模式 — Local(直连)/Proxy(走代理)/Auto(自动检测)，需求7</summary>
    public string NetworkMode { get; set; } = "Auto";

    /// <summary>代理地址（NetworkMode=Proxy 时使用，如 http://127.0.0.1:7890）</summary>
    public string? ProxyUrl { get; set; }
}
