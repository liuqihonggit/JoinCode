namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 二进制Content-Type检测单数据源 — 统一 McpBinaryHelper/BinaryContentTypeDetector 的重复实现
/// 白名单排除法：排除text/*、json、xml、js、form-urlencoded，其余视为二进制
/// Span 优化 + 大小写不敏感，所有消费方必须委托本类
/// </summary>
public static class BinaryContentTypeCatalog {
    /// <summary>
    /// 判断Content-Type是否为二进制类型 — 大小写不敏感
    /// </summary>
    /// <param name="contentType">Content-Type 字符串，可包含 charset 参数。</param>
    /// <returns>二进制类型返回 true，文本类型或空返回 false。</returns>
    public static bool IsBinaryContentType(string? contentType) {
        if (string.IsNullOrEmpty(contentType))
            return false;

        var mt = contentType.AsSpan();
        var semiIndex = mt.IndexOf(';');
        if (semiIndex >= 0)
            mt = mt[..semiIndex];
        mt = mt.Trim();

        // text/* 前缀 → 非二进制
        if (mt.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return false;

        // application/json 或 *+json 后缀 → 非二进制
        if (mt.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            return false;
        if (mt.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return false;

        // application/xml 或 *+xml 后缀 → 非二进制
        if (mt.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
            return false;
        if (mt.Equals("application/xml", StringComparison.OrdinalIgnoreCase))
            return false;

        // application/javascript 前缀 → 非二进制
        if (mt.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase))
            return false;

        // application/x-www-form-urlencoded → 非二进制
        if (mt.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            return false;

        // 其余全部视为二进制（PDF、图片、音视频、Office文档等）
        return true;
    }
}
