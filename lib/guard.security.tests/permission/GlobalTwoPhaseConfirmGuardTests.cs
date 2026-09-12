namespace Guard.Security.Tests;

using Core.Hooks.Execution.Interception;
using Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// GlobalTwoPhaseConfirmGuard 单元测试 — ADR 0012 阶段7
/// 验证防丢字符二次确认（防 MTP 加速推理时丢字符/乱入字符导致命令变形）
/// </summary>
public class GlobalTwoPhaseConfirmGuardTests
{
    private readonly GlobalTwoPhaseConfirmGuard _guard = new();

    private IReadOnlyDictionary<string, object> CreateContext(bool enabled = false, string? confirmedCommand = null)
    {
#pragma warning disable JCC1001
        var dict = new Dictionary<string, object> { ["WorkingDirectory"] = "D:\\project\\w2" };
        if (enabled) dict["AntiCharLossConfirm"] = true;
        if (confirmedCommand is not null) dict["ConfirmedCommand"] = confirmedCommand;
        return dict;
#pragma warning restore JCC1001
    }

    #region 未启用时放行

    [Theory]
    [InlineData("echo hello")]
    [InlineData("rm -rf /")]
    [InlineData("git commit -m test")]
    public void CanHandle_Disabled_Should_Return_False(string command)
    {
        _guard.CanHandle(command, CreateContext(enabled: false)).Should().BeFalse();
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("rm -rf /")]
    public void Evaluate_Disabled_Should_Allow(string command)
    {
        var decision = _guard.Evaluate(command, CreateContext(enabled: false));
        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    #endregion

    #region 启用但未确认时拒绝

    [Theory]
    [InlineData("echo hello")]
    [InlineData("rm -rf /")]
    [InlineData("git commit -m test")]
    public void CanHandle_Enabled_Not_Confirmed_Should_Return_True(string command)
    {
        _guard.CanHandle(command, CreateContext(enabled: true)).Should().BeTrue();
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("rm -rf /")]
    public void Evaluate_Enabled_Not_Confirmed_Should_Deny(string command)
    {
        var decision = _guard.Evaluate(command, CreateContext(enabled: true));
        decision.Should().BeOfType<CommandDecision.Deny>();
    }

    #endregion

    #region 启用且已确认时放行

    [Theory]
    [InlineData("echo hello")]
    [InlineData("rm -rf /")]
    public void Evaluate_Enabled_Confirmed_Should_Allow(string command)
    {
        var decision = _guard.Evaluate(command, CreateContext(enabled: true, confirmedCommand: command));
        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    [Theory]
    [InlineData("echo hello", "echo goodbye")]
    public void Evaluate_Enabled_Different_Confirmed_Should_Deny(string command, string confirmed)
    {
        var decision = _guard.Evaluate(command, CreateContext(enabled: true, confirmedCommand: confirmed));
        decision.Should().BeOfType<CommandDecision.Deny>();
    }

    #endregion
}
