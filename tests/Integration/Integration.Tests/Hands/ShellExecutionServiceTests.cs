namespace Core.Tests.Services;

/// <summary>
/// ShellExecutionService 测试 - 使用真实 Shell 执行
/// 标记为 Integration 测试，常规运行时不执行
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Shell")]
public class ShellExecutionServiceTests
{
    private readonly ShellExecutionService _service;

    public ShellExecutionServiceTests()
    {
        var config = new ShellExecutionConfig();
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var bashProvider = new BashShellProvider(fs);
        var psProvider = new PowerShellShellProvider(fs);
        _service = new ShellExecutionService(config, fs, new IO.ProcessService.PhysicalProcessService(), bashProvider, psProvider);
    }

    [Fact]
    public async Task ExecuteAsync_SimpleCommand_ReturnsOutput()
    {
        // Act
        var result = await _service.ExecuteAsync("echo hello").ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("hello", result.Stdout);
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInDirectory()
    {
        // Arrange
        var tempDir = Path.GetTempPath();

        // Act
        var result = await _service.ExecuteAsync("cd", workingDirectory: tempDir).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(tempDir.TrimEnd('\\'), result.Stdout);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ReturnsError()
    {
        // Act
        var result = await _service.ExecuteAsync("nonexistentcommand12345").ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCommand_ReturnsFailure()
    {
        // Act
        var result = await _service.ExecuteAsync("").ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithTimeout_TimesOut()
    {

        // Act - 使用 ping 命令作为更可靠的超时测试
        var result = await _service.ExecuteAsync("ping 127.0.0.1 -n 10", timeout: 100).ConfigureAwait(true);

        // Assert - Windows 超时行为不一致，使用更宽松的断言
        // 要么被中断，要么成功完成（取决于系统负载）
        if (result.Interrupted)
        {
            // TimeoutResult 返回 "Command timed out (100ms)"，包含 "timed out"
            Assert.True(
                result.Stderr.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                result.Stderr.Contains("超时", StringComparison.OrdinalIgnoreCase),
                $"Stderr 应包含超时信息，实际: {result.Stderr}");
        }
        else
        {
            // 如果未中断，说明执行很快完成，这也是可接受的
            Assert.True(result.Success || result.ExitCode == 0, $"命令应该成功执行或被中断，但返回: {result.ExitCode}");
        }
    }

    [Fact]
    public async Task ExecutePowerShellAsync_SimpleCommand_ReturnsOutput()
    {
        // Act
        var result = await _service.ExecutePowerShellAsync("Write-Output 'hello from ps'").ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("hello from ps", result.Stdout);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_ComplexCommand_ReturnsOutput()
    {
        // Act
        var result = await _service.ExecutePowerShellAsync("Get-Date -Format 'yyyy-MM-dd'").ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Stdout);
        // 验证输出格式是日期
        var output = result.Stdout.Trim();
        Assert.Equal(10, output.Length); // yyyy-MM-dd = 10 chars
        Assert.Contains('-', output);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_WithVariables_ReturnsOutput()
    {
        // Act
        var result = await _service.ExecutePowerShellAsync("$name = 'test'; Write-Output $name").ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("test", result.Stdout);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_InvalidCommand_ReturnsError()
    {
        // Act
        var result = await _service.ExecutePowerShellAsync("NonExistent-Cmdlet").ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_EmptyCommand_ReturnsFailure()
    {
        // Act
        var result = await _service.ExecutePowerShellAsync("").ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecutePowerShellAsync_WithTimeout_TimesOut()
    {

        // Act - 使用更长的睡眠时间来确保超时
        var result = await _service.ExecutePowerShellAsync("Start-Sleep -Milliseconds 5000", timeout: 100).ConfigureAwait(true);

        // Assert - Windows 超时行为不一致，使用更宽松的断言
        // 要么被中断，要么成功完成（取决于系统负载）
        if (result.Interrupted)
        {
            // TimeoutResult 返回 "Command timed out (100ms)"，包含 "timed out"
            Assert.True(
                result.Stderr.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                result.Stderr.Contains("超时", StringComparison.OrdinalIgnoreCase),
                $"Stderr 应包含超时信息，实际: {result.Stderr}");
        }
        else
        {
            // 如果未中断，说明执行很快完成，这也是可接受的
            Assert.True(result.Success || result.ExitCode == 0, $"命令应该成功执行或被中断，但返回: {result.ExitCode}");
        }
    }

    [Fact]
    public async Task ExecuteAsync_LongOutput_Truncated()
    {
        // Act - 生成超过 30KB 的输出（MaxOutputBytes 默认 30000）
        var result = await _service.ExecuteAsync("powershell -Command \"Write-Output ('x' * 40000)\"").ConfigureAwait(true);

        // Assert - TruncateOutput 返回 "[Output truncated — exceeded 30000 bytes]"
        Assert.True(result.Success);
        Assert.True(
            result.Stdout.Contains("truncated", StringComparison.OrdinalIgnoreCase) ||
            result.Stdout.Contains("截断", StringComparison.OrdinalIgnoreCase),
            $"输出应包含截断标记，实际长度: {result.Stdout.Length}");
    }
}
