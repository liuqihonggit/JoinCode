namespace Guard.Security.Tests;

/// <summary>
/// CommandDangerClassifier 单元测试 — 验证新4级分级: Safe/LightValidation/Execution/Dangerous
/// 绿色ask(LightValidation)=可撤回, 红色ask(Execution)=不可撤回, Dangerous=直接拒绝
/// </summary>
public class CommandDangerClassifierTests
{
    private readonly CommandDangerClassifier _classifier = new();

    #region Dangerous 级测试 — 直接拒绝不提示

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
    public void Dangerous_Commands_Should_Return_Dangerous(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.Dangerous);
        result.IsDangerous.Should().BeTrue();
        result.RequiresIntervention.Should().BeTrue();
    }

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("format c:")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    public void IsDangerous_Should_Return_True(string command)
    {
        _classifier.IsDangerous(command).Should().BeTrue();
    }

    #endregion

    #region Execution 级测试 — 红色 ask / 不可撤回

    [Theory]
    [InlineData("rm -rf /tmp/important")]
    [InlineData("del /s /q C:\\temp")]
    [InlineData("erase /s /q C:\\temp")]
    [InlineData("git reset --hard")]
    [InlineData("git clean -f")]
    [InlineData("shutdown /s")]
    [InlineData("chmod 777 /var/www")]
    [InlineData("dd of=/tmp/image.iso")]
    [InlineData("powershell -enc abc123")]
    [InlineData("echo hello | bash")]
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
    [InlineData("format D:")]
    [InlineData("shutdown /r")]
    [InlineData("reg add HKCU\\Test")]
    public void Execution_Commands_Should_Return_Execution(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.Execution);
        result.IsDangerous.Should().BeFalse();
        result.RequiresIntervention.Should().BeTrue();
    }

    #endregion

    #region LightValidation 级测试 — 绿色 ask / 可撤回

    [Theory]
    [InlineData("git commit -m \"msg\"")]
    [InlineData("git push")]
    [InlineData("git add file.txt")]
    [InlineData("git pull")]
    [InlineData("git merge feature")]
    [InlineData("git stash")]
    [InlineData("git tag v1.0")]
    public void LightValidation_Commands_Should_Return_LightValidation(string command)
    {
        var result = _classifier.Classify(command);

        result.Level.Should().Be(CommandDangerLevel.LightValidation);
        result.IsDangerous.Should().BeFalse();
        result.RequiresIntervention.Should().BeTrue();
    }

    #endregion

    #region Safe 级测试 — 自动通过

    [Theory]
    [InlineData("ls")]
    [InlineData("cat file.txt")]
    [InlineData("grep pattern file.txt")]
    [InlineData("git status")]
    [InlineData("git log")]
    [InlineData("git diff")]
    [InlineData("git show HEAD")]
    [InlineData("git branch")]
    [InlineData("git remote -v")]
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
    [InlineData("mkfs", CommandDangerLevel.Dangerous)]
    [InlineData("fdisk", CommandDangerLevel.Dangerous)]
    [InlineData("shred", CommandDangerLevel.Dangerous)]
    [InlineData("git", CommandDangerLevel.LightValidation)]
    [InlineData("rm", CommandDangerLevel.Execution)]
    [InlineData("del", CommandDangerLevel.Execution)]
    [InlineData("mv", CommandDangerLevel.Execution)]
    [InlineData("chmod", CommandDangerLevel.Execution)]
    [InlineData("sudo", CommandDangerLevel.Execution)]
    [InlineData("curl", CommandDangerLevel.Execution)]
    [InlineData("format", CommandDangerLevel.Execution)]
    [InlineData("shutdown", CommandDangerLevel.Execution)]
    [InlineData("reg", CommandDangerLevel.Execution)]
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
    public void SafeResult_Should_Have_Correct_Properties()
    {
        DangerClassificationResult.SafeResult.Level.Should().Be(CommandDangerLevel.Safe);
        DangerClassificationResult.SafeResult.RequiresIntervention.Should().BeFalse();
        DangerClassificationResult.SafeResult.IsDangerous.Should().BeFalse();
    }

    #endregion

    #region 危险路径测试

    [Theory]
    [InlineData("rm /", CommandDangerLevel.Dangerous)]
    [InlineData("rm C:\\", CommandDangerLevel.Dangerous)]
    [InlineData("rm /root", CommandDangerLevel.Dangerous)]
    [InlineData("rm /etc/passwd", CommandDangerLevel.Execution)]
    [InlineData("rm /home/user/file", CommandDangerLevel.Execution)]
    public void Dangerous_Paths_Should_Escalate_Level(string command, CommandDangerLevel expectedMinLevel)
    {
        var result = _classifier.Classify(command);

        ((int)result.Level).Should().BeGreaterThanOrEqualTo((int)expectedMinLevel);
    }

    #endregion
}
