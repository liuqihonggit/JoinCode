namespace Services.Web;

/// <summary>
/// 二进制Content-Type检测 — 对齐TS版 mcpOutputStorage.ts 的 isBinaryContentType
/// 白名单排除法：排除text/*、json、xml、js、form-urlencoded，其余视为二进制
/// </summary>
internal static class BinaryContentTypeDetector
{
    /// <summary>
    /// 判断Content-Type是否为二进制类型
    /// </summary>
    public static bool IsBinaryContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;

        // 提取MIME类型（去掉charset等参数）
        var mt = GetMimeType(contentType);

        // text/* 前缀 → 非二进制
        if (mt.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return false;

        // application/json 或 *+json 后缀 → 非二进制
        if (mt.Equals("application/json", StringComparison.OrdinalIgnoreCase)) return false;
        if (mt.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) return false;

        // application/xml 或 *+xml 后缀 → 非二进制
        if (mt.Equals("application/xml", StringComparison.OrdinalIgnoreCase)) return false;
        if (mt.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)) return false;

        // application/javascript 前缀 → 非二进制
        if (mt.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase)) return false;

        // application/x-www-form-urlencoded → 非二进制
        if (mt.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) return false;

        // 其余全部视为二进制（PDF、图片、音视频、Office文档等）
        return true;
    }

    /// <summary>
    /// 从Content-Type中提取MIME类型（去掉charset等参数）
    /// </summary>
    private static string GetMimeType(string contentType)
    {
        var separatorIndex = contentType.IndexOf(';');
        var mime = separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
        return mime.Trim().ToLowerInvariant();
    }
}
