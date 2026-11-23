namespace Mcp.Tests;

/// <summary>
/// ToolInterventionManager 时序测试 — 验证规则过期行为(依赖 Task.Delay 让过期生效)。
/// 从 mcp_pure/ToolInterventionManagerTests.cs 迁移,因依赖时序不属于确定性测试。
/// </summary>
[Trait("Category", "Timing")]
public sealed class ToolInterventionManagerTimingTests {
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
    public async Task GetScorePenalty_ExpiredRule_ReturnsNull() {
        await using var manager = new ToolInterventionManager(new InMemoryFileSystem());
        await manager.AddRuleAsync("expired", InterventionType.Downgrade, "test", TimeSpan.FromMilliseconds(-1));
        await Task.Delay(10);
        manager.GetScorePenalty("expired").Should().BeNull();
    }
}
