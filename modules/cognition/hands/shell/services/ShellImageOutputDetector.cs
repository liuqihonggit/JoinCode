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
    public static async Task<(string MediaType, string Base64Data)?> ResizeIfOversizedAsync(string mediaType, string base64Data) {
        var bytes = Convert.FromBase64String(base64Data);
        if (bytes.Length <= MaxImageFileSizeBytes)
            return (mediaType, base64Data);

        try {
            using var original = SKBitmap.Decode(bytes);
            if (original is null) return (mediaType, base64Data);
            var maxDimension = 2048;
            var target = original;
            var shouldDisposeTarget = false;

            if (original.Width > maxDimension || original.Height > maxDimension) {
                var scale = Math.Min((float)maxDimension / original.Width, (float)maxDimension / original.Height);
                var newWidth = (int)(original.Width * scale);
                var newHeight = (int)(original.Height * scale);
                target = new SKBitmap(newWidth, newHeight, original.ColorType, original.AlphaType);
                shouldDisposeTarget = true;
                using var canvas = new SKCanvas(target);
                canvas.DrawBitmap(original, new SKRectI(0, 0, newWidth, newHeight),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                canvas.Flush();
            }

            try {
                var (format, quality) = mediaType switch {
                    "image/png" => (SKEncodedImageFormat.Png, 100),
                    "image/gif" => (SKEncodedImageFormat.Gif, 100),
                    _ => (SKEncodedImageFormat.Jpeg, 85),
                };

                using var image = SKImage.FromBitmap(target);
                using var data = image.Encode(format, quality);
                var compressedBase64 = Convert.ToBase64String(data.ToArray());
                var resultMediaType = format == SKEncodedImageFormat.Jpeg ? "image/jpeg" : mediaType;
                return (resultMediaType, compressedBase64);
            } finally {
                if (shouldDisposeTarget) target.Dispose();
            }
        } catch {
            return (mediaType, base64Data);
        }
    }
}