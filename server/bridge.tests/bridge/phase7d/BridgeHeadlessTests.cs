
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region BuildSdkUrl / BuildCCRv2SdkUrl — URL 构建测试

    [Fact]
    public void BuildSdkUrl_ProducesWssUrl()
    {
        var url = BridgeWorkSecretDecoder.BuildSdkUrl("https://api.test.com", "session-1");
        Assert.Equal("wss://api.test.com/v1/session_ingress/ws/session-1", url);
    }

    [Fact]
    public void BuildSdkUrl_Localhost_ProducesWsV2Url()
    {
        var url = BridgeWorkSecretDecoder.BuildSdkUrl("http://localhost:8080", "session-2");
        Assert.Equal("ws://localhost:8080/v2/session_ingress/ws/session-2", url);
    }

    [Fact]
    public void BuildCCRv2SdkUrl_ProducesHttpUrl()
    {
        var url = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl("https://api.test.com", "session-3");
        Assert.Equal("https://api.test.com/v1/code/sessions/session-3", url);
    }

    [Fact]
    public void BuildCCRv2SdkUrl_TrailingSlash_Stripped()
    {
        var url = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl("https://api.test.com/", "session-4");
        Assert.Equal("https://api.test.com/v1/code/sessions/session-4", url);
    }

    #endregion

    #region BridgeHeadlessPermanentError — 永久性错误

    [Fact]
    public void BridgeHeadlessPermanentError_CanBeConstructed()
    {
        var error = new BridgeHeadlessPermanentError("workspace not trusted");
        Assert.Equal("workspace not trusted", error.Message);
        Assert.IsAssignableFrom<Exception>(error);
    }

    [Fact]
    public void BridgeHeadlessPermanentError_IsNotBridgeFatalError()
    {
        var error = new BridgeHeadlessPermanentError("test");
        Assert.IsNotType<BridgeFatalError>(error);
    }

    #endregion

    #region BridgeHeadlessOpts — 选项模型

    [Fact]
    public void BridgeHeadlessOpts_RequiredFields()
    {
        var opts = new BridgeHeadlessOpts
        {
            Dir = "C:\\workspace",
            SpawnMode = BridgeSpawnMode.SameDir,
            Capacity = 5,
            GetAccessToken = () => "token-123",
            Log = _ => { },
            GetBaseUrl = () => "https://api.test.com",
        };

        Assert.Equal("C:\\workspace", opts.Dir);
        Assert.Equal(BridgeSpawnMode.SameDir, opts.SpawnMode);
        Assert.Equal(5, opts.Capacity);
        Assert.Equal("token-123", opts.GetAccessToken());
        Assert.Equal("https://api.test.com", opts.GetBaseUrl());
    }

    [Fact]
    public void BridgeHeadlessOpts_OptionalFields_Defaults()
    {
        var opts = new BridgeHeadlessOpts
        {
            Dir = "C:\\workspace",
            SpawnMode = BridgeSpawnMode.Worktree,
            Capacity = 3,
            GetAccessToken = () => "token",
            Log = _ => { },
            GetBaseUrl = () => "https://api.test.com",
        };

        Assert.Null(opts.Name);
        Assert.Null(opts.PermissionMode);
        Assert.False(opts.Sandbox);
        Assert.Equal(0, opts.SessionTimeoutMs);
        Assert.False(opts.CreateSessionOnStart);
        Assert.Null(opts.OnAuth401);
        Assert.Null(opts.CheckWorkspaceTrusted);
        Assert.Null(opts.CheckGitRepoExists);
        Assert.Null(opts.CheckWorktreeCreateHooks);
    }

    [Fact]
    public void BridgeHeadlessOpts_AllFields()
    {
        var opts = new BridgeHeadlessOpts
        {
            Dir = "C:\\workspace",
            Name = "test-session",
            SpawnMode = BridgeSpawnMode.SameDir,
            Capacity = 10,
            PermissionMode = "auto-accept",
            Sandbox = true,
            SessionTimeoutMs = 3600000,
            CreateSessionOnStart = true,
            GetAccessToken = () => "token-abc",
            OnAuth401 = token => Task.FromResult(true),
            Log = msg => { },
            GetBaseUrl = () => "https://api.test.com",
            CheckWorkspaceTrusted = () => true,
            CheckGitRepoExists = dir => true,
            CheckWorktreeCreateHooks = () => false,
        };

        Assert.Equal("test-session", opts.Name);
        Assert.Equal("auto-accept", opts.PermissionMode);
        Assert.True(opts.Sandbox);
        Assert.Equal(3600000, opts.SessionTimeoutMs);
        Assert.True(opts.CreateSessionOnStart);
        Assert.NotNull(opts.OnAuth401);
        Assert.True(opts.CheckWorkspaceTrusted!());
        Assert.True(opts.CheckGitRepoExists!("C:\\workspace"));
        Assert.False(opts.CheckWorktreeCreateHooks!());
    }

    #endregion

    #region RunHeadlessAsync — 永久性验证

    [Fact]
    public async Task RunHeadlessAsync_WorkspaceNotTrusted_ThrowsPermanentError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(checkWorkspaceTrusted: false);

        var ex = await Assert.ThrowsAsync<BridgeHeadlessPermanentError>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.Contains("not trusted", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_NoAccessToken_ThrowsTransientError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(accessToken: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.Contains("access token", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_HttpUrl_ThrowsPermanentError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(baseUrl: "http://evil.example.com");

        var ex = await Assert.ThrowsAsync<BridgeHeadlessPermanentError>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.Contains("HTTP", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_WorktreeMode_NoGitNoHooks_ThrowsPermanentError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(
            spawnMode: BridgeSpawnMode.Worktree,
            checkGitRepoExists: false,
            checkWorktreeCreateHooks: false);

        var ex = await Assert.ThrowsAsync<BridgeHeadlessPermanentError>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.Contains("Worktree mode requires", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_WorktreeMode_HasGitRepo_PassesValidation()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(
            spawnMode: BridgeSpawnMode.Worktree,
            checkGitRepoExists: true,
            checkWorktreeCreateHooks: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.DoesNotContain("Worktree mode requires", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_WorktreeMode_HasHooks_PassesValidation()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(
            spawnMode: BridgeSpawnMode.Worktree,
            checkGitRepoExists: false,
            checkWorktreeCreateHooks: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.DoesNotContain("Worktree mode requires", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_LocalhostHttp_PassesHttpsCheck()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        var opts = BridgeTestHelperMethods.CreateHeadlessOpts(baseUrl: "http://localhost:8080");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => main.RunHeadlessAsync(opts)).ConfigureAwait(true);
        Assert.DoesNotContain("HTTP", ex.Message);
    }

    [Fact]
    public async Task RunHeadlessAsync_NullOpts_ThrowsArgumentNullException()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        await using var main = new BridgeMain(deps);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => main.RunHeadlessAsync(null!)).ConfigureAwait(true);
    }

    #endregion
}
