namespace Core.Tests.Permission;

/// <summary>
/// PermissionManager 临时批准（ApproveToolTemporarily）接口契约测试
/// 验证 GUI/CLI 权限确认闭环所需的接口能力：临时批准后 CheckPermissionAsync 立即 Granted
/// </summary>
public sealed class PermissionManagerTemporaryApprovalTests
{
    private static IToolPermissionManager CreatePermissionManager(FakeTimeProvider? timeProvider = null)
    {
        var config = Options.Create(PermissionConfig.CreateDefault());
        var checker = new PermissionChecker(
            new MiddlewarePipeline<PermissionCheckContext>([]),
            config,
            new InMemoryFileSystem());
        return new PermissionManager(
            checker,
            config,
            logger: null,
            timeProvider: timeProvider);
    }

    [Fact]
    public async Task ApproveToolTemporarily_未批准时返回待确认()
    {
        var manager = CreatePermissionManager();
        var request = new PermissionRequest(ShellToolNameConstants.Bash);

        var result = await manager.CheckPermissionAsync(request);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveToolTemporarily_批准后立即授权()
    {
        var manager = CreatePermissionManager();
        var request = new PermissionRequest(ShellToolNameConstants.Bash);

        manager.ApproveToolTemporarily(ShellToolNameConstants.Bash, TimeSpan.FromMinutes(5));

        var result = await manager.CheckPermissionAsync(request);

        result.IsGranted.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task ApproveToolTemporarily_过期后恢复待确认()
    {
        var timeProvider = new FakeTimeProvider();
        var manager = CreatePermissionManager(timeProvider) as PermissionManager;
        var request = new PermissionRequest(ShellToolNameConstants.Bash);

        manager!.ApproveToolTemporarily(ShellToolNameConstants.Bash, TimeSpan.FromMinutes(1));
        var granted = await manager.CheckPermissionAsync(request);
        granted.IsGranted.Should().BeTrue();

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        manager.CleanupExpiredCache();

        var result = await manager.CheckPermissionAsync(request);

        result.IsGranted.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
    }
}
