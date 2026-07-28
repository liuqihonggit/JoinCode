namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 换行符检测器 — 对齐 TS: fileRead.ts detectLineEndingsForString
/// 统计 CRLF vs LF 出现次数，多数者胜出，平局默认 LF
/// </summary>
public static class LineEndingDetector
{
    /// <summary>
    /// 换行符类型 — 对齐 TS: LineEndingType
    /// </summary>
    public enum LineEndingType
    {
        /// <summary>LF (\n)</summary>
        LF,
        /// <summary>CRLF (\r\n)</summary>
        CRLF
    }

    /// <summary>
    /// 从字符串内容检测换行符类型 — 对齐 TS: fileRead.ts L51-66
    /// 仅扫描前 4096 个字符（与 TS 一致），避免大文件全量扫描
    /// </summary>
    public static LineEndingType DetectFromString(ReadOnlySpan<char> content)
    {
        var scanLength = Math.Min(content.Length, 4096);
        var span = content[..scanLength];

        var crlfCount = 0;
        var lfCount = 0;

        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n')
            {
                if (i > 0 && span[i - 1] == '\r')
                    crlfCount++;
                else
                    lfCount++;
            }
        }

        return crlfCount > lfCount ? LineEndingType.CRLF : LineEndingType.LF;
    }

    /// <summary>
    /// 将内容中的换行符恢复为指定类型 — 对齐 TS: file.ts writeTextContent
    /// CRLF: 先将所有 \r\n 归一化为 \n，再统一替换为 \r\n（防止 \r\r\n 双重转换）
    /// LF: 直接使用（内部已是 LF）
    /// </summary>
    public static string RestoreLineEndings(string content, LineEndingType lineEndings)
    {
        if (lineEndings == LineEndingType.LF)
            return content;

        // CRLF: normalize to LF first, then replace LF with CRLF
        // 对齐 TS: content.replaceAll('\r\n', '\n').split('\n').join('\r\n')
        var normalized = content.Replace("\r\n", "\n");
        return normalized.Replace("\n", "\r\n");
    }
}
