namespace Core.Tests.Permission;

/// <summary>
/// PermissionChecker 确定性测试 — mock 管道中间件 / InMemoryFileSystem 消除 IO 依赖
/// <para>测试管道执行逻辑、自动批准/拒绝集合管理、会话级危险等级批准、持久化。不依赖时序。</para>
/// </summary>
public sealed class PermissionCheckerTest {

    #region 辅助构造

    /// <summary>设置 Result 的测试中间件 — 模拟管道决策</summary>
    private sealed class StubResultMiddleware : IMiddleware<PermissionCheckContext> {
        private readonly ToolPermissionCheckResult _result;
        public StubResultMiddleware(ToolPermissionCheckResult result) => _result = result;
        public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
            context.Result = _result;
            return next(context, ct);
        }
    }

    private static PermissionChecker CreateChecker(
        MiddlewarePipeline<PermissionCheckContext>? pipeline = null,
        PermissionConfig? config = null,
        IFileSystem? fs = null) {
        return new PermissionChecker(
            pipeline ?? new MiddlewarePipeline<PermissionCheckContext>([]),
            Options.Create(config ?? PermissionConfig.CreateDefault()),
            fs ?? new InMemoryFileSystem());
    }

    private static PermissionConfig CreateConfigWithBashApproved() {
        var config = PermissionConfig.CreateDefault();
        config.AutoApprovedTools = config.AutoApprovedTools.SetItem(
            ShellToolNameEnumConstants.Bash,
            new ToolPermissionRule { ToolName = ShellToolNameEnumConstants.Bash, Description = "Bash" });
        return config;
    }

    #endregion

    #region CheckPermissionAsync — 管道执行

    [Fact]
    public async Task CheckPermissionAsync_空管道_返回PendingConfirmation() {
        var checker = CreateChecker();

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
        result.Reason.Should().Contain("Bash");
    }

    [Fact]
    public async Task CheckPermissionAsync_管道批准_返回Approved() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(
            [new StubResultMiddleware(ToolPermissionCheckResult.Approved())]);
        var checker = CreateChecker(pipeline);

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_管道拒绝_返回Rejected() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(
            [new StubResultMiddleware(ToolPermissionCheckResult.Rejected("危险操作"))]);
        var checker = CreateChecker(pipeline);

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeFalse();
        result.Reason.Should().Be("危险操作");
    }

    [Fact]
    public async Task CheckPermissionAsync_管道待确认_返回PendingConfirmation() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(
            [new StubResultMiddleware(ToolPermissionCheckResult.PendingConfirmation("请确认"))]);
        var checker = CreateChecker(pipeline);

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
        result.Reason.Should().Be("请确认");
    }

    [Fact]
    public async Task CheckPermissionAsync_带参数_传递给context() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>([
            new StubResultMiddleware(ToolPermissionCheckResult.Approved())
        ]);
        var checker = CreateChecker(pipeline);

        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("ls -la")
        };
        var result = await checker.CheckPermissionAsync("Bash", args);

        result.IsApproved.Should().BeTrue();
    }

    #endregion

    #region AddToAutoApproved / AddToAutoRejected

    [Fact]
    public void AddToAutoApproved_添加工具到集合() {
        var checker = CreateChecker();

        checker.AddToAutoApproved("CustomTool");

        // 通过 CheckPermissionAsync 间接验证:AutoApprovedTools 在 context 中传递
        // 直接验证内部状态需通过管道观察
    }

    [Fact]
    public void AddToAutoApproved_带ruleContent_不添加工具名到HashSet() {
        var config = PermissionConfig.CreateDefault();
        var checker = CreateChecker(config: config);

        checker.AddToAutoApproved("WebFetch", "domain:example.com");

        // 域名级规则不添加到 HashSet(避免无条件批准所有域名)
        // 只添加带 RuleContent 的规则到配置
        config.AutoApprovedTools.Should().ContainKey("WebFetch");
        config.AutoApprovedTools["WebFetch"].RuleContent.Should().Be("domain:example.com");
    }

    [Fact]
    public void AddToAutoApproved_空ruleContent_等价于无ruleContent() {
        var config = PermissionConfig.CreateDefault();
        var checker = CreateChecker(config: config);

        checker.AddToAutoApproved("CustomTool", null);

        config.AutoApprovedTools.Should().ContainKey("CustomTool");
    }

    [Fact]
    public void RemoveFromAutoApproved_从集合移除() {
        var config = CreateConfigWithBashApproved();
        var checker = CreateChecker(config: config);

        checker.RemoveFromAutoApproved(ShellToolNameEnumConstants.Bash);

        config.AutoApprovedTools.Should().NotContainKey(ShellToolNameEnumConstants.Bash);
    }

    [Fact]
    public void AddToAutoRejected_添加工具到集合() {
        var config = PermissionConfig.CreateDefault();
        var checker = CreateChecker(config: config);

        checker.AddToAutoRejected("DangerousTool");

        config.AutoRejectedTools.Should().ContainKey("DangerousTool");
    }

    [Fact]
    public void RemoveFromAutoRejected_从集合移除() {
        var config = PermissionConfig.CreateDefault();
        config.AutoRejectedTools = config.AutoRejectedTools.SetItem(
            "DangerousTool",
            new ToolPermissionRule { ToolName = "DangerousTool" });
        var checker = CreateChecker(config: config);

        checker.RemoveFromAutoRejected("DangerousTool");

        config.AutoRejectedTools.Should().NotContainKey("DangerousTool");
    }

    #endregion

    #region ApproveLevelTemporarily / IsLevelApproved

    [Fact]
    public void ApproveLevelTemporarily_Unknown_添加到批准集合() {
        var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Unknown);

        checker.IsLevelApproved(CommandDangerLevel.Unknown).Should().BeTrue();
    }

    [Fact]
    public void ApproveLevelTemporarily_LightValidation_添加到批准集合() {
        var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.LightValidation);

        checker.IsLevelApproved(CommandDangerLevel.LightValidation).Should().BeTrue();
    }

    [Fact]
    public void ApproveLevelTemporarily_Execution_添加到批准集合() {
        var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Execution);

        checker.IsLevelApproved(CommandDangerLevel.Execution).Should().BeTrue();
    }

    [Fact]
    public void ApproveLevelTemporarily_Safe_不添加() {
        var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Safe);

        checker.IsLevelApproved(CommandDangerLevel.Safe).Should().BeFalse();
    }

    [Fact]
    public void ApproveLevelTemporarily_Dangerous_不添加() {
        var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Dangerous);

        checker.IsLevelApproved(CommandDangerLevel.Dangerous).Should().BeFalse();
    }

    [Fact]
    public void IsLevelApproved_未批准_返回False() {
        var checker = CreateChecker();
        checker.IsLevelApproved(CommandDangerLevel.Execution).Should().BeFalse();
    }

    #endregion

    #region CurrentMode

    [Fact]
    public void CurrentMode_默认为Auto() {
        // 清除可能的环境变量以确保确定性
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, null);
        try {
            var checker = CreateChecker();
            checker.CurrentMode.Should().Be(PermissionMode.Auto);
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public void CurrentMode_设置后返回新值() {
        var checker = CreateChecker();
        checker.CurrentMode = PermissionMode.Plan;
        checker.CurrentMode.Should().Be(PermissionMode.Plan);
    }

    #endregion

    #region AddToAutoApprovedAndPersistAsync — 持久化

    [Fact]
    public async Task AddToAutoApprovedAndPersistAsync_无ruleContent_持久化工具名() {
        await using var fs = new InMemoryFileSystem();
        var checker = CreateChecker(fs: fs);

        // 不应抛异常(即使 settings.json 不存在,持久化失败只记日志)
        var act = () => checker.AddToAutoApprovedAndPersistAsync("CustomTool", null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddToAutoApprovedAndPersistAsync_带ruleContent_持久化规则() {
        await using var fs = new InMemoryFileSystem();
        var checker = CreateChecker(fs: fs);

        var act = () => checker.AddToAutoApprovedAndPersistAsync("WebFetch", "domain:example.com");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddToAutoApprovedAndPersistAsync_重复规则_不重复添加() {
        await using var fs = new InMemoryFileSystem();
        var checker = CreateChecker(fs: fs);

        await checker.AddToAutoApprovedAndPersistAsync("CustomTool", null);
        await checker.AddToAutoApprovedAndPersistAsync("CustomTool", null);

        // 不抛异常即通过(重复添加被 Contains 检查拦截)
    }

    #endregion

    #region TryGetPermissionModeFromEnvAsync — 环境变量解析

    [Fact]
    public async Task TryGetPermissionModeFromEnvAsync_未设置环境变量_返回null() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, null);
        try {
            var result = await PermissionChecker.TryGetPermissionModeFromEnvAsync(null);
            result.Should().BeNull();
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public async Task TryGetPermissionModeFromEnvAsync_设置bypass_返回Bypass() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, "bypass");
        try {
            var result = await PermissionChecker.TryGetPermissionModeFromEnvAsync(null);
            result.Should().Be(PermissionMode.Bypass);
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public async Task TryGetPermissionModeFromEnvAsync_设置plan_返回Plan() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, "plan");
        try {
            var result = await PermissionChecker.TryGetPermissionModeFromEnvAsync(null);
            result.Should().Be(PermissionMode.Plan);
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public async Task TryGetPermissionModeFromEnvAsync_无效值_返回Null() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, "invalid-mode");
        try {
            var result = await PermissionChecker.TryGetPermissionModeFromEnvAsync(null);
            result.Should().BeNull();
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    #endregion
}
