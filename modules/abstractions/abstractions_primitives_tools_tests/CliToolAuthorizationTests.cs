namespace CliNodes.Tests;

/// <summary>
/// CLI 工具硬策略判定测试 — ADR 0168 集中授权核心。
/// <para>判定必须独立于 PermissionMode.Bypass：角色工具域、策略 deny、未知参数都不可被模式短路。</para>
/// </summary>
public sealed class CliToolAuthorizationTests {
    private const string PolicyYaml = """
        schema_version: 1
        policy_id: cli.file
        policy_version: 1
        executables:
          - jcc-tool-file.exe
        tools:
          read:
            effect: allow
            parameters:
              file_path:
                effect: allow
                capabilities: [workspace_read]
              pages:
                effect: deny
          write:
            effect: allow
            parameters:
              file_path:
                effect: allow
                capabilities: [workspace_write]
              content:
                effect: allow
                capabilities: [workspace_write]
          file_delete:
            effect: allow
            parameters:
              file_path:
                effect: allow
                capabilities: [filesystem_delete]
        """;

    [Fact]
    public void MainAgentWithoutToolRestriction_AllowsRead() {
        var decision = Evaluate(Unrestricted(), "read", PathArgs("/tmp/a.txt"));

        Assert.True(decision.Allowed, decision.Reason);
    }

    [Fact]
    public void ReadOnlyAgent_DeniesWriteByToolDomain() {
        var scope = Scope(["read", "glob", "grep"], []);

        var decision = Evaluate(scope, "write", WriteArgs("/tmp/a.txt", "x"));

        Assert.False(decision.Allowed);
        Assert.Contains("write", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("工具域", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DeniedTool_WinsOverAllowedAliasMatch() {
        var scope = Scope(["file_read"], ["read"]);

        var decision = Evaluate(scope, "read", PathArgs("/tmp/a.txt"));

        Assert.False(decision.Allowed);
        Assert.Contains("禁止", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowedAlias_MatchesCanonicalTool() {
        var scope = Scope(["file_read"], []);

        var decision = Evaluate(scope, "read", PathArgs("/tmp/a.txt"));

        Assert.True(decision.Allowed, decision.Reason);
    }

    [Fact]
    public void UnknownParameter_IsDeniedBeforeHandlerRuns() {
        var decision = Evaluate(Unrestricted(), "read", new Dictionary<string, string> {
            ["file_path"] = "/tmp/a.txt",
            ["unexpected"] = "x"
        });

        Assert.False(decision.Allowed);
        Assert.Contains("unexpected", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("未登记", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DenyOnlyParameter_IsDeniedEvenWhenToolAllowed() {
        var decision = Evaluate(Unrestricted(), "read", new Dictionary<string, string> {
            ["file_path"] = "/tmp/a.pdf",
            ["pages"] = "1-5"
        });

        Assert.False(decision.Allowed);
        Assert.Contains("pages", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("deny", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownTool_IsDenied() {
        var decision = Evaluate(Unrestricted(), "missing_tool", PathArgs("/tmp/a.txt"));

        Assert.False(decision.Allowed);
        Assert.Contains("missing_tool", decision.Reason, StringComparison.Ordinal);
    }

    private static CliAuthorizationDecision Evaluate(
        CliAuthorizationScope scope,
        string toolId,
        Dictionary<string, string> arguments) {
        var policy = CliToolPolicyLoader.Parse(PolicyYaml, "file.yaml");
        return CliToolPolicyEvaluator.Evaluate(
            policy,
            scope,
            groupId: "file",
            executableName: "jcc-tool-file.exe",
            requestedTool: toolId,
            arguments);
    }

    private static CliAuthorizationScope Unrestricted() => Scope([], []);

    private static CliAuthorizationScope Scope(string[] allowed, string[] denied) => new() {
        AgentId = "agent-test",
        SessionId = "session-test",
        AllowedTools = allowed,
        DeniedTools = denied,
    };

    private static Dictionary<string, string> PathArgs(string path) => new(StringComparer.Ordinal) {
        ["file_path"] = path
    };

    private static Dictionary<string, string> WriteArgs(string path, string content) => new(StringComparer.Ordinal) {
        ["file_path"] = path,
        ["content"] = content
    };
}
