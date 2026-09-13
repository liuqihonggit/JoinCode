namespace Guard.Security.Tests;

using Core.Hooks.Execution.Interception;
using Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// NullRedirectTwoPhaseGuard 单元测试 — ADR 0012 阶段5
/// 验证 &gt; &lt;保留设备名&gt; 重定向拦截（nul/con/prn/aux/com1-9/lpt1-9）
/// </summary>
public class NullRedirectTwoPhaseGuardTests
{
    private readonly NullRedirectTwoPhaseGuard _guard = new();

    private static GuardContext CreateContext() =>
        new(SystemActuatorKind.Bash, "D:\\project\\w2");

    #region CanHandle 测试 — 重定向到保留设备名

    [Theory]
    [InlineData("echo x > nul")]
    [InlineData("echo x >nul")]
    [InlineData("echo x 2>nul")]
    [InlineData("echo x >>nul")]
    [InlineData("echo x 2>>nul")]
    [InlineData("cat <nul")]
    [InlineData("cat 0<nul")]
    [InlineData("echo x > NUL")]
    [InlineData("echo x > Nul")]
    [InlineData("echo x > con")]
    [InlineData("echo x > CON")]
    [InlineData("echo x > prn")]
    [InlineData("echo x > aux")]
    [InlineData("echo x > com1")]
    [InlineData("echo x > lpt1")]
    [InlineData("echo x > COM9")]
    [InlineData("echo x > LPT9")]
    public void CanHandle_Retained_Device_Redirect_Should_Return_True(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
    }

    #endregion

    #region CanHandle 测试 — 非保留设备名重定向

    [Theory]
    [InlineData("echo x > /dev/null")]
    [InlineData("echo x > normal.txt")]
    [InlineData("echo x > output.log")]
    [InlineData("echo x 2>/dev/null")]
    [InlineData("echo hello")]
    [InlineData("touch nul")]
    [InlineData("touch con")]
    [InlineData("cp file nul")]
    [InlineData("mv file con")]
    public void CanHandle_Non_Retained_Redirect_Should_Return_False(string command)
    {
        _guard.CanHandle(command, CreateContext()).Should().BeFalse();
    }

    #endregion

    #region Evaluate 测试 — 拦截并返回 Deny

    [Theory]
    [InlineData("echo x > nul")]
    [InlineData("echo x > con")]
    [InlineData("echo x 2>nul")]
    [InlineData("echo x >>nul")]
    [InlineData("cat <nul")]
    public void Evaluate_Retained_Device_Redirect_Should_Deny(string command)
    {
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Deny>();
    }

    #endregion

    #region Evaluate 测试 — 非保留设备名放行

    [Theory]
    [InlineData("echo x > /dev/null")]
    [InlineData("echo x > normal.txt")]
    [InlineData("touch nul")]
    public void Evaluate_Non_Retained_Redirect_Should_Allow(string command)
    {
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    #endregion
}
