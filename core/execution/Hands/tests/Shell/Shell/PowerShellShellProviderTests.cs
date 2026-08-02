namespace Hands.Tests.Shell;

[Trait("Category", "Unit")]
public class PowerShellShellProviderTests
{
    private readonly Mock<IFileSystem> _fsMock = new();

    public PowerShellShellProviderTests()
    {
        _fsMock.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
    }

    [Fact]
    public void EncodePowerShellCommand_Base64Utf16Le()
    {
        var command = "Write-Output 'hello'";
        var encoded = PowerShellShellProvider.EncodePowerShellCommand(command);

        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        decoded.Should().Be(command);
    }

    [Fact]
    public void EncodePowerShellCommand_EmptyString()
    {
        var encoded = PowerShellShellProvider.EncodePowerShellCommand("");
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        decoded.Should().Be("");
    }

    [Fact]
    public void EncodePowerShellCommand_SpecialCharacters()
    {
        var command = "$name = 'test'; Write-Output $name";
        var encoded = PowerShellShellProvider.EncodePowerShellCommand(command);

        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        decoded.Should().Be(command);
    }

    [Fact]
    public async Task BuildExecCommandAsync_NonSandbox_ReturnsBareCommand()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var result = await provider.BuildExecCommandAsync("Write-Output hello", new ShellExecOptions
        {
            SessionId = "test-session",
            UseSandbox = false
        });

        result.CommandString.Should().Contain("Write-Output hello");
        result.CommandString.Should().NotContain("-EncodedCommand");
    }

    [Fact]
    public async Task BuildExecCommandAsync_Sandbox_ReturnsEncodedCommand()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var result = await provider.BuildExecCommandAsync("Write-Output hello", new ShellExecOptions
        {
            SessionId = "test-session",
            UseSandbox = true,
            SandboxTmpDir = "/tmp/sandbox"
        });

        result.CommandString.Should().Contain("-EncodedCommand");
        result.CommandString.Should().Contain("-NoProfile");
        result.CommandString.Should().Contain("-NonInteractive");
    }

    [Fact]
    public async Task BuildExecCommandAsync_Sandbox_CwdFileInSandboxDir()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var result = await provider.BuildExecCommandAsync("Write-Output hello", new ShellExecOptions
        {
            SessionId = "test-session",
            UseSandbox = true,
            SandboxTmpDir = "/tmp/sandbox"
        });

        result.CwdFilePath.Should().Contain("sandbox");
        result.CwdFilePath.Should().Contain("test-session");
    }

    [Fact]
    public async Task BuildExecCommandAsync_IncludesExitCodeCapture()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var result = await provider.BuildExecCommandAsync("Write-Output hello", new ShellExecOptions
        {
            SessionId = "test-session",
            UseSandbox = false
        });

        result.CommandString.Should().Contain("$LASTEXITCODE");
        result.CommandString.Should().Contain("Get-Location");
    }

    [Fact]
    public void GetSpawnArgs_ReturnsPowerShellFlags()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var args = provider.GetSpawnArgs("Write-Output hello");

        args.Should().Contain("-NoProfile");
        args.Should().Contain("-NonInteractive");
        args.Should().Contain("-Command");
        args.Should().Contain("Write-Output hello");
    }

    [Fact]
    public async Task GetEnvironmentOverridesAsync_NonSandbox_ReturnsBaseEnvVars()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        var env = await provider.GetEnvironmentOverridesAsync("Write-Output hello");

        env.ContainsKey("CLAUDECODE").Should().BeTrue();
        env["CLAUDECODE"].Should().Be("1");
        env.ContainsKey("GIT_EDITOR").Should().BeTrue();
        env["GIT_EDITOR"].Should().Be("true");
    }

    [Fact]
    public async Task GetEnvironmentOverridesAsync_Sandbox_IncludesTmpDir()
    {
        var provider = new PowerShellShellProvider(_fsMock.Object, "pwsh.exe", NullLogger.Instance);

        await provider.BuildExecCommandAsync("Write-Output hello", new ShellExecOptions
        {
            SessionId = "test-session",
            UseSandbox = true,
            SandboxTmpDir = "/tmp/sandbox"
        });

        var env = await provider.GetEnvironmentOverridesAsync("Write-Output hello");

        env.ContainsKey("TMPDIR").Should().BeTrue();
        env["TMPDIR"].Should().Be("/tmp/sandbox");
        env.ContainsKey("JCC_TMPDIR").Should().BeTrue();
        env["JCC_TMPDIR"].Should().Be("/tmp/sandbox");
    }
}
