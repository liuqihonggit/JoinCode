namespace Composition.Tests.Commands;

/// <summary>
/// /version 命令单元测试 — 输出版本号/运行时/架构/OS 四要素,始终 Continue
/// </summary>
[Collection("command-output")]
public sealed class VersionCommandTest {
    [Fact]
    public async Task Execute_PrintsVersionRuntimeArchitectureAndOs() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new VersionCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("JoinCode");
        output.Should().Contain("  版本: ", "应输出版本号行");
        output.Should().Contain("  运行时: ", "应输出运行时信息行");
        output.Should().Contain("  架构: ", "应输出进程架构行");
        output.Should().Contain("  OS: ", "应输出操作系统行");
    }

    [Fact]
    public async Task Execute_VersionLine_ShowsAssemblySemantics() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new VersionCommand().ExecuteAsync(context));

        var versionLine = output.Split('\n').FirstOrDefault(l => l.Contains("版本:"));
        versionLine.Should().NotBeNull();
        Assert.NotNull(versionLine);
        // Assembly 版本缺省回退 "1.0.0",格式应为可解析的 SemVer 三段以上
        var versionText = versionLine.Substring(versionLine.IndexOf("版本:", StringComparison.Ordinal) + "版本:".Length).Trim();
        versionText.Should().NotBeEmpty();
        versionText.Split('.').Length.Should().BeGreaterThanOrEqualTo(3,
            "版本号应为 Major.Minor.Patch 三段格式");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new VersionCommand();
        cmd.Name.Should().Be("version");
        cmd.Usage.Should().Be("/version");
        cmd.Description.Should().Be("显示版本信息");
    }
}
