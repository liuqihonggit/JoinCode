namespace Sync.Tests.ToolHandlers;

public class RemoteTriggerToolHandlersTests
{
    private readonly RemoteTriggerToolHandlers _handler = new(NullLogger<RemoteTriggerToolHandlers>.Instance);

    [Fact]
    public async Task ManageRemoteTriggerAsync_ListWithoutService_ReturnsError()
    {
        var result = await _handler.ManageRemoteTriggerAsync("list", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("远程触发器服务未配置", result.GetTextContent());
    }

    [Fact]
    public async Task ManageRemoteTriggerAsync_GetWithoutId_ReturnsError()
    {
        var result = await _handler.ManageRemoteTriggerAsync("get", trigger_id: null, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("远程触发器服务未配置", result.GetTextContent());
    }

    [Fact]
    public async Task ManageRemoteTriggerAsync_InvalidAction_ReturnsError()
    {
        var result = await _handler.ManageRemoteTriggerAsync("invalid", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("远程触发器服务未配置", result.GetTextContent());
    }
}
