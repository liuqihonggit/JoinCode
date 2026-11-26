namespace CliNodes.Tests;

/// <summary>
/// 集中授权 broker 的真实命名管道往返 — 验证工具 exe 不需要自己解释 YAML/角色。
/// </summary>
public sealed class CliAuthorizationBrokerTests {
    [Fact]
    public void PolicyResourceReader_LoadsEmbeddedPolicyFromAssembly() {
        var policy = CliToolPolicyResourceReader.LoadFromExecutable(
            typeof(CliAuthorizationBrokerTests).Assembly.Location,
            "JoinCode.CliNodes.Tests.dll");

        Assert.Equal("cli.auth_test", policy.PolicyId);
        Assert.Contains(policy.Tools, tool => tool.ToolId == "read");
    }

    [Fact]
    public async Task Broker_UnrestrictedScope_AllowsRead() {
        await using var broker = new CliAuthorizationBroker();
        var session = broker.CreateSession(CreateScope([], []));

        var decision = await CliToolAuthorizationClient.AuthorizeAsync(
            session,
            groupId: "file",
            toolId: "read",
            policyImagePath: typeof(CliAuthorizationBrokerTests).Assembly.Location,
            executableName: "JoinCode.CliNodes.Tests.dll",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            CancellationToken.None);

        Assert.True(decision.Allowed, decision.Reason);
    }

    [Fact]
    public async Task Broker_ReadOnlyScope_DeniesWriteOverPipe() {
        await using var broker = new CliAuthorizationBroker();
        var session = broker.CreateSession(CreateScope(["read"], []));

        var decision = await CliToolAuthorizationClient.AuthorizeAsync(
            session,
            groupId: "file",
            toolId: "write",
            policyImagePath: typeof(CliAuthorizationBrokerTests).Assembly.Location,
            executableName: "JoinCode.CliNodes.Tests.dll",
            new Dictionary<string, string> {
                ["file_path"] = "/tmp/a.txt",
                ["content"] = "x"
            },
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Contains("write", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeEnvironment_IsReusableForSameScope() {
        var first = CliAuthorizationBrokerRuntime.EnsureEnvironment("agent-a", "session-a", ["read"], [], null);
        var second = CliAuthorizationBrokerRuntime.EnsureEnvironment("agent-a", "session-a", ["read"], [], null);

        Assert.Equal(first[CliAuthorizationEnvironment.PipeVariable], second[CliAuthorizationEnvironment.PipeVariable]);
        Assert.Equal(first[CliAuthorizationEnvironment.TokenVariable], second[CliAuthorizationEnvironment.TokenVariable]);
        Assert.NotEmpty(first[CliAuthorizationEnvironment.TokenVariable]);
    }

    [Fact]
    public async Task Broker_RejectsForgedToken() {
        await using var broker = new CliAuthorizationBroker();
        var session = broker.CreateSession(CreateScope([], []));
        var forged = session with { Token = "00000000" };

        var decision = await CliToolAuthorizationClient.AuthorizeAsync(
            forged,
            groupId: "file",
            toolId: "read",
            policyImagePath: typeof(CliAuthorizationBrokerTests).Assembly.Location,
            executableName: "JoinCode.CliNodes.Tests.dll",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Contains("token", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gate_PolicyBearingImageWithoutSession_DeniesAuthMissing() {
        var decision = await CliToolAuthorizationClient.AuthorizeCoreAsync(
            groupId: "file",
            toolId: "read",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            policyImagePath: typeof(CliAuthorizationBrokerTests).Assembly.Location,
            executableName: "JoinCode.CliNodes.Tests.dll",
            pipeName: null,
            token: null,
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("Auth.Missing", decision.Layer);
        Assert.Contains("jcc tool call", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate_PolicyFreeImageWithoutSession_KeepsLegacyExecution() {
        var policyFreeImage = typeof(CliAuthorizationScope).Assembly.Location;
        Assert.False(CliToolPolicyResourceReader.HasEmbeddedPolicy(policyFreeImage));

        var decision = await CliToolAuthorizationClient.AuthorizeCoreAsync(
            groupId: "file",
            toolId: "read",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            policyImagePath: policyFreeImage,
            executableName: Path.GetFileName(policyFreeImage),
            pipeName: null,
            token: null,
            CancellationToken.None);

        Assert.True(decision.Allowed, decision.Reason);
        Assert.Equal("Policy.NotEnforced", decision.Layer);
    }

    [Fact]
    public void PolicyResourceReader_DetectsEmbeddedPolicyPresence() {
        Assert.True(CliToolPolicyResourceReader.HasEmbeddedPolicy(typeof(CliAuthorizationBrokerTests).Assembly.Location));
        Assert.False(CliToolPolicyResourceReader.HasEmbeddedPolicy(typeof(CliAuthorizationScope).Assembly.Location));
    }

    [Fact]
    public async Task Broker_ScopeOnlyRequest_EnforcesHostToolDomain() {
        await using var broker = new CliAuthorizationBroker();
        var session = broker.CreateSession(CreateScope(["todo_write"], []));

        var allowed = await CliToolAuthorizationClient.AuthorizeHostToolAsync(
            session.PipeName, session.Token, "TodoWrite", CancellationToken.None);
        var denied = await CliToolAuthorizationClient.AuthorizeHostToolAsync(
            session.PipeName, session.Token, "todo_read", CancellationToken.None);

        Assert.True(allowed.Allowed, allowed.Reason);
        Assert.Equal("Host.Allow", allowed.Layer);
        Assert.False(denied.Allowed);
        Assert.Equal("Agent.AllowedTools", denied.Layer);
    }

    [Fact]
    public async Task Gate_SubAgentScopeEnvironment_IntersectsToolDomain() {
        var env = CliAuthorizationBrokerRuntime.EnsureEnvironment(
            agentId: "agent-sub-" + Guid.NewGuid().ToString("N"),
            sessionId: "session-sub",
            allowedTools: ["read"],
            deniedTools: [],
            permissionMode: "unattended");
        var pipe = env[CliAuthorizationEnvironment.PipeVariable];
        var token = env[CliAuthorizationEnvironment.TokenVariable];

        var readDecision = await CliToolAuthorizationClient.AuthorizeCoreAsync(
            "file", "read",
            new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
            typeof(CliAuthorizationBrokerTests).Assembly.Location,
            "JoinCode.CliNodes.Tests.dll",
            pipe, token, CancellationToken.None);

        var writeDecision = await CliToolAuthorizationClient.AuthorizeCoreAsync(
            "file", "write",
            new Dictionary<string, string> {
                ["file_path"] = "/tmp/a.txt",
                ["content"] = "x"
            },
            typeof(CliAuthorizationBrokerTests).Assembly.Location,
            "JoinCode.CliNodes.Tests.dll",
            pipe, token, CancellationToken.None);

        Assert.True(readDecision.Allowed, readDecision.Reason);
        Assert.False(writeDecision.Allowed);
        Assert.Equal("Agent.AllowedTools", writeDecision.Layer);
    }

    private static CliAuthorizationScope CreateScope(string[] allowed, string[] denied) => new() {
        AgentId = "agent-test",
        SessionId = "session-test",
        AllowedTools = allowed,
        DeniedTools = denied,
    };
}
