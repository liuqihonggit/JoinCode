
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region BridgeMainResult

    [Fact]
    public void BridgeMainResult_Defaults()
    {
        var result = new BridgeMainResult();
        Assert.False(result.Completed);
        Assert.Null(result.HelpText);
        Assert.Null(result.Error);
        Assert.False(result.HasError);
    }

    [Fact]
    public void BridgeMainResult_WithError()
    {
        var result = new BridgeMainResult { Error = "something failed" };
        Assert.True(result.HasError);
    }

    [Fact]
    public void BridgeMainResult_Completed()
    {
        var result = new BridgeMainResult { Completed = true };
        Assert.True(result.Completed);
        Assert.False(result.HasError);
    }

    #endregion

    #region BridgeMainDeps — 必填字段验证

    [Fact]
    public void BridgeMainDeps_AllRequiredFields_Set()
    {
        var fs = new InMemoryFileSystem();
        var deps = new BridgeMainDeps
        {
            ApiClient = BridgeTestHelperMethods.CreateMockApiClient(),
            Spawner = BridgeTestHelperMethods.CreateMockSpawner(),
            FileSystem = fs,
            PointerService = new BridgePointerService(fs),
            WorkingDirectory = "C:\\test",
            GetAccessToken = () => "test-token",
            GetBaseUrl = () => "https://api.test.com",
        };

        Assert.NotNull(deps.ApiClient);
        Assert.NotNull(deps.Spawner);
        Assert.NotNull(deps.FileSystem);
        Assert.NotNull(deps.PointerService);
        Assert.Equal("C:\\test", deps.WorkingDirectory);
        Assert.Equal("test-token", deps.GetAccessToken());
        Assert.Equal("https://api.test.com", deps.GetBaseUrl());
    }

    [Fact]
    public void BridgeMainDeps_OptionalFields_DefaultNull()
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

        Assert.Null(deps.CheckRemoteDialogAccepted);
        Assert.Null(deps.RemoteControlDialog);
        Assert.Null(deps.MarkRemoteDialogSeen);
        Assert.Null(deps.SpawnModeDialog);
        Assert.Null(deps.SaveSpawnModePreference);
        Assert.Null(deps.GetSavedSpawnMode);
        Assert.Null(deps.IsWorktreeAvailable);
        Assert.Null(deps.DefaultSpawnMode);
        Assert.Null(deps.GitBranch);
        Assert.Null(deps.GitRepoUrl);
        Assert.Null(deps.WorktreeDir);
        Assert.Null(deps.PermissionMode);
        Assert.Null(deps.PollConfig);
        Assert.Null(deps.CapacityWake);
        Assert.Null(deps.TokenRefreshScheduler);
        Assert.Null(deps.ArchiveSession);
        Assert.Null(deps.CreateBridgeSession);
    }

    [Fact]
    public void BridgeMainDeps_CreateBridgeSession_CanBeSet()
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
            CreateBridgeSession = (_, _) => Task.FromResult<string?>("session-123"),
        };

        Assert.NotNull(deps.CreateBridgeSession);
    }

    #endregion

    #region BridgeMainPollConfig

    [Fact]
    public void BridgeMainPollConfig_Defaults()
    {
        var config = new BridgeMainPollConfig();
        Assert.Equal(5000, config.PollIntervalMs);
        Assert.Equal(30000, config.HeartbeatIntervalMs);
        Assert.Equal(30000, config.ShutdownGraceMs);
    }

    #endregion

    #region BridgeMainArgs — 属性

    [Fact]
    public void BridgeMainArgs_Defaults()
    {
        var args = new BridgeMainArgs();
        Assert.False(args.Verbose);
        Assert.False(args.Sandbox);
        Assert.Null(args.DebugFile);
        Assert.Null(args.SessionTimeoutMs);
        Assert.Null(args.PermissionMode);
        Assert.Null(args.Name);
        Assert.Null(args.SpawnMode);
        Assert.Null(args.Capacity);
        Assert.Null(args.CreateSessionInDir);
        Assert.Null(args.SessionId);
        Assert.False(args.ContinueSession);
        Assert.False(args.Help);
        Assert.Null(args.Error);
        Assert.False(args.HasError);
    }

    [Fact]
    public void BridgeMainArgs_HasError_WithNullError()
    {
        var args = new BridgeMainArgs { Error = null };
        Assert.False(args.HasError);
    }

    [Fact]
    public void BridgeMainArgs_HasError_WithEmptyError()
    {
        var args = new BridgeMainArgs { Error = "" };
        Assert.False(args.HasError);
    }

    [Fact]
    public void BridgeMainArgs_HasError_WithError()
    {
        var args = new BridgeMainArgs { Error = "something" };
        Assert.True(args.HasError);
    }

    #endregion
}
