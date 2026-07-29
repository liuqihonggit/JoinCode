namespace Infrastructure.IO;

/// <summary>
/// 图片降采样服务 — 包装 ImageResizer 静态方法，实现 IImageResizeService
/// 对齐 TS maybeResizeAndDownsampleImageBuffer
/// </summary>
[Register(typeof(JoinCode.Abstractions.LLM.Chat.IImageResizeService))]
public sealed partial class ImageResizeService : JoinCode.Abstractions.LLM.Chat.IImageResizeService
{
    [Inject] private readonly ILogger<ImageResizeService>? _logger;

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
