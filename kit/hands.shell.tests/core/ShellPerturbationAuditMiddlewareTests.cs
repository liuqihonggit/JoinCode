namespace Hands.Tests.Shell;

/// <summary>
/// ShellPerturbationAuditMiddleware 单元测试 — 验证 MTP 扰动审计中间件的 PostToolUse 统计行为
/// </summary>
public class ShellPerturbationAuditMiddlewareTests {
    [Fact]
    public async Task NullExecutionResult_DoesNotRecord() {
        var node = new MtpPerturbationNode();
        var sut = new ShellPerturbationAuditMiddleware(node);
        var context = CreateContext("echo hello");
        context.ExecutionResult = null;

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        var report = node.AnalyzeRecentRecords();
        report.TotalRecords.Should().Be(0, "无执行结果不应记录");
    }

    [Fact]
    public async Task SuccessExecution_RecordsWithoutTrigger() {
        var node = new MtpPerturbationNode();
        var sut = new ShellPerturbationAuditMiddleware(node);
        var context = CreateContext("echo hello");
        context.ExecutionResult = CreateResult(exitCode: 0, stderr: "");

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        var report = node.AnalyzeRecentRecords();
        report.TotalRecords.Should().Be(1);
        report.ShouldTriggerAdaptive.Should().BeFalse("成功执行不触发自适应");
    }

    [Fact]
    public async Task FailedExecutionWithRedirect_RecordsAnomaly() {
        var node = new MtpPerturbationNode();
        var sut = new ShellPerturbationAuditMiddleware(node);
        var context = CreateContext("cmd >nul");
        context.ExecutionResult = CreateResult(exitCode: 1, stderr: "The system cannot find the path specified");

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        var report = node.AnalyzeRecentRecords();
        report.TotalRecords.Should().Be(1);
        report.ConsecutiveAnomalies.Should().Be(1, "非零退出码+重定向符号=异常");
    }

    [Fact]
    public async Task ThreeConsecutiveAnomalies_TriggersAdaptive() {
        var node = new MtpPerturbationNode();
        var sut = new ShellPerturbationAuditMiddleware(node);

        for (var i = 0; i < 3; i++) {
            var context = CreateContext("cmd >nul");
            context.ExecutionResult = CreateResult(exitCode: 1, stderr: "not found");
            await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);
        }

        var report = node.AnalyzeRecentRecords();
        report.ShouldTriggerAdaptive.Should().BeTrue("连续3次异常应触发自适应");
        report.ConsecutiveAnomalies.Should().Be(3);
    }

    [Fact]
    public async Task NextMiddleware_AlwaysCalledFirst() {
        var node = new MtpPerturbationNode();
        var sut = new ShellPerturbationAuditMiddleware(node);
        var context = CreateContext("echo hello");
        context.ExecutionResult = CreateResult(exitCode: 0, stderr: "");

        var nextCalled = false;
        await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("next 应总是被调用（post-next 模式）");
    }

    private static ShellPipelineContext CreateContext(string command) {
        var provider = new Mock<ISystemActuator>();
        provider.SetupGet(x => x.Kind).Returns(SystemActuatorKind.Bash);
        return new ShellPipelineContext {
            Command = command,
            Provider = provider.Object,
        };
    }

    private static SystemActuatorExecutionResult CreateResult(int exitCode, string stderr)
        => new() {
            Stdout = string.Empty,
            Stderr = stderr,
            ExitCode = exitCode,
            ExecutionTime = TimeSpan.Zero,
        };
}