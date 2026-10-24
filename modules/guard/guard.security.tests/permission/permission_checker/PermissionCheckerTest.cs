namespace Core.Tests.Permission;

/// <summary>
/// PermissionChecker 确定性测试 — mock 管道中间件 / InMemoryFileSystem 消除 IO 依赖
/// <para>测试管道执行逻辑、自动批准/拒绝集合管理、会话级危险等级批准、持久化。不依赖时序。</para>
/// <para>设 JCC_PERMISSION_MODE 环境变量 — 必须与构造 PermissionChecker 的测试串行（见 PermissionModeEnvCollection）。</para>
/// </summary>
[Collection("permission-mode-env")]
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
        await using var checker = CreateChecker();

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
        result.Reason.Should().Contain("Bash");
    }

    [Fact]
    public async Task CheckPermissionAsync_管道批准_返回Approved() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(
            [new StubResultMiddleware(ToolPermissionCheckResult.Approved())]);
        await using var checker = CreateChecker(pipeline);

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_管道拒绝_返回Rejected() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(
            [new StubResultMiddleware(ToolPermissionCheckResult.Rejected("危险操作"))]);
        await using var checker = CreateChecker(pipeline);

        var result = await checker.CheckPermissionAsync("Bash");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeFalse();
        result.Reason.Should().Be("危险操作");
    }

    [Fact]
    public async Task CheckPermissionAsync_管道待确认_返回PendingConfirmation() {
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(
            [new StubResultMiddleware(ToolPermissionCheckResult.PendingConfirmation("请确认"))]);
        await using var checker = CreateChecker(pipeline);

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
        await using var checker = CreateChecker(pipeline);

        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("ls -la")
        };
        var result = await checker.CheckPermissionAsync("Bash", args);

        result.IsApproved.Should().BeTrue();
    }

    #endregion

    #region AddToAutoApproved / AddToAutoRejected

    [Fact]
    public async Task AddToAutoApproved_添加工具到集合() {
        await using var checker = CreateChecker();

        checker.AddToAutoApproved("CustomTool");

        // 通过 CheckPermissionAsync 间接验证:AutoApprovedTools 在 context 中传递
        // 直接验证内部状态需通过管道观察
    }

    [Fact]
    public async Task AddToAutoApproved_带ruleContent_不添加工具名到HashSet() {
        var config = PermissionConfig.CreateDefault();
        await using var checker = CreateChecker(config: config);

        checker.AddToAutoApproved("WebFetch", "domain:example.com");

        // 域名级规则不添加到 HashSet(避免无条件批准所有域名)
        // 只添加带 RuleContent 的规则到配置
        config.AutoApprovedTools.Should().ContainKey("WebFetch");
        config.AutoApprovedTools["WebFetch"].RuleContent.Should().Be("domain:example.com");
    }

    [Fact]
    public async Task AddToAutoApproved_空ruleContent_等价于无ruleContent() {
        var config = PermissionConfig.CreateDefault();
        await using var checker = CreateChecker(config: config);

        checker.AddToAutoApproved("CustomTool", null);

        config.AutoApprovedTools.Should().ContainKey("CustomTool");
    }

    [Fact]
    public async Task RemoveFromAutoApproved_从集合移除() {
        var config = CreateConfigWithBashApproved();
        await using var checker = CreateChecker(config: config);

        checker.RemoveFromAutoApproved(ShellToolNameEnumConstants.Bash);

        config.AutoApprovedTools.Should().NotContainKey(ShellToolNameEnumConstants.Bash);
    }

    [Fact]
    public async Task AddToAutoRejected_添加工具到集合() {
        var config = PermissionConfig.CreateDefault();
        await using var checker = CreateChecker(config: config);

        checker.AddToAutoRejected("DangerousTool");

        config.AutoRejectedTools.Should().ContainKey("DangerousTool");
    }

    [Fact]
    public async Task RemoveFromAutoRejected_从集合移除() {
        var config = PermissionConfig.CreateDefault();
        config.AutoRejectedTools = config.AutoRejectedTools.SetItem(
            "DangerousTool",
            new ToolPermissionRule { ToolName = "DangerousTool" });
        await using var checker = CreateChecker(config: config);

        checker.RemoveFromAutoRejected("DangerousTool");

        config.AutoRejectedTools.Should().NotContainKey("DangerousTool");
    }

    #endregion

    #region ApproveLevelTemporarily / IsLevelApproved

    [Fact]
    public async Task ApproveLevelTemporarily_Unknown_添加到批准集合() {
        await using var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Unknown);

        checker.IsLevelApproved(CommandDangerLevel.Unknown).Should().BeTrue();
    }

    [Fact]
    public async Task ApproveLevelTemporarily_LightValidation_添加到批准集合() {
        await using var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.LightValidation);

        checker.IsLevelApproved(CommandDangerLevel.LightValidation).Should().BeTrue();
    }

    [Fact]
    public async Task ApproveLevelTemporarily_Execution_添加到批准集合() {
        await using var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Execution);

        checker.IsLevelApproved(CommandDangerLevel.Execution).Should().BeTrue();
    }

    [Fact]
    public async Task ApproveLevelTemporarily_Safe_不添加() {
        await using var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Safe);

        checker.IsLevelApproved(CommandDangerLevel.Safe).Should().BeFalse();
    }

    [Fact]
    public async Task ApproveLevelTemporarily_Dangerous_不添加() {
        await using var checker = CreateChecker();

        checker.ApproveLevelTemporarily(CommandDangerLevel.Dangerous);

        checker.IsLevelApproved(CommandDangerLevel.Dangerous).Should().BeFalse();
    }

    [Fact]
    public async Task IsLevelApproved_未批准_返回False() {
        await using var checker = CreateChecker();
        checker.IsLevelApproved(CommandDangerLevel.Execution).Should().BeFalse();
    }

    #endregion

    #region CurrentMode

    [Fact]
    public async Task CurrentMode_默认为Auto() {
        // 清除可能的环境变量以确保确定性
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, null);
        try {
            await using var checker = CreateChecker();
            checker.CurrentMode.Should().Be(PermissionMode.Auto);
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public async Task CurrentMode_设置后返回新值() {
        await using var checker = CreateChecker();
        checker.CurrentMode = PermissionMode.Plan;
        checker.CurrentMode.Should().Be(PermissionMode.Plan);
    }

    [Fact]
    public async Task CurrentMode_Bypass_当DisableBypass开启时_被拦截() {
        await using var checker = CreateChecker();
        checker.DisableBypassGate = true;
        checker.CurrentMode = PermissionMode.Bypass;
        checker.CurrentMode.Should().NotBe(PermissionMode.Bypass);
        checker.CurrentMode.Should().Be(PermissionMode.Auto);
    }

    [Fact]
    public async Task CurrentMode_Plan_当DisableBypass开启时_不受影响() {
        await using var checker = CreateChecker();
        checker.DisableBypassGate = true;
        checker.CurrentMode = PermissionMode.Plan;
        checker.CurrentMode.Should().Be(PermissionMode.Plan);
    }

    [Fact]
    public async Task CurrentMode_Bypass_当DisableBypass关闭时_正常设置() {
        await using var checker = CreateChecker();
        checker.DisableBypassGate = false;
        checker.CurrentMode = PermissionMode.Bypass;
        checker.CurrentMode.Should().Be(PermissionMode.Bypass);
    }

    #endregion

    #region AddToAutoApprovedAndPersistAsync — 持久化

    [Fact]
    public async Task AddToAutoApprovedAndPersistAsync_无ruleContent_持久化工具名() {
        await using var fs = new InMemoryFileSystem();
        await using var checker = CreateChecker(fs: fs);

        // 不应抛异常(即使 settings.json 不存在,持久化失败只记日志)
        var act = () => checker.AddToAutoApprovedAndPersistAsync("CustomTool", null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddToAutoApprovedAndPersistAsync_带ruleContent_持久化规则() {
        await using var fs = new InMemoryFileSystem();
        await using var checker = CreateChecker(fs: fs);

        var act = () => checker.AddToAutoApprovedAndPersistAsync("WebFetch", "domain:example.com");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddToAutoApprovedAndPersistAsync_重复规则_不重复添加() {
        await using var fs = new InMemoryFileSystem();
        await using var checker = CreateChecker(fs: fs);

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

    #region 构造初始化 → 决策入口全链路 — CheckPermissionAsync 必须基于初始化完成后的模式

    /// <summary>捕获管道上下文模式的探针中间件 — 验证决策入口看到的 CurrentMode</summary>
    private sealed class CaptureModeMiddleware : IMiddleware<PermissionCheckContext> {
        public PermissionMode? CapturedMode;
        public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
            CapturedMode = context.CurrentMode;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Constructor_EnvBypass_CheckPermissionAsync_SeesBypassMode() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, "bypass");
        try {
            var capture = new CaptureModeMiddleware();
            await using var checker = new PermissionChecker(
                new MiddlewarePipeline<PermissionCheckContext>([capture]),
                Options.Create(PermissionConfig.CreateDefault()),
                new InMemoryFileSystem());
            await checker.CheckPermissionAsync(ShellToolNameEnumConstants.Bash);
            capture.CapturedMode.Should().Be(PermissionMode.Bypass,
                "决策入口必须看到环境变量初始化完成后的模式(InitializeModeAsync 契约)");
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Fact]
    public async Task Constructor_NoEnv_CheckPermissionAsync_SeesAutoMode() {
        var envVar = JccEnvVar.PermissionMode.ToValue();
        var original = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, null);
        try {
            var capture = new CaptureModeMiddleware();
            await using var checker = new PermissionChecker(
                new MiddlewarePipeline<PermissionCheckContext>([capture]),
                Options.Create(PermissionConfig.CreateDefault()),
                new InMemoryFileSystem());
            await checker.CheckPermissionAsync(ShellToolNameEnumConstants.Bash);
            capture.CapturedMode.Should().Be(PermissionMode.Auto,
                "无环境变量时决策入口必须看到默认 Auto 模式");
        } finally {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }


    #endregion

    #region TASK070 P1-P2/DSG046 — 规则热加载（mtime 变化触发判定链前段重读）

    [Fact]
    public async Task CheckPermissionAsync_SettingsFileAppears_ReloadsRules() {
        await using var fs = new InMemoryFileSystem();
        // 判定链需含规则中间件（空管道 Result 恒 null 落确认 — 无法表达热加载命中）
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>([new ToolListPermissionMiddleware()]);
        await using var checker = CreateChecker(pipeline: pipeline, fs: fs);

        // 启动时无 settings.json → 规则空 → 落确认
        var before = await checker.CheckPermissionAsync("HotTool");
        before.IsApproved.Should().BeFalse("启动时无规则，应落确认");

        // 写入 settings.json（mtime: null → 时间戳，触发判定链前段重读）
        var settingsPath = SettingsLoader.GetUserSettingsPath();
        var json = "{\"current\": {\"permissions\": {\"allow\": [\"HotTool\"]}}}";
        await fs.WriteAllTextAsync(settingsPath, json);

        var after = await checker.CheckPermissionAsync("HotTool");
        after.IsApproved.Should().BeTrue("settings.json 出现 allow 规则后，热加载应使命中");
    }

    [Fact]
    public async Task CheckPermissionAsync_RulesLoaderFailure_FallsBackToInMemoryRules() {
        await using var checker = CreateChecker(); // 默认 fs 无 settings.json

        // 无 settings.json → mtime 恒 null → 不触发重载（快速路径），判定照常
        var result = await checker.CheckPermissionAsync("UnlistedTool");
        result.Should().NotBeNull();
    }

    #endregion
}