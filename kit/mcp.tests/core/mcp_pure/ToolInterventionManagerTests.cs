namespace Mcp.Tests;

/// <summary>
/// ToolInterventionManager 单元测试 — 验证 GetDefaultRedirect 静态分派 + IsBlacklisted/GetScorePenalty 实例判定
/// </summary>
public sealed class ToolInterventionManagerTests {
    [Fact]
    public void GetDefaultRedirect_Cmd_ReturnsPowershell() {
        ToolInterventionManager.GetDefaultRedirect("cmd").Should().Be(ShellToolNameEnumConstants.Powershell);
    }

    [Fact]
    public void GetDefaultRedirect_CmdUpperCase_ReturnsPowershell() {
        // 实现先 ToLowerInvariant,所以 "CMD" → "cmd" → powershell
        ToolInterventionManager.GetDefaultRedirect("CMD").Should().Be(ShellToolNameEnumConstants.Powershell);
    }

    [Fact]
    public void GetDefaultRedirect_Bash_ReturnsPowershell() {
        ToolInterventionManager.GetDefaultRedirect(ShellToolNameEnumConstants.Bash).Should().Be(ShellToolNameEnumConstants.Powershell);
    }

    [Fact]
    public void GetDefaultRedirect_BashUpperCase_ReturnsNull() {
        // ShellToolNameEnumConstants.Bash = "bash", ToLowerInvariant 后 "bash" 匹配
        // 但 "BASH" → "bash" 也匹配(因为 switch 用 ShellToolNameEnumConstants.Bash = "bash")
        ToolInterventionManager.GetDefaultRedirect("BASH").Should().Be(ShellToolNameEnumConstants.Powershell);
    }

    [Theory]
    [InlineData("python")]
    [InlineData("ruby")]
    [InlineData("unknown")]
    [InlineData("")]
    public void GetDefaultRedirect_OtherTools_ReturnsNull(string toolName) {
        ToolInterventionManager.GetDefaultRedirect(toolName).Should().BeNull();
    }

    [Fact]
    public async Task IsBlacklisted_BlacklistRule_ReturnsTrue() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        await manager.AddRuleAsync("dangerous-tool", InterventionType.Blacklist, "test reason");
        manager.IsBlacklisted("dangerous-tool").Should().BeTrue();
    }

    [Fact]
    public async Task IsBlacklisted_NonBlacklistRule_ReturnsFalse() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        await manager.AddRuleAsync("downgraded-tool", InterventionType.Downgrade, "test reason");
        manager.IsBlacklisted("downgraded-tool").Should().BeFalse();
    }

    [Fact]
    public async Task IsBlacklisted_NoRule_ReturnsFalse() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        manager.IsBlacklisted("no-such-tool").Should().BeFalse();
    }

    [Fact]
    public async Task IsBlacklisted_ExpiredRule_ReturnsFalse() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        // duration 设为负值,立即过期
        await manager.AddRuleAsync("expired-tool", InterventionType.Blacklist, "test", TimeSpan.FromMilliseconds(-1));
        // 等待规则过期生效
        await Task.Delay(10);
        manager.IsBlacklisted("expired-tool").Should().BeFalse();
    }

    [Fact]
    public async Task GetScorePenalty_DowngradeRule_ReturnsPenalty() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        await manager.AddRuleAsync("downgraded", InterventionType.Downgrade, "test");
        var penalty = manager.GetScorePenalty("downgraded");
        penalty.Should().Be(-50);
    }

    [Fact]
    public async Task GetScorePenalty_BlacklistRule_ReturnsNull() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        await manager.AddRuleAsync("blacklisted", InterventionType.Blacklist, "test");
        manager.GetScorePenalty("blacklisted").Should().BeNull();
    }

    [Fact]
    public async Task GetScorePenalty_NoRule_ReturnsNull() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        manager.GetScorePenalty("no-such-tool").Should().BeNull();
    }

    [Fact]
    public async Task GetScorePenalty_ExpiredRule_ReturnsNull() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        await manager.AddRuleAsync("expired", InterventionType.Downgrade, "test", TimeSpan.FromMilliseconds(-1));
        await Task.Delay(10);
        manager.GetScorePenalty("expired").Should().BeNull();
    }
}
