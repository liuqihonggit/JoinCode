namespace Hands.Tests.ToolHandlers;

public class WebBrowserToolHandlersTests
{
    private readonly Mock<IWebService> _webService = new();
    private readonly Mock<IBrowserAutomationService> _browserService = new();
    private readonly WebBrowserToolHandlers _handler;

    public WebBrowserToolHandlersTests()
    {
        _webService.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebFetchResult(Success: true, Url: "https://example.com", Content: "page", Bytes: 4));

        _browserService.Setup(x => x.IsAvailable).Returns(false);

        _handler = new WebBrowserToolHandlers(_webService.Object, _browserService.Object, NullLogger<WebBrowserToolHandlers>.Instance);
    }

    [Fact]
    public async Task WebBrowserActionAsync_EmptyTarget_ReturnsError()
    {
        var result = await _handler.WebBrowserActionAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("不能为空", result.GetTextContent());
    }

    [Fact]
    public async Task WebBrowserActionAsync_Open_ReturnsSuccess()
    {
        var result = await _handler.WebBrowserActionAsync("https://example.com", "open", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("已获取", result.GetTextContent());
    }

    [Fact]
    public async Task WebBrowserActionAsync_Screenshot_NoBrowser_ReturnsNotSupported()
    {
        var result = await _handler.WebBrowserActionAsync("https://example.com", "screenshot", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("截图功能", result.GetTextContent());
    }

    [Fact]
    public async Task WebBrowserActionAsync_Evaluate_NoBrowser_ReturnsNotSupported()
    {
        var result = await _handler.WebBrowserActionAsync("document.title", "evaluate", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("浏览器", result.GetTextContent());
    }

    [Fact]
    public async Task WebBrowserActionAsync_Screenshot_WithBrowser_ReturnsImage()
    {
        var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        _browserService.Setup(x => x.IsAvailable).Returns(true);
        _browserService.Setup(x => x.ScreenshotAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserScreenshotResult(Success: true, PngData: pngData));

        var handler = new WebBrowserToolHandlers(_webService.Object, _browserService.Object, NullLogger<WebBrowserToolHandlers>.Instance);
        var result = await handler.WebBrowserActionAsync("https://example.com", "screenshot", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("已获取", result.GetTextContent());
    }

    [Fact]
    public async Task WebBrowserActionAsync_Evaluate_WithBrowser_ReturnsResult()
    {
        _browserService.Setup(x => x.IsAvailable).Returns(true);
        _browserService.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserEvaluateResult(Success: true, Result: "Hello World"));

        var handler = new WebBrowserToolHandlers(_webService.Object, _browserService.Object, NullLogger<WebBrowserToolHandlers>.Instance);
        var result = await handler.WebBrowserActionAsync("document.title", "evaluate", url: "https://example.com", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("Hello World", result.GetTextContent());
    }

    [Fact]
    public async Task WebBrowserActionAsync_InvalidAction_ReturnsError()
    {
        var result = await _handler.WebBrowserActionAsync("https://example.com", "invalid", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("未知操作", result.GetTextContent());
    }
}
