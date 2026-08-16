namespace Guard.Tests.Permission.Utils;

public sealed class ToolFilterPolicyTests
{
    private readonly ToolFilterPolicy _sut = new();

    private static readonly FrozenSet<string> EmptySet = FrozenSet<string>.Empty;

    [Fact]
    public void Check_BypassMode_ShouldAlwaysAllow()
    {
        var context = new ToolFilterContext(
            "any_tool",
            PermissionMode.Bypass,
            EmptySet,
            null,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeTrue();
        result.DeniedLayer.Should().Be(0);
    }

    [Fact]
    public void Check_ToolInAllAgentDisallowed_ShouldDeny_Layer1()
    {
        var disallowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "dangerous_tool");

        var context = new ToolFilterContext(
            "dangerous_tool",
            PermissionMode.Auto,
            disallowed,
            null,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(1);
        result.Reason.Should().Contain("全局禁用");
    }

    [Fact]
    public void Check_WildcardInAllAgentDisallowed_ShouldDeny_Layer1()
    {
        var disallowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "*");

        var context = new ToolFilterContext(
            "any_tool",
            PermissionMode.Auto,
            disallowed,
            null,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(1);
        result.Reason.Should().Contain("通配符");
    }

    [Fact]
    public void Check_ToolNotInAllowedWhitelist_ShouldDeny_Layer2()
    {
        var allowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "tool_a", "tool_b");

        var context = new ToolFilterContext(
            "tool_c",
            PermissionMode.Auto,
            EmptySet,
            allowed,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(2);
        result.Reason.Should().Contain("白名单");
    }

    [Fact]
    public void Check_ToolInAllowedWhitelist_ShouldAllow()
    {
        var allowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "tool_a", "tool_b");

        var context = new ToolFilterContext(
            "tool_a",
            PermissionMode.Auto,
            EmptySet,
            allowed,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Check_EmptyAllowedWhitelist_ShouldNotRestrict()
    {
        var context = new ToolFilterContext(
            "any_tool",
            PermissionMode.Auto,
            EmptySet,
            EmptySet,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Check_NullAllowedWhitelist_ShouldNotRestrict()
    {
        var context = new ToolFilterContext(
            "any_tool",
            PermissionMode.Auto,
            EmptySet,
            null,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Check_ToolInAgentDisallowed_ShouldDeny_Layer3()
    {
        var agentDenied = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "forbidden_tool");

        var context = new ToolFilterContext(
            "forbidden_tool",
            PermissionMode.Auto,
            EmptySet,
            null,
            agentDenied);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(3);
        result.Reason.Should().Contain("代理定义禁用");
    }

    [Fact]
    public void Check_Layer1_ShouldTakePrecedenceOverLayer3()
    {
        var allDisallowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "recursive_tool");
        var agentDenied = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "recursive_tool");

        var context = new ToolFilterContext(
            "recursive_tool",
            PermissionMode.Auto,
            allDisallowed,
            null,
            agentDenied);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(1);
    }

    [Fact]
    public void Check_Layer1_ShouldTakePrecedenceOverLayer2()
    {
        var allDisallowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "dangerous");
        var allowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "dangerous", "safe");

        var context = new ToolFilterContext(
            "dangerous",
            PermissionMode.Auto,
            allDisallowed,
            allowed,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(1);
    }

    [Fact]
    public void Check_Layer2_ShouldTakePrecedenceOverLayer3()
    {
        var allowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "tool_a");
        var agentDenied = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "tool_b");

        var context = new ToolFilterContext(
            "tool_b",
            PermissionMode.Auto,
            EmptySet,
            allowed,
            agentDenied);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(2);
    }

    [Fact]
    public void Check_AllLayersPass_ShouldAllow()
    {
        var allDisallowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "bad_tool");
        var allowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "good_tool", "another_good");
        var agentDenied = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "bad_tool");

        var context = new ToolFilterContext(
            "good_tool",
            PermissionMode.Auto,
            allDisallowed,
            allowed,
            agentDenied);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeTrue();
        result.DeniedLayer.Should().Be(0);
    }

    [Fact]
    public void Check_CaseInsensitive_ShouldMatch()
    {
        var disallowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "Dangerous_Tool");

        var context = new ToolFilterContext(
            "dangerous_tool",
            PermissionMode.Auto,
            disallowed,
            null,
            null);

        var result = _sut.Check(context);

        result.IsAllowed.Should().BeFalse();
        result.DeniedLayer.Should().Be(1);
    }
}
