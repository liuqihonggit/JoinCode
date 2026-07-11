namespace Sync.Tests.ToolHandlers;

public class SubscribePRToolHandlersTests
{
    private readonly SubscribePRToolHandlers _handler = new(NullLogger<SubscribePRToolHandlers>.Instance);

    [Fact]
    public async Task SubscribePRAsync_ListWithoutService_ReturnsError()
    {
        var result = await _handler.SubscribePRAsync("list", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("GitHub 服务未配置", result.GetTextContent());
    }

    [Fact]
    public async Task SubscribePRAsync_SubscribeWithoutRef_ReturnsError()
    {
        var result = await _handler.SubscribePRAsync("subscribe", pr_ref: null, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("GitHub 服务未配置", result.GetTextContent());
    }

    [Fact]
    public async Task SubscribePRAsync_InvalidAction_ReturnsError()
    {
        var result = await _handler.SubscribePRAsync("invalid", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        // FromValue("invalid") 返回 null，在到达 GitHub 服务检查之前就返回了未知操作错误
        Assert.Contains("未知操作", result.GetTextContent());
    }
}
