namespace Hands.Shell.Tests.Core;

/// <summary>
/// ShellSandboxPolicy 测试 — settings 驱动的 per-command 沙箱门控在 Shell 链路的落点：
/// 未启用时保留旧行为（仅逃生阀参数），启用时由 <see cref="SandboxGate"/> 决定是否绕过；
/// 未进入会话沙箱时命令必须被逐命令包装。
/// </summary>
public class ShellSandboxPolicyTest {
    private static readonly ISandboxProvider BubblewrapProvider = Mock.Of<ISandboxProvider>(
        p => p.Capabilities == SandboxCapabilities.FullIsolation);

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
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, Config(enabled: false), new FakeSandboxManager())
            .Should().BeTrue("sandbox.enabled=false 时不接管手动 /sandbox 会话语义");
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, Config(enabled: false), new FakeSandboxManager())
            .Should().BeFalse();
    }

    [Fact]
    public void Given_启用且条件满足_When_Resolve_Then_不走绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls -la", false, Config(enabled: true), new FakeSandboxManager())
            .Should().BeFalse();
    }

    [Fact]
    public void Given_启用且逃生阀允许_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, Config(enabled: true, allowUnsandboxed: true), new FakeSandboxManager())
            .Should().BeTrue();
    }

    [Fact]
    public void Given_启用且策略禁止逃生阀_When_Resolve_Then_忽略参数() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", true, Config(enabled: true, allowUnsandboxed: false), new FakeSandboxManager())
            .Should().BeFalse("allowUnsandboxedCommands=false 时 dangerously_disable_sandbox 必须被忽略");
    }

    [Fact]
    public void Given_排除清单命中_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("docker ps", false, Config(enabled: true, excluded: ["docker:*"]), new FakeSandboxManager())
            .Should().BeTrue();
    }

    [Fact]
    public void Given_无可用提供器_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, Config(enabled: true), new FakeSandboxManager { AvailableTypes = [] })
            .Should().BeTrue("没有任何提供器时不能假装在沙箱里执行");
    }

    [Fact]
    public void Given_平台不在白名单_When_Resolve_Then_绕过() {
        ShellSandboxPolicy.ResolveDisableSandbox("ls", false, Config(enabled: true, platforms: ["plan9"]), new FakeSandboxManager())
            .Should().BeTrue("enabledPlatforms 不含当前平台时不进沙箱");
    }

    [Fact]
    public void Given_启用且未进入会话沙箱_When_Resolve_Then_命令被逐命令包装() {
        var decision = ShellSandboxPolicy.Resolve("ls -la", false, Config(enabled: true), new FakeSandboxManager(), "/tmp/work");

        decision.DisableSandbox.Should().BeFalse();
        decision.Command.Should().StartWith("bwrap ");
        decision.Command.Should().Contain("-- /bin/sh -c 'ls -la'");
    }

    [Fact]
    public async Task Given_已进入会话沙箱_When_Resolve_Then_不重复包装() {
        await using var manager = new FakeSandboxManager { InSandbox = true };

        var decision = ShellSandboxPolicy.Resolve("ls", false, Config(enabled: true), manager, null);

        decision.DisableSandbox.Should().BeFalse();
        decision.Command.Should().Be("ls");
        manager.WrapCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Given_提供器不支持逐命令包装_When_Resolve_Then_显式降级并告警() {
        await using var manager = new FakeSandboxManager { SupportsWrap = false };

        var decision = ShellSandboxPolicy.Resolve("ls", false, Config(enabled: true), manager, null);

        decision.DisableSandbox.Should().BeTrue("拿不到包装命令时不能假装已沙箱");
        decision.WrapWarning.Should().Contain("不沙箱执行");
    }

    [Fact]
    public async Task Given_filesystem配置_When_Resolve_Then_清单进入包装请求() {
        await using var manager = new FakeSandboxManager();
        var config = new WorkflowConfig {
            Sandbox = new SandboxConfig {
                Enabled = true,
                Filesystem = new SandboxFilesystemConfig {
                    AllowWrite = ["/tmp/out"],
                    DenyRead = ["/tmp/secret"],
                },
                Network = new SandboxNetworkConfig { AllowedDomains = ["example.com"] },
            },
        };

        _ = ShellSandboxPolicy.Resolve("ls", false, config, manager, "/tmp/work");

        var captured = manager.LastRequest;
        if (captured is null) {
            throw new InvalidOperationException("包装请求未被捕获");
        }

        captured.WorkingDirectory.Should().Be(Path.GetFullPath("/tmp/work"));
        captured.AllowWritePaths.Should().Contain("/tmp/out");
        captured.DenyReadPaths.Should().Contain("/tmp/secret");
        captured.AllowedDomains.Should().Contain("example.com");
        captured.RestrictNetwork.Should().BeTrue();
    }

    /// <summary>测试替身 — 只实现策略消费的成员，其余保持安全默认。</summary>
    private sealed class FakeSandboxManager : ISandboxManager {
        public SandboxType[] AvailableTypes { get; init; } = [SandboxType.Bubblewrap];
        public bool InSandbox { get; init; }
        public bool SupportsWrap { get; init; } = true;
        public int WrapCallCount { get; private set; }
        public SandboxWrapRequest? LastRequest { get; private set; }

        public ISandboxProvider? ActiveProvider => BubblewrapProvider;
        public SandboxType ActiveSandboxType => SandboxType.Bubblewrap;
        public bool IsInSandbox => InSandbox;
        public SandboxInfo? CurrentSandbox => null;
        public string? CurrentSandboxId => null;
        public SandboxHealthState HealthState => SandboxHealthState.Healthy;
        public bool CanWrapCommand => SupportsWrap;

        public bool IsTypeAvailable(SandboxType type) => AvailableTypes.Contains(type);
        public SandboxType[] GetAvailableTypes() => AvailableTypes;
        public ISandboxProvider? GetProvider(SandboxType type) => AvailableTypes.Contains(type) ? BubblewrapProvider : null;

        public bool TryWrapCommandForSandbox(SandboxWrapRequest request, [NotNullWhen(true)] out SandboxWrappedCommand? wrapped) {
            WrapCallCount++;
            LastRequest = request;
            if (!SupportsWrap) {
                wrapped = null;
                return false;
            }

            wrapped = new SandboxWrappedCommand {
                Command = $"bwrap --unshare-all --chdir {request.WorkingDirectory} -- /bin/sh -c '{request.Command}'",
                ProviderType = SandboxType.Bubblewrap,
            };
            return true;
        }

        public Task<SandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ExitSandboxAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SwitchProviderAsync(SandboxType type, CancellationToken ct = default) => throw new NotSupportedException();
        public string ResolvePath(string path) => path;
        public Task<SandboxInfo> CreateSandboxAsync(SandboxType type, SandboxOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default) => throw new NotSupportedException();
        public SandboxInfo? GetSandboxInfo(string sandboxId) => null;
        public string ResolvePath(string path, string sandboxId) => path;
        public Task<SandboxDegradationResult> TryEnterWithFallbackAsync(SandboxOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SandboxExecutionResult> ExecuteInSandboxAsync(string command, SandboxExecutionOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SandboxExecutionResult> ContinueExecutionAsync(string executionId, string action, CancellationToken ct = default) => throw new NotSupportedException();
        public bool AddProvider(ISandboxProvider provider) => false;
        public bool RemoveProvider(SandboxType type) => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
