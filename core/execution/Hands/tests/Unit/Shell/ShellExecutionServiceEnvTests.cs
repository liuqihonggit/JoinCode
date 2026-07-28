namespace Hands.Tests.Shell;

[Trait("Category", "Unit")]
public class ShellExecutionServiceEnvTests
{
    private readonly ShellExecutionConfig _config = new();
    private readonly Mock<IFileSystem> _fsMock = new();

    public ShellExecutionServiceEnvTests()
    {
        _fsMock.Setup(x => x.GetCurrentDirectory()).Returns("C:\\project");
        _fsMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fsMock.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
    }

    [Fact]
    public void Constructor_AcceptsBashAndPsProviders()
    {
        var bashProvider = new BashShellProvider(_fsMock.Object, "bash.exe", NullLogger.Instance);
        var psProvider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var service = new ShellExecutionService(_config, _fsMock.Object, bashProvider, psProvider);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ThrowsOnNullConfig()
    {
        var bashProvider = new BashShellProvider(_fsMock.Object, "bash.exe", NullLogger.Instance);
        var psProvider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var act = () => new ShellExecutionService(null!, _fsMock.Object, bashProvider, psProvider);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_ThrowsOnNullFileSystem()
    {
        var bashProvider = new BashShellProvider(_fsMock.Object, "bash.exe", NullLogger.Instance);
        var psProvider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var act = () => new ShellExecutionService(_config, null!, bashProvider, psProvider);

        act.Should().Throw<ArgumentNullException>().WithParameterName("fs");
    }
}
