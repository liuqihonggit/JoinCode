
namespace Bridge.Tests.Phase7D;

/// <summary>
/// 测试用 BridgeLogger — 记录 SetSessionTitle 调用
/// </summary>
internal sealed class TestBridgeLogger : IBridgeLogger
{
    public Action<string, string>? OnSetSessionTitle { get; init; }

    public void PrintBanner(BridgeConfig config, string environmentId) { }
    public void UpdateIdleStatus() { }
    public void UpdateReconnectingStatus(string delayStr, string elapsedStr) { }
    public void UpdateSessionStatus(string sessionId, string elapsed, BridgeSessionActivity activity, IReadOnlyList<string> trail) { }
    public void ClearStatus() { }
    public void SetRepoInfo(string repoName, string branch) { }
    public void SetDebugLogPath(string path) { }
    public void SetAttached(string sessionId) { }
    public void UpdateFailedStatus(string error) { }
    public void ToggleQr() { }
    public void UpdateSessionCount(int active, int max, BridgeSpawnMode mode) { }
    public void SetSpawnModeDisplay(BridgeSpawnMode? mode) { }
    public void AddSession(string sessionId, string url) { }
    public void UpdateSessionActivity(string sessionId, BridgeSessionActivity activity) { }
    public void SetSessionTitle(string sessionId, string title) => OnSetSessionTitle?.Invoke(sessionId, title);
    public void RemoveSession(string sessionId) { }
    public void RefreshDisplay() { }
}

/// <summary>
/// 模拟 HTTP 消息处理器 — 避免测试发起真实 HTTP 请求导致卡死
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public MockHttpMessageHandler(HttpResponseMessage response)
    {
        _response = response;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_response);
    }
}

internal static class BridgeTestHelperMethods
{
    internal static BridgeMainDeps CreateDeps(
        string? accessToken = "test-token",
        string baseUrl = "https://api.test.com",
        bool checkRemoteDialog = true,
        BridgeApiClient? apiClient = null)
    {
        var fs = new InMemoryFileSystem();
        return new BridgeMainDeps
        {
            ApiClient = apiClient ?? CreateMockApiClient(),
            Spawner = CreateMockSpawner(),
            FileSystem = fs,
            PointerService = new BridgePointerService(fs),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => accessToken,
            GetBaseUrl = () => baseUrl,
            CheckRemoteDialogAccepted = checkRemoteDialog ? (() => true) : (() => false),
        };
    }

    internal static BridgeApiClient CreateMockApiClient()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var options = new BridgeApiOptions
        {
            BaseUrl = "https://api.test.com",
            ApiKey = "test-key",
        };
        return new BridgeApiClient(httpClient, options);
    }

    internal static BridgeSubprocessSpawner CreateMockSpawner()
    {
        var processService = new Mock<IProcessService>().Object;
        return new BridgeSubprocessSpawner(new InMemoryFileSystem(), processService)
        {
            ExecPath = "echo",
        };
    }

    internal static BridgeHeadlessOpts CreateHeadlessOpts(
        string? accessToken = "test-token",
        string baseUrl = "https://api.test.com",
        BridgeSpawnMode spawnMode = BridgeSpawnMode.SameDir,
        bool? checkWorkspaceTrusted = true,
        bool? checkGitRepoExists = null,
        bool? checkWorktreeCreateHooks = null)
    {
        return new BridgeHeadlessOpts
        {
            Dir = "C:\\workspace",
            SpawnMode = spawnMode,
            Capacity = 5,
            GetAccessToken = () => accessToken,
            Log = _ => { },
            GetBaseUrl = () => baseUrl,
            CheckWorkspaceTrusted = checkWorkspaceTrusted.HasValue ? (() => checkWorkspaceTrusted.Value) : null,
            CheckGitRepoExists = checkGitRepoExists.HasValue ? ((string _) => checkGitRepoExists.Value) : null,
            CheckWorktreeCreateHooks = checkWorktreeCreateHooks.HasValue ? (() => checkWorktreeCreateHooks.Value) : null,
        };
    }
}
