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
    public async Task Broker_ScopeSnapshot_ReturnsAllowedAndDeniedWithoutExecution() {
        await using var broker = new CliAuthorizationBroker();
        var session = broker.CreateSession(CreateScope(["read", "edit"], ["file_delete"]));

        var snapshot = await CliToolAuthorizationClient.GetScopeSnapshotAsync(session, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(["read", "edit"], snapshot.AllowedTools);
        Assert.Equal(["file_delete"], snapshot.DeniedTools);
    }

    [Fact]
    public async Task Broker_AuditLog_WritesAllowedAndDeniedEntriesWithDigest() {
        var path = Path.Combine(Path.GetTempPath(), $"jcc-audit-{Guid.NewGuid():N}.jsonl");
        var previous = Environment.GetEnvironmentVariable(CliAuthorizationEnvironment.AuditLogVariable);
        Environment.SetEnvironmentVariable(CliAuthorizationEnvironment.AuditLogVariable, path);
        try {
            await using var broker = new CliAuthorizationBroker();
            var session = broker.CreateSession(CreateScope(["read"], []));

            var allow = await CliToolAuthorizationClient.AuthorizeAsync(
                session, "file", "read",
                typeof(CliAuthorizationBrokerTests).Assembly.Location,
                "JoinCode.CliNodes.Tests.dll",
                new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt" },
                CancellationToken.None);
            var deny = await CliToolAuthorizationClient.AuthorizeAsync(
                session, "file", "write",
                typeof(CliAuthorizationBrokerTests).Assembly.Location,
                "JoinCode.CliNodes.Tests.dll",
                new Dictionary<string, string> { ["file_path"] = "/tmp/a.txt", ["content"] = "x" },
                CancellationToken.None);

            Assert.True(allow.Allowed, allow.Reason);
            Assert.False(deny.Allowed);

            var lines = (await FileSystemFactory.Create().ReadAllText(path))
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            var allowedEntry = System.Text.Json.JsonSerializer.Deserialize(
                lines[0], CliNodeJsonContext.Default.CliAuthorizationAuditEntry);
            var deniedEntry = System.Text.Json.JsonSerializer.Deserialize(
                lines[1], CliNodeJsonContext.Default.CliAuthorizationAuditEntry);
            Assert.NotNull(allowedEntry);
            Assert.NotNull(deniedEntry);

            Assert.True(allowedEntry.Allowed);
            Assert.Equal("agent-test", allowedEntry.AgentId);
            Assert.Equal("session-test", allowedEntry.SessionId);
            Assert.Equal("cli.auth_test", allowedEntry.PolicyId);
            Assert.False(string.IsNullOrEmpty(allowedEntry.ArgumentDigest));
            Assert.Equal(["file_path"], allowedEntry.ParameterNames ?? []);

            Assert.False(deniedEntry.Allowed);
            Assert.Equal("Agent.AllowedTools", deniedEntry.Layer);
            Assert.NotEqual(allowedEntry.ArgumentDigest, deniedEntry.ArgumentDigest);
            Assert.Equal(["content", "file_path"], deniedEntry.ParameterNames ?? []);
        } finally {
            Environment.SetEnvironmentVariable(CliAuthorizationEnvironment.AuditLogVariable, previous);
        }
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
            "jcc-tool-file.exe",
            pipe, token, CancellationToken.None);

        var writeDecision = await CliToolAuthorizationClient.AuthorizeCoreAsync(
            "file", "write",
            new Dictionary<string, string> {
                ["file_path"] = "/tmp/a.txt",
                ["content"] = "x"
            },
            typeof(CliAuthorizationBrokerTests).Assembly.Location,
            "jcc-tool-file.exe",
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
