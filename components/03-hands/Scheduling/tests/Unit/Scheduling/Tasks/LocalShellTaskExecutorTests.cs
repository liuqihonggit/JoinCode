
namespace Sync.Tests.Scheduling.Tasks;

public class LocalShellTaskExecutorTests
{
    private readonly Mock<IShellExecutionService> _shellMock;
    private readonly LocalShellTaskExecutor _executor;

    public LocalShellTaskExecutorTests()
    {
        _shellMock = new Mock<IShellExecutionService>();
        _executor = new LocalShellTaskExecutor(
            _shellMock.Object,
            NullLogger<LocalShellTaskExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteShellAsync_SuccessfulExecution_ShouldReturnSuccessResult()
    {
        var shellResult = ShellExecutionResult.SuccessResult("hello world", "", 0);

        _shellMock
            .Setup(x => x.ExecuteAsync("echo hello", null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shellResult);

        var definition = new LocalShellTaskDefinition
        {
            TaskId = "shell-001",
            Command = "echo hello"
        };

        var result = await _executor.ExecuteShellAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.TaskId.Should().Be("shell-001");
        result.AgentId.Should().Be("local-shell");
        result.Output.Should().Be("hello world");
    }

    [Fact]
    public async Task ExecuteShellAsync_FailedExecution_ShouldReturnFailureResult()
    {
        var shellResult = ShellExecutionResult.FailureResult("command not found");

        _shellMock
            .Setup(x => x.ExecuteAsync("bad_cmd", null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shellResult);

        var definition = new LocalShellTaskDefinition
        {
            TaskId = "shell-002",
            Command = "bad_cmd"
        };

        var result = await _executor.ExecuteShellAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.TaskId.Should().Be("shell-002");
        result.AgentId.Should().Be("local-shell");
        result.Error.Should().Be("command not found");
    }

    [Fact]
    public async Task ExecuteShellAsync_NullDefinition_ShouldThrowArgumentNullException()
    {
        var act = () => _executor.ExecuteShellAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteShellAsync_WithStderr_ShouldIncludeStderrInOutput()
    {
        var shellResult = ShellExecutionResult.SuccessResult("stdout", "stderr warning", 0);

        _shellMock
            .Setup(x => x.ExecuteAsync("cmd", null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shellResult);

        var definition = new LocalShellTaskDefinition
        {
            TaskId = "shell-003",
            Command = "cmd"
        };

        var result = await _executor.ExecuteShellAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Contain("stdout");
        result.Output.Should().Contain("[stderr] stderr warning");
    }

    [Fact]
    public async Task ExecutePowerShellAsync_SuccessfulExecution_ShouldReturnSuccessResult()
    {
        var shellResult = ShellExecutionResult.SuccessResult("PS output", "", 0);

        _shellMock
            .Setup(x => x.ExecutePowerShellAsync("Get-Date", null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shellResult);

        var definition = new LocalShellTaskDefinition
        {
            TaskId = "ps-001",
            Command = "Get-Date"
        };

        var result = await _executor.ExecutePowerShellAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.TaskId.Should().Be("ps-001");
        result.AgentId.Should().Be("local-powershell");
        result.Output.Should().Be("PS output");
    }

    [Fact]
    public async Task ExecutePowerShellAsync_FailedExecution_ShouldReturnFailureResult()
    {
        var shellResult = ShellExecutionResult.FailureResult("PowerShell error");

        _shellMock
            .Setup(x => x.ExecutePowerShellAsync("bad_ps", null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shellResult);

        var definition = new LocalShellTaskDefinition
        {
            TaskId = "ps-002",
            Command = "bad_ps"
        };

        var result = await _executor.ExecutePowerShellAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.AgentId.Should().Be("local-powershell");
        result.Error.Should().Be("PowerShell error");
    }

    [Fact]
    public async Task ExecutePowerShellAsync_NullDefinition_ShouldThrowArgumentNullException()
    {
        var act = () => _executor.ExecutePowerShellAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteShellAsync_ServiceThrowsException_ShouldReturnFailureResult()
    {
        _shellMock
            .Setup(x => x.ExecuteAsync("crash", null, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service crashed"));

        var definition = new LocalShellTaskDefinition
        {
            TaskId = "shell-crash",
            Command = "crash"
        };

        var result = await _executor.ExecuteShellAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Service crashed");
    }
}
