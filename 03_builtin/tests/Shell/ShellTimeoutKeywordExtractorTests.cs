namespace Hands.Tests.Shell;

/// <summary>
/// ShellTimeoutKeywordExtractor 单元测试 — 验证脚本内等待关键字的时间提取
/// 覆盖 PowerShell + Bash + cmd.exe + Python/C# 内嵌脚本
/// </summary>
public class ShellTimeoutKeywordExtractorTests
{
    [Theory]
    [InlineData("Start-Sleep -Seconds 60", 60)]
    [InlineData("Start-Sleep -Seconds 60.5", 61)]
    [InlineData("Start-Sleep -Milliseconds 60000", 60)]
    [InlineData("Start-Sleep -Milliseconds 500", 1)]
    [InlineData("Start-Sleep 60", 60)]
    [InlineData("Start-Sleep 30.5", 31)]
    [InlineData("start-sleep -seconds 90", 90)]
    [InlineData("Start-Sleep -s 45", 45)]
    [InlineData("Start-Sleep -ms 30000", 30)]
    public void PowerShell_StartSleep_ExtractsSeconds(string command, int expected)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("sleep 60", 60)]
    [InlineData("sleep 60.5", 61)]
    [InlineData("sleep 0.5m", 30)]
    [InlineData("sleep 2h", 7200)]
    [InlineData("sleep 1d", 86400)]
    [InlineData("sleep 90s", 90)]
    public void Bash_Sleep_ExtractsSeconds(string command, int expected)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("timeout /t 60", 60)]
    [InlineData("timeout /t 60 /nobreak", 60)]
    [InlineData("TIMEOUT /T 30", 30)]
    public void Cmd_Timeout_ExtractsSeconds(string command, int expected)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("ping -n 60 127.0.0.1 > nul", 60)]
    [InlineData("ping -n 30 localhost", 30)]
    [InlineData("PING -n 10 8.8.8.8", 10)]
    public void Cmd_PingDelay_ExtractsSeconds(string command, int expected)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("python -c \"import time; time.sleep(60)\"", 60)]
    [InlineData("python3 -c \"time.sleep(90)\"", 90)]
    [InlineData("python -c \"time.sleep(30.5)\"", 31)]
    public void Python_InlinedSleep_ExtractsSeconds(string command, int expected)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("dotnet script -e \"Thread.Sleep(60000)\"", 60)]
    [InlineData("Thread.Sleep(30000)", 30)]
    [InlineData("Thread.Sleep(500)", 1)]
    public void CSharp_InlinedThreadSleep_ExtractsSeconds(string command, int expected)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(expected);
    }

    [Fact]
    public void MultipleSleeps_ReturnsMax()
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds("sleep 30; sleep 60").Should().Be(60);
    }

    [Fact]
    public void MixedCommand_ExtractsSleepFromMiddle()
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds("echo start; sleep 60; echo end").Should().Be(60);
    }

    [Fact]
    public void MixedShells_TakesMaxAcrossAll()
    {
        var command = "Start-Sleep -Seconds 30 && sleep 90 && timeout /t 10";
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().Be(90);
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("ls -la")]
    [InlineData("git status")]
    [InlineData("dotnet build")]
    [InlineData("")]
    [InlineData("   ")]
    public void NoWaitKeyword_ReturnsNull(string command)
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(command).Should().BeNull();
    }

    [Fact]
    public void NullCommand_ReturnsNull()
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(null!).Should().BeNull();
    }

    [Fact]
    public void PowerShell_StartSleepSeconds_DoesNotMatchPositional()
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds("Start-Sleep -Seconds 60").Should().Be(60);
    }

    [Fact]
    public void FractionalMilliseconds_RoundsUp()
    {
        ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds("Start-Sleep -Milliseconds 1500").Should().Be(2);
    }
}
