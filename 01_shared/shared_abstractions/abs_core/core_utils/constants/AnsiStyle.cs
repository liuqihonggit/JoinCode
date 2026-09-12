namespace JoinCode.Abstractions.Utils;

/// <summary>
/// ANSI 文本样式控制码枚举
/// </summary>
public enum AnsiStyle
{
    [EnumValue("\x1b[")] Esc,
    [EnumValue("\x1b[0m")] Reset,
    [EnumValue("\x1b[1m")] Bold,
    [EnumValue("\x1b[2m")] Dim,
    [EnumValue("\x1b[3m")] Italic,
    [EnumValue("\x1b[4m")] Underline,
    [EnumValue("\x1b[5m")] Blink,
    [EnumValue("\x1b[7m")] Reverse,
    [EnumValue("\x1b[8m")] Hidden,
    [EnumValue("\x1b[9m")] Strikethrough,
}
