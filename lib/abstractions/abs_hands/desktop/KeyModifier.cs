namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 键盘修饰键 — 可按位组合（如 Ctrl+Shift = Control | Shift）
/// </summary>
[Flags]
public enum KeyModifier
{
    /// <summary>无修饰键</summary>
    None = 0,

    /// <summary>Shift 键</summary>
    Shift = 1,

    /// <summary>Ctrl 键</summary>
    Control = 2,

    /// <summary>Alt 键</summary>
    Alt = 4,

    /// <summary>Windows 键</summary>
    Win = 8,
}
