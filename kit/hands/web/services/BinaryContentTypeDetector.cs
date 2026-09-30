namespace Services.Web;

/// <summary>
/// 二进制Content-Type检测 — 委托单数据源 BinaryContentTypeCatalog
/// 保留公共 API 以兼容 kit/hands 内部消费方（WebContentProcessingMiddleware 等）
/// </summary>
internal static class BinaryContentTypeDetector {
    /// <summary>
    /// 判断Content-Type是否为二进制类型 — 委托 BinaryContentTypeCatalog.IsBinaryContentType
    /// </summary>
    /// <param name="contentType">Content-Type 字符串，可包含 charset 参数。大小写不敏感。</param>
    /// <returns>二进制类型返回 true，文本类型返回 false。</returns>
    public static bool IsBinaryContentType(string? contentType) => BinaryContentTypeCatalog.IsBinaryContentType(contentType);
}
