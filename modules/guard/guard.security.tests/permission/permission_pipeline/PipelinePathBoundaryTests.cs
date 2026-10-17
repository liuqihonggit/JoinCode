namespace Core.Tests.Permission.Pipeline;

/// <summary>
/// 权限管道路径边界集成测试 — 复现并回归 AutoClassifier 短路绕过 PathPermission 的漏洞
/// <para>背景（ADR 0138 扩展）：Auto 模式下 AutoClassifierMiddleware 对 SafeWrite/ReadOnly 工具直接
/// 设置 AutoApprove Result 并短路，导致 PathPermissionMiddleware（工作目录边界 9 步链）永远不执行，
/// mcp serve / chat 下 write 可写 C:\Windows\Temp 等任意路径。</para>
/// <para>契约：read/write 工具必须经真实 PathPermissionChecker 判定路径边界，
/// serve 等无确认处理器环境对 cwd 外写入必须拒绝。</para>
/// </summary>
public sealed class PipelinePathBoundaryTests {
    private readonly IFileSystem _fs = new InMemoryFileSystem();
    private readonly string _workingDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "jcc_perm_test_" + Path.GetRandomFileName().TrimEnd('\\')));

    #region 生产管道组装 — 顺序对齐 PipelineComposition.AddAllPipelines（除 CrashSnapshot 外全部真实中间件）

    private MiddlewarePipeline<PermissionCheckContext> BuildProductionPipeline(IPathPermissionChecker pathChecker) {
        return new PipelineBuilder<PermissionCheckContext>()
            .WithShortCircuit(static ctx => ctx.Result is not null)
            .UseRange(new IMiddleware<PermissionCheckContext>[] {
                new BypassPermissionMiddleware(),
                new AgentRestrictionMiddleware(),
                new DangerousCommandProtectionMiddleware(),
                new AutoClassifierMiddleware(autoModeClassifier: new AutoModeClassifier()),
                new ConfigGetOperationMiddleware(),
                new WebFetchPermissionMiddleware(),
                new EarlyPathDenyMiddleware(pathChecker),
                new ToolListPermissionMiddleware(),
                new PathPermissionMiddleware(pathChecker),
                new DangerousOperationMiddleware(),
                new PlanModeMiddleware(),
                new DefaultResultMiddleware(),
            })
            .Build();
    }

    private PermissionChecker CreateChecker(IPathPermissionChecker pathChecker) {
        var pipeline = BuildProductionPipeline(pathChecker);
        return new PermissionChecker(
            pipeline,
            Options.Create(PermissionConfig.CreateDefault()),
            _fs);
    }

    private PathPermissionChecker CreatePathChecker() {
        // 无需物理建目录 — IsPathUnderDirectory 仅做路径前缀判定，边界测试只依赖路径字符串
        return new PathPermissionChecker(_fs, _workingDir);
    }

    private Dictionary<string, JsonElement> ArgsWithPath(string path)
        => new() { ["path"] = JsonSerializer.SerializeToElement(path) };

    #endregion

    #region 端到端：真实 12 层管道

    [Fact]
    public async Task WriteOutsideWorkingDirectory_AutoMode_RequiresConfirmation() {
        // C:\Users\Public 远离 Windows 敏感段（{Windows} 特殊文件夹不命中），
        // 复现 serve 冒烟中 write 越界写 C:\Users\Public 成功的真实通路
        await using var checker = CreateChecker(CreatePathChecker());

        var result = await checker.CheckPermissionAsync(
            FileToolNameEnumConstants.FileWrite,
            ArgsWithPath(@"C:\Users\Public\pwned_pipeline_test.txt"));

        result.IsApproved.Should().BeFalse("cwd 外写入不应被自动批准");
        result.ConfirmationRequired.Should().BeTrue("cwd 外写入应进入待确认（无确认处理器的 serve 场景即为拒绝）");
    }

    [Fact]
    public async Task WriteToWindowsDirectory_AutoMode_StillRequiresConfirmation() {
        // 敏感路径（{Windows} 特殊文件夹命中）双保险护栏 — DangerousCmd 层与后续层都不得放行
        await using var checker = CreateChecker(CreatePathChecker());

        var result = await checker.CheckPermissionAsync(
            FileToolNameEnumConstants.FileWrite,
            ArgsWithPath(@"C:\Windows\Temp\pwned_pipeline_win.txt"));

        result.IsApproved.Should().BeFalse("写入 Windows 目录必须拒绝批准");
        result.ConfirmationRequired.Should().BeTrue("写入 Windows 目录必须进入待确认");
    }

    [Fact]
    public async Task WriteInsideWorkingDirectory_AutoMode_Allowed() {
        await using var checker = CreateChecker(CreatePathChecker());

        var result = await checker.CheckPermissionAsync(
            FileToolNameEnumConstants.FileWrite,
            ArgsWithPath(Path.Combine(_workingDir, "allowed_output.txt")));

        result.IsApproved.Should().BeTrue("cwd 内写入应保持自动批准（开心路径不回归）");
    }

    [Fact]
    public async Task ReadOutsideWorkingDirectory_NonexistentFile_IsNotApproved() {
        await using var checker = CreateChecker(CreatePathChecker());

        var result = await checker.CheckPermissionAsync(
            FileToolNameEnumConstants.FileRead,
            ArgsWithPath(@"C:\Windows\Temp\definitely_missing_secrets.txt"));

        result.IsApproved.Should().BeFalse("cwd 外读取不应被分类器无条件放行，应交给路径决策链判定");
    }

    [Fact]
    public async Task ReadInsideWorkingDirectory_AutoMode_Allowed() {
        await using var checker = CreateChecker(CreatePathChecker());

        var result = await checker.CheckPermissionAsync(
            FileToolNameEnumConstants.FileRead,
            ArgsWithPath(Path.Combine(_workingDir, "existing.md")));

        result.IsApproved.Should().BeTrue("cwd 内读取应保持自动批准（开心路径不回归）");
    }

    [Fact]
    public async Task BashShell_AutoMode_StillRequiresConfirmation() {
        await using var checker = CreateChecker(CreatePathChecker());

        var result = await checker.CheckPermissionAsync(
            ShellToolNameEnumConstants.Bash,
            new Dictionary<string, JsonElement> {
                ["command"] = JsonSerializer.SerializeToElement("dir")
            });

        // 修复护栏：分类器收窄后 shell 仍走 DangerousOperation → 需确认（不得变成无条件放行）
        result.ConfirmationRequired.Should().BeTrue("Auto 模式 shell 命令必须保持待确认语义");
    }

    #endregion

    #region 单中间件层：根因定位

    private static IMiddleware<PermissionCheckContext>[] ProbeLayers(IPathPermissionChecker checker) {
        return [
            new BypassPermissionMiddleware(),
            new AgentRestrictionMiddleware(),
            new DangerousCommandProtectionMiddleware(),
            new AutoClassifierMiddleware(),
            new ConfigGetOperationMiddleware(),
            new WebFetchPermissionMiddleware(),
            new EarlyPathDenyMiddleware(checker),
            new ToolListPermissionMiddleware(),
            new PathPermissionMiddleware(checker),
            new DangerousOperationMiddleware(),
            new PlanModeMiddleware(),
            new DefaultResultMiddleware(),
        ];
    }

    private static PermissionCheckContext NewContext(Dictionary<string, JsonElement> args, string toolName) {
        return new PermissionCheckContext {
            ToolName = toolName,
            Arguments = args,
            CurrentMode = PermissionMode.Auto,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = [],
            AutoRejectedTools = [],
        };
    }

    [Fact]
    public async Task AutoClassifierMiddleware_WriteTool_WithRealClassifier_StillReachesPathCheck() {
        // 生产 DI 注入真实 AutoModeClassifier；write 属 SafeWriteTools，分类器曾在此直接 AutoApprove 短路
        await using var middleware = new AutoClassifierMiddleware(
            autoModeClassifier: new AutoModeClassifier());
        var nextCalled = false;

        var context = new PermissionCheckContext {
            ToolName = FileToolNameEnumConstants.FileWrite,
            Arguments = ArgsWithPath(@"C:\Windows\Temp\pwned_single_test.txt"),
            CurrentMode = PermissionMode.Auto,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = [],
            AutoRejectedTools = [],
        };

        await middleware.InvokeAsync(context, (ctx, ct) => {
            nextCalled = true;
            ctx.Result = ToolPermissionCheckResult.Rejected("sentinel");
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("写类工具必须交给 PathPermissionMiddleware 细化路径边界，分类器不得短路");
        context.Result?.Reason.Should().Be("sentinel", "next 决策必须成为最终结果");
    }

    [Fact]
    public async Task ToolListPermissionMiddleware_ReadToolInAutoApprovedList_StillReachesPathCheck() {
        // read 在默认 AutoApprovedTools；曾因 Contains 直接 Approved 短路，绕过路径决策链
        await using var middleware = new ToolListPermissionMiddleware();
        var nextCalled = false;

        var context = new PermissionCheckContext {
            ToolName = FileToolNameEnumConstants.FileRead,
            Arguments = ArgsWithPath(@"C:\Windows\Temp\definitely_missing_secrets.txt"),
            CurrentMode = PermissionMode.Auto,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = [FileToolNameEnumConstants.FileRead],
            AutoRejectedTools = [],
        };

        await middleware.InvokeAsync(context, (ctx, ct) => {
            nextCalled = true;
            ctx.Result = ToolPermissionCheckResult.Rejected("sentinel");
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("带路径参数的文件类工具不得被工具级 allow 短路，路径决策必须先执行");
        context.Result?.Reason.Should().Be("sentinel", "next 决策必须成为最终结果");
    }

    [Fact]
    public async Task PathPermissionMiddleware_CheckerNull_WriteTool_FailsClosed() {
        await using var middleware = new PathPermissionMiddleware(null);

        var context = new PermissionCheckContext {
            ToolName = FileToolNameEnumConstants.FileWrite,
            Arguments = ArgsWithPath(@"C:\Windows\Temp\pwned_nullchecker.txt"),
            CurrentMode = PermissionMode.Auto,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = [],
            AutoRejectedTools = [],
        };

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, CancellationToken.None);

        var decision = context.Result;
        decision.Should().NotBeNull("checker 未注册属于配置错误，必须 fail-closed");
        if (decision is null) {
            return;
        }

        decision.IsApproved.Should().BeFalse("fail-closed 不得放行写类工具");
    }

    #endregion
}
