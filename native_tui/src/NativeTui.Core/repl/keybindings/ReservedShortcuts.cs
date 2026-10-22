namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 保留键清单 — 对齐基准 keybindings/reservedShortcuts.ts NON_REBINDABLE（3 项，error 级）。
/// ctrl+c/ctrl+d 是退出安全键（硬编码双击/空输入退出语义，用户重绑会破坏退出通道）；
/// ctrl+m 与 Enter 在终端同发 CR，重绑即劫持回车。
/// 基准 TERMINAL_RESERVED（ctrl+z SIGTSTP / ctrl+\ SIGQUIT）为 Unix 信号语义，w2 Windows 主战场不适用故不转录；
/// MACOS_RESERVED（cmd+*）依赖 Super 修饰，w2 键模型无该通道同样不适用。
/// 校验语义对齐基准 validate.ts checkReservedShortcuts：单键完整匹配才拒绝（序列 "ctrl+x ctrl+c"
/// 整串不等于保留键，第二段 ctrl+c 完全可用 — 基准 normalizeKeyForComparison 整串比较）。
/// </summary>
public static class ReservedShortcuts
{
    /// <summary>不可重绑键位（键串为 KeyShortcut.ToString 规范形，可直接比较）。</summary>
    public static readonly (string Key, string Reason)[] NonRebindable =
    [
        ("ctrl+c", "中断/退出安全键 — 双击退出语义硬编码（基准 reservedShortcuts.ts:18-22 \"used for interrupt/exit (hardcoded)\"）"),
        ("ctrl+d", "退出安全键 — 空输入退出硬编码（基准 reservedShortcuts.ts:23-27 \"used for exit (hardcoded)\"）"),
        ("ctrl+m", "与 Enter 同码 — 终端对两者都发 CR，重绑即劫持回车（基准 reservedShortcuts.ts:28-32 \"identical to Enter in terminals\"）"),
    ];

    /// <summary>查询快捷键是否保留不可重绑；保留则返回原因文本。</summary>
    /// <param name="shortcut">待查快捷键（规范形比较 — ToString 后与键串全等）。</param>
    /// <returns>保留原因；非保留键返回 null。</returns>
    public static string? ReservedReason(KeyShortcut shortcut)
    {
        var text = shortcut.ToString();
        foreach (var (key, reason) in NonRebindable)
        {
            if (key == text)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>查询键序列是否保留 — 仅单键序列参与（多键序列整串不等于保留键，对齐基准整串比较语义）。</summary>
    /// <param name="chord">待查键序列。</param>
    /// <returns>保留原因；非保留返回 null。</returns>
    public static string? ReservedReason(KeyChord chord) =>
        chord.Steps.Length == 1 ? ReservedReason(chord.Steps[0]) : null;
}
