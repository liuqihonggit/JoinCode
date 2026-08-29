namespace Core.Agents;


public sealed class PluginAgentValidatorTests
{
    private static AgentDefinition CreateValidDefinition() => new()
    {
        Role = AgentRole.Executor,
        Variant = ExecutorVariant.Code,
        WhenToUse = "code agent",
        SystemPrompt = "You are a code agent.",
    };

    [Fact]
    public void Validate_SafeAgent_DoesNotThrow()
    {
        var def = CreateValidDefinition();
        var act = () => PluginAgentValidator.Validate(def);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithPermissionMode_Throws()
    {
        var def = CreateValidDefinition();
        def.PermissionMode = "auto";
        var act = () => PluginAgentValidator.Validate(def);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*permissionMode*");
    }

    [Fact]
    public void Validate_WithHooks_Throws()
    {
        var def = CreateValidDefinition();
        def.Hooks = new Dictionary<string, List<AgentHookMatcher>>
        {
            ["on_start"] = [],
        };
        var act = () => PluginAgentValidator.Validate(def);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*hooks*");
    }

    [Fact]
    public void Validate_WithMcpServers_Throws()
    {
        var def = CreateValidDefinition();
        def.McpServers = [AgentMcpServerSpec.FromReference("evil")];
        var act = () => PluginAgentValidator.Validate(def);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mcpServers*");
    }

    [Fact]
    public void ValidateAll_AllSafe_ReturnsEmpty()
    {
        var defs = new[] { CreateValidDefinition(), CreateValidDefinition() };
        var violations = PluginAgentValidator.ValidateAll(defs);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAll_MixedViolations_ReturnsAllMessages()
    {
        var def1 = CreateValidDefinition();
        def1.PermissionMode = "auto";
        var def2 = CreateValidDefinition();
        def2.Hooks = new Dictionary<string, List<AgentHookMatcher>> { ["x"] = [] };
        var defs = new[] { def1, def2 };

        var violations = PluginAgentValidator.ValidateAll(defs);
        violations.Should().HaveCount(2);
        violations.Should().Contain(v => v.Contains("permissionMode"));
        violations.Should().Contain(v => v.Contains("hooks"));
    }
}
