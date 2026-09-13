
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region BridgeMainDeps — 交互对话框回调

    [Fact]
    public void BridgeMainDeps_RemoteControlDialog_CanBeSet()
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
            RemoteControlDialog = _ => Task.FromResult(true),
            MarkRemoteDialogSeen = () => { },
        };

        Assert.NotNull(deps.RemoteControlDialog);
        Assert.NotNull(deps.MarkRemoteDialogSeen);
    }

    [Fact]
    public void BridgeMainDeps_SpawnModeDialog_CanBeSet()
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
            SpawnModeDialog = _ => Task.FromResult(BridgeSpawnMode.Worktree),
            SaveSpawnModePreference = _ => { },
            GetSavedSpawnMode = () => null,
            IsWorktreeAvailable = () => true,
        };

        Assert.NotNull(deps.SpawnModeDialog);
        Assert.NotNull(deps.SaveSpawnModePreference);
        Assert.NotNull(deps.GetSavedSpawnMode);
        Assert.NotNull(deps.IsWorktreeAvailable);
    }

    [Fact]
    public void BridgeMainDeps_DialogCallbacks_DefaultNull()
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

        Assert.Null(deps.RemoteControlDialog);
        Assert.Null(deps.MarkRemoteDialogSeen);
        Assert.Null(deps.SpawnModeDialog);
        Assert.Null(deps.SaveSpawnModePreference);
        Assert.Null(deps.GetSavedSpawnMode);
        Assert.Null(deps.IsWorktreeAvailable);
    }

    #endregion

    #region RunAsync — 远程确认对话框

    [Fact]
    public async Task RunAsync_RemoteDialogNotSeen_NoDialogCallback_ReturnsError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(checkRemoteDialog: false);
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.True(result.HasError);
        Assert.Contains("not accepted", result.Error!);
    }

    [Fact]
    public async Task RunAsync_RemoteDialogNotSeen_DialogDeclined_ReturnsError()
    {
        var deps = BridgeTestHelperMethods.CreateDeps(checkRemoteDialog: false);
        deps.RemoteControlDialog = _ => Task.FromResult(false);
        deps.MarkRemoteDialogSeen = () => { };
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.True(result.HasError);
        Assert.Contains("not accepted", result.Error!);
    }

    [Fact]
    public async Task RunAsync_RemoteDialogNotSeen_DialogAccepted_MarkSeenCalled()
    {
        var markSeenCalled = false;
        var deps = BridgeTestHelperMethods.CreateDeps(checkRemoteDialog: false);
        deps.RemoteControlDialog = _ => Task.FromResult(true);
        deps.MarkRemoteDialogSeen = () => markSeenCalled = true;
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.True(markSeenCalled);
        Assert.DoesNotContain("not accepted", result.Error ?? "");
    }

    [Fact]
    public async Task RunAsync_RemoteDialogSeen_SkipsDialog()
    {
        var dialogCalled = false;
        var deps = BridgeTestHelperMethods.CreateDeps(checkRemoteDialog: true);
        deps.RemoteControlDialog = _ => { dialogCalled = true; return Task.FromResult(true); };
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.False(dialogCalled);
    }

    #endregion

    #region RunAsync — Spawn 模式选择对话框

    [Fact]
    public async Task RunAsync_SpawnModeDialog_SavedPreferenceUsed()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.GetSavedSpawnMode = () => BridgeSpawnMode.Worktree;
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.DoesNotContain("spawn", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_SpawnModeDialog_ExplicitArgOverridesSaved()
    {
        var savedModeUsed = false;
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.GetSavedSpawnMode = () => { savedModeUsed = true; return BridgeSpawnMode.Worktree; };
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs { SpawnMode = BridgeSpawnMode.SameDir }).ConfigureAwait(true);

        Assert.False(savedModeUsed);
    }

    [Fact]
    public async Task RunAsync_SpawnModeDialog_NotCalledWhenResuming()
    {
        var dialogCalled = false;
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.SpawnModeDialog = _ => { dialogCalled = true; return Task.FromResult(BridgeSpawnMode.SameDir); };
        deps.IsWorktreeAvailable = () => true;
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs { SessionId = "existing-session" }).ConfigureAwait(true);

        Assert.False(dialogCalled);
    }

    [Fact]
    public async Task RunAsync_SpawnModeDialog_WorktreeNotAvailable_SkipsDialog()
    {
        var dialogCalled = false;
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.SpawnModeDialog = _ => { dialogCalled = true; return Task.FromResult(BridgeSpawnMode.SameDir); };
        deps.IsWorktreeAvailable = () => false;
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.False(dialogCalled);
    }

    [Fact]
    public async Task RunAsync_SpawnModeDialog_ChooseWorktree_SavesPreference()
    {
        BridgeSpawnMode? savedMode = null;
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.SpawnModeDialog = _ => Task.FromResult(BridgeSpawnMode.Worktree);
        deps.SaveSpawnModePreference = mode => savedMode = mode;
        deps.IsWorktreeAvailable = () => true;
        deps.IsMultiSessionSpawnEnabled = () => true;
        await using var main = new BridgeMain(deps);

        var result = await main.RunAsync(new BridgeMainArgs()).ConfigureAwait(true);

        Assert.Equal(BridgeSpawnMode.Worktree, savedMode);
    }

    #endregion
}
