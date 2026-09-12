namespace Services.Web;

/// <summary>
/// MIME类型到文件扩展名映射 — 对齐TS版 mcpOutputStorage.ts 的 extensionForMimeType
/// 覆盖PDF、Office全家桶、音视频、图片、压缩包等常见类型，未知回退.bin
/// </summary>
internal static class MimeTypeExtensionMapper
{
    /// <summary>
    /// 根据MIME类型获取文件扩展名（不含点号）
    /// </summary>
    public static string GetExtension(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType)) return "bin";

        var mt = GetMimeType(mimeType);

        return mt switch
        {
            "application/pdf" => "pdf",
            "application/json" => "json",
            "text/csv" => "csv",
            "text/plain" => "txt",
            "text/html" => "html",
            "text/markdown" => "md",
            "application/zip" => "zip",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
            "application/msword" => "doc",
            "application/vnd.ms-excel" => "xls",
            "audio/mpeg" => "mp3",
            "audio/wav" => "wav",
            "audio/ogg" => "ogg",
            "video/mp4" => "mp4",
            "video/webm" => "webm",
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/svg+xml" => "svg",
            _ => "bin"
        };
    }

    private static string GetMimeType(string mimeType)
    {
        var separatorIndex = mimeType.IndexOf(';');
        var mime = separatorIndex >= 0 ? mimeType[..separatorIndex] : mimeType;
        return mime.Trim().ToLowerInvariant();
    }
}
