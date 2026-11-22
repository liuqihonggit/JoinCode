namespace Core.Tests.Security.PermissionPipeline;

/// <summary>
/// AutoClassifierMiddleware 模式映射测试 — Unattended 没有宿主可确认，MediumRisk 必须自动批准；
/// Block 仍拒绝；前台 Auto 保留 ask 语义。
/// </summary>
public sealed class AutoClassifierModeMappingTests {
    private static PermissionCheckContext CreateContext(PermissionMode mode, bool llmJudgeEnabled = true) {
        var config = PermissionConfig.CreateDefault();
        config.LlmJudgeEnabled = llmJudgeEnabled;
        return new PermissionCheckContext {
            ToolName = ShellToolNameEnumConstants.Bash,
            Arguments = [],
            CurrentMode = mode,
            Config = config,
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
    }

    [Fact]
    public async Task Invoke_Unattended_MediumRiskAutoApproves() {
        var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation));
        var context = CreateContext(PermissionMode.Unattended);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeTrue("Unattended 没有宿主可确认，MediumRisk 必须自动执行");
    }

    [Fact]
    public async Task Invoke_Unattended_DangerousBlocks() {
        var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.Dangerous, SecurityAction.Block));
        var context = CreateContext(PermissionMode.Unattended);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse("Block 是硬拒绝，Unattended 也不能放行");
    }

    [Fact]
    public async Task Invoke_Auto_MediumRiskKeepsPendingConfirmation() {
        var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation));
        var context = CreateContext(PermissionMode.Auto);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
        context.Result.ConfirmationRequired.Should().BeTrue("前台 Auto 保留 ask 语义");
    }

    [Fact]
    public async Task Invoke_LlmJudgeDisabled_UsesRuleClassifier() {
        var rule = new FixedRuleClassifier(SecurityClassification.LowRisk, SecurityAction.AutoApprove);
        var llm = new CountingLlmClassifier(SecurityClassification.LowRisk, SecurityAction.AutoApprove);
        var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: rule,
            llmAutoModeClassifier: llm);
        var context = CreateContext(PermissionMode.Auto, llmJudgeEnabled: false);

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        llm.CallCount.Should().Be(0, "LlmJudgeEnabled=false 时不得调用 AI 判定");
        context.Result!.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_WebFetch_AlwaysDefersToDomainPermissionMiddleware() {
        var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.Safe, SecurityAction.AutoApprove));
        var context = CreateContext(PermissionMode.Unattended);
        context = new PermissionCheckContext {
            ToolName = WebToolNameEnumConstants.WebFetch,
            Arguments = [],
            CurrentMode = PermissionMode.Unattended,
            Config = context.Config,
            AutoApprovedTools = context.AutoApprovedTools,
            AutoRejectedTools = context.AutoRejectedTools,
        };
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("WebFetch 的域名 deny/ask/allow 必须由 WebFetchPermissionMiddleware 判定");
        context.Result.Should().BeNull();
    }

    private sealed class FixedRuleClassifier(SecurityClassification classification, SecurityAction action) : IAutoModeClassifier {
        public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default)
            => Task.FromResult(new ClassificationResult {
                Classification = classification,
                Confidence = 0.9,
                Reason = "test",
                Action = action,
            });
    }

    private sealed class CountingLlmClassifier(SecurityClassification classification, SecurityAction action) : ILlmAutoModeClassifier {
        public int CallCount { get; private set; }

        public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default) {
            CallCount++;
            return Task.FromResult(new ClassificationResult {
                Classification = classification,
                Confidence = 0.9,
                Reason = "llm-test",
                Action = action,
            });
        }
    }
}
