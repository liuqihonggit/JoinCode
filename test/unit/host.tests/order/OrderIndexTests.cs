namespace Host.Tests.Order;

/// <summary>
/// 注册顺序表守卫 — <c>resources/orders.yaml</c> 是「谁先执行」的唯一数据源，
/// 实现类里的位次必须由它派生，不得写死数字。
/// <para>本测试锁数据形状与顺序基线；源码级双向对账与真实执行顺序以
/// <c>non_deliverables_tools/scripts/Verify-RegistrationOrder.py</c> 为准。</para>
/// </summary>
public sealed class OrderIndexTests {
    /// <summary>命令守卫链的顺序基线 — 迁移前由 Priority 1000→30 降序决定，执行顺序不得改变。</summary>
    private static readonly string[] ExpectedCommandGuardOrder = [
        "GitCommitGuard",
        "DriveProtectionGuard",
        "CmdIndirectCallGuard",
        "RobocopyMirrorGuard",
        "HeredocGuard",
        "GhPrBodyGuard",
        "GhTimeoutGuard",
        "VpnRouteGuard",
    ];

    [Fact]
    public void Default_LoadsEveryDeclaredList() {
        var index = OrderIndex.Default;

        index.Get("command_guards").Should().NotBeEmpty("命令守卫链是安全相关机制，必须有显式顺序");
        index.Get("command_interceptors").Should().NotBeNull();
        index.Get("tool_fix_hooks").Should().NotBeEmpty();
        index.Get("agent_tool_middlewares").Should().NotBeEmpty();
        index.Get("query_stop_hooks").Should().NotBeNull();
    }

    [Fact]
    public void Get_UnknownList_ReturnsEmpty() {
        OrderIndex.Default.Get("没有这个列表").Should().BeEmpty();
    }

    [Fact]
    public void Require_KnownName_ReturnsItsPosition() {
        OrderIndex.Default.Require("command_guards", "GitCommitGuard").Should().Be(0);
        OrderIndex.Default.Require("command_guards", "VpnRouteGuard").Should().Be(7);
    }

    [Fact]
    public void Require_UnknownName_ThrowsWithActionableGuidance() {
        var act = () => OrderIndex.Default.Require("command_guards", "没登记的守卫");

        var error = act.Should().Throw<InvalidOperationException>().Which;
        error.Message.Should().Contain("command_guards");
        error.Message.Should().Contain("没登记的守卫");
        // 错误提示必须告诉调用方怎么改，而不是只说「不允许」
        error.Message.Should().Contain("resources/orders.yaml");
        error.Message.Should().Contain("已登记");
    }

    [Fact]
    public void PriorityOf_FirstItemGetsHighestValue_AndLastGetsOne() {
        // 守卫链用「数字大 = 先执行」的约定，位次由表反推
        OrderIndex.Default.PriorityOf("command_guards", "GitCommitGuard")
            .Should().BeGreaterThan(OrderIndex.Default.PriorityOf("command_guards", "DriveProtectionGuard"));
        OrderIndex.Default.PriorityOf("command_guards", "VpnRouteGuard").Should().Be(1);
    }

    [Fact]
    public void OrderOf_FirstItemGetsLowestValue() {
        // 中间件链用「数字小 = 先执行」的约定
        OrderIndex.Default.OrderOf("agent_tool_middlewares", "AgentValidationMiddleware").Should().Be(1);
        OrderIndex.Default.OrderOf("agent_tool_middlewares", "AgentHandoffMiddleware")
            .Should().BeGreaterThan(OrderIndex.Default.OrderOf("agent_tool_middlewares", "AgentValidationMiddleware"));
    }

    [Fact]
    public void CommandGuardOrder_MatchesPreMigrationBaseline() {
        // 迁移前顺序由 Priority 数字决定（1000/900/800/500/200/100/50/30 降序），
        // 迁移到 YAML 后执行顺序必须一模一样 —— 这是「行为不变」的硬约束
        OrderIndex.Default.Get("command_guards").Should().Equal(ExpectedCommandGuardOrder);
    }

    [Fact]
    public void FromText_DuplicateName_Throws() {
        const string yaml = """
            version: 1
            lists:
              demo:
                - A
                - A
            """;

        var act = () => OrderIndex.FromText(yaml, "test");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("demo");
    }

    [Fact]
    public void FromText_ListMustBeSequence_Throws() {
        const string yaml = """
            version: 1
            lists:
              demo: not-a-sequence
            """;

        var act = () => OrderIndex.FromText(yaml, "test");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("demo");
    }

    [Fact]
    public void FromText_MissingListsMapping_Throws() {
        const string yaml = """
            version: 1
            """;

        var act = () => OrderIndex.FromText(yaml, "test");

        act.Should().Throw<InvalidOperationException>();
    }
}
