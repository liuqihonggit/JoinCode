namespace NativeTui.Core.Grid;

/// <summary>
/// UAX #29 Extended Grapheme Cluster 分段器（核心规则子集）— 对齐基准 ink/stringWidth.ts
/// grapheme 分段前提（DSG044 L1）：ZWJ emoji 链整块、RI 旗帜奇偶配对、组合符/变体选择器
/// 附加、代理对完整、CRLF 合并。0-GC 枚举形态（Span 切片 — 不适用 StringInfo 每次分配的热路径）。
/// 消费方：RuneWidth.WidthOfGrapheme（grapheme 整块计宽）与 InputBuffer grapheme 光标模型。
/// </summary>
public static class GraphemeSegmenter
{
    /// <summary>枚举 [start, end) 的下一个 grapheme cluster；start 越界返回 false。</summary>
    public static bool TryNext(ReadOnlySpan<char> text, int start, out int end)
    {
        if (start < 0 || start >= text.Length)
        {
            end = start;
            return false;
        }

        var i = start + CharLengthAt(text, start);
        if (text[start] == '\r' && i < text.Length && text[i] == '\n')
        {
            end = i + 1;
            return true;
        }

        while (i < text.Length)
        {
            var (rune, length) = DecodeAt(text, i);
            if (rune.Value == 0x200D)
            {
                // ZWJ 并入 + 强制下一字符并入（Emoji ZWJ 链 GB11）
                i += length;
                if (i < text.Length)
                {
                    i += CharLengthAt(text, i);
                }
            }
            else if (IsExtend(rune))
            {
                i += length;
            }
            else if (IsRegionalIndicator(rune) && HasUnpairedRi(text, start, i))
            {
                // RI 奇偶配对（GB12/13）— cluster 内已含奇数个 RI 时并入配对
                i += length;
            }
            else
            {
                break;
            }
        }

        end = i;
        return true;
    }

    /// <summary>组合附加类（GB9 Extend 核心子集：组合符/变体选择器/组合符号附加/ZWNJ）。</summary>
    private static bool IsExtend(Rune rune) =>
        rune.Value is (>= 0x0300 and <= 0x036F) or (>= 0xFE00 and <= 0xFE0F)
            or (>= 0x20D0 and <= 0x20F0) or (>= 0xFE20 and <= 0xFE2F) or 0x200C;

    private static bool IsRegionalIndicator(Rune rune) =>
        rune.Value is >= 0x1F1E6 and <= 0x1F1FF;

    /// <summary>cluster 内 [clusterStart, before) 的连续 RI 计数是否为奇数（待配对）。</summary>
    private static bool HasUnpairedRi(ReadOnlySpan<char> text, int clusterStart, int before)
    {
        var count = 0;
        var i = clusterStart;
        while (i < before)
        {
            var (rune, length) = DecodeAt(text, i);
            count = IsRegionalIndicator(rune) ? count + 1 : 0;
            i += length;
        }

        return (count & 1) == 1;
    }

    private static (Rune Rune, int Length) DecodeAt(ReadOnlySpan<char> text, int i) =>
        char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
            ? (new Rune(text[i], text[i + 1]), 2)
            : ((Rune)text[i], 1);

    private static int CharLengthAt(ReadOnlySpan<char> text, int i) =>
        char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
}
