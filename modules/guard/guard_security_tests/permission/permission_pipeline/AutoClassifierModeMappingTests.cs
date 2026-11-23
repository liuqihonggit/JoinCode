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
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation));
        var context = CreateContext(PermissionMode.Unattended);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        var result = context.Result ?? throw new Xunit.Sdk.XunitException("期望权限管道给出自动批准结果");
        result.IsApproved.Should().BeTrue("Unattended 没有宿主可确认，MediumRisk 必须自动执行");
    }

    [Fact]
    public async Task Invoke_Unattended_DangerousBlocks() {
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.Dangerous, SecurityAction.Block));
        var context = CreateContext(PermissionMode.Unattended);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        var result = context.Result ?? throw new Xunit.Sdk.XunitException("期望权限管道给出拒绝结果");
        result.IsApproved.Should().BeFalse("Block 是硬拒绝，Unattended 也不能放行");
    }

    [Fact]
    public async Task Invoke_Auto_MediumRiskKeepsPendingConfirmation() {
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: new FixedRuleClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation));
        var context = CreateContext(PermissionMode.Auto);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        var result = context.Result ?? throw new Xunit.Sdk.XunitException("期望权限管道给出待确认结果");
        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue("前台 Auto 保留 ask 语义");
    }

    [Fact]
    public async Task Invoke_LlmJudgeDisabled_UsesRuleClassifier() {
        var rule = new FixedRuleClassifier(SecurityClassification.LowRisk, SecurityAction.AutoApprove);
        var llm = new CountingLlmClassifier(SecurityClassification.LowRisk, SecurityAction.AutoApprove);
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: rule,
            llmAutoModeClassifier: llm);
        var context = CreateContext(PermissionMode.Auto, llmJudgeEnabled: false);

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        llm.CallCount.Should().Be(0, "LlmJudgeEnabled=false 时不得调用 AI 判定");
        var result = context.Result ?? throw new Xunit.Sdk.XunitException("期望权限管道给出自动批准结果");
        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_WebFetch_AlwaysDefersToDomainPermissionMiddleware() {
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(
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

    [Fact]
    public async Task Invoke_LlmJudgeShadow_PassesFlagToClassifierAndKeepsRuleDecision() {
        var rule = new FixedRuleClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation);
        var llm = new CountingLlmClassifier(SecurityClassification.LowRisk, SecurityAction.AutoApprove);
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(
            autoModeClassifier: rule,
            llmAutoModeClassifier: llm);
        var context = CreateContext(PermissionMode.Auto, llmJudgeEnabled: true);
        context.Config.LlmJudgeShadow = true;

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        llm.CallCount.Should().Be(1, "shadow 仍调用 AI 以收集对比数据");
        llm.LastShadow.Should().BeTrue("中间件必须把 LlmJudgeShadow 透传给分类器");
        var result = context.Result ?? throw new Xunit.Sdk.XunitException("期望权限管道给出自动批准结果");
        result.IsApproved.Should().BeTrue();
    }

    /// <summary>缓存用管道解析后的有效模式，而不是从工具参数或代理默认值推断。</summary>
    [Theory]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Unattended)]
    public async Task Invoke_PassesEffectiveCacheModeToLlmClassifier(PermissionMode mode) {
        var llm = new CountingLlmClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation);
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(llmAutoModeClassifier: llm);
        var context = CreateContext(mode);

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        llm.LastCacheMode.Should().Be(mode);
    }

    /// <summary>中间件把判定预算与超时配置透传给分类器。</summary>
    [Fact]
    public async Task Invoke_PassesJudgeBudgetAndTimeoutConfigToLlmClassifier() {
        var llm = new CountingLlmClassifier(SecurityClassification.MediumRisk, SecurityAction.RequireConfirmation);
        await using var middleware = new Core.Permission.AutoClassifierMiddleware(llmAutoModeClassifier: llm);
        var context = CreateContext(PermissionMode.Auto);
        context.Config.LlmJudgeMaxCallsPerTask = 3;
        context.Config.LlmJudgeTimeoutMs = 2500;

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        llm.LastMaxCallsPerTask.Should().Be(3);
        llm.LastTimeoutMs.Should().Be(2500);
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
        public bool LastShadow { get; private set; }
        public PermissionMode? LastCacheMode { get; private set; }
        public int? LastMaxCallsPerTask { get; private set; }
        public int? LastTimeoutMs { get; private set; }

        public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default)
            => ClassifyAsync(request, shadow: false, ct);

        public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, bool shadow, CancellationToken ct = default) {
            CallCount++;
            LastShadow = shadow;
            LastCacheMode = request.CacheMode;
            LastMaxCallsPerTask = request.JudgeMaxCallsPerTask;
            LastTimeoutMs = request.JudgeTimeoutMs;
            return Task.FromResult(new ClassificationResult {
                Classification = classification,
                Confidence = 0.9,
                Reason = "llm-test",
                Action = action,
            });
        }
    }
}
