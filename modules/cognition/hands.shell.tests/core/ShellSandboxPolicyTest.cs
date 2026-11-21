namespace Hands.Shell.Tests.Core;

/// <summary>
/// ShellSandboxPolicy 测试 — settings 驱动的 per-command 沙箱门控在 Shell 链路的落点：
/// 未启用时保留旧行为（仅逃生阀参数），启用时由 <see cref="SandboxGate"/> 决定是否绕过。
/// </summary>
public class ShellSandboxPolicyTest {
    private static readonly ISandboxProvider BubblewrapProvider = Mock.Of<ISandboxProvider>(
        p => p.Capabilities == SandboxCapabilities.FullIsolation);

    private static readonly ISandboxManager SandboxedManager = CreateManager();

    private static ISandboxManager CreateManager() {
        var manager = new Mock<ISandboxManager>();
        manager.Setup(m => m.GetAvailableTypes()).Returns([SandboxType.Bubblewrap]);
        manager.Setup(m => m.GetProvider(SandboxType.Bubblewrap)).Returns(BubblewrapProvider);
        manager.Setup(m => m.IsInSandbox).Returns(false);
        return manager.Object;
    }

    private static ISandboxManager ManagerWithProviders() => SandboxedManager;

    private static WorkflowConfig Config(bool enabled, bool allowUnsandboxed = true, IReadOnlyList<string>? excluded = null, IReadOnlyList<string>? platforms = null)
        => new() {
            Sandbox = new SandboxConfig {
                Enabled = enabled,
                AllowUnsandboxedCommands = allowUnsandboxed,
                ExcludedCommands = excluded is null ? [] : [.. excluded],
                EnabledPlatforms = platforms is null ? [] : [.. platforms],
            },
        };

    [Fact]
    public void Given_未装配配置_When_Resolve_Then_逃生阀直通() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, config: null, sandboxManager: null).Should().BeTrue();
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, config: null, sandboxManager: null).Should().BeFalse();
    }

    [Fact]
    public void Given_总开关关闭_When_Resolve_Then_保持旧行为() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, Config(enabled: false), ManagerWithProviders())
            .Should().BeTrue("sandbox.enabled=false 时不接管手动 /sandbox 会话语义");
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, Config(enabled: false), ManagerWithProviders())
            .Should().BeFalse();
    }

    [Fact]
    public void Given_启用且条件满足_When_Resolve_Then_不走绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls -la", false, Config(enabled: true), ManagerWithProviders())
            .Should().BeFalse();
    }

    [Fact]
    public void Given_启用且逃生阀允许_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, Config(enabled: true, allowUnsandboxed: true), ManagerWithProviders())
            .Should().BeTrue();
    }

    [Fact]
    public void Given_启用且策略禁止逃生阀_When_Resolve_Then_忽略参数() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, Config(enabled: true, allowUnsandboxed: false), ManagerWithProviders())
            .Should().BeFalse("allowUnsandboxedCommands=false 时 dangerously_disable_sandbox 必须被忽略");
    }

    [Fact]
    public void Given_排除清单命中_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("docker ps", false, Config(enabled: true, excluded: ["docker:*"]), ManagerWithProviders())
            .Should().BeTrue();
    }

    [Fact]
    public void Given_无可用提供器_When_Resolve_Then_绕过() {
        var manager = Mock.Of<ISandboxManager>(m => m.GetAvailableTypes() == Array.Empty<SandboxType>());
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, Config(enabled: true), manager)
            .Should().BeTrue("没有任何提供器时不能假装在沙箱里执行");
    }

    [Fact]
    public void Given_平台不在白名单_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, Config(enabled: true, platforms: ["plan9"]), ManagerWithProviders())
            .Should().BeTrue("enabledPlatforms 不含当前平台时不进沙箱");
    }
}
