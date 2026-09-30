namespace Services.Web;

/// <summary>
/// MIME类型到文件扩展名映射 — 委托单数据源 MimeExtensionCatalog
/// 保留公共 API 以兼容 kit/hands 内部消费方（BinaryContentStorage 等）
/// </summary>
internal static class MimeTypeExtensionMapper {
    /// <summary>
    /// 根据MIME类型获取文件扩展名（不含点号） — 委托 MimeExtensionCatalog.GetExtension
    /// </summary>
    /// <param name="mimeType">MIME 类型字符串，可包含 charset 参数。大小写不敏感。</param>
    /// <returns>对应的文件扩展名（不含点号），未知类型返回 "bin"。</returns>
    public static string GetExtension(string? mimeType) => MimeExtensionCatalog.GetExtension(mimeType);
}
