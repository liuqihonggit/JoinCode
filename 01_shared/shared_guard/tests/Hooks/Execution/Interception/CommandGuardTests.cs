namespace Guard.Tests.Hooks.Execution.Interception;

/// <summary>
/// 守卫单元测试 — 验证 4 个迁移守卫(GhTimeout/GhPrBody/VpnRoute/Heredoc)的 CanHandle/Evaluate 行为
/// </summary>
public sealed class CommandGuardTests
{
    // === GhTimeoutGuard ===

    [Fact]
    public void GhTimeoutGuard_CanHandle_GhCommand_ReturnsTrue()
    {
        var guard = new GhTimeoutGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        guard.CanHandle("gh pr list", ctx).Should().BeTrue();
    }

    [Fact]
    public void GhTimeoutGuard_CanHandle_NonGhCommand_ReturnsFalse()
    {
        var guard = new GhTimeoutGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        guard.CanHandle("git status", ctx).Should().BeFalse();
    }

    [Fact]
    public void GhTimeoutGuard_Evaluate_ReturnsAllow()
    {
        var guard = new GhTimeoutGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        var decision = guard.Evaluate("gh pr list", ctx);

        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    // === GhPrBodyGuard ===

    [Fact]
    public void GhPrBodyGuard_CanHandle_GhPrCreate_ReturnsTrue()
    {
        var guard = new GhPrBodyGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        guard.CanHandle("gh pr create --title foo", ctx).Should().BeTrue();
    }

    [Fact]
    public void GhPrBodyGuard_CanHandle_GhPrList_ReturnsFalse()
    {
        var guard = new GhPrBodyGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        guard.CanHandle("gh pr list", ctx).Should().BeFalse();
    }

    [Fact]
    public void GhPrBodyGuard_Evaluate_MissingBody_ReturnsRewrite()
    {
        var guard = new GhPrBodyGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        var decision = guard.Evaluate("gh pr create --title foo", ctx);

        var rewrite = decision.Should().BeOfType<CommandDecision.Rewrite>().Subject;
        rewrite.NewCommand.Should().Contain("--body");
    }

    [Fact]
    public void GhPrBodyGuard_Evaluate_HasBody_ReturnsAllow()
    {
        var guard = new GhPrBodyGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        var decision = guard.Evaluate("gh pr create --title foo --body existing", ctx);

        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    [Fact]
    public void GhPrBodyGuard_Evaluate_ContextBody_UsedOverDefault()
    {
        var guard = new GhPrBodyGuard();
        var ctx = new Dictionary<string, object> { ["pr_body"] = "custom body content" };

        var decision = guard.Evaluate("gh pr create --title foo", ctx);

        var rewrite = decision.Should().BeOfType<CommandDecision.Rewrite>().Subject;
        rewrite.NewCommand.Should().Contain("custom body content");
    }

    // === VpnRouteGuard ===

    [Fact]
    public void VpnRouteGuard_CanHandle_NoVpnActive_ReturnsFalse()
    {
        // 默认测试环境无 VPN 进程/代理,CanHandle 应返回 false
        var guard = new VpnRouteGuard();
        var ctx = new Dictionary<string, object> { ["proxy_url"] = "http://proxy:8080" };

        guard.CanHandle("git fetch", ctx).Should().BeFalse();
    }

    // === HeredocGuard ===

    [Fact]
    public void HeredocGuard_CanHandle_ContainsHeredocMarker_ReturnsTrue()
    {
        var guard = new HeredocGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        guard.CanHandle("echo <<EOF", ctx).Should().BeTrue();
    }

    [Fact]
    public void HeredocGuard_CanHandle_NoHeredocMarker_ReturnsFalse()
    {
        var guard = new HeredocGuard();
        var ctx = FrozenDictionary<string, object>.Empty;

        guard.CanHandle("echo hello", ctx).Should().BeFalse();
    }

    [Fact]
    public void HeredocGuard_Evaluate_BashShell_ReturnsAllow()
    {
        var guard = new HeredocGuard();
        var ctx = new Dictionary<string, object> { ["ShellKind"] = SystemActuatorKind.Bash };

        var decision = guard.Evaluate("cat <<'EOF'\nhello\nEOF", ctx);

        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    [Fact]
    public void HeredocGuard_Evaluate_PowerShellShell_RewritesHeredoc()
    {
        var guard = new HeredocGuard();
        var ctx = new Dictionary<string, object> { ["ShellKind"] = SystemActuatorKind.PowerShell };

        var decision = guard.Evaluate("cat <<'EOF'\nhello\nEOF", ctx);

        var rewrite = decision.Should().BeOfType<CommandDecision.Rewrite>().Subject;
        rewrite.NewCommand.Should().NotContain("<<'EOF'");
    }

    [Fact]
    public void HeredocGuard_Evaluate_PowerShell_NoHeredocContent_ReturnsAllow()
    {
        var guard = new HeredocGuard();
        var ctx = new Dictionary<string, object> { ["ShellKind"] = SystemActuatorKind.PowerShell };

        // 只有孤立 << 标记,无完整 HEREDOC — 会被转义为 `<`<,产生 Rewrite
        // 用无 << 的命令测试 Allow 路径(但 CanHandle 会 false,这里直接测 Evaluate)
        var decision = guard.Evaluate("echo hello", ctx);

        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    // === 优先级 ===

    [Fact]
    public void Guards_HaveExpectedPriorities()
    {
        new HeredocGuard().Priority.Should().Be(200);
        new GhPrBodyGuard().Priority.Should().Be(100);
        new GhTimeoutGuard().Priority.Should().Be(50);
        new VpnRouteGuard().Priority.Should().Be(30);
    }
}
