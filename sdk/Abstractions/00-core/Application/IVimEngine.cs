namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Vim 模式枚举
/// </summary>
public enum VimMode
{
    Normal,
    Insert,
    Visual,
    VisualLine,
    VisualBlock,
    Command
}

/// <summary>
/// Vim 寄存器名称
/// </summary>
public enum VimRegisterName
{
    Unnamed,
    Clipboard,
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z
}

/// <summary>
/// Vim 寄存器内容
/// </summary>
public sealed class VimRegisterContent
{
    public required string Text { get; init; }
    public bool IsLinewise { get; init; }
}

/// <summary>
/// Vim 标记
/// </summary>
public sealed class VimMark
{
    public required string Name { get; init; }
    public required int Position { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
}

/// <summary>
/// Vim 按键处理结果
/// </summary>
public sealed class VimKeyResult
{
    public static readonly VimKeyResult Handled = new() { IsHandled = true };
    public static readonly VimKeyResult NotHandled = new() { IsHandled = false };
    public static VimKeyResult Submit(string text) => new() { IsHandled = true, SubmitText = text };
    public static VimKeyResult Cancelled => new() { IsHandled = true, IsCancelled = true };

    public bool IsHandled { get; init; }
    public string? SubmitText { get; init; }
    public bool IsCancelled { get; init; }
}

/// <summary>
/// Vim 命令事件参数
/// </summary>
public sealed class VimCommandEventArgs : EventArgs
{
    public required string Command { get; init; }
}

/// <summary>
/// Vim 引擎接口 — CLI 简化版
/// </summary>
public interface IVimEngine
{
    VimMode CurrentMode { get; }
    bool IsEnabled { get; }
    string CommandBuffer { get; }
    int RepeatCount { get; }
    bool IsRecordingMacro { get; }
    char? MacroRegister { get; }
    bool HasSearchHighlight { get; }
    int DisplayWidth { get; }
    int ViewportHeight { get; }

    void Enable();
    void Disable();
    void SetDisplayWidth(int width);
    void SetViewportHeight(int height);
    VimMode SwitchToMode(VimMode mode);

    VimKeyResult ProcessKey(ConsoleKeyInfo keyInfo, StringBuilder input, ref int cursorPosition);

    void SetRegister(VimRegisterName name, string text, bool isLinewise = false);
    VimRegisterContent? GetRegister(VimRegisterName name);

    void SetMark(string name, int position, int line, int column);
    VimMark? GetMark(string name);

    void StartMacroRecording(char register);
    void StopMacroRecording();
    bool ReplayMacro(char register, StringBuilder input, ref int cursorPosition);

    event EventHandler<VimMode>? ModeChanged;
    event EventHandler<VimCommandEventArgs>? CommandExecuted;
}
