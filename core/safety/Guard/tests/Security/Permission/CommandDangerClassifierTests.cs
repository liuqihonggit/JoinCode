namespace Guard.Security.Tests;

/// <summary>
/// CommandDangerClassifier 单元测试 — 验证危险命令分级和 Forbidden 拒绝逻辑
/// </summary>
public class CommandDangerClassifierTests
{
    private readonly CommandDangerClassifier _classifier = new();

    #region Forbidden 级测试 — AI 永远拒绝

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("rm -r -f /")]
    [InlineData("format c:")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("fdisk /dev/sda")]
    [InlineData("shred /etc/passwd")]
    [InlineData("wipe /home/user/secret")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    [InlineData("diskpart clean")]
    public void Forbidden_Commands_Should_Return_Forbidden(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.Forbidden);
        result.IsForbidden.Should().BeTrue();
        result.RequiresIntervention.Should().BeTrue();
    }

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("format c:")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    public void IsForbidden_Should_Return_True_For_Forbidden_Commands(string command)
    {
        _classifier.IsForbidden(command).Should().BeTrue();
    }

    #endregion

    #region Critical 级测试 — 需显式确认，不可批量批准

    [Theory]
    [InlineData("rm -rf /tmp/important")]
    [InlineData("del /s /q C:\\temp")]
    [InlineData("erase /s /q C:\\temp")]
    [InlineData("git reset --hard")]
    [InlineData("shutdown /s")]
    [InlineData("chmod 777 /var/www")]
    [InlineData("dd of=/tmp/image.iso")]
    [InlineData("powershell -enc abc123")]
    [InlineData("echo hello | bash")]
    public void Critical_Commands_Should_Return_Critical(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.Critical);
        result.IsForbidden.Should().BeFalse();
        result.RequiresIntervention.Should().BeTrue();
    }

    #endregion

    #region Dangerous 级测试 — 需确认

    [Theory]
    [InlineData("rm file.txt")]
    [InlineData("del file.txt")]
    [InlineData("Remove-Item file.txt")]
    [InlineData("rmdir emptydir")]
    [InlineData("mv file.txt newfile.txt")]
    [InlineData("cp file.txt backup.txt")]
    [InlineData("chmod 755 script.sh")]
    [InlineData("kill 1234")]
    [InlineData("taskkill /pid 1234")]
    [InlineData("sudo apt update")]
    [InlineData("curl http://example.com")]
    [InlineData("wget http://example.com/file.zip")]
    public void Dangerous_Commands_Should_Return_Dangerous(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.Dangerous);
        result.IsForbidden.Should().BeFalse();
        result.RequiresIntervention.Should().BeTrue();
    }

    #endregion

    #region Safe 级测试 — 自动批准

    [Theory]
    [InlineData("ls")]
    [InlineData("cat file.txt")]
    [InlineData("grep pattern file.txt")]
    [InlineData("git status")]
    [InlineData("git log")]
    [InlineData("echo hello")]
    [InlineData("pwd")]
    [InlineData("whoami")]
    public void Safe_Commands_Should_Return_Safe(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.Safe);
        result.RequiresIntervention.Should().BeFalse();
    }

    #endregion

    #region GetCommandLevel 测试

    [Theory]
    [InlineData("mkfs", CommandDangerLevel.Forbidden)]
    [InlineData("fdisk", CommandDangerLevel.Forbidden)]
    [InlineData("shred", CommandDangerLevel.Forbidden)]
    [InlineData("format", CommandDangerLevel.Critical)]
    [InlineData("shutdown", CommandDangerLevel.Critical)]
    [InlineData("reg", CommandDangerLevel.Critical)]
    [InlineData("rm", CommandDangerLevel.Dangerous)]
    [InlineData("del", CommandDangerLevel.Dangerous)]
    [InlineData("mv", CommandDangerLevel.Dangerous)]
    [InlineData("chmod", CommandDangerLevel.Dangerous)]
    [InlineData("sudo", CommandDangerLevel.Dangerous)]
    [InlineData("curl", CommandDangerLevel.Dangerous)]
    [InlineData("ls", CommandDangerLevel.Safe)]
    [InlineData("cat", CommandDangerLevel.Safe)]
    [InlineData("unknowncmd", CommandDangerLevel.Safe)]
    public void GetCommandLevel_Should_Return_Correct_Level(string commandName, CommandDangerLevel expectedLevel)
    {
        _classifier.GetCommandLevel(commandName).Should().Be(expectedLevel);
    }

    #endregion

    #region 边界测试

    [Fact]
    public void Empty_Command_Should_Return_Safe()
    {
        _classifier.Classify("").Level.Should().Be(CommandDangerLevel.Safe);
        _classifier.Classify("   ").Level.Should().Be(CommandDangerLevel.Safe);
    }

    [Fact]
    public void Null_Command_Should_Return_Safe()
    {
        _classifier.Classify((string)null!).Level.Should().Be(CommandDangerLevel.Safe);
    }

    [Fact]
    public void SafeResult_Should_Have_Correct_Properties()
    {
        DangerClassificationResult.SafeResult.Level.Should().Be(CommandDangerLevel.Safe);
        DangerClassificationResult.SafeResult.RequiresIntervention.Should().BeFalse();
        DangerClassificationResult.SafeResult.IsForbidden.Should().BeFalse();
    }

    #endregion

    #region 危险路径测试

    [Theory]
    [InlineData("rm /", CommandDangerLevel.Forbidden)]
    [InlineData("rm C:\\", CommandDangerLevel.Forbidden)]
    [InlineData("rm /root", CommandDangerLevel.Forbidden)]
    [InlineData("rm /etc/passwd", CommandDangerLevel.Critical)]
    [InlineData("rm /home/user/file", CommandDangerLevel.Critical)]
    public void Dangerous_Paths_Should_Escalate_Level(string command, CommandDangerLevel expectedMinLevel)
    {
        var result = _classifier.Classify(command);

        ((int)result.Level).Should().BeGreaterThanOrEqualTo((int)expectedMinLevel);
    }

    #endregion
}
