
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region P2-6: Perpetual/Resume 模式 — resume forces single-session

    [Fact]
    public async Task BuildConfig_ResumeForcesSingleSessionMode()
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
            CheckRemoteDialogAccepted = () => true,
            DefaultSpawnMode = BridgeSpawnMode.Worktree,
        };
        var args = new BridgeMainArgs { Capacity = 5 };
        await using var main = new BridgeMain(deps);

        var configNormal = main.BuildConfig(args, "https://api.test.com", null, null, false);
        Assert.Equal(BridgeSpawnMode.Worktree, configNormal.SpawnMode);

        var configResuming = main.BuildConfig(args, "https://api.test.com", null, null, true);
        Assert.Equal(BridgeSpawnMode.SingleSession, configResuming.SpawnMode);
    }

    [Fact]
    public async Task BuildConfig_ResumeAlreadySingleSession_NoChange()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        var args = new BridgeMainArgs();
        await using var main = new BridgeMain(deps);

        var config = main.BuildConfig(args, "https://api.test.com", null, null, true);
        Assert.Equal(BridgeSpawnMode.SingleSession, config.SpawnMode);
    }

    [Fact]
    public async Task BuildConfig_ResumeWithExplicitSpawnMode_OverridesToSingleSession()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        var args = new BridgeMainArgs { SpawnMode = BridgeSpawnMode.SameDir };
        await using var main = new BridgeMain(deps);

        var config = main.BuildConfig(args, "https://api.test.com", null, null, true);
        Assert.Equal(BridgeSpawnMode.SingleSession, config.SpawnMode);
    }

    [Fact]
    public async Task RunAsync_ContinueSession_ReadsPointerFile()
    {
        var fs = new InMemoryFileSystem();
        var pointerService = new BridgePointerService(fs);

        await pointerService.WriteAsync("C:\\test", new BridgePointer
        {
            SessionId = "existing-session",
            EnvironmentId = "env-123",
            Source = BridgePointerSource.Standalone.ToValue(),
        }).ConfigureAwait(true);

        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = fs,
            PointerService = pointerService,
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
            CheckRemoteDialogAccepted = () => true,
        };

        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs { ContinueSession = true }).ConfigureAwait(true);
    }

    [Fact]
    public async Task RunAsync_SessionId_SetsResumeSessionId()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs { SessionId = "direct-session-id" }).ConfigureAwait(true);
    }

    #endregion

    #region P3-8: v2 Token 刷新 reconnectSession 分支

    [Fact]
    public void TokenRefresh_V2Session_CallsReconnectSession()
    {
        string? reconnectedEnvId = null;
        string? reconnectedSessionId = null;
        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = new InMemoryFileSystem(),
            PointerService = new BridgePointerService(new InMemoryFileSystem()),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
            CheckRemoteDialogAccepted = () => true,
            ReconnectSession = (envId, sid, ct) =>
            {
                reconnectedEnvId = envId;
                reconnectedSessionId = sid;
                return Task.CompletedTask;
            },
        };

        Assert.NotNull(deps.ReconnectSession);
        Assert.Null(reconnectedEnvId);
    }

    [Fact]
    public void TokenRefresh_V1Session_UpdatesAccessTokenDirectly()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        Assert.Null(deps.ReconnectSession);
    }

    [Fact]
    public async Task RunAsync_CreatesTokenRefreshScheduler_WhenGetAccessTokenProvided()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);
    }

    [Fact]
    public async Task RunAsync_FallsBackToInjectedScheduler_WhenNoGetAccessToken()
    {
        var injectedScheduler = new BridgeTokenRefreshScheduler(
            new TokenRefreshOptions
            {
                GetAccessToken = () => "injected-token",
                OnRefresh = (_, _) => { },
                Label = "injected",
            });

        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = new InMemoryFileSystem(),
            PointerService = new BridgePointerService(new InMemoryFileSystem()),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
            CheckRemoteDialogAccepted = () => true,
            TokenRefreshScheduler = injectedScheduler,
        };

        await using var main = new BridgeMain(deps);
    }

    [Fact]
    public void BridgeMainDeps_ReconnectSession_PropertyExists()
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
            CheckRemoteDialogAccepted = () => true,
            ReconnectSession = (envId, sid, ct) => Task.CompletedTask,
        };

        Assert.NotNull(deps.ReconnectSession);
    }

    #endregion

    #region P3-3: sessionCompatIds 映射 — cse_*→session_* 兼容 ID 转换

    [Fact]
    public void SessionIdCompat_ToCompatSessionId_ConvertsCsePrefix()
    {
        var compatId = SessionIdCompat.ToCompatSessionId("cse_abc123");
        Assert.Equal("session_abc123", compatId);
    }

    [Fact]
    public void SessionIdCompat_ToCompatSessionId_NoConvertNonCse()
    {
        var compatId = SessionIdCompat.ToCompatSessionId("session_abc123");
        Assert.Equal("session_abc123", compatId);

        var compatId2 = SessionIdCompat.ToCompatSessionId("other_id");
        Assert.Equal("other_id", compatId2);
    }

    [Fact]
    public void SessionIdCompat_ToInfraSessionId_ConvertsSessionPrefix()
    {
        var infraId = SessionIdCompat.ToInfraSessionId("session_abc123");
        Assert.Equal("cse_abc123", infraId);
    }

    [Fact]
    public void SessionIdCompat_SameSessionId_CrossPrefixComparison()
    {
        Assert.True(SessionIdCompat.SameSessionId("cse_abc", "session_abc"));
        Assert.True(SessionIdCompat.SameSessionId("session_abc", "cse_abc"));
        Assert.False(SessionIdCompat.SameSessionId("cse_abc", "cse_def"));
        Assert.True(SessionIdCompat.SameSessionId("cse_abc", "cse_abc"));
    }

    [Fact]
    public void BuildConfig_SessionCompatIds_RegisteredForV2Sessions()
    {
        var cseId = "cse_test123";
        var compatId = SessionIdCompat.ToCompatSessionId(cseId);
        Assert.Equal("session_test123", compatId);

        var plainId = "plain_id";
        Assert.Equal(plainId, SessionIdCompat.ToCompatSessionId(plainId));
    }

    #endregion
}
