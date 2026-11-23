namespace Guard.Tests.Configuration;

/// <summary>
/// SandboxPlatformSupport 测试 — 平台真值必须区分「企业白名单」与「本仓库是否真有该平台 provider」：
/// Linux 支持、Windows 不支持、macOS Seatbelt 未实现。
/// </summary>
public sealed class SandboxPlatformSupportTests {
    [Fact]
    public void Given_Linux_When_Evaluate_Then_支持并说明bwrap依赖() {
        var info = SandboxPlatformSupport.Evaluate(SandboxGate.PlatformLinux);

        info.Supported.Should().BeTrue();
        info.Platform.Should().Be(SandboxGate.PlatformLinux);
        info.Reason.Should().Contain("bubblewrap");
    }

    [Fact]
    public void Given_Windows_When_Evaluate_Then_不支持并说明原因() {
        var info = SandboxPlatformSupport.Evaluate(SandboxGate.PlatformWindows);

        info.Supported.Should().BeFalse();
        info.Reason.Should().Contain("Windows");
        info.Reason.Should().Contain("不支持");
    }

    [Fact]
    public void Given_macOS_When_Evaluate_Then_不支持并指出Seatbelt缺口() {
        var info = SandboxPlatformSupport.Evaluate(SandboxGate.PlatformMacOs);

        info.Supported.Should().BeFalse();
        info.Reason.Should().Contain("Seatbelt");
        info.Reason.Should().Contain("尚未实现");
    }

    [Fact]
    public void Given_未知平台_When_Evaluate_Then_不支持() {
        var info = SandboxPlatformSupport.Evaluate("freebsd");

        info.Supported.Should().BeFalse();
        info.Reason.Should().Contain("freebsd");
    }
}
