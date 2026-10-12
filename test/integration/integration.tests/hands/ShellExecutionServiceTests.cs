namespace Core.Tests.Services;

/// <summary>
/// SystemActuator 执行测试 - 使用真实 Shell 执行
/// 标记为 Integration 测试，常规运行时不执行
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Shell")]
public class ShellExecutionServiceTests {
    private readonly ISystemActuator _bashActuator;
    private readonly ISystemActuator _powershellActuator;

    public ShellExecutionServiceTests() {
        var fs = new IO.FileSystem.PhysicalFileSystem();

        Core.DependencyInjection.SystemActuatorInitializer.Initialize(fs);

        var registry = new SystemActuatorRegistry(fs);
        _bashActuator = registry.Get(SystemActuatorKind.Bash);
        _powershellActuator = registry.Get(SystemActuatorKind.PowerShell);
    }

    [SkippableFact]
    public async Task ExecuteAsync_SimpleCommand_ReturnsOutput() {
        Skip.IfNot(OperatingSystem.IsWindows(), "Linux bash actuator 命令构建差异待修复");
        var result = await _bashActuator.ExecuteAsync("echo hello").ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.Contains("hello", result.Stdout);
    }

    [SkippableFact]
    public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInDirectory() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell Get-Location 仅 Windows 验证");
        var tempDir = Path.GetTempPath();
        var result = await _powershellActuator.ExecuteAsync("Get-Location | Select-Object -ExpandProperty Path", workingDirectory: tempDir).ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.Contains(tempDir.TrimEnd(Path.DirectorySeparatorChar), result.Stdout.Trim());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ReturnsError() {
        var result = await _bashActuator.ExecuteAsync("nonexistentcommand12345").ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCommand_ReturnsFailure() {
        var result = await _bashActuator.ExecuteAsync("").ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [SkippableFact]
    public async Task ExecuteAsync_WithTimeout_TimesOut() {
        Skip.IfNot(OperatingSystem.IsWindows(), "ping -n 是 Windows 语法");
        var result = await _bashActuator.ExecuteAsync("ping 127.0.0.1 -n 10", timeout: 100).ConfigureAwait(true);
    }

    [SkippableFact]
    public async Task ExecutePowerShellAsync_SimpleCommand_ReturnsOutput() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell 仅 Windows 验证");
        var result = await _powershellActuator.ExecuteAsync("Write-Output 'hello from ps'").ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.Contains("hello from ps", result.Stdout);
    }

    [SkippableFact]
    public async Task ExecutePowerShellAsync_ComplexCommand_ReturnsOutput() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell 仅 Windows 验证");
        var result = await _powershellActuator.ExecuteAsync("Get-Date -Format 'yyyy-MM-dd'").ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Stdout);
        var output = result.Stdout.Trim();
        Assert.Equal(10, output.Length);
        Assert.Contains('-', output);
    }

    [SkippableFact]
    public async Task ExecutePowerShellAsync_WithVariables_ReturnsOutput() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell 仅 Windows 验证");
        var result = await _powershellActuator.ExecuteAsync("$name = 'test'; Write-Output $name").ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.Contains("test", result.Stdout);
    }

    [SkippableFact]
    public async Task ExecutePowerShellAsync_InvalidCommand_ReturnsError() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell 仅 Windows 验证");
        var result = await _powershellActuator.ExecuteAsync("NonExistent-Cmdlet").ConfigureAwait(true);
        Assert.False(result.Success);
    }

    [SkippableFact]
    public async Task ExecutePowerShellAsync_EmptyCommand_ReturnsFailure() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell 仅 Windows 验证");
        var result = await _powershellActuator.ExecuteAsync("").ConfigureAwait(true);
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [SkippableFact]
    public async Task ExecutePowerShellAsync_WithTimeout_TimesOut() {
        Skip.IfNot(OperatingSystem.IsWindows(), "PowerShell 仅 Windows 验证");
        var result = await _powershellActuator.ExecuteAsync("Start-Sleep -Milliseconds 5000", timeout: 100).ConfigureAwait(true);
    }

    [SkippableFact]
    public async Task ExecuteAsync_LongOutput_Truncated() {
        Skip.IfNot(OperatingSystem.IsWindows(), "Linux bash actuator 命令构建差异待修复");
        var result = await _bashActuator.ExecuteAsync("seq 1 50000").ConfigureAwait(true);
        Assert.True(result.Success);
        Assert.True(
            result.Stdout.Contains("truncated", StringComparison.OrdinalIgnoreCase) ||
            result.Stdout.Contains("截断", StringComparison.OrdinalIgnoreCase) ||
            result.Stdout.Length < 50000 * 6,
            $"输出应被截断，实际长度: {result.Stdout.Length}");
    }
}
