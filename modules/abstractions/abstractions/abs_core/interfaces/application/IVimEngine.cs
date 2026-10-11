namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Vim 模式枚举
/// </summary>
public enum VimMode {
    [EnumValue("normal")] Normal,
    [EnumValue("insert")] Insert,
    [EnumValue("visual")] Visual,
    [EnumValue("visual_line")] VisualLine,
    [EnumValue("visual_block")] VisualBlock,
    [EnumValue("command")] Command
}

/// <summary>
/// Vim 寄存器名称
/// </summary>
public enum VimRegisterName {
    [EnumValue("unnamed")] Unnamed,
    [EnumValue("clipboard")] Clipboard,
    [EnumValue("a")] A, B, C, D, E, F, G, H, I, J, K, L, M,
    [EnumValue("n")] N, O, P, Q, R, S, T, U, V, W, X, Y, Z
}

/// <summary>
/// Vim 寄存器内容
/// </summary>
public sealed class VimRegisterContent {
    /// <summary>获取寄存器文本内容。</summary>
    public required string Text { get; init; }
    /// <summary>获取是否为行整寄存。</summary>
    public bool IsLinewise { get; init; }
}

/// <summary>
/// Vim 标记
/// </summary>
public sealed class VimMark {
    /// <summary>获取标记名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取缓冲区位置。</summary>
    public required int Position { get; init; }
    /// <summary>获取行号。</summary>
    public required int Line { get; init; }
    /// <summary>获取列号。</summary>
    public required int Column { get; init; }
}

/// <summary>
/// Vim 按键处理结果
/// </summary>
public sealed class VimKeyResult {
    public static readonly VimKeyResult Handled = new() { IsHandled = true };
    public static readonly VimKeyResult NotHandled = new() { IsHandled = false };
    /// <summary>创建提交文本的按键结果。</summary>
    /// <param name="text">要提交的文本。</param>
    public static VimKeyResult Submit(string text) => new() { IsHandled = true, SubmitText = text };
    /// <summary>获取已取消的按键结果。</summary>
    public static VimKeyResult Cancelled => new() { IsHandled = true, IsCancelled = true };

    /// <summary>获取按键是否已处理。</summary>
    public bool IsHandled { get; init; }
    /// <summary>获取要提交的文本。</summary>
    public string? SubmitText { get; init; }
    /// <summary>获取是否已取消。</summary>
    public bool IsCancelled { get; init; }
}

/// <summary>
/// Vim 命令事件参数
/// </summary>
public sealed class VimCommandEventArgs : EventArgs {
    /// <summary>获取命令文本。</summary>
    public required string Command { get; init; }
}

/// <summary>
/// Vim 引擎接口 — CLI 简化版
/// </summary>
public interface IVimEngine {
    /// <summary>获取当前 Vim 模式。</summary>
    VimMode CurrentMode { get; }
    /// <summary>获取是否已启用。</summary>
    bool IsEnabled { get; }
    /// <summary>获取命令缓冲区内容。</summary>
    string CommandBuffer { get; }
    /// <summary>获取重复次数。</summary>
    int RepeatCount { get; }
    /// <summary>获取是否正在录制宏。</summary>
    bool IsRecordingMacro { get; }
    /// <summary>获取宏寄存器名称。</summary>
    char? MacroRegister { get; }
    /// <summary>获取是否存在搜索高亮。</summary>
    bool HasSearchHighlight { get; }
    /// <summary>获取显示宽度。</summary>
    int DisplayWidth { get; }
    /// <summary>获取视口高度。</summary>
    int ViewportHeight { get; }

    /// <summary>启用 Vim 引擎。</summary>
    void Enable();
    /// <summary>禁用 Vim 引擎。</summary>
    void Disable();
    /// <summary>设置显示宽度。</summary>
    /// <param name="width">显示宽度。</param>
    void SetDisplayWidth(int width);
    /// <summary>设置视口高度。</summary>
    /// <param name="height">视口高度。</param>
    void SetViewportHeight(int height);
    /// <summary>切换到指定模式。</summary>
    /// <param name="mode">目标模式。</param>
    VimMode SwitchToMode(VimMode mode);

    /// <summary>处理按键输入。</summary>
    /// <param name="keyInfo">按键信息。</param>
    /// <param name="input">输入缓冲区。</param>
    /// <param name="cursorPosition">光标位置(引用传递)。</param>
    VimKeyResult ProcessKey(ConsoleKeyInfo keyInfo, StringBuilder input, ref int cursorPosition);

    /// <summary>设置寄存器内容。</summary>
    /// <param name="name">寄存器名称。</param>
    /// <param name="text">文本内容。</param>
    /// <param name="isLinewise">是否为行整寄存。</param>
    void SetRegister(VimRegisterName name, string text, bool isLinewise = false);
    /// <summary>获取寄存器内容。</summary>
    /// <param name="name">寄存器名称。</param>
    VimRegisterContent? GetRegister(VimRegisterName name);

    /// <summary>设置标记。</summary>
    /// <param name="name">标记名称。</param>
    /// <param name="position">缓冲区位置。</param>
    /// <param name="line">行号。</param>
    /// <param name="column">列号。</param>
    void SetMark(string name, int position, int line, int column);
    /// <summary>获取标记。</summary>
    /// <param name="name">标记名称。</param>
    VimMark? GetMark(string name);

    /// <summary>开始录制宏。</summary>
    /// <param name="register">寄存器字符。</param>
    void StartMacroRecording(char register);
    /// <summary>停止录制宏。</summary>
    void StopMacroRecording();
    /// <summary>重放宏。</summary>
    /// <param name="register">寄存器字符。</param>
    /// <param name="input">输入缓冲区。</param>
    /// <param name="cursorPosition">光标位置(引用传递)。</param>
    bool ReplayMacro(char register, StringBuilder input, ref int cursorPosition);

    event EventHandler<VimMode>? ModeChanged;
    event EventHandler<VimCommandEventArgs>? CommandExecuted;
}