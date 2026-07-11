namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 图片降采样服务接口 — 对齐 TS maybeResizeAndDownsampleImageBuffer
/// </summary>
public interface IImageResizeService
{
    /// <summary>
    /// 对图片进行降采样/压缩 — 对齐 TS maybeResizeAndDownsampleImageBuffer
    /// </summary>
    /// <param name="imageBuffer">原始图片字节</param>
    /// <param name="originalSize">原始文件大小</param>
    /// <param name="extension">文件扩展名（如 "png", "jpg"）</param>
    /// <returns>降采样结果</returns>
    Task<McpImageResizeResult> ResizeAsync(byte[] imageBuffer, long originalSize, string extension);
}

/// <summary>
/// 图片降采样结果 — 对齐 TS ResizeResult
/// </summary>
public sealed class McpImageResizeResult
{
    /// <summary>处理后的图片字节数据</summary>
    public required byte[] Buffer { get; init; }

    /// <summary>媒体类型（如 "image/png", "image/jpeg"）</summary>
    public required string MediaType { get; init; }

    /// <summary>原始宽度</summary>
    public int? OriginalWidth { get; init; }

    /// <summary>原始高度</summary>
    public int? OriginalHeight { get; init; }

    /// <summary>显示宽度（缩放后）</summary>
    public int? DisplayWidth { get; init; }

    /// <summary>显示高度（缩放后）</summary>
    public int? DisplayHeight { get; init; }
}
