namespace Core.Tests.Permission;

/// <summary>
/// PermissionManager 确定性测试 — mock PermissionChecker 管道 / FakeTimeProvider 消除时序依赖
/// <para>测试危险规则剥离/恢复、模式切换、临时批准、缓存清理。不依赖时序。</para>
/// <para>已有 PermissionManagerTemporaryApprovalTests 覆盖临时批准接口契约,本类补充其余方法。</para>
/// </summary>
public sealed class PermissionManagerTest {

    #region 辅助构造

    private static (PermissionManager manager, PermissionConfig config) CreateManager(
        PermissionConfig? config = null,
        FakeTimeProvider? timeProvider = null) {
        config ??= PermissionConfig.CreateDefault();
        var configOptions = Options.Create(config);
        var checker = new PermissionChecker(
            new MiddlewarePipeline<PermissionCheckContext>([]),
            configOptions,
            new InMemoryFileSystem());
        var manager = new PermissionManager(
            checker,
            configOptions,
            logger: null,
            timeProvider: timeProvider);
        return (manager, config);
    }

    private static PermissionConfig CreateConfigWithDangerousTools() {
        var config = PermissionConfig.CreateDefault();
        config.AutoApprovedTools = config.AutoApprovedTools
            .SetItem(ShellToolNameEnumConstants.Bash, new ToolPermissionRule { ToolName = ShellToolNameEnumConstants.Bash, Description = "Bash" })
            .SetItem(FileToolNameEnumConstants.FileEdit, new ToolPermissionRule { ToolName = FileToolNameEnumConstants.FileEdit, Description = "Edit" })
            .SetItem(FileToolNameEnumConstants.FileWrite, new ToolPermissionRule { ToolName = FileToolNameEnumConstants.FileWrite, Description = "Write" })
            .SetItem(NotebookToolNameEnumConstants.NotebookEdit, new ToolPermissionRule { ToolName = NotebookToolNameEnumConstants.NotebookEdit, Description = "Notebook" });
        return config;
    }

    #endregion

    #region StripDangerousRulesAsync / RestoreDangerousRulesAsync

    [Fact]
    public async Task StripDangerousRulesAsync_含危险工具_剥离并返回数量() {
        var config = CreateConfigWithDangerousTools();
        var (manager, _) = CreateManager(config);

        var count = await manager.StripDangerousRulesAsync();

        count.Should().Be(4);
        config.AutoApprovedTools.Should().NotContainKey(ShellToolNameEnumConstants.Bash);
        config.AutoApprovedTools.Should().NotContainKey(FileToolNameEnumConstants.FileEdit);
        config.AutoApprovedTools.Should().NotContainKey(FileToolNameEnumConstants.FileWrite);
        config.AutoApprovedTools.Should().NotContainKey(NotebookToolNameEnumConstants.NotebookEdit);
    }

    [Fact]
    public async Task StripDangerousRulesAsync_无危险工具_返回零() {
        var (manager, _) = CreateManager(PermissionConfig.CreateDefault());

        var count = await manager.StripDangerousRulesAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task StripDangerousRulesAsync_仅含Bash_剥离1条() {
        var config = PermissionConfig.CreateDefault();
        config.AutoApprovedTools = config.AutoApprovedTools
            .SetItem(ShellToolNameEnumConstants.Bash, new ToolPermissionRule { ToolName = ShellToolNameEnumConstants.Bash });
        var (manager, _) = CreateManager(config);

        var count = await manager.StripDangerousRulesAsync();

        count.Should().Be(1);
        config.AutoApprovedTools.Should().NotContainKey(ShellToolNameEnumConstants.Bash);
    }

    [Fact]
    public async Task RestoreDangerousRulesAsync_剥离后恢复_规则回归() {
        var config = CreateConfigWithDangerousTools();
        var (manager, _) = CreateManager(config);

        var strippedCount = await manager.StripDangerousRulesAsync();
        await manager.RestoreDangerousRulesAsync(strippedCount);

        config.AutoApprovedTools.Should().ContainKey(ShellToolNameEnumConstants.Bash);
        config.AutoApprovedTools.Should().ContainKey(FileToolNameEnumConstants.FileEdit);
        config.AutoApprovedTools.Should().ContainKey(FileToolNameEnumConstants.FileWrite);
        config.AutoApprovedTools.Should().ContainKey(NotebookToolNameEnumConstants.NotebookEdit);
    }

    [Fact]
    public async Task RestoreDangerousRulesAsync_未剥离_无操作() {
        var (manager, config) = CreateManager(PermissionConfig.CreateDefault());
        var originalCount = config.AutoApprovedTools.Count;

        await manager.RestoreDangerousRulesAsync(0);

        config.AutoApprovedTools.Count.Should().Be(originalCount);
    }

    [Fact]
    public async Task StripThenRestore_往返保持配置不变() {
        var config = CreateConfigWithDangerousTools();
        var originalCount = config.AutoApprovedTools.Count;
        var (manager, _) = CreateManager(config);

        var stripped = await manager.StripDangerousRulesAsync();
        await manager.RestoreDangerousRulesAsync(stripped);

        config.AutoApprovedTools.Count.Should().Be(originalCount);
    }

    #endregion

    #region SetPermissionModeAsync / GetCurrentModeAsync

    [Fact]
    public async Task SetPermissionModeAsync_切换到Plan_GetCurrentMode返回Plan() {
        var (manager, _) = CreateManager();

        await manager.SetPermissionModeAsync(PermissionMode.Plan);
        var mode = await manager.GetCurrentModeAsync();

        mode.Should().Be(PermissionMode.Plan);
    }

    [Fact]
    public async Task SetPermissionModeAsync_切换到Bypass_GetCurrentMode返回Bypass() {
        var (manager, _) = CreateManager();

        await manager.SetPermissionModeAsync(PermissionMode.Bypass);
        var mode = await manager.GetCurrentModeAsync();

        mode.Should().Be(PermissionMode.Bypass);
    }

    [Fact]
    public async Task SetPermissionModeAsync_切换到Ask_GetCurrentMode返回Ask() {
        var (manager, _) = CreateManager();

        await manager.SetPermissionModeAsync(PermissionMode.Ask);
        var mode = await manager.GetCurrentModeAsync();

        mode.Should().Be(PermissionMode.Ask);
    }

    [Fact]
    public async Task SetPermissionModeAsync_切换到Unattended_GetCurrentMode返回Unattended() {
        var (manager, _) = CreateManager();

        await manager.SetPermissionModeAsync(PermissionMode.Unattended);
        var mode = await manager.GetCurrentModeAsync();

        mode.Should().Be(PermissionMode.Unattended);
    }

    [Fact]
    public async Task GetCurrentModeAsync_默认为Auto或环境变量() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, null);
        try {
            var (manager, _) = CreateManager();
            var mode = await manager.GetCurrentModeAsync();
            mode.Should().Be(PermissionMode.Auto);
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    #endregion

    #region AddAllowedPromptAsync

    [Fact]
    public async Task AddAllowedPromptAsync_添加后Bash临时批准() {
        var (manager, _) = CreateManager();

        await manager.AddAllowedPromptAsync("run tests");

        // 添加后 Bash 工具应被临时批准,CheckPermissionAsync 返回 Granted
        var result = await manager.CheckPermissionAsync(new PermissionRequest(ShellToolNameEnumConstants.Bash));
        result.IsGranted.Should().BeTrue();
    }

    #endregion

    #region RemoveTemporaryApproval

    [Fact]
    public async Task RemoveTemporaryApproval_移除后不再自动批准() {
        var (manager, _) = CreateManager();

        manager.ApproveToolTemporarily(ShellToolNameEnumConstants.Bash, TimeSpan.FromMinutes(5));
        var granted = await manager.CheckPermissionAsync(new PermissionRequest(ShellToolNameEnumConstants.Bash));
        granted.IsGranted.Should().BeTrue();

        manager.RemoveTemporaryApproval(ShellToolNameEnumConstants.Bash);
        var result = await manager.CheckPermissionAsync(new PermissionRequest(ShellToolNameEnumConstants.Bash));
        result.IsGranted.Should().BeFalse();
    }

    #endregion

    #region ApproveLevelTemporarily

    [Fact]
    public void ApproveLevelTemporarily_Execution_委托给Checker() {
        var (manager, _) = CreateManager();

        manager.ApproveLevelTemporarily(CommandDangerLevel.Execution);

        // 不抛异常即通过;级别批准由 PermissionChecker 内部管理
    }

    [Fact]
    public void ApproveLevelTemporarily_Safe_无效果() {
        var (manager, _) = CreateManager();

        manager.ApproveLevelTemporarily(CommandDangerLevel.Safe);

        // Safe 无需批准,不抛异常即通过
    }

    #endregion

    #region ClearCacheAsync

    [Fact]
    public async Task ClearCacheAsync_不抛异常() {
        var (manager, _) = CreateManager();

        var act = () => manager.ClearCacheAsync();

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region CleanupExpiredCache

    [Fact]
    public void CleanupExpiredCache_无临时批准_不抛异常() {
        var (manager, _) = CreateManager();

        var act = () => manager.CleanupExpiredCache();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task CleanupExpiredCache_过期后清理临时批准() {
        var timeProvider = new FakeTimeProvider();
        var (manager, _) = CreateManager(timeProvider: timeProvider);

        manager.ApproveToolTemporarily(ShellToolNameEnumConstants.Bash, TimeSpan.FromMinutes(1));
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        manager.CleanupExpiredCache();

        // 清理后 Bash 不再被临时批准
        var result = await manager.CheckPermissionAsync(new PermissionRequest(ShellToolNameEnumConstants.Bash));
        result.IsGranted.Should().BeFalse();
    }

    #endregion

    #region Dispose

    [Fact]
    public async Task DisposeAsync_多次调用_不抛异常() {
        var (manager, _) = CreateManager();

        await manager.DisposeAsync();
        var act = () => manager.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Dispose后_CheckPermissionAsync_抛ObjectDisposedException() {
        var (manager, _) = CreateManager();

        await manager.DisposeAsync();

        var act = () => manager.CheckPermissionAsync(new PermissionRequest("Bash"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose后_SetPermissionModeAsync_抛ObjectDisposedException() {
        var (manager, _) = CreateManager();

        await manager.DisposeAsync();

        var act = () => manager.SetPermissionModeAsync(PermissionMode.Plan);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion
}
