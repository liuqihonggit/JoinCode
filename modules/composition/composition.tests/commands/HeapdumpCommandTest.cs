namespace Composition.Tests.Commands;

/// <summary>
/// /heapdump 命令单元测试 — 覆盖运行时诊断信息渲染(GC 内存/集合代数/线程池/进程内存/句柄)
/// <para>命令零服务依赖,纯 Console 输出;GC/线程池为运行时真实值只断言结构不变量</para>
/// </summary>
[Collection("command-output")]
public sealed class HeapdumpCommandTest {
    [Fact]
    public async Task Execute_RendersRuntimeDiagnosticsSections() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HeapdumpCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("堆转储 / 运行时诊断:");
        output.Should().Contain("当前托管内存:");
        output.Should().Contain("压缩后内存:");
        output.Should().Contain("GC 最大代数:");
        output.Should().Contain("GC 集合次数:");
        output.Should().Contain("0 代:");
        output.Should().Contain("1 代:");
        output.Should().Contain("2 代:");
        output.Should().Contain("线程池:");
        output.Should().Contain("工作线程:");
        output.Should().Contain("I/O 线程:");
        output.Should().Contain("进程内存:");
        output.Should().Contain("句柄数:");
    }

    [Fact]
    public async Task Execute_IgnoresArguments_AlwaysRendersDiagnostics() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "anything");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HeapdumpCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("多余参数不影响诊断输出");
        output.Should().Contain("堆转储 / 运行时诊断:");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new HeapdumpCommand();
        cmd.Name.Should().Be("heapdump");
        cmd.Usage.Should().Be("/heapdump");
        cmd.IsHidden.Should().BeTrue();
    }
}
