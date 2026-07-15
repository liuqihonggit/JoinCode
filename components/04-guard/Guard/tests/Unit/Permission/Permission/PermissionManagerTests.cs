
namespace Core.Tests.Permission;

/// <summary>
/// PermissionManager 测试 - 使用共享静态配置（已通过分片锁保护）
/// </summary>
public class PermissionManagerTests : IAsyncDisposable
{
    private readonly PermissionManager _manager;

    public PermissionManagerTests()
    {
        TestConfiguration.IsFastTestMode = true;
        TestConfiguration.TimeAccelerationFactor = 10;

        _manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);
    }

    private static PermissionManager CreateManagerWithDefaultConfig(ILogger<PermissionManager>? logger = null, TimeProvider? timeProvider = null)
    {
        var config = PermissionConfig.CreateDefault();
        var configOptions = Options.Create(config);
        var checker = CreateCheckerWithConfig(config);
        return new PermissionManager(checker, configOptions, logger, timeProvider);
    }

    private static PermissionChecker CreateCheckerWithConfig(PermissionConfig config)
    {
        var middlewares = new IMiddleware<PermissionCheckContext>[]
        {
            new BypassPermissionMiddleware(),
            new AgentRestrictionMiddleware(),
            new DangerousCommandProtectionMiddleware(destructiveCommandDetector: new DestructiveCommandDetector()),
            new AutoClassifierMiddleware(),
            new ConfigGetOperationMiddleware(),
            new WebFetchPermissionMiddleware(),
            new EarlyPathDenyMiddleware(),
            new ToolListPermissionMiddleware(),
            new PathPermissionMiddleware(),
            new DangerousOperationMiddleware(),
            new PlanModeMiddleware(),
            new DefaultResultMiddleware()
        };
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(middlewares);
        return new PermissionChecker(pipeline, Options.Create(config), new IO.FileSystem.PhysicalFileSystem());
    }

    public async ValueTask DisposeAsync()
    {
        // 清理快速测试模式设置
        TestConfiguration.IsFastTestMode = false;

        await _manager.DisposeAsync().ConfigureAwait(true);
    }

    #region Permission Mode Tests

    [Theory]
    [InlineData(PermissionMode.Default)]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.BypassPermissions)]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Ask)]
    public async Task SetPermissionModeAsync_AllModes_ShouldUpdateMode(PermissionMode mode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(mode, cts.Token).ConfigureAwait(true);

        var currentMode = await _manager.GetCurrentModeAsync(cts.Token).ConfigureAwait(true);
        currentMode.Should().Be(mode);
    }

    [Fact]
    public async Task CheckPermissionAsync_DefaultMode_AutoApprovedTool_ShouldGrant()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new PermissionRequest(FileToolNameConstants.FileRead);

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermissionAsync_DefaultMode_UnknownTool_ShouldRequireConfirmation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new PermissionRequest("unknown_dangerous_tool");

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_BypassPermissionsMode_ShouldAlwaysGrant()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.BypassPermissions, cts.Token).ConfigureAwait(true);
        var request = new PermissionRequest("any_dangerous_tool");

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermissionAsync_AutoMode_ReadTool_ShouldGrant()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.Auto, cts.Token).ConfigureAwait(true);
        var request = new PermissionRequest(FileToolNameConstants.FileRead);

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_AutoMode_DangerousCommand_ShouldBeRejected()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.Auto, cts.Token).ConfigureAwait(true);
        var request = new PermissionRequest(ShellToolNameConstants.Bash, new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement("rm -rf /")
        });

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse("Auto 模式下危险命令应被拒绝而非待确认");
    }

    [Fact]
    public async Task CheckPermissionAsync_PlanMode_ReadTool_ShouldGrant()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.Plan, cts.Token).ConfigureAwait(true);
        var request = new PermissionRequest(FileToolNameConstants.FileRead);

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_AskMode_NonAutoApprovedTool_ShouldRequireConfirmation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.Ask, cts.Token).ConfigureAwait(true);
        var request = new PermissionRequest("unknown_custom_tool");

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
    }

    #endregion

    #region Caching Tests

    [Fact]
    public async Task CheckPermissionAsync_CalledTwice_ShouldUseCache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new PermissionRequest(FileToolNameConstants.FileRead);

        var result1 = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);
        var result2 = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result1.IsGranted.Should().BeTrue();
        result2.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_DifferentArguments_ShouldNotShareCache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request1 = new PermissionRequest(FileToolNameConstants.FileWrite, new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("C:\\safe\\file.txt")
        });
        var request2 = new PermissionRequest(FileToolNameConstants.FileWrite, new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("C:\\Windows\\system32\\file.txt")
        });

        var result1 = await _manager.CheckPermissionAsync(request1, cts.Token).ConfigureAwait(true);
        var result2 = await _manager.CheckPermissionAsync(request2, cts.Token).ConfigureAwait(true);

        result1.RequiresConfirmation.Should().BeTrue();
        result2.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task ClearCache_ShouldRemoveAllCachedResults()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ClearCache();

        // 缓存清除后，后续请求会重新检查权限
        var request = new PermissionRequest(FileToolNameConstants.FileRead);
        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task SetPermissionModeAsync_ShouldClearCache()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.Auto, cts.Token).ConfigureAwait(true);
        var request = new PermissionRequest("unknown_tool");
        await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        await _manager.SetPermissionModeAsync(PermissionMode.BypassPermissions, cts.Token).ConfigureAwait(true);

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);
        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public void CacheExpiration_ShouldBeConfigurable()
    {
        var customExpiration = TimeSpan.FromMinutes(10);
        _manager.CacheExpiration = customExpiration;

        _manager.CacheExpiration.Should().Be(customExpiration);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task CheckPermissionAsync_ConcurrentReads_ShouldBeThreadSafe()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tasks = new List<Task>();
        var request = new PermissionRequest(FileToolNameConstants.FileRead);

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);
                result.IsGranted.Should().BeTrue();
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Fact]
    public async Task SetPermissionModeAsync_ConcurrentModeChanges_ShouldBeThreadSafe()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var mode = i % 2 == 0 ? PermissionMode.Auto : PermissionMode.Default;
            tasks.Add(Task.Run(async () =>
            {
                await _manager.SetPermissionModeAsync(mode, cts.Token).ConfigureAwait(true);
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);

        var finalMode = await _manager.GetCurrentModeAsync(cts.Token).ConfigureAwait(true);
        Enum.IsDefined(typeof(PermissionMode), finalMode).Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_ConcurrentMixedOperations_ShouldBeThreadSafe()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tasks = new List<Task>();

        // 并发读取权限
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var request = new PermissionRequest(FileToolNameConstants.FileRead);
                var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);
                result.IsGranted.Should().BeTrue();
            }));
        }

        // 并发切换模式
        for (int i = 0; i < 10; i++)
        {
            var mode = (PermissionMode)(i % 5);
            tasks.Add(Task.Run(async () =>
            {
                await _manager.SetPermissionModeAsync(mode, cts.Token).ConfigureAwait(true);
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    #endregion

    #region Temporary Approval Tests

    [Fact]
    public async Task ApproveToolTemporarily_ThenCheck_ShouldGrant()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ApproveToolTemporarily("custom_tool", TimeSpan.FromMinutes(5));
        var request = new PermissionRequest("custom_tool");

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveToolTemporarily_Expired_ShouldRequireConfirmation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ApproveToolTemporarily("expired_tool", TimeSpan.FromMilliseconds(1));
        await TestConfiguration.DelayAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);

        var request = new PermissionRequest("expired_tool");
        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveTemporaryApproval_ThenCheck_ShouldRequireConfirmation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ApproveToolTemporarily("temp_tool", TimeSpan.FromMinutes(5));
        _manager.RemoveTemporaryApproval("temp_tool");

        var request = new PermissionRequest("temp_tool");
        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveToolTemporarily_MultipleTools_ShouldWorkIndependently()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ApproveToolTemporarily("tool1", TimeSpan.FromMinutes(5));
        _manager.ApproveToolTemporarily("tool2", TimeSpan.FromMinutes(5));

        var result1 = await _manager.CheckPermissionAsync(new PermissionRequest("tool1"), cts.Token).ConfigureAwait(true);
        var result2 = await _manager.CheckPermissionAsync(new PermissionRequest("tool2"), cts.Token).ConfigureAwait(true);
        var result3 = await _manager.CheckPermissionAsync(new PermissionRequest("tool3"), cts.Token).ConfigureAwait(true);

        result1.IsGranted.Should().BeTrue();
        result2.IsGranted.Should().BeTrue();
        result3.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveToolTemporarily_UpdateExpiration_ShouldExtendDuration()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ApproveToolTemporarily("extendable_tool", TimeSpan.FromMinutes(1));
        _manager.ApproveToolTemporarily("extendable_tool", TimeSpan.FromHours(1));

        await TestConfiguration.DelayAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(true);

        var request = new PermissionRequest("extendable_tool");
        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
    }

    #endregion

    #region Expiration Cleanup Tests

    [Fact]
    public async Task CleanupExpiredCache_ShouldRemoveExpiredItems()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.CacheExpiration = TimeSpan.FromMilliseconds(1);
        var request = new PermissionRequest(FileToolNameConstants.FileRead);
        await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        await TestConfiguration.DelayAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);
        _manager.CleanupExpiredCache();

        // 清理后应该重新检查权限
        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);
        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupExpiredCache_ShouldRemoveExpiredTemporaryApprovals()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _manager.ApproveToolTemporarily("expired_tool1", TimeSpan.FromMilliseconds(1));
        _manager.ApproveToolTemporarily("valid_tool", TimeSpan.FromHours(1));

        await TestConfiguration.DelayAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);
        _manager.CleanupExpiredCache();

        var expiredResult = await _manager.CheckPermissionAsync(new PermissionRequest("expired_tool1"), cts.Token).ConfigureAwait(true);
        var validResult = await _manager.CheckPermissionAsync(new PermissionRequest("valid_tool"), cts.Token).ConfigureAwait(true);

        expiredResult.RequiresConfirmation.Should().BeTrue();
        validResult.IsGranted.Should().BeTrue();
    }

    [Fact]
    public void CleanupExpiredCache_NoExpiredItems_ShouldNotThrow()
    {
        var act = () => _manager.CleanupExpiredCache();
        act.Should().NotThrow();
    }

    #endregion

    #region Permission Result Tests

    [Fact]
    public async Task CheckPermissionAsync_GrantedResult_ShouldHaveCorrectProperties()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new PermissionRequest(FileToolNameConstants.FileRead);

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.DenyReason.Should().BeNull();
        result.ConfirmationPrompt.Should().BeNull();
        result.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermissionAsync_PendingConfirmationResult_ShouldHaveCorrectProperties()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new PermissionRequest("unknown_custom_tool");

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationPrompt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckPermissionAsync_DeniedResult_ShouldHaveCorrectProperties()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var config = new PermissionConfig();
        config.AutoRejectedTools.Add(new ToolPermissionRule { ToolName = "blocked_tool" });
        var manager = new PermissionManager(CreateCheckerWithConfig(config), Options.Create(config), NullLogger<PermissionManager>.Instance);

        var request = new PermissionRequest("blocked_tool");
        var result = await manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
        result.DenyReason.Should().NotBeNullOrEmpty();

        await manager.DisposeAsync().ConfigureAwait(true);
    }

    #endregion

    #region Null and Edge Case Tests

    [Fact]
    public async Task CheckPermissionAsync_NullRequest_ShouldThrowArgumentNullException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = async () => await _manager.CheckPermissionAsync(null!, cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);
        await manager.DisposeAsync().ConfigureAwait(true);

        var act = async () => await manager.DisposeAsync().ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CheckPermissionAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);
        await manager.DisposeAsync().ConfigureAwait(true);

        var act = async () => await manager.CheckPermissionAsync(new PermissionRequest(FileToolNameConstants.FileRead), cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SetPermissionModeAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);
        await manager.DisposeAsync().ConfigureAwait(true);

        var act = async () => await manager.SetPermissionModeAsync(PermissionMode.Auto, cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task GetCurrentModeAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);
        await manager.DisposeAsync().ConfigureAwait(true);

        var act = async () => await manager.GetCurrentModeAsync(cts.Token).ConfigureAwait(true);
        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ApproveToolTemporarily_AfterDispose_ShouldThrowObjectDisposedException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);
        await manager.DisposeAsync().ConfigureAwait(true);

        var act = () => manager.ApproveToolTemporarily("tool", TimeSpan.FromMinutes(5));
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public async Task Constructor_WithCustomConfig_ShouldUseConfig()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var config = new PermissionConfig
        {
            AutoApprovedTools = new List<ToolPermissionRule>
            {
                new() { ToolName = "custom_auto_approved" }
            }
        };

        var manager = new PermissionManager(CreateCheckerWithConfig(config), Options.Create(config), NullLogger<PermissionManager>.Instance);

        var request = new PermissionRequest("custom_auto_approved");
        var result = await manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        result.IsGranted.Should().BeTrue();
        await manager.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CreateDefault_ShouldCreateWithDefaultConfig()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManagerWithDefaultConfig(NullLogger<PermissionManager>.Instance);

        manager.Should().NotBeNull();
        var request = new PermissionRequest(FileToolNameConstants.FileRead);
        var result = await manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);
        result.IsGranted.Should().BeTrue();

        await manager.DisposeAsync().ConfigureAwait(true);
    }

    #endregion

    #region Key Parameter Tests

    [Theory]
    [InlineData("path", "C:\\Windows\\test.txt", true)]
    [InlineData("path", "C:\\Users\\test.txt", false)]
    [InlineData("command", "rm -rf /", true)]
    [InlineData("command", "echo hello", false)]
    public async Task CheckPermissionAsync_KeyParameters_ShouldAffectResult(
        string paramName, string paramValue, bool shouldBlock)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _manager.SetPermissionModeAsync(PermissionMode.Auto, cts.Token).ConfigureAwait(true);
        var toolName = paramName == "command" ? ShellToolNameConstants.Bash : FileToolNameConstants.FileWrite;
        var request = new PermissionRequest(toolName, new Dictionary<string, JsonElement>
        {
            [paramName] = JsonSerializer.SerializeToElement(paramValue)
        });

        var result = await _manager.CheckPermissionAsync(request, cts.Token).ConfigureAwait(true);

        if (shouldBlock)
        {
            result.IsGranted.Should().BeFalse("Auto 模式下危险操作应被阻止");
        }
        else
        {
            result.IsGranted.Should().BeTrue();
        }
    }

    #endregion
}
