namespace JoinCode.Hands.ToolHandlers.Handlers.SystemTools;

/// <summary>
/// WebBrowser工具处理器 — 对齐TS版 WebBrowserTool 的 open/screenshot/evaluate 三种操作
/// screenshot/evaluate 通过 IBrowserAutomationService 解耦，默认 NoOp，安装 PuppeteerSharp 卫星包后启用
/// </summary>
[McpToolHandler(ToolCategory.Browser, Optional = true)]
public partial class WebBrowserToolHandlers
{
    private readonly IWebService _webService;
    private readonly IBrowserAutomationService _browserService;
    [Inject] private readonly ILogger<WebBrowserToolHandlers>? _logger;

    public WebBrowserToolHandlers(
        IWebService webService,
        IBrowserAutomationService browserService,
        ILogger<WebBrowserToolHandlers>? logger = null)
    {
        _webService = webService ?? throw new ArgumentNullException(nameof(webService));
        _browserService = browserService ?? throw new ArgumentNullException(nameof(browserService));
        _logger = logger;
    }

    [McpTool(WebToolNameConstants.WebBrowser, "Open a URL in the browser or perform browser actions", "web")]
    public async Task<ToolResult> WebBrowserActionAsync(
        [McpToolParameter("URL to open/screenshot, or JavaScript expression to evaluate")] string target,
        [McpToolParameter("Action type: open/screenshot/evaluate (default open)", Required = false)] string action = "open",
        [McpToolParameter("Wait time in milliseconds (optional, default 3000)", Required = false)] int? wait_ms = 3000,
        [McpToolParameter("URL of the page context for evaluate action (optional)", Required = false)] string? url = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            return McpResultBuilder.Error().WithText(L.T(StringKey.BrowserTargetCannotBeEmpty)).Build();

        var validationError = ValidationHelper.ValidateRange(wait_ms, 0, 60000, "wait_ms");
        if (validationError != null)
            return McpResultBuilder.Error().WithText(validationError).Build();

        var browserAction = BrowserActionExtensions.FromValue(action);
        if (browserAction == null)
            return McpResultBuilder.Error().WithText(L.T(StringKey.BrowserUnknownAction, action)).Build();

        try
        {
            var response = new System.Text.StringBuilder();
            var waitMs = wait_ms ?? 3000;

            switch (browserAction.Value)
            {
                case BrowserAction.Open:
                    var fetchResult = await _webService.FetchAsync(target, cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.BrowserFetched, target));
                    if (fetchResult.Success)
                    {
                        response.AppendLine(L.T(StringKey.BrowserContentSize, fetchResult.Bytes));
                        if (fetchResult.Truncated)
                            response.AppendLine(L.T(StringKey.BrowserContentTruncated));
                        if (!string.IsNullOrEmpty(fetchResult.Content))
                        {
                            response.AppendLine();
                            response.AppendLine(fetchResult.Content);
                        }
                    }
                    else
                    {
                        response.AppendLine(L.T(StringKey.BrowserFetchFailed, fetchResult.ErrorMessage ?? L.T(StringKey.BrowserUnknownError)));
                    }
                    break;

                case BrowserAction.Screenshot:
                    if (!_browserService.IsAvailable)
                    {
                        response.AppendLine(L.T(StringKey.BrowserScreenshotNotSupported));
                        response.AppendLine(L.T(StringKey.BrowserUseWebFetch));
                        break;
                    }

                    var screenshotResult = await _browserService.ScreenshotAsync(target, waitMs, cancellationToken).ConfigureAwait(false);
                    if (screenshotResult.Success && screenshotResult.Data is { Length: > 0 })
                    {
                        response.AppendLine(L.T(StringKey.BrowserFetched, target));
                        response.AppendLine(L.T(StringKey.BrowserContentSize, screenshotResult.Data.Length));
                        var base64Png = Convert.ToBase64String(screenshotResult.Data);
                        return McpResultBuilder.Success()
                            .WithText(response.ToString())
                            .WithImage(base64Png, "image/png")
                            .Build();
                    }
                    else
                    {
                        response.AppendLine(L.T(StringKey.BrowserFetchFailed, screenshotResult.ErrorMessage ?? L.T(StringKey.BrowserUnknownError)));
                    }
                    break;

                case BrowserAction.Evaluate:
                    if (!_browserService.IsAvailable)
                    {
                        response.AppendLine(L.T(StringKey.BrowserJsNotSupported));
                        break;
                    }

                    // evaluate 时 target 是 JS 表达式，url 是页面上下文
                    var pageUrl = string.IsNullOrEmpty(url) ? "about:blank" : url;
                    var evaluateResult = await _browserService.EvaluateAsync(pageUrl, target, waitMs, cancellationToken).ConfigureAwait(false);
                    if (evaluateResult.Success)
                    {
                        response.AppendLine($"JavaScript evaluation result on {pageUrl}:");
                        if (!string.IsNullOrEmpty(evaluateResult.Data))
                        {
                            response.AppendLine();
                            response.AppendLine(evaluateResult.Data);
                        }
                    }
                    else
                    {
                        response.AppendLine(L.T(StringKey.BrowserFetchFailed, evaluateResult.ErrorMessage ?? L.T(StringKey.BrowserUnknownError)));
                    }
                    break;

                default:
                    return McpResultBuilder.Error().WithText(L.T(StringKey.BrowserUnknownAction, action)).Build();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.BrowserFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.BrowserFailed, ex.Message)).Build();
        }
    }
}
