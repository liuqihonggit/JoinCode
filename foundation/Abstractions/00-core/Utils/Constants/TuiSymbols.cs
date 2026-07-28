namespace JoinCode.Abstractions.Utils;

/// <summary>
/// TUI 符号常量 — 统一管理所有 TUI 渲染用的 Unicode 符号
/// </summary>
public static class TuiSymbols
{
    /// <summary>工具调用指示器 — Windows/Linux 用 ●,macOS 用 ⏺ (U+23FA)</summary>
    public const string ToolIndicator = "\u25CF";

    /// <summary>等待权限指示器 — ⏸ (U+23F8)</summary>
    public const string PauseIndicator = "\u23F8";

    /// <summary>错误消息前缀 — ✗ (U+2717)</summary>
    public const string ErrorPrefix = "\u2717";

    /// <summary>成功消息前缀 — ✓ (U+2713)</summary>
    public const string SuccessPrefix = "\u2713";

    /// <summary>警告消息前缀 — ⚠ (U+26A0)</summary>
    public const string WarningPrefix = "\u26A0";

    /// <summary>Spinner 动画帧 — Windows/Linux 版本 (✳ 替换为 *)</summary>
    public static readonly string[] SpinnerFrames = ["\u00B7", "\u2722", "*", "\u2736", "\u273B", "\u273D"];

    /// <summary>Bridge 连接 Spinner 帧 (·|· ·/· ·—· ·\·)</summary>
    public static readonly string[] BridgeSpinnerFrames = ["\u00B7|\u00B7", "\u00B7/\u00B7", "\u00B7\u2014\u00B7", "\u00B7\\\u00B7"];

    /// <summary>输入提示符 — ❯ (U+276F)</summary>
    public const string PromptPointer = "\u276F";
}
