

namespace McpToolDispatch;

/// <summary>
/// 终端捕获工具处理器 — 提供终端屏幕/缓冲区内容快照捕获功能
/// </summary>
[McpToolDispatch(ToolCategory.Terminal, Optional = true)]
public partial class TerminalCaptureToolHandlers
{
    private readonly ILogger<TerminalCaptureToolHandlers>? _logger;
    private readonly ITerminalCaptureService? _captureService;

    /// <summary>
    /// 初始化终端捕获工具处理器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="captureService">终端捕获服务（可选）</param>
    public TerminalCaptureToolHandlers(ILogger<TerminalCaptureToolHandlers>? logger = null, ITerminalCaptureService? captureService = null)
    {
        _logger = logger;
        _captureService = captureService;
    }

    /// <summary>
    /// 捕获终端屏幕内容快照
    /// </summary>
    /// <param name="capture_type">捕获类型：screen/buffer（默认 screen）</param>
    /// <param name="max_lines">最大行数（可选，默认 50）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含终端内容快照的工具执行结果</returns>
    [McpTool(SystemToolNameConstants.TerminalCapture, "Capture terminal screen content snapshot", "terminal")]
    public async Task<ToolResult> CaptureTerminalAsync(
        [McpToolParameter("Capture type: screen/buffer (default: screen)", Required = false)] string? capture_type = "screen",
        [McpToolParameter("Max lines (optional, default: 50)", Required = false)] int? max_lines = 50,
        CancellationToken cancellationToken = default)
    {
        var captureType = CaptureTypeExtensions.FromValue(capture_type ?? "screen") ?? CaptureType.Screen;
        try
        {
            var effectiveMaxLines = max_lines ?? 50;

            if (_captureService != null)
            {
                return CaptureWithService(captureType, effectiveMaxLines);
            }

            return CaptureFallback(captureType, effectiveMaxLines);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.TerminalCaptureFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.TerminalCaptureFailed, ex.Message)).Build();
        }
    }

    private ToolResult CaptureWithService(CaptureType captureType, int maxLines)
    {
        var response = new System.Text.StringBuilder();

        if (captureType == CaptureType.Buffer)
        {
            var captureService = _captureService ?? throw new InvalidOperationException("CaptureService is not available");
            var snapshot = captureService.CaptureBuffer(maxLines);
            if (snapshot == null)
            {
                response.AppendLine(L.T(StringKey.TerminalBufferCapture));
                response.AppendLine();
                response.AppendLine(L.T(StringKey.BufferCaptureUnavailable));
                response.AppendLine(L.T(StringKey.UseScreenModeCapture));
                return ToolResultBuilder.Success().WithText(response.ToString()).Build();
            }

            response.AppendLine(L.T(StringKey.TerminalBufferCapture));
            response.AppendLine(L.T(StringKey.TerminalLabelSize, snapshot.Width, snapshot.Height));
            response.AppendLine(L.T(StringKey.TerminalLabelCaptureTime, snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            response.AppendLine();
            response.AppendLine(snapshot.Content);
        }
        else
        {
            var captureService = _captureService ?? throw new InvalidOperationException("CaptureService is not available");
            var snapshot = captureService.CaptureScreen();
            response.AppendLine(L.T(StringKey.TerminalScreenCapture));
            response.AppendLine(L.T(StringKey.TerminalLabelSize, snapshot.Width, snapshot.Height));
            response.AppendLine(L.T(StringKey.TerminalLabelCaptureTime, snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            response.AppendLine();
            response.AppendLine(snapshot.Content);
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    private ToolResult CaptureFallback(CaptureType captureType, int maxLines)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.TerminalCapture));
        response.AppendLine();

        try
        {
            var bufferWidth = Console.BufferWidth;
            var bufferHeight = Console.BufferHeight;
            var windowWidth = Console.WindowWidth;
            var windowHeight = Console.WindowHeight;

            response.AppendLine(L.T(StringKey.TerminalLabelTerminalSize, windowWidth, windowHeight));
            response.AppendLine(L.T(StringKey.TerminalLabelBufferSize, bufferWidth, bufferHeight));
            response.AppendLine();

            if (Console.IsOutputRedirected)
            {
                response.AppendLine(L.T(StringKey.OutputRedirectedCannotCapture));
            }
            else
            {
                response.AppendLine(L.T(StringKey.CaptureServiceNotEnabled));
                response.AppendLine(L.T(StringKey.TerminalLabelCaptureLimit, maxLines));
            }
        }
        catch (PlatformNotSupportedException)
        {
            response.AppendLine(L.T(StringKey.PlatformNotSupportTerminalCapture));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
