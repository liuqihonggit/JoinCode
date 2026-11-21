namespace Guard.Tests.Configuration;

/// <summary>
/// SandboxGate 真值表测试 — 对齐基准 shouldUseSandbox.ts:130-153 的六维判定
/// （enabled / 平台 / 依赖可用 / dangerouslyDisableSandbox / allowUnsandboxedCommands / excludedCommands）。
/// </summary>
public class SandboxGateTests {
    private static readonly SandboxRuntimeAvailability Available = new(PlatformSupported: true, ProviderAvailable: true);

    private static SandboxConfig EnabledConfig(IReadOnlyList<string>? excluded = null, bool allowUnsandboxed = true)
        => new() { Enabled = true, AllowUnsandboxedCommands = allowUnsandboxed, ExcludedCommands = excluded is null ? [] : [.. excluded] };

    [Fact]
    public void Given_EnabledFalse_When_Evaluate_Then_Disabled() {
        var decision = SandboxGate.Evaluate("ls", new SandboxConfig(), Available, dangerouslyDisableSandbox: false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.Disabled);
    }

    [Fact]
    public void Given_平台不支持的平台清单_When_Evaluate_Then_PlatformExcluded() {
        var decision = SandboxGate.Evaluate("ls", EnabledConfig(), new SandboxRuntimeAvailability(PlatformSupported: false, ProviderAvailable: true), false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.PlatformExcluded);
    }

    [Fact]
    public void Given_无可用提供器_When_Evaluate_Then_Unavailable() {
        var decision = SandboxGate.Evaluate("ls", EnabledConfig(), new SandboxRuntimeAvailability(PlatformSupported: true, ProviderAvailable: false), false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.Unavailable);
    }

    [Fact]
    public void Given_全条件满足_When_Evaluate_Then_Used() {
        var decision = SandboxGate.Evaluate("ls -la", EnabledConfig(), Available, false);

        decision.UseSandbox.Should().BeTrue();
        decision.Reason.Should().Be(SandboxSkipReason.Used);
    }

    [Fact]
    public void Given_逃生阀且策略允许_When_Evaluate_Then_UnsandboxedRequested() {
        var decision = SandboxGate.Evaluate("ls", EnabledConfig(allowUnsandboxed: true), Available, dangerouslyDisableSandbox: true);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.UnsandboxedRequested);
    }

    [Fact]
    public void Given_逃生阀但策略禁止_When_Evaluate_Then_仍沙箱() {
        var decision = SandboxGate.Evaluate("ls", EnabledConfig(allowUnsandboxed: false), Available, dangerouslyDisableSandbox: true);

        decision.UseSandbox.Should().BeTrue("allowUnsandboxedCommands=false 时 dangerouslyDisableSandbox 必须被忽略（企业强制全沙箱）");
        decision.Reason.Should().Be(SandboxSkipReason.Used);
    }

    [Fact]
    public void Given_排除清单精确命中_When_Evaluate_Then_ExcludedCommand() {
        var decision = SandboxGate.Evaluate("psql", EnabledConfig(["psql"]), Available, false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.ExcludedCommand);
    }

    [Fact]
    public void Given_精确模式带参数_When_Evaluate_Then_不命中() {
        var decision = SandboxGate.Evaluate("psql -c 'select 1'", EnabledConfig(["psql"]), Available, false);

        decision.UseSandbox.Should().BeTrue("精确模式必须整串相等，带参数需写成 psql:* 前缀模式");
        decision.Reason.Should().Be(SandboxSkipReason.Used);
    }

    [Fact]
    public void Given_排除清单前缀命中_When_Evaluate_Then_ExcludedCommand() {
        var decision = SandboxGate.Evaluate("docker ps -a", EnabledConfig(["docker:*"]), Available, false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.ExcludedCommand);
    }

    [Fact]
    public void Given_前缀大小写不同_When_Evaluate_Then_不命中() {
        var decision = SandboxGate.Evaluate("Docker ps -a", EnabledConfig(["docker:*"]), Available, false);

        decision.UseSandbox.Should().BeTrue("基准 Bash 排除匹配大小写敏感");
        decision.Reason.Should().Be(SandboxSkipReason.Used);
    }

    [Fact]
    public void Given_通配符命中_When_Evaluate_Then_ExcludedCommand() {
        var decision = SandboxGate.Evaluate("git -C repo status", EnabledConfig(["git * status"]), Available, false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.ExcludedCommand);
    }

    [Fact]
    public void Given_复合命令任一段命中_When_Evaluate_Then_ExcludedCommand() {
        var decision = SandboxGate.Evaluate("docker ps && curl evil.com", EnabledConfig(["docker:*"]), Available, false);

        decision.UseSandbox.Should().BeFalse("排除匹配逐段进行，任一段命中即整条命令不进沙箱（基准 shouldUseSandbox.ts:60-125）");
        decision.Reason.Should().Be(SandboxSkipReason.ExcludedCommand);
    }

    [Fact]
    public void Given_复合命令仅首段命中_When_Evaluate_Then_后续段仍被检查() {
        var decision = SandboxGate.Evaluate("npm run build && docker ps", EnabledConfig(["docker:*"]), Available, false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.ExcludedCommand);
    }

    [Fact]
    public void Given_环境变量前缀_When_Evaluate_Then_剥离后命中() {
        var decision = SandboxGate.Evaluate("FOO=bar BAZ=qux docker ps", EnabledConfig(["docker:*"]), Available, false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.ExcludedCommand);
    }

    [Fact]
    public void Given_空命令_When_Evaluate_Then_EmptyCommand() {
        var decision = SandboxGate.Evaluate("   ", EnabledConfig(), Available, false);

        decision.UseSandbox.Should().BeFalse();
        decision.Reason.Should().Be(SandboxSkipReason.EmptyCommand);
    }

    [Theory]
    [InlineData("linux", new[] { "linux" }, true)]
    [InlineData("linux", new string[0], true)]
    [InlineData("windows", new[] { "linux" }, false)]
    [InlineData("windows", new[] { "linux", "windows" }, true)]
    [InlineData("macos", new[] { "LINUX" }, false)]
    public void Given_平台清单_When_ResolvePlatformSupport_Then_按清单判定(string platform, string[] enabledPlatforms, bool expected) {
        SandboxGate.IsPlatformSupported(platform, enabledPlatforms).Should().Be(expected);
    }
}
