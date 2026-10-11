namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// MIME类型到文件扩展名映射单数据源 — 统一 McpOutputStorage/MimeTypeExtensionMapper/McpClientToolHandlers 的重复映射
/// 覆盖PDF、Office全家桶、音视频、图片、压缩包等常见类型，未知返回 bin
/// 所有消费方必须委托本类，禁止本地硬编码映射表
/// </summary>
public static class MimeExtensionCatalog {
    /// <summary>
    /// 根据MIME类型获取文件扩展名（不含点号）
    /// </summary>
    /// <param name="mimeType">MIME 类型字符串，可包含 charset 参数。大小写不敏感。</param>
    /// <returns>对应的文件扩展名（不含点号），未知或空类型返回 "bin"。</returns>
    public static string GetExtension(string? mimeType) {
        if (!TryGetExtension(mimeType, out var ext))
            return "bin";
        return ext;
    }

    /// <summary>
    /// 尝试根据MIME类型获取文件扩展名（不含点号） — 消费方可据此区分命中与未知，再决定回退策略
    /// </summary>
    /// <param name="mimeType">MIME 类型字符串，可包含 charset 参数。大小写不敏感。</param>
    /// <param name="extension">命中时输出对应扩展名（不含点号），未命中输出 "bin"。</param>
    /// <returns>命中已知映射返回 true，空或未知类型返回 false。</returns>
    public static bool TryGetExtension(string? mimeType, out string extension) {
        extension = "bin";
        if (string.IsNullOrEmpty(mimeType))
            return false;

        var mt = ExtractMimeType(mimeType);
        var known = LookupKnown(mt);
        if (known is null)
            return false;
        extension = known;
        return true;
    }

    private static string ExtractMimeType(string mimeType) {
        var separatorIndex = mimeType.IndexOf(';');
        var mime = separatorIndex >= 0 ? mimeType[..separatorIndex] : mimeType;
        return mime.Trim().ToLowerInvariant();
    }

    private static string? LookupKnown(string mt) => mt switch {
        // 文档
        "application/pdf" => "pdf",
        "application/json" => "json",
        "text/csv" => "csv",
        "text/plain" => "txt",
        "text/html" => "html",
        "text/markdown" => "md",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
        "application/msword" => "doc",
        "application/vnd.ms-excel" => "xls",
        // 音频
        "audio/mpeg" => "mp3",
        "audio/wav" => "wav",
        "audio/ogg" => "ogg",
        // 视频
        "video/mp4" => "mp4",
        "video/webm" => "webm",
        // 图片
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        // 压缩
        "application/zip" => "zip",
        "application/gzip" => "gz",
        _ => null
    };
}
