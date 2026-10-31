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

    /// <summary>QuickEdit 鼠标选择 — 保留原状（对齐基准 Node 行为：conhost 宿主下是鼠标选择复制的唯一机制，禁用即无法选择；选择期间输出暂停由 conhost 管理，取消后恢复）。</summary>
    public const uint EnableQuickEditMode = 0x40;

    /// <summary>扩展标志位（QuickEdit/INSERT 生效的前提）— 保留原状，不改变终端默认选择行为。</summary>
    public const uint EnableExtendedFlags = 0x80;

    /// <summary>虚拟终端处理输出（ANSI 序列直通）。</summary>
    public const uint EnableVirtualTerminalProcessing = 0x4;

    /// <summary>虚拟终端输入处理（VT 序列解析为 KEY_EVENT — Node.js raw mode 启用此项；
    /// TASK077 验证 WT 是否需此项才把 VT 鼠标序列发到 stdin）。</summary>
    public const uint EnableVirtualTerminalInput = 0x0200;

    /// <summary>
    /// 计算 raw 输入模式：清除行输入/回显/处理输入，启用 VT 输入处理，QuickEdit/EXTENDED/窗口/鼠标位保留原样
    /// （QuickEdit 保留 = conhost 鼠标选择复制可用，Windows Terminal 原生选择不受影响）。
    /// </summary>
    public static uint ToRawInput(uint current) =>
        (current & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput)) | EnableVirtualTerminalInput;

    /// <summary>计算 VT 输出模式：置位虚拟终端处理，保留其余位。</summary>
    public static uint ToVtOutput(uint current) => current | EnableVirtualTerminalProcessing;
}
