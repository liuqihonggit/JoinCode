namespace JoinCode.Cli;

/// <summary>
/// RGB 颜色值 — 简化版，用于 CLI 模式下的 ANSI 24位颜色输出
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>
    /// 转换为 ANSI 前景色转义序列
    /// </summary>
    public string ToAnsiFg() => $"\x1b[38;2;{R};{G};{B}m";

    /// <summary>
    /// 转换为 ANSI 背景色转义序列
    /// </summary>
    public string ToAnsiBg() => $"\x1b[48;2;{R};{G};{B}m";
}
