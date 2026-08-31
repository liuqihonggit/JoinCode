namespace Guard.Security.Tests;

/// <summary>
/// DangerousCommandProtectionMiddleware 单元测试 — 验证 Bypass 模式下黑灯(Dangerous)穿透漏洞已修复
/// 安全红线: Dangerous 级在所有模式(含 Bypass)下都必须拒绝
/// </summary>
public class DangerousCommandProtectionMiddlewareTests
{
    private readonly CommandDangerClassifier _classifier = new();

    /// <summary>
    /// 构造 Shell 工具权限检查上下文
    /// </summary>
    private static PermissionCheckContext CreateContext(PermissionMode mode, string command, HashSet<CommandDangerLevel>? approvedLevels = null)
    {
        var config = PermissionConfig.CreateDefault();
        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement(command)
        };
        return new PermissionCheckContext
        {
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

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("rm -r -f /")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("fdisk /dev/sda")]
    [InlineData("shred /etc/passwd")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    public async Task Bypass_Mode_Should_Reject_Dangerous_Command(string command)
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
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
    public async Task Bypass_Mode_Should_Allow_NonDangerous_Command(string command)
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(PermissionMode.Bypass, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("非 Dangerous 命令在 Bypass 下应放行");
        context.Result.Should().BeNull("非 Dangerous 命令不应设置拒绝结果");
    }

    #endregion

    #region 非 Bypass 模式 Dangerous 拒绝验证

    [Theory]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Ask)]
    public async Task All_NonBypass_Modes_Should_Reject_Dangerous(PermissionMode mode)
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var context = CreateContext(mode, "rm -rf /");

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsApproved.Should().BeFalse();
    }

    #endregion

    #region 同级别自动通过验证

    [Fact]
    public async Task SameLevelApproval_LightValidation_Should_AutoApprove()
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.LightValidation };
        var context = CreateContext(PermissionMode.Ask, "git commit -m \"msg\"", approved);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("同等级已批准应自动放行");
        context.Result.Should().BeNull("放行不应设置拒绝结果");
    }

    [Fact]
    public async Task SameLevelApproval_Execution_Should_AutoApprove()
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.Execution };
        var context = CreateContext(PermissionMode.Ask, "rm file.txt", approved);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("同等级已批准应自动放行");
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task DifferentLevelApproval_Should_Still_Confirm()
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.LightValidation };
        var context = CreateContext(PermissionMode.Ask, "rm file.txt", approved);

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull("不同等级不应自动放行");
        context.Result!.ConfirmationRequired.Should().BeTrue("红灯命令在绿灯已批准时仍需确认");
    }

    [Fact]
    public async Task DangerousLevel_Should_Never_AutoApprove_Even_If_In_ApprovedLevels()
    {
        var middleware = new DangerousCommandProtectionMiddleware(dangerClassifier: _classifier);
        var approved = new HashSet<CommandDangerLevel> { CommandDangerLevel.Dangerous };
        var context = CreateContext(PermissionMode.Ask, "rm -rf /", approved);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        context.Result.Should().NotBeNull("Dangerous 永不自动通过");
        context.Result!.IsApproved.Should().BeFalse();
        nextCalled.Should().BeFalse();
    }

    #endregion
}
