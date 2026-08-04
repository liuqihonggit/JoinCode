namespace Mcp.Tests.Management;

/// <summary>
/// ToolInterventionManager 单元测试 — 验证规则增删、黑名单、降权、过期、重定向
/// </summary>
public sealed class ToolInterventionManagerTest : IAsyncLifetime
{
    private InMemoryFileSystem _fs = null!;
    private ToolInterventionManager _manager = null!;

    public Task InitializeAsync()
    {
        _fs = new InMemoryFileSystem();
        _manager = new ToolInterventionManager(_fs);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    // === AddRuleAsync ===

    [Fact]
    public async Task AddRuleAsync_Blacklist_CreatesBlacklistRule()
    {
        await _manager.AddRuleAsync("dangerous_tool", InterventionType.Blacklist, "安全风险");

        var rule = await _manager.GetRuleAsync("dangerous_tool");
        rule.Should().NotBeNull();
        rule!.Type.Should().Be(InterventionType.Blacklist);
        rule.Reason.Should().Be("安全风险");
        rule.Expiry.Should().BeNull();
    }

    [Fact]
    public async Task AddRuleAsync_Downgrade_CreatesDowngradeRuleWithPenalty()
    {
        await _manager.AddRuleAsync("slow_tool", InterventionType.Downgrade, "性能问题");

        var rule = await _manager.GetRuleAsync("slow_tool");
        rule.Should().NotBeNull();
        rule!.Type.Should().Be(InterventionType.Downgrade);
        rule.ScorePenalty.Should().Be(-50);
    }

    [Fact]
    public async Task AddRuleAsync_Redirect_CreatesRedirectRule()
    {
        await _manager.AddRuleAsync("cmd", InterventionType.Redirect, "推荐使用PowerShell");

        var rule = await _manager.GetRuleAsync("cmd");
        rule.Should().NotBeNull();
        rule!.Type.Should().Be(InterventionType.Redirect);
        rule.RedirectTo.Should().Be("powershell");
    }

    [Fact]
    public async Task AddRuleAsync_Redirect_BashRedirectsToPowershell()
    {
        await _manager.AddRuleAsync("bash", InterventionType.Redirect, "推荐使用PowerShell");

        var rule = await _manager.GetRuleAsync("bash");
        rule!.RedirectTo.Should().Be("powershell");
    }

    [Fact]
    public async Task AddRuleAsync_Redirect_UnknownTool_HasNoRedirectTarget()
    {
        await _manager.AddRuleAsync("unknown", InterventionType.Redirect, "无替代");

        var rule = await _manager.GetRuleAsync("unknown");
        rule!.RedirectTo.Should().BeNull();
    }

    [Fact]
    public async Task AddRuleAsync_WithDuration_SetsExpiry()
    {
        var duration = TimeSpan.FromMinutes(30);
        await _manager.AddRuleAsync("temp_tool", InterventionType.Blacklist, "临时禁用", duration);

        var rule = await _manager.GetRuleAsync("temp_tool");
        rule.Should().NotBeNull();
        rule!.Expiry.Should().NotBeNull();
        rule.Expiry!.Value.Should().BeCloseTo(DateTime.UtcNow + duration, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AddRuleAsync_Overwrite_ReplacesExistingRule()
    {
        await _manager.AddRuleAsync("tool_a", InterventionType.Blacklist, "原因1");
        await _manager.AddRuleAsync("tool_a", InterventionType.Downgrade, "原因2");

        var rule = await _manager.GetRuleAsync("tool_a");
        rule.Should().NotBeNull();
        rule!.Type.Should().Be(InterventionType.Downgrade);
        rule.Reason.Should().Be("原因2");
    }

    // === RemoveRuleAsync ===

    [Fact]
    public async Task RemoveRuleAsync_ExistingRule_RemovesRule()
    {
        await _manager.AddRuleAsync("tool_a", InterventionType.Blacklist, "test");
        await _manager.RemoveRuleAsync("tool_a");

        var rule = await _manager.GetRuleAsync("tool_a");
        rule.Should().BeNull();
    }

    [Fact]
    public async Task RemoveRuleAsync_NonExistentRule_DoesNotThrow()
    {
        var act = async () => await _manager.RemoveRuleAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    // === IsBlacklisted ===

    [Fact]
    public async Task IsBlacklisted_BlacklistedTool_ReturnsTrue()
    {
        await _manager.AddRuleAsync("dangerous", InterventionType.Blacklist, "test");
        _manager.IsBlacklisted("dangerous").Should().BeTrue();
    }

    [Fact]
    public async Task IsBlacklisted_NonBlacklistedTool_ReturnsFalse()
    {
        await _manager.AddRuleAsync("slow", InterventionType.Downgrade, "test");
        _manager.IsBlacklisted("slow").Should().BeFalse();
    }

    [Fact]
    public void IsBlacklisted_NoRule_ReturnsFalse()
    {
        _manager.IsBlacklisted("nonexistent").Should().BeFalse();
    }

    [Fact]
    public async Task IsBlacklisted_ExpiredRule_ReturnsFalse()
    {
        await _manager.AddRuleAsync("expired_tool", InterventionType.Blacklist, "test", TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        _manager.IsBlacklisted("expired_tool").Should().BeFalse();
    }

    // === GetScorePenalty ===

    [Fact]
    public async Task GetScorePenalty_DowngradeTool_ReturnsPenalty()
    {
        await _manager.AddRuleAsync("slow", InterventionType.Downgrade, "test");
        _manager.GetScorePenalty("slow").Should().Be(-50);
    }

    [Fact]
    public async Task GetScorePenalty_NonDowngradeTool_ReturnsNull()
    {
        await _manager.AddRuleAsync("dangerous", InterventionType.Blacklist, "test");
        _manager.GetScorePenalty("dangerous").Should().BeNull();
    }

    [Fact]
    public void GetScorePenalty_NoRule_ReturnsNull()
    {
        _manager.GetScorePenalty("nonexistent").Should().BeNull();
    }

    [Fact]
    public async Task GetScorePenalty_ExpiredRule_ReturnsNull()
    {
        await _manager.AddRuleAsync("expired", InterventionType.Downgrade, "test", TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        _manager.GetScorePenalty("expired").Should().BeNull();
    }

    // === 过期规则 ===

    [Fact]
    public async Task GetRuleAsync_ExpiredRule_ReturnsNull()
    {
        await _manager.AddRuleAsync("temp", InterventionType.Blacklist, "test", TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        var rule = await _manager.GetRuleAsync("temp");
        rule.Should().BeNull();
    }

    [Fact]
    public async Task GetRuleAsync_NonExpiredRule_ReturnsRule()
    {
        await _manager.AddRuleAsync("active", InterventionType.Blacklist, "test", TimeSpan.FromHours(1));

        var rule = await _manager.GetRuleAsync("active");
        rule.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRuleAsync_NoExpiry_RuleNeverExpires()
    {
        await _manager.AddRuleAsync("permanent", InterventionType.Blacklist, "永久禁用");

        var rule = await _manager.GetRuleAsync("permanent");
        rule.Should().NotBeNull();
        rule!.Expiry.Should().BeNull();
        rule.IsExpired.Should().BeFalse();
    }

    // === GetActiveRulesAsync ===

    [Fact]
    public async Task GetActiveRulesAsync_ReturnsOnlyNonExpiredRules()
    {
        await _manager.AddRuleAsync("active1", InterventionType.Blacklist, "test");
        await _manager.AddRuleAsync("active2", InterventionType.Downgrade, "test");
        await _manager.AddRuleAsync("expired1", InterventionType.Blacklist, "test", TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        var active = await _manager.GetActiveRulesAsync();
        active.Count.Should().Be(2);
        active.Should().ContainKey("active1");
        active.Should().ContainKey("active2");
    }

    [Fact]
    public async Task GetActiveRulesAsync_NoRules_ReturnsEmptyDictionary()
    {
        var active = await _manager.GetActiveRulesAsync();
        active.Should().BeEmpty();
    }

    // === 持久化 ===

    [Fact]
    public async Task AddRuleAsync_PersistsToDisk()
    {
        await _manager.AddRuleAsync("persist_tool", InterventionType.Blacklist, "持久化测试");

        var manager2 = new ToolInterventionManager(_fs);
        var rule = await manager2.GetRuleAsync("persist_tool");
        rule.Should().NotBeNull();
        rule!.Type.Should().Be(InterventionType.Blacklist);
    }

    // === InterventionType 枚举值 ===

    [Fact]
    public void InterventionType_EnumValues_MatchEnumValueAttributes()
    {
        InterventionType.Blacklist.ToValue().Should().Be("blacklist");
        InterventionType.Downgrade.ToValue().Should().Be("downgrade");
        InterventionType.Redirect.ToValue().Should().Be("redirect");
    }

    // === InterventionRule record ===

    [Fact]
    public void InterventionRule_IsExpired_WithFutureExpiry_ReturnsFalse()
    {
        var rule = new InterventionRule
        {
            Type = InterventionType.Blacklist,
            Reason = "test",
            Expiry = DateTime.UtcNow.AddHours(1)
        };
        rule.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void InterventionRule_IsExpired_WithPastExpiry_ReturnsTrue()
    {
        var rule = new InterventionRule
        {
            Type = InterventionType.Blacklist,
            Reason = "test",
            Expiry = DateTime.UtcNow.AddHours(-1)
        };
        rule.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void InterventionRule_IsExpired_WithNoExpiry_ReturnsFalse()
    {
        var rule = new InterventionRule
        {
            Type = InterventionType.Blacklist,
            Reason = "test",
            Expiry = null
        };
        rule.IsExpired.Should().BeFalse();
    }
}
