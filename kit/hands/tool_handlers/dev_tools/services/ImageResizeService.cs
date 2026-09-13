namespace Infrastructure.IO;

/// <summary>
/// 图片降采样服务 — 包装 ImageResizer 静态方法，实现 IImageResizeService
/// 对齐 TS maybeResizeAndDownsampleImageBuffer
/// </summary>
[Register(typeof(JoinCode.Abstractions.LLM.Chat.IImageResizeService), ServiceLifetime.Singleton)]
public sealed partial class ImageResizeService : ServiceEntity, JoinCode.Abstractions.LLM.Chat.IImageResizeService
{

    /// <summary>
    /// 构造图片降采样服务
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public ImageResizeService(ILogger<ImageResizeService>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<ImageResizeService>? _logger;

    /// <summary>
    /// 缩放和压缩图像缓冲区，委托给 ImageResizer 静态方法
    /// </summary>
    /// <param name="imageBuffer">原始图像字节数据</param>
    /// <param name="originalSize">原始文件大小</param>
    /// <param name="extension">文件扩展名</param>
    /// <returns>缩放/压缩后的结果</returns>
    public async Task<JoinCode.Abstractions.LLM.Chat.McpImageResizeResult> ResizeAsync(byte[] imageBuffer, long originalSize, string extension)
    {
        var result = await IO.Services.FileOps.ImageResizer.ResizeAsync(imageBuffer, originalSize, extension).ConfigureAwait(false);

        return new JoinCode.Abstractions.LLM.Chat.McpImageResizeResult
        {
            Buffer = result.Buffer,
            MediaType = result.MediaType,
            OriginalWidth = result.OriginalWidth,
            OriginalHeight = result.OriginalHeight,
            DisplayWidth = result.DisplayWidth,
            DisplayHeight = result.DisplayHeight
        };
    }
}
