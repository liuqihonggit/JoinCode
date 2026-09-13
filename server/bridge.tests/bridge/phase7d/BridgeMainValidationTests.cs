
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region RunAsync — 参数验证

    [Fact]
    public async Task RunAsync_Help_ReturnsHelpText()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs { Help = true }).ConfigureAwait(true);

        Assert.NotNull(result.HelpText);
        Assert.Contains("Usage:", result.HelpText);
        Assert.False(result.Completed);
    }

    [Fact]
    public async Task RunAsync_ArgsError_ReturnsError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs { Error = "test error" }).ConfigureAwait(true);

        Assert.True(result.HasError);
        Assert.Equal("test error", result.Error);
    }

    [Fact]
    public async Task RunAsync_NoAccessToken_ReturnsError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(accessToken: null);
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.True(result.HasError);
        Assert.Contains("access token", result.Error!);
    }

    [Fact]
    public async Task RunAsync_RemoteDialogNotAccepted_ReturnsError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(checkRemoteDialog: false);
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.True(result.HasError);
        Assert.Contains("not accepted", result.Error!);
    }

    [Fact]
    public async Task RunAsync_NonHttpsUrl_ReturnsError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(baseUrl: "http://evil.example.com");
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.True(result.HasError);
        Assert.Contains("HTTPS", result.Error!);
    }

    [Fact]
    public async Task RunAsync_LocalhostHttp_PassesHttpsCheck()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(baseUrl: "http://localhost:8080");
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.False(result.Error?.Contains("HTTPS") == true);
    }

    [Fact]
    public async Task RunAsync_127001Http_PassesHttpsCheck()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(baseUrl: "http://127.0.0.1:3000");
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.False(result.Error?.Contains("HTTPS") == true);
    }

    #endregion
}
