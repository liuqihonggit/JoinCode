namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 图像缩放/压缩结果
/// 对齐 TS: ResizeResult
/// </summary>
public sealed class ImageResizeResult
{
    /// <summary>处理后的图像字节数据</summary>
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

/// <summary>
/// 图像缩放/压缩服务。
/// 对齐 TS: imageResizer.ts — maybeResizeAndDownsampleImageBuffer
/// </summary>
public static class ImageResizer
{
    /// <summary>
    /// 缩放和压缩图像缓冲区，使其满足大小和尺寸约束。
    /// 对齐 TS: maybeResizeAndDownsampleImageBuffer
    /// </summary>
    /// <param name="imageBuffer">原始图像字节数据</param>
    /// <param name="originalSize">原始文件大小</param>
    /// <param name="extension">文件扩展名（如 "png", "jpg"）</param>
    /// <returns>缩放/压缩后的结果</returns>
    public static async Task<ImageResizeResult> ResizeAsync(
        byte[] imageBuffer, long originalSize, string extension)
    {
        if (imageBuffer.Length == 0)
            throw new InvalidOperationException("图像文件为空（0 字节）");

        try
        {
            using var image = Image.Load(imageBuffer);
            var format = image.Metadata.DecodedImageFormat;
            var mediaType = GetNormalizedMediaType(format, extension);

            var originalWidth = image.Width;
            var originalHeight = image.Height;

            // 检查原始文件是否已满足约束
            if (originalSize <= FileOperationConfig.ImageTargetRawSize &&
                originalWidth <= FileOperationConfig.ImageMaxWidth &&
                originalHeight <= FileOperationConfig.ImageMaxHeight)
            {
                return new ImageResizeResult
                {
                    Buffer = imageBuffer,
                    MediaType = mediaType,
                    OriginalWidth = originalWidth,
                    OriginalHeight = originalHeight,
                    DisplayWidth = originalWidth,
                    DisplayHeight = originalHeight,
                };
            }

            var needsDimensionResize =
                originalWidth > FileOperationConfig.ImageMaxWidth ||
                originalHeight > FileOperationConfig.ImageMaxHeight;
            var isPng = mediaType == "image/png";

            // 尺寸在限制内但文件过大，先尝试压缩
            if (!needsDimensionResize && originalSize > FileOperationConfig.ImageTargetRawSize)
            {
                // PNG 先尝试 PNG 调色板压缩（保留透明度）
                if (isPng)
                {
                    var pngCompressed = await EncodeToPngPaletteAsync(image).ConfigureAwait(false);
                    if (pngCompressed.Length <= FileOperationConfig.ImageTargetRawSize)
                    {
                        return new ImageResizeResult
                        {
                            Buffer = pngCompressed,
                            MediaType = "image/png",
                            OriginalWidth = originalWidth,
                            OriginalHeight = originalHeight,
                            DisplayWidth = originalWidth,
                            DisplayHeight = originalHeight,
                        };
                    }
                }

                // 渐进 JPEG 压缩
                foreach (var quality in new[] { 80, 60, 40, 20 })
                {
                    var compressed = await EncodeToJpegAsync(image, quality).ConfigureAwait(false);
                    if (compressed.Length <= FileOperationConfig.ImageTargetRawSize)
                    {
                        return new ImageResizeResult
                        {
                            Buffer = compressed,
                            MediaType = "image/jpeg",
                            OriginalWidth = originalWidth,
                            OriginalHeight = originalHeight,
                            DisplayWidth = originalWidth,
                            DisplayHeight = originalHeight,
                        };
                    }
                }
                // 压缩不够，继续缩放
            }

            // 约束尺寸（保持宽高比）
            var width = originalWidth;
            var height = originalHeight;

            if (width > FileOperationConfig.ImageMaxWidth)
            {
                height = (int)Math.Round((double)height * FileOperationConfig.ImageMaxWidth / width);
                width = FileOperationConfig.ImageMaxWidth;
            }

            if (height > FileOperationConfig.ImageMaxHeight)
            {
                width = (int)Math.Round((double)width * FileOperationConfig.ImageMaxHeight / height);
                height = FileOperationConfig.ImageMaxHeight;
            }

            // 缩放图像
            using var resizedImage = image.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3,
                }));

            var resizedBuffer = await EncodeToBufferAsync(resizedImage, format).ConfigureAwait(false);

            // 缩放后仍过大，尝试压缩
            if (resizedBuffer.Length > FileOperationConfig.ImageTargetRawSize)
            {
                if (isPng)
                {
                    var pngCompressed = await EncodeToPngPaletteAsync(resizedImage).ConfigureAwait(false);
                    if (pngCompressed.Length <= FileOperationConfig.ImageTargetRawSize)
                    {
                        return new ImageResizeResult
                        {
                            Buffer = pngCompressed,
                            MediaType = "image/png",
                            OriginalWidth = originalWidth,
                            OriginalHeight = originalHeight,
                            DisplayWidth = width,
                            DisplayHeight = height,
                        };
                    }
                }

                // 渐进 JPEG 压缩
                foreach (var quality in new[] { 80, 60, 40, 20 })
                {
                    var compressed = await EncodeToJpegAsync(resizedImage, quality).ConfigureAwait(false);
                    if (compressed.Length <= FileOperationConfig.ImageTargetRawSize)
                    {
                        return new ImageResizeResult
                        {
                            Buffer = compressed,
                            MediaType = "image/jpeg",
                            OriginalWidth = originalWidth,
                            OriginalHeight = originalHeight,
                            DisplayWidth = width,
                            DisplayHeight = height,
                        };
                    }
                }

                // 最终方案：更小尺寸 + 激进 JPEG 压缩
                var smallerWidth = Math.Min(width, 1000);
                var smallerHeight = (int)Math.Round((double)height * smallerWidth / Math.Max(width, 1));

                using var finalImage = image.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(smallerWidth, smallerHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                    }));

                var finalBuffer = await EncodeToJpegAsync(finalImage, 20).ConfigureAwait(false);
                return new ImageResizeResult
                {
                    Buffer = finalBuffer,
                    MediaType = "image/jpeg",
                    OriginalWidth = originalWidth,
                    OriginalHeight = originalHeight,
                    DisplayWidth = smallerWidth,
                    DisplayHeight = smallerHeight,
                };
            }

            return new ImageResizeResult
            {
                Buffer = resizedBuffer,
                MediaType = mediaType,
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                DisplayWidth = width,
                DisplayHeight = height,
            };
        }
        catch (Exception)
        {
            // 图像处理失败时的回退逻辑（对齐 TS catch 块）
            return await FallbackResizeAsync(imageBuffer, originalSize, extension).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 图像处理失败时的回退逻辑。
    /// 对齐 TS: maybeResizeAndDownsampleImageBuffer 的 catch 块
    /// </summary>
    private static Task<ImageResizeResult> FallbackResizeAsync(
        byte[] imageBuffer, long originalSize, string extension)
    {
        // 用 magic bytes 检测实际格式
        var detected = ImageMediaTypeHelper.DetectFromMagicBytes(imageBuffer);
        var mediaType = detected is not null
            ? ImageMediaTypeHelper.GetMimeType(detected.Value)
            : $"image/{extension}";

        // 计算 base64 大小
        var base64Size = (long)Math.Ceiling(originalSize * 4.0 / 3.0);

        // PNG 尺寸超限检查（对齐 TS: overDim）
        var overDim = IsPngOverDimensionLimit(imageBuffer);

        // base64 在 API 限制内且尺寸未超限，允许通过
        if (base64Size <= FileOperationConfig.ApiImageMaxBase64Size && !overDim)
        {
            return Task.FromResult(new ImageResizeResult
            {
                Buffer = imageBuffer,
                MediaType = mediaType,
            });
        }

        // 图像过大且压缩失败
        var message = overDim
            ? $"无法缩放图像 — 尺寸超过 {FileOperationConfig.ImageMaxWidth}x{FileOperationConfig.ImageMaxHeight}px 限制，且图像处理失败。请手动缩小图像尺寸。"
            : $"无法缩放图像（原始 {JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.FormatFileSize(originalSize)}，base64 {JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.FormatFileSize(base64Size)}）。图像超过 5MB API 限制且压缩失败。请使用更小的图像。";

        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// 检查 PNG 图像是否超过尺寸限制。
    /// 对齐 TS: PNG header 尺寸检测（IHDR 在偏移 16-24 字节）
    /// </summary>
    private static bool IsPngOverDimensionLimit(byte[] buffer)
    {
        // PNG 签名 8 字节 + IHDR chunk: 长度(4) + 类型(4) + 宽度(4) + 高度(4)
        if (buffer.Length < 24) return false;
        if (buffer[0] != 0x89 || buffer[1] != 0x50 || buffer[2] != 0x4E || buffer[3] != 0x47)
            return false;

        // 读取 IHDR 中的宽度和高度（大端序）
        var width = ReadUInt32BigEndian(buffer, 16);
        var height = ReadUInt32BigEndian(buffer, 20);
        return width > FileOperationConfig.ImageMaxWidth || height > FileOperationConfig.ImageMaxHeight;
    }

    /// <summary>
    /// 大端序读取 32 位无符号整数
    /// </summary>
    private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        => (uint)(buffer[offset] << 24 | buffer[offset + 1] << 16 | buffer[offset + 2] << 8 | buffer[offset + 3]);

    /// <summary>
    /// 规范化媒体类型（jpg → jpeg）
    /// </summary>
    private static string GetNormalizedMediaType(IImageFormat? format, string extension)
    {
        if (format is not null)
        {
            var mime = format.DefaultMimeType;
            // ImageSharp 可能返回 "image/jpeg" 或 "image/png" 等
            if (!string.IsNullOrEmpty(mime))
                return mime;
        }

        // 从扩展名推断
        var ext = extension.ToLowerInvariant();
        if (ext == "jpg") ext = "jpeg";
        return $"image/{ext}";
    }

    /// <summary>
    /// 按原始格式编码到字节数组
    /// </summary>
    private static async Task<byte[]> EncodeToBufferAsync(Image image, IImageFormat? format)
    {
        using var ms = new MemoryStream();
        if (format is JpegFormat)
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 80 }).ConfigureAwait(false);
        else if (format is PngFormat)
            await image.SaveAsPngAsync(ms).ConfigureAwait(false);
        else if (format is WebpFormat)
            await image.SaveAsWebpAsync(ms, new WebpEncoder { Quality = 80 }).ConfigureAwait(false);
        else
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 80 }).ConfigureAwait(false);

        return ms.ToArray();
    }

    /// <summary>
    /// PNG 调色板压缩（对齐 TS: png({ compressionLevel: 9, palette: true })）
    /// </summary>
    private static async Task<byte[]> EncodeToPngPaletteAsync(Image image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms, new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.BestCompression,
            ColorType = PngColorType.Palette,
        }).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>
    /// JPEG 压缩（对齐 TS: jpeg({ quality })）
    /// </summary>
    private static async Task<byte[]> EncodeToJpegAsync(Image image, int quality)
    {
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality }).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>
    /// Token 驱动的激进压缩 — 对齐 TS compressImageBufferWithTokenLimit
    /// 当标准压缩后 token 估算仍超预算时，应用渐进缩放+格式优化
    /// </summary>
    /// <param name="imageBuffer">标准压缩后的图像字节数据</param>
    /// <param name="maxTokens">最大 token 预算</param>
    /// <param name="extension">文件扩展名</param>
    /// <returns>压缩后的结果；若无法满足预算则返回 null</returns>
    public static async Task<ImageResizeResult?> CompressWithTokenBudgetAsync(
        byte[] imageBuffer, int maxTokens, string extension)
    {
        if (imageBuffer.Length == 0)
        {
            return null;
        }

        // Token → 字节转换: 1 token ≈ 8 base64 字符 ≈ 6 原始字节
        var maxBase64Chars = (int)Math.Floor(maxTokens / 0.125);
        var maxBytes = (int)Math.Floor(maxBase64Chars * 0.75);

        try
        {
            using var image = Image.Load(imageBuffer);
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            // 渐进缩放链 — 对齐 TS tryProgressiveResizing
            var scaleFactors = new[] { 1.0, 0.75, 0.5, 0.25 };
            foreach (var scale in scaleFactors)
            {
                var targetWidth = (int)Math.Round(originalWidth * scale);
                var targetHeight = (int)Math.Round(originalHeight * scale);
                if (targetWidth < 50 || targetHeight < 50) break;

                using var scaled = image.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(targetWidth, targetHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                    }));

                // 尝试 PNG 调色板压缩
                var pngBuffer = await EncodeToPngPaletteAsync(scaled).ConfigureAwait(false);
                if (pngBuffer.Length <= maxBytes)
                {
                    return new ImageResizeResult
                    {
                        Buffer = pngBuffer,
                        MediaType = "image/png",
                        OriginalWidth = originalWidth,
                        OriginalHeight = originalHeight,
                        DisplayWidth = targetWidth,
                        DisplayHeight = targetHeight,
                    };
                }

                // 尝试 JPEG quality=80
                var jpegBuffer = await EncodeToJpegAsync(scaled, 80).ConfigureAwait(false);
                if (jpegBuffer.Length <= maxBytes)
                {
                    return new ImageResizeResult
                    {
                        Buffer = jpegBuffer,
                        MediaType = "image/jpeg",
                        OriginalWidth = originalWidth,
                        OriginalHeight = originalHeight,
                        DisplayWidth = targetWidth,
                        DisplayHeight = targetHeight,
                    };
                }
            }

            // PNG 调色板极限压缩 — 对齐 TS tryPalettePNG (800px, 64色)
            {
                var paletteWidth = Math.Min(originalWidth, 800);
                var paletteHeight = (int)Math.Round((double)originalHeight * paletteWidth / Math.Max(originalWidth, 1));
                using var paletteImage = image.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(paletteWidth, paletteHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                    }));

                using var ms = new MemoryStream();
                await paletteImage.SaveAsPngAsync(ms, new PngEncoder
                {
                    CompressionLevel = PngCompressionLevel.BestCompression,
                    ColorType = PngColorType.Palette,
                    Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = 64 }),
                }).ConfigureAwait(false);
                var paletteBuffer = ms.ToArray();

                if (paletteBuffer.Length <= maxBytes)
                {
                    return new ImageResizeResult
                    {
                        Buffer = paletteBuffer,
                        MediaType = "image/png",
                        OriginalWidth = originalWidth,
                        OriginalHeight = originalHeight,
                        DisplayWidth = paletteWidth,
                        DisplayHeight = paletteHeight,
                    };
                }
            }

            // JPEG 转换 — 对齐 TS tryJPEGConversion (600px, q=50)
            {
                var jpegWidth = Math.Min(originalWidth, 600);
                var jpegHeight = (int)Math.Round((double)originalHeight * jpegWidth / Math.Max(originalWidth, 1));
                using var jpegImage = image.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(jpegWidth, jpegHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                    }));

                var jpegBuffer = await EncodeToJpegAsync(jpegImage, 50).ConfigureAwait(false);
                if (jpegBuffer.Length <= maxBytes)
                {
                    return new ImageResizeResult
                    {
                        Buffer = jpegBuffer,
                        MediaType = "image/jpeg",
                        OriginalWidth = originalWidth,
                        OriginalHeight = originalHeight,
                        DisplayWidth = jpegWidth,
                        DisplayHeight = jpegHeight,
                    };
                }
            }

            // 超极限 JPEG — 对齐 TS createUltraCompressedJPEG (400px, q=20)
            {
                var ultraWidth = Math.Min(originalWidth, 400);
                var ultraHeight = (int)Math.Round((double)originalHeight * ultraWidth / Math.Max(originalWidth, 1));
                using var ultraImage = image.Clone(ctx =>
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(ultraWidth, ultraHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                    }));

                var ultraBuffer = await EncodeToJpegAsync(ultraImage, 20).ConfigureAwait(false);
                if (ultraBuffer.Length <= maxBytes)
                {
                    return new ImageResizeResult
                    {
                        Buffer = ultraBuffer,
                        MediaType = "image/jpeg",
                        OriginalWidth = originalWidth,
                        OriginalHeight = originalHeight,
                        DisplayWidth = ultraWidth,
                        DisplayHeight = ultraHeight,
                    };
                }
            }

            // 所有压缩策略都无法满足预算
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 创建图像元数据文本，包含缩放比例和坐标映射信息。
    /// 对齐 TS: createImageMetadataText — imageResizer.ts L835-879
    /// 格式: [Image: source: {path}, original {w}x{h}, displayed at {dw}x{dh}. Multiply coordinates by {scale} to map to original image.]
    /// </summary>
    /// <param name="result">图像缩放结果</param>
    /// <param name="sourcePath">图像来源路径（可选）</param>
    /// <returns>元数据文本；若无需生成则返回 null</returns>
    public static string? CreateImageMetadataText(ImageResizeResult result, string? sourcePath = null)
    {
        var originalWidth = result.OriginalWidth;
        var originalHeight = result.OriginalHeight;
        var displayWidth = result.DisplayWidth;
        var displayHeight = result.DisplayHeight;

        // 无效维度检查（含零值防除零）— 对齐 TS: 无效维度时仅返回 source
        if (originalWidth is not > 0 || originalHeight is not > 0 ||
            displayWidth is not > 0 || displayHeight is not > 0)
        {
            if (sourcePath is not null)
                return $"[Image source: {sourcePath}]";
            return null;
        }

        // 判断是否被缩放
        var wasResized = originalWidth != displayWidth || originalHeight != displayHeight;

        // 未缩放且无 sourcePath → 不生成
        if (!wasResized && sourcePath is null)
            return null;

        // 构建元数据
        var parts = new List<string>(2);
        if (sourcePath is not null)
            parts.Add($"source: {sourcePath}");

        if (wasResized)
        {
            var scaleFactor = (double)originalWidth.Value / displayWidth.Value;
            parts.Add(
                $"original {originalWidth}x{originalHeight}, displayed at {displayWidth}x{displayHeight}. " +
                $"Multiply coordinates by {scaleFactor:F2} to map to original image.");
        }

        return $"[Image: {string.Join(", ", parts)}]";
    }
}
