namespace Tools.Shell;

/// <summary>
/// Shell 命令输出图片检测与压缩 — 对齐 TS BashTool/utils.ts isImageOutput/parseDataUri/resizeShellImageOutput
/// 检测 stdout 中的 Data URI 格式 base64 图片数据，超过 20MB 时自动压缩
/// </summary>
public static class ShellImageOutputDetector {
    /// <summary>
    /// 图片最大文件大小 — 对齐 TS MAX_IMAGE_FILE_SIZE (20MB)
    /// </summary>
    private const int MaxImageFileSizeBytes = 20 * 1024 * 1024;

    /// <summary>
    /// 检测 stdout 是否为 Data URI 格式的图片输出 — 对齐 TS isImageOutput
    /// 格式: data:image/xxx;base64,...
    /// </summary>
    /// <param name="stdout">命令标准输出文本</param>
    /// <returns>若为 Data URI 格式图片返回 true，否则返回 false</returns>
    public static bool IsImageOutput(string stdout) {
        if (string.IsNullOrEmpty(stdout))
            return false;

        var trimmed = stdout.AsSpan().Trim();
        if (!trimmed.StartsWith("data:image/".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        var semicolonIndex = trimmed.IndexOf(';');
        if (semicolonIndex < 0)
            return false;

        var base64Index = trimmed.Slice(semicolonIndex + 1);
        return base64Index.StartsWith("base64,".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 Data URI — 对齐 TS parseDataUri
    /// 返回 (mediaType, base64Data) 或 null
    /// </summary>
    /// <param name="dataUri">Data URI 格式字符串</param>
    /// <returns>解析成功的 (媒体类型, base64 数据) 元组；格式不合法则返回 null</returns>
    public static (string MediaType, string Base64Data)? ParseDataUri(string dataUri) {
        if (string.IsNullOrEmpty(dataUri))
            return null;

        var trimmed = dataUri.Trim();

        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        var semicolonIndex = trimmed.IndexOf(';');
        if (semicolonIndex < 0)
            return null;

        var mediaType = trimmed[5..semicolonIndex];
        var afterSemicolon = trimmed[(semicolonIndex + 1)..];

        if (!afterSemicolon.StartsWith("base64,", StringComparison.OrdinalIgnoreCase))
            return null;

        var base64Data = afterSemicolon[7..];
        if (string.IsNullOrEmpty(base64Data))
            return null;

        return (mediaType, base64Data);
    }

    /// <summary>
    /// 压缩过大的图片输出 — 对齐 TS resizeShellImageOutput
    /// 超过 20MB 时降低质量/尺寸，防止超出 API 限制
    /// </summary>
    /// <param name="mediaType">图片媒体类型（如 image/png）</param>
    /// <param name="base64Data">图片 base64 编码数据</param>
    /// <returns>压缩后的 (媒体类型, base64 数据)；若未超限或压缩失败则返回原始数据</returns>
    public static (string MediaType, string Base64Data)? ResizeIfOversized(string mediaType, string base64Data) {
        var bytes = Convert.FromBase64String(base64Data);
        if (bytes.Length <= MaxImageFileSizeBytes)
            return (mediaType, base64Data);

        try {
            using var image = SixLabors.ImageSharp.Image.Load(bytes);
            var maxDimension = 2048;
            if (image.Width > maxDimension || image.Height > maxDimension) {
                image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions {
                    Size = new SixLabors.ImageSharp.Size(maxDimension, maxDimension),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                }));
            }

            var bufferWriter = new ArrayBufferWriter<byte>();
            var encoder = mediaType switch {
                "image/png" => (SixLabors.ImageSharp.Formats.IImageEncoder)new SixLabors.ImageSharp.Formats.Png.PngEncoder(),
                "image/gif" => new SixLabors.ImageSharp.Formats.Gif.GifEncoder(),
                _ => new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 },
            };
            image.Save(bufferWriter, encoder);

            var compressedBase64 = Convert.ToBase64String(bufferWriter.WrittenSpan);
            var resultMediaType = encoder is SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder ? "image/jpeg" : mediaType;
            return (resultMediaType, compressedBase64);
        } catch {
            return (mediaType, base64Data);
        }
    }
}