

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Terminal, Optional = true)]
public partial class TerminalCaptureToolHandlers
{
    [Inject] private readonly ILogger<TerminalCaptureToolHandlers>? _logger;
    private readonly ITerminalCaptureService? _captureService;

    public TerminalCaptureToolHandlers(ILogger<TerminalCaptureToolHandlers>? logger = null, ITerminalCaptureService? captureService = null)
    {
        _logger = logger;
        _captureService = captureService;
    }

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
            return McpResultBuilder.Error().WithText(L.T(StringKey.TerminalCaptureFailed, ex.Message)).Build();
        }
    }

    private ToolResult CaptureWithService(CaptureType captureType, int maxLines)
    {
        var response = new System.Text.StringBuilder();

        if (captureType == CaptureType.Buffer)
        {
            var snapshot = _captureService!.CaptureBuffer(maxLines);
            if (snapshot == null)
            {
                response.AppendLine(L.T(StringKey.TerminalBufferCapture));
                response.AppendLine();
                response.AppendLine(L.T(StringKey.BufferCaptureUnavailable));
                response.AppendLine(L.T(StringKey.UseScreenModeCapture));
                return McpResultBuilder.Success().WithText(response.ToString()).Build();
            }

            response.AppendLine(L.T(StringKey.TerminalBufferCapture));
            response.AppendLine(L.T(StringKey.TerminalLabelSize, snapshot.Width, snapshot.Height));
            response.AppendLine(L.T(StringKey.TerminalLabelCaptureTime, snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            response.AppendLine();
            response.AppendLine(snapshot.Content);
        }
        else
        {
            var snapshot = _captureService!.CaptureScreen();
            response.AppendLine(L.T(StringKey.TerminalScreenCapture));
            response.AppendLine(L.T(StringKey.TerminalLabelSize, snapshot.Width, snapshot.Height));
            response.AppendLine(L.T(StringKey.TerminalLabelCaptureTime, snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            response.AppendLine();
            response.AppendLine(snapshot.Content);
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
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

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
