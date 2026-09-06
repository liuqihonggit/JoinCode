
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 行范围索引器 — 零 GC Span 行遍历构建行范围 + 二分查找定位字符偏移所在行
/// <para>统一 RgEngine/SearchService 两处逐字符相同的行范围构建逻辑</para>
/// </summary>
public static class LineSpanIndexer
{
    /// <summary>
    /// 构建行范围列表 — 零分配 Span 遍历，支持 \r\n 和 \n 行尾
    /// </summary>
    /// <param name="content">内容 Span</param>
    /// <param name="ct">取消令牌（可选）</param>
    /// <returns>行范围列表，每项为 (起始偏移, 行长度)</returns>
    public static List<(int Start, int Length)> BuildLineRanges(ReadOnlySpan<char> content, CancellationToken ct = default)
    {
        var lineRanges = new List<(int Start, int Length)>();
        if (content.Length == 0)
            return lineRanges;
        var pos = 0;
        while (pos < content.Length)
        {
            ct.ThrowIfCancellationRequested();

            var nlIdx = content.Slice(pos).IndexOf('\n');
            if (nlIdx < 0)
            {
                lineRanges.Add((pos, content.Length - pos));
                break;
            }
            var lineStart = pos;
            var lineLen = nlIdx;
            if (lineLen > 0 && content[lineStart + lineLen - 1] == '\r')
                lineLen--;
            lineRanges.Add((lineStart, lineLen));
            pos += nlIdx + 1;
        }
        return lineRanges;
    }

    /// <summary>
    /// 二分查找定位字符偏移所在行号
    /// </summary>
    /// <param name="lineRanges">行范围列表</param>
    /// <param name="charIndex">字符偏移</param>
    /// <returns>行号（0-based），找不到返回 -1</returns>
    public static int FindLineIndex(List<(int Start, int Length)> lineRanges, int charIndex)
    {
        var lo = 0;
        var hi = lineRanges.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var (start, length) = lineRanges[mid];
            if (charIndex < start)
                hi = mid - 1;
            else if (charIndex >= start + length)
                lo = mid + 1;
            else
                return mid;
        }
        return lo < lineRanges.Count ? lo : -1;
    }
}
