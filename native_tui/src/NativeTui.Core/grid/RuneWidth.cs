namespace NativeTui.Core.Grid;

/// <summary>
/// Rune 终端显示宽度 — East Asian Width 简化判定表。
/// 对齐基准依赖 get-east-asian-width 的核心行为：
/// CJK/全角/emoji = 2，组合符/ZWJ = 0，其余 = 1；
/// Ambiguous（⏺ ● ✻ 等）按非 CJK 上下文取 1。
/// </summary>
public static class RuneWidth
{
    /// <summary>宽字符（宽度 2）区间的 (含) 范围表 — 已按升序排列
    ///（BMP EAW Wide 区段全补 — 对齐基准 eastAsianWidth 库全表覆盖）。</summary>
    private static readonly (int Lo, int Hi)[] s_wideRanges =
    [
        (0x1100, 0x115F),     // Hangul Jamo
        (0x231A, 0x231B),     // ⌚⌛ 手表/沙漏（EAW Wide — 此前缺失错判 1 列）
        (0x2329, 0x232A),     // 〈〉 尖括号
        (0x23E9, 0x23EC),     // ⏩⏪ 双三角箭头
        (0x23F0, 0x23F0),     // ⏰ 闹钟
        (0x23F3, 0x23F3),     // ⏳ 沙漏流
        (0x25FD, 0x25FE),     // ◽ 中白小方
        (0x2614, 0x2615),     // ☔☂ 雨伞
        (0x2648, 0x2653),     // ♈-♓ 星座
        (0x267F, 0x267F),     // ♿ 轮椅
        (0x2693, 0x2693),     // ⚓ 锚
        (0x26A1, 0x26A1),     // ⚡ 高压电
        (0x26AA, 0x26AB),     // ⚪⚫ 白/黑圆
        (0x26BD, 0x26BE),     // ⚽⚾ 球类
        (0x26C4, 0x26C5),     // ⛄🌤 雪人/太阳雪
        (0x26CE, 0x26CE),     // ⛎ 蛇夫座
        (0x26D4, 0x26D4),     // ⛔ 禁止入内
        (0x26EA, 0x26EA),     // ⛪ 教堂
        (0x26F2, 0x26F3),     // ⛲⛳ 喷泉/旗洞
        (0x26F5, 0x26F5),     // ⛵ 帆船
        (0x26FA, 0x26FA),     // ⛺ 帐篷
        (0x26FD, 0x26FD),     // ⛽ 加油站
        (0x2705, 0x2705),     // ✅ 白勾
        (0x270A, 0x270B),     // ✊✋ 举拳/举手
        (0x2728, 0x2728),     // ✨ 闪光
        (0x274C, 0x274C),     // ❌ 叉
        (0x274E, 0x274E),     // ❎ 白叉
        (0x2753, 0x2755),     // ❓❔❕❗ 问叹
        (0x2757, 0x2757),     // ❗ 感叹
        (0x2795, 0x2797),     // ➕➖➗ 加减除
        (0x27B0, 0x27B0),     // ➰ 弯曲
        (0x27BF, 0x27BF),     // ➿ 双弯曲
        (0x2B1B, 0x2B1C),     // ⬛⬜ 黑/白大方
        (0x2B50, 0x2B50),     // ⭐ 星
        (0x2B55, 0x2B55),     // ⭕ 红圆
        (0x2E80, 0x303E),     // CJK 部首/康熙/符号
        (0x3041, 0x33FF),     // 假名/注音/CJK 补充
        (0x3400, 0x4DBF),     // CJK 扩展 A
        (0x4E00, 0x9FFF),     // CJK 统一表意
        (0xA000, 0xA4CF),     // 彝文
        (0xA960, 0xA97F),     // Hangul Jamo 扩展 A
        (0xAC00, 0xD7A3),     // 谐音
        (0xF900, 0xFAFF),     // CJK 兼容表意
        (0xFE10, 0xFE19),     // 竖排形式
        (0xFE30, 0xFE52),     // CJK 兼容形式
        (0xFE54, 0xFE66),
        (0xFE68, 0xFE6B),
        (0xFF00, 0xFF60),     // 全角形式
        (0xFFE0, 0xFFE6),     // 全角符号
        (0x16FE0, 0x16FE4),   // 表意描述
        (0x17000, 0x187F7),   // 西夏
        (0x18800, 0x18CD5),
        (0x1B000, 0x1B152),   // 假名补充
        (0x1B164, 0x1B167),
        (0x1B170, 0x1B2FB),
        (0x1F004, 0x1F004),   // emoji
        (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A),
        (0x1F200, 0x1F320),   // emoji 补充平面（含 😀 区段）
        (0x1F32D, 0x1F335),
        (0x1F337, 0x1F37C),
        (0x1F37E, 0x1F393),
        (0x1F3A0, 0x1F3CA),
        (0x1F3CF, 0x1F3D3),
        (0x1F3E0, 0x1F3F0),
        (0x1F3F4, 0x1F3F4),
        (0x1F3F8, 0x1F43E),
        (0x1F440, 0x1F440),
        (0x1F442, 0x1F4FC),
        (0x1F4FF, 0x1F53D),
        (0x1F54B, 0x1F54E),
        (0x1F550, 0x1F567),
        (0x1F57A, 0x1F57A),
        (0x1F595, 0x1F596),
        (0x1F5A4, 0x1F5A4),
        (0x1F5FB, 0x1F64F),
        (0x1F680, 0x1F6C5),
        (0x1F6CC, 0x1F6CC),
        (0x1F6D0, 0x1F6D2),
        (0x1F6EB, 0x1F6EC),
        (0x1F6F4, 0x1F6FC),
        (0x1F7E0, 0x1F7EB),
        (0x1F90C, 0x1F93A),
        (0x1F93C, 0x1F945),
        (0x1F947, 0x1F9FF),
        (0x1FA70, 0x1FAFF),
        (0x20000, 0x2FFFD),   // CJK 扩展 B-F
        (0x30000, 0x3FFFD),
    ];

    /// <summary>零宽字符区间（组合符/变体选择器/ZWJ 等）。</summary>
    private static readonly (int Lo, int Hi)[] s_zeroRanges =
    [
        (0x0300, 0x036F),     // 组合附加符号
        (0x0483, 0x0489),
        (0x0591, 0x05BD),
        (0x0610, 0x061A),
        (0x064B, 0x065F),
        (0x0670, 0x0670),
        (0x06D6, 0x06DC),
        (0x0730, 0x074A),
        (0x07A6, 0x07B0),
        (0x0900, 0x0903),
        (0x093A, 0x093C),
        (0x0941, 0x0948),
        (0x1AB0, 0x1AFF),
        (0x1DC0, 0x1DFF),
        (0x200B, 0x200F),     // 零宽空格/ZWNJ/ZWJ/LRM/RLM
        (0x202A, 0x202E),
        (0x2060, 0x2064),
        (0x20D0, 0x20F0),
        (0xFE00, 0xFE0F),     // 变体选择器
        (0xFE20, 0xFE2F),     // 组合半符号
        (0xFEFF, 0xFEFF),     // BOM
    ];

    /// <summary>计算 rune 的终端显示宽度：0（零宽）、1（常规）、2（宽字符）。</summary>
    public static int Of(Rune rune)
    {
        var value = rune.Value;
        if (value < 0x1100)
        {
            return value >= 0x20 || value == 0 ? 1 : 0;
        }

        if (IsInRanges(value, s_zeroRanges))
        {
            return 0;
        }

        return IsInRanges(value, s_wideRanges) ? 2 : 1;
    }

    /// <summary>计算字符串的终端显示总宽度。</summary>
    public static int OfText(ReadOnlySpan<char> text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += Of(rune);
        }

        return width;
    }

    private static bool IsInRanges(int value, (int Lo, int Hi)[] ranges)
    {
        var lo = 0;
        var hi = ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var (rangeLo, rangeHi) = ranges[mid];
            if (value < rangeLo)
            {
                hi = mid - 1;
            }
            else if (value > rangeHi)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
