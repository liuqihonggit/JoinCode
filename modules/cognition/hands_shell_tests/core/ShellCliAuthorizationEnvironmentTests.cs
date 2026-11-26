namespace Hands.Shell.Tests;

/// <summary>
/// shell 子进程的 CLI 授权环境注入 — AsyncLocal 不能跨进程，令牌必须落在环境变量里。
/// </summary>
public sealed class ShellCliAuthorizationEnvironmentTests {
    static ShellCliAuthorizationEnvironmentTests() {
        SystemActuatorBase.RegisterCapability(new SystemActuatorCapability {
            Kind = SystemActuatorKind.Cmd,
            ShellPath = "cmd.exe",
            DisplayName = "CMD (test)",
        });
    }

    [Fact]
    public async Task GetEnvironmentOverrides_InjectsBrokerSessionVariables() {
        var context = CreateContext("agent-shell", ["read"]);
        await using var actuator = new CmdSystemActuator(new PhysicalFileSystem());

        using (context.EnterScope()) {
            var env = await actuator.GetEnvironmentOverridesAsync("echo hi");

            env.Should().ContainKey(CliAuthorizationEnvironment.PipeVariable);
            env.Should().ContainKey(CliAuthorizationEnvironment.TokenVariable);
            env[CliAuthorizationEnvironment.PipeVariable].Should().NotBeNullOrWhiteSpace();
            env[CliAuthorizationEnvironment.TokenVariable].Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetEnvironmentOverrides_SameScope_ReusesSameSession() {
        var context = CreateContext("agent-shell", ["read"]);
        await using var actuator = new CmdSystemActuator(new PhysicalFileSystem());

        using (context.EnterScope()) {
            var first = await actuator.GetEnvironmentOverridesAsync("echo a");
            var second = await actuator.GetEnvironmentOverridesAsync("echo b");

            first[CliAuthorizationEnvironment.TokenVariable]
                .Should().Be(second[CliAuthorizationEnvironment.TokenVariable]);
        }
    }

    [Fact]
    public async Task GetEnvironmentOverrides_DifferentScope_GetsDifferentToken() {
        await using var actuator = new CmdSystemActuator(new PhysicalFileSystem());

        string firstToken;
        using (CreateContext("agent-a", ["read"]).EnterScope()) {
            var env = await actuator.GetEnvironmentOverridesAsync("echo a");
            firstToken = env[CliAuthorizationEnvironment.TokenVariable];
        }

        string secondToken;
        using (CreateContext("agent-b", ["read"]).EnterScope()) {
            var env = await actuator.GetEnvironmentOverridesAsync("echo b");
            secondToken = env[CliAuthorizationEnvironment.TokenVariable];
        }

        firstToken.Should().NotBe(secondToken);
    }

    [Fact]
    public async Task GetEnvironmentOverrides_InjectedScope_RestrictsBrokerToolDomain() {
        var context = CreateContext("agent-tool-domain", ["todo_write"]);
        await using var actuator = new CmdSystemActuator(new PhysicalFileSystem());

        using (context.EnterScope()) {
            var env = await actuator.GetEnvironmentOverridesAsync("echo hi");
            var pipe = env[CliAuthorizationEnvironment.PipeVariable];
            var token = env[CliAuthorizationEnvironment.TokenVariable];

            using var processEnv = EnvVarScope
                .Set(CliAuthorizationEnvironment.PipeVariable, pipe)
                .Add(CliAuthorizationEnvironment.TokenVariable, token);

            var allowed = await CliHostToolAuthorization.AuthorizeAsync("TodoWrite", CancellationToken.None);
            var denied = await CliHostToolAuthorization.AuthorizeAsync("TodoRead", CancellationToken.None);

            allowed.Allowed.Should().BeTrue(allowed.Reason);
            denied.Allowed.Should().BeFalse();
            denied.Layer.Should().Be("Agent.AllowedTools");
        }
    }

    private static SubAgentContext CreateContext(string agentId, string[] allowedTools) => new() {
        AgentId = agentId,
        Role = AgentRole.Executor,
        Task = "authorization environment test",
        SessionId = "session-" + agentId,
        AllowedTools = allowedTools,
    };
}
