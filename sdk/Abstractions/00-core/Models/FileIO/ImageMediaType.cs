namespace JoinCode.Abstractions.Models.FileIO;

/// <summary>
/// 图像媒体类型枚举 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// 对齐 TS: IMAGE_EXTENSIONS + imageMediaTypes
/// </summary>
public enum ImageMediaType
{
    [EnumValue("png")] Png,
    [EnumValue("jpg")] Jpg,
    [EnumValue("jpeg")] Jpeg,
    [EnumValue("gif")] Gif,
    [EnumValue("webp")] Webp,
}

/// <summary>
/// 图像媒体类型辅助类 — 提供扩展名集合和MIME类型查询
/// </summary>
public static class ImageMediaTypeHelper
{
    /// <summary>
    /// 所有支持的图像扩展名（小写，用于 FrozenSet 构造）
    /// </summary>
    public static readonly string[] Extensions =
    [
        ImageMediaType.Png.ToValue(),
        ImageMediaType.Jpg.ToValue(),
        ImageMediaType.Jpeg.ToValue(),
        ImageMediaType.Gif.ToValue(),
        ImageMediaType.Webp.ToValue(),
    ];

    /// <summary>
    /// 从扩展名解析图像媒体类型（忽略大小写，由生成器的 FromValue 实现）
    /// </summary>
    public static ImageMediaType? FromExtension(string extension)
        => ImageMediaTypeExtensions.FromValue(extension);

    /// <summary>
    /// 获取MIME类型字符串（如 "image/png"）
    /// </summary>
    public static string GetMimeType(ImageMediaType type)
        => $"image/{type.ToValue()}";

    /// <summary>
    /// 从文件头部的 magic bytes 检测图像格式。
    /// 对齐 TS: detectImageFormatFromBuffer
    /// </summary>
    /// <param name="buffer">图像字节数组（至少需要 12 字节用于 WebP 检测）</param>
    /// <returns>检测到的图像类型，未知格式返回 null</returns>
    public static ImageMediaType? DetectFromMagicBytes(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4)
            return null;

        // PNG signature: 89 50 4E 47
        if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
            return ImageMediaType.Png;

        // JPEG signature: FF D8 FF
        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return ImageMediaType.Jpeg;

        // GIF signature: 47 49 46 (GIF)
        if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46)
            return ImageMediaType.Gif;

        // WebP signature: RIFF....WEBP
        // 0-3: RIFF (52 49 46 46)
        // 8-11: WEBP (57 45 42 50)
        if (buffer.Length >= 12 &&
            buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
            buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
            return ImageMediaType.Webp;

        return null;
    }

    /// <summary>
    /// 从 base64 字符串检测图像格式。
    /// 对齐 TS: detectImageFormatFromBase64
    /// </summary>
    /// <param name="base64Data">Base64 编码的图像数据</param>
    /// <returns>检测到的图像类型，解码失败或未知格式返回 null</returns>
    public static ImageMediaType? DetectFromBase64(string base64Data)
    {
        if (string.IsNullOrEmpty(base64Data))
            return null;

        try
        {
            // 仅解码前 12 字节用于 magic bytes 检测
            var buffer = new Span<byte>(new byte[12]);
            if (!Convert.TryFromBase64String(base64Data, buffer, out _))
                return null;
            return DetectFromMagicBytes(buffer);
        }
        catch
        {
            return null;
        }
    }
}
