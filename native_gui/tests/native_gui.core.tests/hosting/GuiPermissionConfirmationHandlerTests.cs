namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// GUI 权限确认处理器测试 — 引擎同步确认点转接宿主异步委托：
/// 未注入委托 / 取消 / 拒绝 → fail-closed；允许 → 临时批准工具 + 会话级批准危险等级（与 CLI 同源）。
/// </summary>
public class GuiPermissionConfirmationHandlerTests {
    [Fact]
    public async Task ConfirmAsync_NoHandler_Denies() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);

        var result = await handler.ConfirmAsync("bash", "[红灯ask] 是否允许执行 bash？");

        result.Action.Should().Be(PermissionConfirmAction.Deny, "未注入委托时必须 fail-closed");
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

        var result = await handler.ConfirmAsync("bash", "是否允许？", cancellationToken: cts.Token);

        result.Action.Should().Be(PermissionConfirmAction.Deny);
        invoked.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_HandlerCanceled_DeniesWithoutApproval() {
        var fakePermission = new FakePermissionManager();
        var handler = new GuiPermissionConfirmationHandler(fakePermission);
        handler.Handler = _ => throw new OperationCanceledException();

        var result = await handler.ConfirmAsync("bash", "是否允许？");

        result.Action.Should().Be(PermissionConfirmAction.Deny);
        fakePermission.ApprovedTool.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAsync_Deny_DoesNotApprove() {
        var fakePermission = new FakePermissionManager();
        var handler = new GuiPermissionConfirmationHandler(fakePermission);
        handler.Handler = _ => Task.FromResult(PermissionConfirmationDecision.Deny);

        var result = await handler.ConfirmAsync("bash", "是否允许？");

        result.Action.Should().Be(PermissionConfirmAction.Deny);
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

        var result = await handler.ConfirmAsync("bash", "[红灯ask] 危险命令确认");

        result.Action.Should().Be(PermissionConfirmAction.Allow);
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

        var result = await handler.ConfirmAsync("bash", "[黄灯ask] 未知命令确认");

        result.Action.Should().Be(PermissionConfirmAction.AlwaysAllow);
        fakePermission.LastDuration.Should().BeGreaterThan(TimeSpan.FromMinutes(1), "始终允许窗口应长于本次允许");
    }

    [Fact]
    public async Task ConfirmAsync_NoPermissionManager_StillReturnsDecision() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);
        handler.Handler = _ => Task.FromResult(PermissionConfirmationDecision.Allow);

        var result = await handler.ConfirmAsync("bash", "是否允许？");

        result.Action.Should().Be(PermissionConfirmAction.Allow, "无权限管理器时不应抛异常，仅跳过批准副作用");
    }

    /// <summary>
    /// TASK069-P5 — 触发原因规则内容必须原样透传到 GUI 请求，前端才有规则块可渲染。
    /// 此前该字段被写死 <c>null</c>，而 <see cref="PermissionConfirmationRequest.HasRuleContent"/>、
    /// <c>PermissionRequestMessage.RuleContent</c>、web 渲染三层全都在等它 —— 属「通道齐备但源头恒空」。
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_WithRuleContent_PassesItToGuiRequest() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);
        PermissionConfirmationRequest? seen = null;
        handler.Handler = request => {
            seen = request;
            return Task.FromResult(PermissionConfirmationDecision.Allow);
        };

        await handler.ConfirmAsync("WebFetch", "是否允许访问该域名？", "domain:example.com");

        var captured = seen.Should().BeOfType<PermissionConfirmationRequest>().Subject;
        captured.RuleContent.Should().Be("domain:example.com", "GUI 前端按此渲染「触发原因」块");
        captured.HasRuleContent.Should().BeTrue("非空规则驱动规则块显隐");
    }

    /// <summary>无结构化来源时不编造规则内容 —— 保持 null，前端整块折叠。</summary>
    [Fact]
    public async Task ConfirmAsync_WithoutRuleContent_KeepsNull() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);
        PermissionConfirmationRequest? seen = null;
        handler.Handler = request => {
            seen = request;
            return Task.FromResult(PermissionConfirmationDecision.Deny);
        };

        await handler.ConfirmAsync("bash", "是否允许？");

        var captured = seen.Should().BeOfType<PermissionConfirmationRequest>().Subject;
        captured.RuleContent.Should().BeNull();
        captured.HasRuleContent.Should().BeFalse();
    }

    /// <summary>GUI 弹窗只采集动作、不采集补充说明 — Feedback 恒 null（与 CLI ^ 提示符同状态，非缺陷）。</summary>
    [Fact]
    public async Task ConfirmAsync_NeverReportsFeedback() {
        var handler = new GuiPermissionConfirmationHandler(permissionManager: null);
        handler.Handler = _ => Task.FromResult(PermissionConfirmationDecision.Allow);

        var result = await handler.ConfirmAsync("bash", "是否允许？", "bash(rm:*)");

        result.Action.Should().Be(PermissionConfirmAction.Allow);
        result.Feedback.Should().BeNull("GUI 通道尚无输入框，不得凭规则内容伪造说明");
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

        /// <summary>TASK070 批次3 接口扩展 — 永久批准记录（AlwaysAllow 持久化面）。</summary>
        public (string Tool, string? RuleContent)? PermanentApproval { get; private set; }

        public Task ApproveToolPermanentlyAsync(string toolName, string? ruleContent, CancellationToken cancellationToken = default) {
            PermanentApproval = (toolName, ruleContent);
            return Task.CompletedTask;
        }

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
