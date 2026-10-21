namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// GUI 权限确认处理器测试 — 引擎同步确认点转接宿主异步委托：
/// 未注入委托 / 取消 / 拒绝 → fail-closed；允许 → 临时批准工具 + 会话级批准危险等级（与 CLI 同源）。
/// </summary>
public class GuiPermissionConfirmationHandlerTests {
    [Fact]
    public async Task ConfirmAsync_NoHandler_Denies() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);

        var action = await handler.ConfirmAsync("bash", "[红灯ask] 是否允许执行 bash？");

        action.Should().Be(PermissionConfirmAction.Deny, "未注入委托时必须 fail-closed");
    }

    [Fact]
    public async Task ConfirmAsync_AlreadyCanceled_DeniesWithoutInvokingHandler() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);
        var invoked = 0;
        handler.Handler = _ => {
            invoked++;
            return Task.FromResult(PermissionConfirmationDecision.Allow);
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = await handler.ConfirmAsync("bash", "是否允许？", cts.Token);

        action.Should().Be(PermissionConfirmAction.Deny);
        invoked.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_HandlerCanceled_DeniesWithoutApproval() {
        var fakePermission = new FakePermissionManager();
        var handler = new GuiPermissionConfirmationHandler(fakePermission);
        handler.Handler = _ => throw new OperationCanceledException();

        var action = await handler.ConfirmAsync("bash", "是否允许？");

        action.Should().Be(PermissionConfirmAction.Deny);
        fakePermission.ApprovedTool.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAsync_Deny_DoesNotApprove() {
        var fakePermission = new FakePermissionManager();
        var handler = new GuiPermissionConfirmationHandler(fakePermission);
        handler.Handler = _ => Task.FromResult(PermissionConfirmationDecision.Deny);

        var action = await handler.ConfirmAsync("bash", "是否允许？");

        action.Should().Be(PermissionConfirmAction.Deny);
        fakePermission.ApprovedTool.Should().BeNull();
        fakePermission.ApprovedLevel.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAsync_Allow_ApprovesToolAndReportsDangerLevel() {
        var fakePermission = new FakePermissionManager();
        var handler = new GuiPermissionConfirmationHandler(fakePermission);
        PermissionConfirmationRequest? seen = null;
        handler.Handler = request => {
            seen = request;
            return Task.FromResult(PermissionConfirmationDecision.Allow);
        };

        var action = await handler.ConfirmAsync("bash", "[红灯ask] 危险命令确认");

        action.Should().Be(PermissionConfirmAction.Allow);
        var captured = seen.Should().BeOfType<PermissionConfirmationRequest>().Subject;
        captured.ToolName.Should().Be("bash");
        captured.DangerLevel.Should().Be(CommandDangerLevel.Execution, "危险等级从提示词标签解析，驱动前端五色灯");
        fakePermission.ApprovedTool.Should().Be("bash");
        fakePermission.ApprovedLevel.Should().Be(CommandDangerLevel.Execution, "同等级在本会话内免二次确认");
    }

    [Fact]
    public async Task ConfirmAsync_AlwaysAllow_ApprovesWithLongerWindow() {
        var fakePermission = new FakePermissionManager();
        var handler = new GuiPermissionConfirmationHandler(fakePermission);
        handler.Handler = _ => Task.FromResult(PermissionConfirmationDecision.AlwaysAllow);

        var action = await handler.ConfirmAsync("bash", "[黄灯ask] 未知命令确认");

        action.Should().Be(PermissionConfirmAction.AlwaysAllow);
        fakePermission.LastDuration.Should().BeGreaterThan(TimeSpan.FromMinutes(1), "始终允许窗口应长于本次允许");
    }

    [Fact]
    public async Task ConfirmAsync_NoPermissionManager_StillReturnsDecision() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);
        handler.Handler = _ => Task.FromResult(PermissionConfirmationDecision.Allow);

        var action = await handler.ConfirmAsync("bash", "是否允许？");

        action.Should().Be(PermissionConfirmAction.Allow, "无权限管理器时不应抛异常，仅跳过批准副作用");
    }

    /// <summary>记录批准调用的假权限管理器（仅需批准相关成员）。</summary>
    private sealed class FakePermissionManager : IToolPermissionManager {
        public string? ApprovedTool { get; private set; }
        public CommandDangerLevel? ApprovedLevel { get; private set; }
        public TimeSpan? LastDuration { get; private set; }

        public Task<PermissionResult> CheckPermissionAsync(PermissionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionResult.Granted());

        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PermissionMode> GetCurrentModeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionMode.Auto);

        public Task AddAllowedPromptAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> StripDangerousRulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task RestoreDangerousRulesAsync(int ruleCount, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ApproveToolTemporarily(string toolName, TimeSpan duration) {
            ApprovedTool = toolName;
            LastDuration = duration;
        }

        public void ApproveLevelTemporarily(CommandDangerLevel level) => ApprovedLevel = level;

        public void RemoveTemporaryApproval(string toolName) {
        }

        public Task ClearCacheAsync() => Task.CompletedTask;
    }
}
