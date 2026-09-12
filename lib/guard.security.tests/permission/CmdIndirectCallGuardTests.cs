namespace Guard.Security.Tests;

using Core.Hooks.Execution.Interception;
using Core.Hooks.Execution.Interception.Guards;
using Core.Security.DangerClassification;

/// <summary>
/// CmdIndirectCallGuard 单元测试 — ADR 0012 阶段4
/// 验证 cmd /c 和 powershell -Command 间接调用内层命令递归分类
/// </summary>
public class CmdIndirectCallGuardTests
{
    private readonly CmdIndirectCallGuard _guard = new(new CommandDangerClassifier());

    private IReadOnlyDictionary<string, object> CreateContext()
    {
#pragma warning disable JCC1001
        return new Dictionary<string, object> { ["WorkingDirectory"] = "D:\\project\\w2" };
#pragma warning restore JCC1001
    }

    #region CanHandle 测试

    [Theory]
    [InlineData("cmd /c \"del file\"")]
    [InlineData("cmd /k \"del file\"")]
    [InlineData("cmd /c del file")]
    [InlineData("powershell -Command \"Remove-Item file\"")]
    [InlineData("pwsh -Command \"Remove-Item file\"")]
    public void CanHandle_Indirect_Call_Should_Return_True(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
    }

    [Theory]
    [InlineData("del file")]
    [InlineData("echo hello")]
    [InlineData("cmd")]
    [InlineData("powershell")]
    public void CanHandle_Direct_Command_Should_Return_False(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeFalse();
    }

    #endregion

    #region 内层危险命令拒绝测试

    [Theory]
    [InlineData("cmd /c \"del \\\\?\\D:\\path\\nul\"")]
    [InlineData("cmd /c \"format c:\"")]
    [InlineData("cmd /c \"rm -rf /\"")]
    [InlineData("powershell -Command \"Remove-Item -Force \\\\?\\D:\\path\"")]
    public void Evaluate_Indirect_Dangerous_Should_Deny(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Deny>();
    }

    [Theory]
    [InlineData("cmd /c \"del file.txt\"")]
    [InlineData("cmd /c \"rm file.txt\"")]
    [InlineData("cmd /c \"git push\"")]
    public void Evaluate_Indirect_Execution_Should_Deny(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Deny>();
    }

    #endregion

    #region 内层安全命令放行测试

    [Theory]
    [InlineData("cmd /c \"echo hello\"")]
    [InlineData("cmd /c \"dir\"")]
    [InlineData("cmd /c echo hello")]
    public void Evaluate_Indirect_Safe_Should_Allow(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    #endregion

    #region 内层命令提取测试

    [Theory]
    [InlineData("cmd /c \"del file\"", "del file")]
    [InlineData("cmd /c del file", "del file")]
    [InlineData("cmd /k \"echo hello\"", "echo hello")]
    [InlineData("powershell -Command \"Get-Date\"", "Get-Date")]
    [InlineData("pwsh -Command \"Write-Host hi\"", "Write-Host hi")]
    public void ExtractInnerCommand_Should_Extract(string command, string expected)
    {
        CmdIndirectCallGuard.ExtractInnerCommand(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("del file")]
    [InlineData("echo hello")]
    [InlineData("cmd")]
    public void ExtractInnerCommand_Non_Indirect_Should_Return_Null(string command)
    {
        CmdIndirectCallGuard.ExtractInnerCommand(command).Should().BeNull();
    }

    #endregion
}
