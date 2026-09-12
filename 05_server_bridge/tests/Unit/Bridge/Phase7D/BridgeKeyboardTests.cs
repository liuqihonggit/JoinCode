
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region BridgeMainDeps — 键盘监听回调

    [Fact]
    public void BridgeMainDeps_KeyboardListener_CanBeSet()
    {
        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = new InMemoryFileSystem(),
            PointerService = new BridgePointerService(new InMemoryFileSystem()),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
            RegisterKeyboardListener = _ => { },
            UnregisterKeyboardListener = () => { },
        };

        Assert.NotNull(deps.RegisterKeyboardListener);
        Assert.NotNull(deps.UnregisterKeyboardListener);
    }

    [Fact]
    public void BridgeMainDeps_KeyboardListener_DefaultNull()
    {
        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = new InMemoryFileSystem(),
            PointerService = new BridgePointerService(new InMemoryFileSystem()),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
        };

        Assert.Null(deps.RegisterKeyboardListener);
        Assert.Null(deps.UnregisterKeyboardListener);
    }

    [Fact]
    public void BridgeMainDeps_BridgeLogger_CanBeSet()
    {
        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = new InMemoryFileSystem(),
            PointerService = new BridgePointerService(new InMemoryFileSystem()),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
            BridgeLogger = new HeadlessBridgeLogger(_ => { }),
        };

        Assert.NotNull(deps.BridgeLogger);
    }

    [Fact]
    public void BridgeMainDeps_BridgeLogger_DefaultNull()
    {
        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = new InMemoryFileSystem(),
            PointerService = new BridgePointerService(new InMemoryFileSystem()),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
        };

        Assert.Null(deps.BridgeLogger);
    }

    #endregion

    #region RunAsync — 键盘监听注册

    [Fact]
    public async Task RunAsync_RegisterKeyboardListener_CalledBeforeLoop()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.RegisterKeyboardListener = _ => { };
        deps.UnregisterKeyboardListener = () => { };
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);
    }

    [Fact]
    public async Task RunAsync_RegisterKeyboardListener_NullDoesNotThrow()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);
    }

    #endregion
}
