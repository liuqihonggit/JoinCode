namespace NativeTui.Core.Terminal;

/// <summary>
/// Windows 控制台模式位运算 — 纯函数，供 <see cref="RawConsoleMode"/> 消费（可单测部分）。
/// 常量值对齐 Win32 GetConsoleMode/SetConsoleMode 的模式位。
/// </summary>
public static class ConsoleModeMath
{
    /// <summary>CTRL 信号处理输入（raw 需清除，ESC 等键直达程序）。</summary>
    public const uint EnableProcessedInput = 0x1;

    /// <summary>行输入（raw 需清除，键即时可达）。</summary>
    public const uint EnableLineInput = 0x2;

    /// <summary>回显（raw 需清除，由程序自绘）。</summary>
    public const uint EnableEchoInput = 0x4;

    /// <summary>QuickEdit 鼠标选择（raw 需禁用，防点击暂停输出）。</summary>
    public const uint EnableQuickEditMode = 0x40;

    /// <summary>扩展标志位（QuickEdit 修改生效的前提）。</summary>
    public const uint EnableExtendedFlags = 0x80;

    /// <summary>虚拟终端处理输出（ANSI 序列直通）。</summary>
    public const uint EnableVirtualTerminalProcessing = 0x4;

    /// <summary>
    /// 计算 raw 输入模式：清除行输入/回显/处理输入与 QuickEdit，保留窗口/鼠标事件等其余位，
    /// 强制置位 EXTENDED_FLAGS（QuickEdit 禁用生效的前提）。
    /// </summary>
    public static uint ToRawInput(uint current) =>
        (current & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput | EnableQuickEditMode)) | EnableExtendedFlags;

    /// <summary>计算 VT 输出模式：置位虚拟终端处理，保留其余位。</summary>
    public static uint ToVtOutput(uint current) => current | EnableVirtualTerminalProcessing;
}
