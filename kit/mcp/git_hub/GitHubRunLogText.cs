namespace McpToolDispatch;

/// <summary>
/// GitHub Run 日志文本处理工具 — 去时间戳、去 ANSI 转义码
/// </summary>
internal static class GitHubRunLogText
{
    /// <summary>
    /// 去掉日志行的时间戳前缀和 ANSI 转义码 — "2026-09-07T17:08:27.5016453Z \x1B[36;1mcontent\x1B[0m" → "content"
    /// </summary>
    public static string StripLogTimestamp(string line)
    {
        // GitHub Actions 日志格式: "2026-09-07T17:08:27.5016453Z content"
        // 找到第一个 'Z ' 后面的内容
        var zIdx = line.IndexOf('Z');
        if (zIdx > 0 && zIdx + 2 < line.Length && line[zIdx + 1] == ' ')
        {
            return StripAnsiEscapes(line[(zIdx + 2)..]);
        }
        // [entry.Name] 前缀的行
        if (line.StartsWith('['))
        {
            var closeIdx = line.IndexOf(']');
            if (closeIdx > 0 && closeIdx + 2 < line.Length)
                return StripAnsiEscapes(line[(closeIdx + 2)..]);
        }
        return StripAnsiEscapes(line);
    }

    /// <summary>
    /// 去除 ANSI 转义码序列(ESC[...m) — Span 查找 ESC,无 ESC 直接返回零分配
    /// </summary>
    public static string StripAnsiEscapes(string s)
    {
        var span = s.AsSpan();
        var escIdx = span.IndexOf('\x1B');
        if (escIdx < 0) return s;
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < span.Length)
        {
            if (span[i] == '\x1B' && i + 1 < span.Length && span[i + 1] == '[')
            {
                i += 2;
                while (i < span.Length && span[i] != 'm') i++;
                i++;
            }
            else
            {
                sb.Append(span[i]);
                i++;
            }
        }
        return sb.ToString();
    }
}
