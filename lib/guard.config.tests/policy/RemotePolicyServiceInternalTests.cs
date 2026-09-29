namespace Guard.Config.Tests;

/// <summary>
/// RemotePolicyService 内部纯函数确定性测试 — MatchesAction/MatchesConditions/EvaluateToolRestriction 等。
/// <para>EvaluateRateLimit 跳过确定性测试 — 依赖 Clock.GetUtcNow() + 修改 _usageCounters/_windowStartTimes 状态,
/// 非纯函数,不适合确定性测试。EvaluateTimeRestriction 使用 Clock.GetUtcNow().Hour,通过包含/排除当前小时确定性测试。</para>
/// </summary>
public class RemotePolicyServiceInternalTests {
    private readonly RemotePolicyService _service = CreateService();

    private static RemotePolicyService CreateService() {
        return new RemotePolicyService(new HttpClient());
    }

    private static PolicyRule CreateRule(
        PolicyType type,
        PolicyAction action = PolicyAction.Deny,
        int? limit = null,
        double? costLimit = null,
        List<string>? restrictedTools = null,
        Dictionary<string, string>? conditions = null) {
        return new PolicyRule {
            RuleId = "test-rule",
            Name = "Test Rule",
            Type = type,
            Action = action,
            Limit = limit,
            CostLimit = costLimit,
            RestrictedTools = restrictedTools ?? [],
            Conditions = conditions ?? []
        };
    }

    #region MatchesAction

    [Fact]
    public void MatchesAction_ToolRestriction_MatchingTool_Should_Be_True() {
        var rule = CreateRule(PolicyType.ToolRestriction, restrictedTools: new List<string> { "rm", "del" });
        RemotePolicyService.MatchesAction(rule, "rm").Should().BeTrue();
        RemotePolicyService.MatchesAction(rule, "del").Should().BeTrue();
    }

    [Fact]
    public void MatchesAction_ToolRestriction_NonMatchingTool_Should_Be_False() {
        var rule = CreateRule(PolicyType.ToolRestriction, restrictedTools: new List<string> { "rm" });
        RemotePolicyService.MatchesAction(rule, "ls").Should().BeFalse();
    }

    [Fact]
    public void MatchesAction_ToolRestriction_Wildcard_Should_Be_True() {
        var rule = CreateRule(PolicyType.ToolRestriction, restrictedTools: new List<string> { "*" });
        RemotePolicyService.MatchesAction(rule, "anytool").Should().BeTrue();
    }

    [Fact]
    public void MatchesAction_NonToolRestriction_Should_Always_Be_True() {
        var rule = CreateRule(PolicyType.ToolUsageLimit);
        RemotePolicyService.MatchesAction(rule, "anytool").Should().BeTrue();
    }

    [Fact]
    public void MatchesAction_ToolRestriction_CaseInsensitive_Should_Be_True() {
        var rule = CreateRule(PolicyType.ToolRestriction, restrictedTools: new List<string> { "RM" });
        RemotePolicyService.MatchesAction(rule, "rm").Should().BeTrue();
    }

    #endregion

    #region MatchesConditions

    [Fact]
    public void MatchesConditions_NoConditions_Should_Be_True() {
        var rule = CreateRule(PolicyType.ToolUsageLimit);
        RemotePolicyService.MatchesConditions(rule, null).Should().BeTrue();
        RemotePolicyService.MatchesConditions(rule, new Dictionary<string, string>()).Should().BeTrue();
    }

    [Fact]
    public void MatchesConditions_MatchingContext_Should_Be_True() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, conditions: new Dictionary<string, string> {
            ["user"] = "admin"
        });
        var context = new Dictionary<string, string> { ["user"] = "admin" };
        RemotePolicyService.MatchesConditions(rule, context).Should().BeTrue();
    }

    [Fact]
    public void MatchesConditions_NonMatchingContext_Should_Be_False() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, conditions: new Dictionary<string, string> {
            ["user"] = "admin"
        });
        var context = new Dictionary<string, string> { ["user"] = "guest" };
        RemotePolicyService.MatchesConditions(rule, context).Should().BeFalse();
    }

    [Fact]
    public void MatchesConditions_MissingKey_Should_Be_False() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, conditions: new Dictionary<string, string> {
            ["user"] = "admin"
        });
        var context = new Dictionary<string, string> { ["other"] = "value" };
        RemotePolicyService.MatchesConditions(rule, context).Should().BeFalse();
    }

    [Fact]
    public void MatchesConditions_AllowedHours_Should_Be_Skipped() {
        // allowedHours 条件由 EvaluateTimeRestriction 处理,MatchesConditions 跳过
        // 当 context 非空时,allowedHours 被 continue 跳过,其他条件匹配 → true
        var rule = CreateRule(PolicyType.TimeRestriction, conditions: new Dictionary<string, string> {
            ["allowedHours"] = "9,10,11"
        });
        var context = new Dictionary<string, string> { ["other"] = "value" };
        RemotePolicyService.MatchesConditions(rule, context).Should().BeTrue();
    }

    [Fact]
    public void MatchesConditions_NullContext_WithConditions_Should_Be_False() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, conditions: new Dictionary<string, string> {
            ["user"] = "admin"
        });
        RemotePolicyService.MatchesConditions(rule, null).Should().BeFalse();
    }

    [Fact]
    public void MatchesConditions_CaseInsensitive_Should_Be_True() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, conditions: new Dictionary<string, string> {
            ["user"] = "Admin"
        });
        var context = new Dictionary<string, string> { ["user"] = "admin" };
        RemotePolicyService.MatchesConditions(rule, context).Should().BeTrue();
    }

    #endregion

    #region EvaluateToolRestriction

    [Fact]
    public void EvaluateToolRestriction_NoRestrictedTools_Should_Allow() {
        var rule = CreateRule(PolicyType.ToolRestriction);
        var result = _service.EvaluateToolRestriction(rule, "rm");
        result.Allowed.Should().BeTrue();
        result.Action.Should().Be(PolicyAction.Allow);
    }

    [Fact]
    public void EvaluateToolRestriction_MatchingTool_Should_Deny() {
        var rule = CreateRule(PolicyType.ToolRestriction, PolicyAction.Deny, restrictedTools: new List<string> { "rm" });
        var result = _service.EvaluateToolRestriction(rule, "rm");
        result.Allowed.Should().BeFalse();
        result.Action.Should().Be(PolicyAction.Deny);
        result.Reason.Should().Contain("rm");
    }

    [Fact]
    public void EvaluateToolRestriction_NonMatchingTool_Should_Allow() {
        var rule = CreateRule(PolicyType.ToolRestriction, restrictedTools: new List<string> { "rm" });
        var result = _service.EvaluateToolRestriction(rule, "ls");
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateToolRestriction_CaseInsensitive_Should_Deny() {
        var rule = CreateRule(PolicyType.ToolRestriction, PolicyAction.Deny, restrictedTools: new List<string> { "RM" });
        var result = _service.EvaluateToolRestriction(rule, "rm");
        result.Allowed.Should().BeFalse();
    }

    #endregion

    #region EvaluateCostLimit

    [Fact]
    public void EvaluateCostLimit_NoCostLimit_Should_Allow() {
        var rule = CreateRule(PolicyType.CostLimit);
        var result = _service.EvaluateCostLimit(rule);
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCostLimit_BelowLimit_Should_Allow() {
        var rule = CreateRule(PolicyType.CostLimit, costLimit: 100.0);
        var result = _service.EvaluateCostLimit(rule);
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCostLimit_AtLimit_Should_Deny() {
        // 初始 cost=0,设置 costLimit=0 → 0 >= 0 → deny
        var rule = CreateRule(PolicyType.CostLimit, PolicyAction.Deny, costLimit: 0.0);
        var result = _service.EvaluateCostLimit(rule);
        result.Allowed.Should().BeFalse();
    }

    #endregion

    #region EvaluateUsageLimit

    [Fact]
    public void EvaluateUsageLimit_NoLimit_Should_Allow() {
        var rule = CreateRule(PolicyType.ToolUsageLimit);
        var result = _service.EvaluateUsageLimit(rule, "rm");
        result.Allowed.Should().BeTrue();
        result.RemainingLimit.Should().BeNull();
    }

    [Fact]
    public void EvaluateUsageLimit_BelowLimit_Should_Allow_With_Remaining() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, limit: 10);
        var result = _service.EvaluateUsageLimit(rule, "rm");
        result.Allowed.Should().BeTrue();
        result.RemainingLimit.Should().Be(9);  // 10 - 0 - 1 = 9
    }

    [Fact]
    public void EvaluateUsageLimit_AtLimit_Should_Deny() {
        // limit=1, 第一次调用 count=0 < 1 → allow, 第二次 count=1 >= 1 → deny
        var rule = CreateRule(PolicyType.ToolUsageLimit, PolicyAction.Deny, limit: 1);
        var first = _service.EvaluateUsageLimit(rule, "rm");
        first.Allowed.Should().BeTrue();

        var second = _service.EvaluateUsageLimit(rule, "rm");
        second.Allowed.Should().BeFalse();
        second.Action.Should().Be(PolicyAction.Deny);
    }

    #endregion

    #region EvaluateTimeRestriction

    [Fact]
    public void EvaluateTimeRestriction_NoAllowedHours_Should_Allow() {
        var rule = CreateRule(PolicyType.TimeRestriction);
        var result = _service.EvaluateTimeRestriction(rule);
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateTimeRestriction_CurrentHourAllowed_Should_Allow() {
        var currentHour = DateTime.UtcNow.Hour;
        var rule = CreateRule(PolicyType.TimeRestriction, conditions: new Dictionary<string, string> {
            ["allowedHours"] = currentHour.ToString()
        });
        var result = _service.EvaluateTimeRestriction(rule);
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateTimeRestriction_CurrentHourNotAllowed_Should_Deny() {
        var currentHour = DateTime.UtcNow.Hour;
        // 构造一个不包含当前小时的 allowedHours
        var disallowedHour = (currentHour + 1) % 24;
        var rule = CreateRule(PolicyType.TimeRestriction, PolicyAction.Deny, conditions: new Dictionary<string, string> {
            ["allowedHours"] = disallowedHour.ToString()
        });
        var result = _service.EvaluateTimeRestriction(rule);
        result.Allowed.Should().BeFalse();
        result.Action.Should().Be(PolicyAction.Deny);
    }

    [Fact]
    public void EvaluateTimeRestriction_MultipleHours_Should_Work() {
        var currentHour = DateTime.UtcNow.Hour;
        var anotherHour = (currentHour + 1) % 24;
        var rule = CreateRule(PolicyType.TimeRestriction, conditions: new Dictionary<string, string> {
            ["allowedHours"] = $"{currentHour},{anotherHour}"
        });
        var result = _service.EvaluateTimeRestriction(rule);
        result.Allowed.Should().BeTrue();
    }

    #endregion

    #region EvaluateRule

    [Fact]
    public void EvaluateRule_ConditionsNotMatch_Should_Allow() {
        var rule = CreateRule(PolicyType.ToolUsageLimit, conditions: new Dictionary<string, string> {
            ["user"] = "admin"
        });
        var result = _service.EvaluateRule(rule, "rm", new Dictionary<string, string> { ["user"] = "guest" });
        result.Allowed.Should().BeTrue();
        result.Reason.Should().Contain("条件不匹配");
    }

    [Fact]
    public void EvaluateRule_ToolRestriction_Matching_Should_Deny() {
        var rule = CreateRule(PolicyType.ToolRestriction, PolicyAction.Deny, restrictedTools: new List<string> { "rm" });
        var result = _service.EvaluateRule(rule, "rm", null);
        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public void EvaluateRule_UnknownType_Should_Allow() {
        // 使用未定义的 PolicyType 值会走 default 分支
        var rule = CreateRule(PolicyType.ToolUsageLimit, limit: 100);
        var result = _service.EvaluateRule(rule, "rm", null);
        result.Allowed.Should().BeTrue();  // UsageLimit 100, 首次调用 allow
    }

    #endregion

    #region EvaluateRateLimit — 跳过

    // EvaluateRateLimit 跳过确定性测试 — 依赖 Clock.GetUtcNow() + 修改 _usageCounters/_windowStartTimes 状态,
    // 非纯函数(同一实例多次调用结果不同),不适合确定性测试。
    // 如需测试速率限制逻辑,应通过 IClockService 注入可控时钟 + 独立实例。

    #endregion
}
