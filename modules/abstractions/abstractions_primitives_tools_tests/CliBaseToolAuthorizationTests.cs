namespace CliNodes.Tests;

/// <summary>
/// jcc 启动器 base 策略 — agent 会话内的危险选项（--exe/--dir/--resume）必须 fail-closed，
/// 且策略登记与代码登记表双向一致（有参无权 = 红）。
/// </summary>
public sealed class CliBaseToolAuthorizationTests {
    [Fact]
    public void BasePolicy_DeclaresExactlyTheCatalogOptions() {
        var policy = CliToolPolicyResourceReader.LoadWithHashFromExecutable(
            typeof(CliBaseToolAuthorizationTests).Assembly.Location,
            expectedExecutableName: null,
            expectedPolicyId: "cli.base").Manifest;

        Assert.Equal("cli.base", policy.PolicyId);
        Assert.Equal(["jcc.exe"], policy.Executables);

        var policyTools = policy.Tools.ToDictionary(tool => tool.ToolId, tool => tool.Parameters.Select(p => p.Name).ToArray(), StringComparer.Ordinal);
        var catalogTools = CliLauncherPolicyCatalog.Tools;

        Assert.Equal(catalogTools.Keys.Order(StringComparer.Ordinal), policyTools.Keys.Order(StringComparer.Ordinal));
        foreach (var (toolId, expectedParameters) in catalogTools) {
            Assert.True(policyTools.TryGetValue(toolId, out var actual), $"base 策略缺少元命令 {toolId}");
            Assert.Equal(
                expectedParameters.Order(StringComparer.Ordinal),
                (actual ?? []).Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData(CliLauncherPolicyCatalog.ToolCall, CliLauncherPolicyCatalog.Exe, "Base.AgentForbidden")]
    [InlineData(CliLauncherPolicyCatalog.ToolCall, CliLauncherPolicyCatalog.Dir, "Base.AgentForbidden")]
    [InlineData(CliLauncherPolicyCatalog.ToolTree, CliLauncherPolicyCatalog.Dir, "Base.AgentForbidden")]
    [InlineData(CliLauncherPolicyCatalog.SlashCall, CliLauncherPolicyCatalog.Resume, "Base.AgentForbidden")]
    [InlineData(CliLauncherPolicyCatalog.ToolCall, "bogus", "Base.Undeclared")]
    public void AgentSession_DeniesDangerousOrUndeclaredOptions(string metaCommand, string parameter, string expectedLayer) {
        var decision = AuthorizeWithEmbeddedPolicy(metaCommand, parameter);

        Assert.False(decision.Allowed);
        Assert.Equal(expectedLayer, decision.Layer);
        Assert.Contains("正确写法", decision.Reason);
    }

    [Theory]
    [InlineData(CliLauncherPolicyCatalog.ToolCall, CliLauncherPolicyCatalog.Timeout)]
    [InlineData(CliLauncherPolicyCatalog.McpCall, CliLauncherPolicyCatalog.Json)]
    [InlineData(CliLauncherPolicyCatalog.McpCall, CliLauncherPolicyCatalog.Model)]
    public void AgentSession_AllowsDeclaredSafeOptions(string metaCommand, string parameter) {
        var decision = AuthorizeWithEmbeddedPolicy(metaCommand, parameter);

        Assert.True(decision.Allowed, decision.Reason);
        Assert.Equal("Base.Allow", decision.Layer);
    }

    [Fact]
    public void ManualSession_IsNotRestrictedByBasePolicy() {
        var pipe = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable);
        var token = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable);
        Environment.SetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable, null);
        Environment.SetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable, null);
        try {
            var decision = CliBaseToolAuthorization.Authorize(
                CliLauncherPolicyCatalog.ToolCall, CliLauncherPolicyCatalog.Exe);

            Assert.True(decision.Allowed);
            Assert.Equal("Base.Manual", decision.Layer);
        } finally {
            Environment.SetEnvironmentVariable(CliAuthorizationEnvironment.PipeVariable, pipe);
            Environment.SetEnvironmentVariable(CliAuthorizationEnvironment.TokenVariable, token);
        }
    }

    [Fact]
    public void MissingPolicy_FailsClosedInAgentSession() {
        var load = new CliBaseToolAuthorization.BasePolicyLoad(null, "镜像未嵌入 base 策略");

        var decision = CliBaseToolAuthorization.AuthorizeWithPolicy(
            CliLauncherPolicyCatalog.ToolCall, CliLauncherPolicyCatalog.Timeout, load);

        Assert.False(decision.Allowed);
        Assert.Equal("Base.PolicyLoad", decision.Layer);
    }

    private static CliAuthorizationDecision AuthorizeWithEmbeddedPolicy(string metaCommand, string parameter) {
        var loaded = CliToolPolicyResourceReader.LoadWithHashFromExecutable(
            typeof(CliBaseToolAuthorizationTests).Assembly.Location,
            expectedExecutableName: null,
            expectedPolicyId: "cli.base");
        return CliBaseToolAuthorization.AuthorizeWithPolicy(
            metaCommand, parameter, new CliBaseToolAuthorization.BasePolicyLoad(loaded, null));
    }
}
