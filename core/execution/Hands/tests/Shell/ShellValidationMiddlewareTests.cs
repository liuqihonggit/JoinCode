namespace Hands.Tests.Shell;

/// <summary>
/// ShellValidationMiddleware 单元测试 — 验证 Shell 命令参数验证中间件的结构化诊断
/// </summary>
public class ShellValidationMiddlewareTests
{
    [Fact]
    public async Task EmptyCommand_SetsValidationErrorWithDiagnostic()
    {
        var sut = new ShellValidationMiddleware();
        var context = CreateContext(command: "");

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.Result.Diagnostic.Should().NotBeNull();
        context.Result.Diagnostic!.Reason.Should().Be("参数验证失败");
        context.Result.Diagnostic.Details.Should().Contain(d => d.Key == "validation_error");
    }

    [Fact]
    public async Task ValidCommand_PassesToNext()
    {
        var sut = new ShellValidationMiddleware();
        var context = CreateContext(command: "echo hello");

        var nextCalled = false;
        await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public void BuildValidationErrorDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellValidationMiddleware.BuildValidationErrorDiagnostic("command is required");

        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("command is required");
        diagnostic.Details.Should().ContainSingle(d => d.Key == "validation_error" && d.Value == "command is required");
    }

    private static ShellPipelineContext CreateContext(string command)
    {
        var provider = new Mock<ISystemActuator>();
        provider.SetupGet(x => x.Kind).Returns(SystemActuatorKind.Bash);

        return new ShellPipelineContext
        {
            Command = command,
            Provider = provider.Object,
            TimeoutPolicy = ToolTimeoutPolicy.None,
        };
    }
}
