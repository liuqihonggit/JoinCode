namespace Guard.Tests.Configuration;

/// <summary>
/// SandboxStartupGate 测试 — 对齐基准 sandbox-adapter.ts:562-592：
/// 启用但不可用时不允许静默忽略；failIfUnavailable=true 必须拒绝启动，否则给可操作警告。
/// </summary>
public class SandboxStartupGateTests {
    [Fact]
    public void Given_未启用_When_Evaluate_Then_Ok() {
        var decision = SandboxStartupGate.Evaluate(new SandboxConfig(), platformSupported: false, realIsolationAvailable: false);

        decision.Outcome.Should().Be(SandboxStartupOutcome.Ok);
        decision.Message.Should().BeEmpty("未启用沙箱不需要任何提示");
    }

    [Fact]
    public void Given_启用且真隔离可用_When_Evaluate_Then_Ok() {
        var decision = SandboxStartupGate.Evaluate(
            new SandboxConfig { Enabled = true }, platformSupported: true, realIsolationAvailable: true);

        decision.Outcome.Should().Be(SandboxStartupOutcome.Ok);
    }

    [Fact]
    public void Given_启用但无真隔离且允许降级_When_Evaluate_Then_Warn() {
        var decision = SandboxStartupGate.Evaluate(
            new SandboxConfig { Enabled = true, FailIfUnavailable = false }, platformSupported: true, realIsolationAvailable: false);

        decision.Outcome.Should().Be(SandboxStartupOutcome.Warn);
        decision.Message.Should().Contain("未找到具备文件系统 + 网络隔离能力的沙箱提供器");
        decision.Message.Should().Contain("修复方式", "警告必须给出下一步，而不是只说坏了");
        decision.Message.Should().Contain("failIfUnavailable=false", "要告诉用户如何显式接受降级");
    }

    [Fact]
    public void Given_启用但平台不支持_When_Evaluate_Then_Warn并指出平台() {
        var decision = SandboxStartupGate.Evaluate(
            new SandboxConfig { Enabled = true }, platformSupported: false, realIsolationAvailable: true, platform: SandboxGate.PlatformLinux);

        decision.Outcome.Should().Be(SandboxStartupOutcome.Warn);
        decision.Message.Should().Contain("enabledPlatforms");
        decision.Message.Should().Contain(SandboxGate.PlatformLinux);
    }

    [Fact]
    public void Given_启用但Windows原生不支持_When_Evaluate_Then_Warn并指出Windows() {
        var decision = SandboxStartupGate.Evaluate(
            new SandboxConfig { Enabled = true }, platformSupported: false, realIsolationAvailable: true, platform: SandboxGate.PlatformWindows);

        decision.Outcome.Should().Be(SandboxStartupOutcome.Warn);
        decision.Message.Should().Contain("Windows");
        decision.Message.Should().Contain("不支持原生沙箱");
        decision.Message.Should().NotContain("enabledPlatforms");
    }

    [Fact]
    public void Given_启用且不可用且要求硬失败_When_Evaluate_Then_Fail() {
        var decision = SandboxStartupGate.Evaluate(
            new SandboxConfig { Enabled = true, FailIfUnavailable = true }, platformSupported: true, realIsolationAvailable: false);

        decision.Outcome.Should().Be(SandboxStartupOutcome.Fail);
        decision.Message.Should().Contain("拒绝启动");
        decision.Message.Should().Contain("sandbox.failIfUnavailable=true");
    }
}
