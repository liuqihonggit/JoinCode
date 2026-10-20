namespace NativeTui.Core.Ansi;

/// <summary>
/// DEC 私有模式种类 — 枚举值即 DEC 模式码（单一数据源，禁止消费方硬编码数字）。
/// 对齐基准源码 AlternateScreen.tsx / App.tsx 的模式生命周期。
/// </summary>
public enum DecPrivateModeKind
{
    /// <summary>光标可见性（25）。</summary>
    CursorVisibility = 25,

    /// <summary>备用屏 DEC 1049（进入含清屏语义，退出恢复主屏）。</summary>
    AlternateScreen = 1049,

    /// <summary>鼠标点击追踪（SGR 编码 1006 配合）。</summary>
    MouseClickTracking = 1000,

    /// <summary>鼠标按下/释放+拖拽追踪。</summary>
    MouseEventTracking = 1002,

    /// <summary>鼠标任意事件追踪（含移动悬停）。</summary>
    MouseAnyEventTracking = 1003,

    /// <summary>SGR 鼠标编码（十进制坐标，\x1b[&lt;btn;col;rowM）。</summary>
    MouseSgrEncoding = 1006,

    /// <summary>括号粘贴模式（\x1b[200~/201~ 包裹粘贴内容）。</summary>
    BracketedPaste = 2004,

    /// <summary>DEC 2026 同步输出（BSU/ESU 原子上屏）。</summary>
    SynchronizedOutput = 2026,
}

/// <summary>
/// DEC 私有模式开关序列构建器。
/// </summary>
public static class DecPrivateMode
{
    /// <summary>启用指定私有模式（CSI ? code h）。</summary>
    public static string Enable(DecPrivateModeKind kind) => $"\x1b[?{(int)kind}h";

    /// <summary>禁用指定私有模式（CSI ? code l）。</summary>
    public static string Disable(DecPrivateModeKind kind) => $"\x1b[?{(int)kind}l";
}
