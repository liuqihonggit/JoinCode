namespace Hands.Tests.Shell;

[Trait("Category", "Unit")]
public class ShellExecutionServiceEnvTests
{
    private readonly ShellExecutionConfig _config = new();
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly Mock<IProcessService> _processMock = new();

    public ShellExecutionServiceEnvTests()
    {
        _fsMock.Setup(x => x.GetCurrentDirectory()).Returns("C:\\project");
        _fsMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fsMock.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
    }

    [Fact]
    public async Task ExecuteAsync_InjectsEnvironmentOverrides_FromBashProvider()
    {
        var bashProvider = new BashShellProvider(_fsMock.Object, "bash.exe", NullLogger.Instance);
        var psProvider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);
        var service = new ShellExecutionService(_config, _fsMock.Object, _processMock.Object, bashProvider, psProvider);

        ProcessOptions? capturedOptions = null;
        _processMock.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "ok",
                StandardError = "",
                ExecutionTime = TimeSpan.FromMilliseconds(100)
            });

        await service.ExecuteAsync("echo hello");

        capturedOptions.Should().NotBeNull();
        capturedOptions?.EnvironmentVariables.Should().NotBeNull();
        capturedOptions?.EnvironmentVariables?.ContainsKey("CLAUDECODE").Should().BeTrue();
        capturedOptions?.EnvironmentVariables?.ContainsKey("GIT_EDITOR").Should().BeTrue();

        var envVars = capturedOptions?.EnvironmentVariables;
        envVars.Should().NotBeNull();
        envVars?["CLAUDECODE"].Should().Be("1");
        envVars?["GIT_EDITOR"].Should().Be("true");
    }

    [Fact]
    public async Task ExecutePowerShellAsync_InjectsEnvironmentOverrides_FromPsProvider()
    {
        var bashProvider = new BashShellProvider(_fsMock.Object, "bash.exe", NullLogger.Instance);
        var psProvider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);
        var service = new ShellExecutionService(_config, _fsMock.Object, _processMock.Object, bashProvider, psProvider);

        ProcessOptions? capturedOptions = null;
        _processMock.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "ok",
                StandardError = "",
                ExecutionTime = TimeSpan.FromMilliseconds(100)
            });

        await service.ExecutePowerShellAsync("Write-Output hello");

        capturedOptions.Should().NotBeNull();
        capturedOptions?.EnvironmentVariables.Should().NotBeNull();
        capturedOptions?.EnvironmentVariables?.ContainsKey("CLAUDECODE").Should().BeTrue();
        capturedOptions?.EnvironmentVariables?.ContainsKey("GIT_EDITOR").Should().BeTrue();

        var envVars = capturedOptions?.EnvironmentVariables;
        envVars.Should().NotBeNull();
        envVars?["CLAUDECODE"].Should().Be("1");
        envVars?["GIT_EDITOR"].Should().Be("true");
    }

    [Fact]
    public async Task ExecuteAsync_BashProvider_SetsShellEnvVar()
    {
        var bashProvider = new BashShellProvider(_fsMock.Object, "bash.exe", NullLogger.Instance);
        var psProvider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);
        var service = new ShellExecutionService(_config, _fsMock.Object, _processMock.Object, bashProvider, psProvider);

        ProcessOptions? capturedOptions = null;
        _processMock.Setup(x => x.ExecuteAsync(It.IsAny<ProcessOptions>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "ok",
                StandardError = "",
                ExecutionTime = TimeSpan.FromMilliseconds(100)
            });

        await service.ExecuteAsync("echo hello");

        capturedOptions.Should().NotBeNull();
        capturedOptions?.EnvironmentVariables.Should().NotBeNull();
        capturedOptions?.EnvironmentVariables?.ContainsKey("SHELL").Should().BeTrue();

        var envVars = capturedOptions?.EnvironmentVariables;
        envVars.Should().NotBeNull();
        envVars?["SHELL"].Should().Be("bash.exe");
    }
}
