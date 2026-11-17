// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
﻿namespace Guard.Security.Tests;

/// <summary>
/// DangerousCommandProtectionMiddleware 单元测试 — 验证 Bypass 模式下黑灯(Dangerous)穿透漏洞已修复
/// 安全红线: Dangerous 级在所有模式(含 Bypass)下都必须拒绝
/// </summary>
public class DangerousCommandProtectionMiddlewareTests {
    private readonly CommandDangerClassifier _classifier = new();

    /// <summary>
    /// 构造 Shell 工具权限检查上下文
    /// </summary>
    private static PermissionCheckContext CreateContext(PermissionMode mode, string command, HashSet<CommandDangerLevel>? approvedLevels = null) {
        var config = PermissionConfig.CreateDefault();
        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(command, HooksJsonContext.Default.String)
        };
        return new PermissionCheckContext {
            ToolName = "bash",
            Arguments = args,
            CurrentMode = mode,
            Config = config,
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ApprovedLevels = approvedLevels ?? []
        };
    }

    #region Bypass 模式黑灯穿透漏洞修复验证

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("rm -r -f /")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("fdisk /dev/sda")]
    [InlineData("shred /etc/passwd")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    public async Task Bypass_Mode_Should_Reject_Dangerous_Command(string command) {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(PermissionMode.Bypass, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        context.Result.Should().NotBeNull("Dangerous 命令在 Bypass 下必须被拦截");
        context.Result!.IsApproved.Should().BeFalse("Dangerous 命令必须被拒绝");
        nextCalled.Should().BeFalse("Dangerous 命令不应放行到 next");
    }

    [Theory]
    [InlineData("rm file.txt")]
    [InlineData("git commit -m \"msg\"")]
    [InlineData("ls")]
    public async Task Bypass_Mode_Should_Allow_NonDangerous_Command(string command) {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(PermissionMode.Bypass, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("非 Dangerous 命令在 Bypass 下应放行");
        context.Result.Should().BeNull("非 Dangerous 命令不应设置拒绝结果");
    }

    #endregion

    #region 非 Bypass 模式 Dangerous 拒绝验证

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Ask)]
    public async Task All_NonBypass_Modes_Should_Reject_Dangerous(PermissionMode mode) {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(mode, "rm -rf /");

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
    }

    #endregion

    #region 同级别自动通过验证

    [Fact]
    public async Task SameLevelApproval_LightValidation_Should_AutoApprove() {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.LightValidation };
        var context = CreateContext(PermissionMode.Ask, "git commit -m \"msg\"", approved);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("同等级已批准应自动放行");
        context.Result.Should().BeNull("放行不应设置拒绝结果");
    }

    [Fact]
    public async Task SameLevelApproval_Execution_Should_AutoApprove() {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.Execution };
        var context = CreateContext(PermissionMode.Ask, "rm file.txt", approved);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("同等级已批准应自动放行");
        context.Result.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task DifferentLevelApproval_Should_Still_Confirm() {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.LightValidation };
        var context = CreateContext(PermissionMode.Ask, "rm file.txt", approved);

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull("不同等级不应自动放行");
        context.Result!.ConfirmationRequired.Should().BeTrue("红灯命令在绿灯已批准时仍需确认");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task DangerousLevel_Should_Never_AutoApprove_Even_If_In_ApprovedLevels() {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.Dangerous };
        var context = CreateContext(PermissionMode.Ask, "rm -rf /", approved);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        context.Result.Should().NotBeNull("Dangerous 永不自动通过");
        context.Result!.IsApproved.Should().BeFalse();
        nextCalled.Should().BeFalse();
    }

    #endregion

    #region B4: 全模式审计记录验证

    /// <summary>
    /// 捕获 auditor — 记录所有 Record 调用供测试验证
    /// </summary>
    private sealed class CapturingAuditor : ICommandExecutionAuditor {
        public List<CommandExecutionAuditEntry> Entries { get; } = [];
        public ValueTask Record(CommandExecutionAuditEntry entry) {
            Entries.Add(entry);
            return default;
        }
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Ask)]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.Unattended)]
    public async Task All_Modes_Should_Produce_Audit_Record(PermissionMode mode) {
        var auditor = new CapturingAuditor();
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier, auditor: auditor);
        var context = CreateContext(mode, "git commit -m \"msg\"");

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        auditor.Entries.Should().NotBeEmpty($"{mode} 模式应产生审计记录");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Bypass_Mode_Should_Produce_Audit_Record_For_NonDangerous() {
        var auditor = new CapturingAuditor();
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier, auditor: auditor);
        var context = CreateContext(PermissionMode.Bypass, "git commit -m \"msg\"");

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        auditor.Entries.Should().NotBeEmpty("Bypass 模式放行非危险命令应产生审计记录");
    }

    #endregion

    #region 复合命令不得靠前缀降级

    // 回归锁：分类器按「首词查表」工作，若不做子命令拆分，
    // `echo hi; rm -rf ./build` 会因首词 echo 被判 Safe —— Safe 是所有权限模式下**直接放行**的级别，
    // 而单跑 `rm -rf ./build` 是 Execution（Auto 模式会被拒）。加个无害前缀就能绕过，属明确降级。
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData("echo hi; rm -rf ./build")]
    [InlineData("true && rm -rf ./build")]
    [InlineData("false || rm -rf ./build")]
    [InlineData("echo hi\nrm -rf ./build")]
    // 管道与分号是同一类降级：首词 echo 无害，真正删文件的是右侧
    [InlineData("echo hi | xargs rm -rf ./build")]
    [InlineData("echo hi | sh")]
    public async Task Auto_Mode_Rejects_CompoundCommand_LikeItsWorstSegment(string command) {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(PermissionMode.Auto, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse("复合命令里的危险子命令不得被前缀放行");
        context.Result.Should().NotBeNull("应给出拒绝或待确认结果");
        context.Result!.IsApproved.Should().BeFalse();
    }

    // 反向锁：判定收严不能变成「见管道就拒」——良性管道在 Auto 模式下必须照常放行，
    // 否则最常见的 `| head` / `| jq` 都会被拦，用户会因为噪音关掉整个防护。
    // git 只读 + 只读管道同样属于此列（`git log | head` 曾按「有管道就需确认」被拒，已修）。
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData("ls -la | sort -k5 -n | head -20")]
    [InlineData("cat notes.sh | head -3")]
    [InlineData("git log | head -20")]
    [InlineData("git diff | cat")]
    [InlineData("git log --oneline | grep fix")]
    public async Task Auto_Mode_Allows_BenignPipeline(string command) {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(PermissionMode.Auto, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue($"良性管道必须放行：'{command}'");
    }

    // 放宽的边界：git 只读命令接「未登记」的管道目标仍要确认 —— 我们只是承认
    // 「右侧是已登记的只读命令」，不是承认「有管道就安全」。
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData("git log | tee out.txt")]
    [InlineData("git log | unknown-tool --flag")]
    public async Task Auto_Mode_Rejects_GitPipeToUnknownTarget(string command) {
        await using var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(PermissionMode.Auto, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse($"git 管道右侧未登记命令应需确认：'{command}'");
        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
    }

    #endregion
}