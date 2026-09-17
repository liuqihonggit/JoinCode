namespace Guard.Security.Tests;

using Core.Hooks.Execution.Interception;
using Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// BashDefense 链式构建器 + RetainedDeviceNode + BashDefenseService 单元测试
/// — MTP 扰动纵深防御（计划 docs/plan/safety/mtp-perturbation-defense-plan.md 阶段1）
/// </summary>
public class BashDefenseTests
{
    private readonly RetainedDeviceNode _retainedDeviceNode = new();
    private readonly BashDefenseService _bashDefenseService;

    public BashDefenseTests()
    {
        _bashDefenseService = new BashDefenseService(_retainedDeviceNode);
    }

    #region BashDefense 链式构建器基本功能

    [Fact]
    public async Task ExecuteAsync_NoSteps_ShouldReturnNullRejection()
    {
        var (ctx, rejection) = await BashDefense
            .Begin("echo hello", "/tmp", SystemActuatorKind.Bash)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull();
        ctx.CurrentCommand.Should().Be("echo hello");
    }

    [Fact]
    public async Task ExecuteAsync_AllStepsPass_ShouldReturnNullRejection()
    {
        var (ctx, rejection) = await _bashDefenseService
            .Begin("echo hello", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull();
        ctx.CurrentCommand.Should().Be("echo hello");
    }

    [Fact]
    public async Task ExecuteAsync_StepRejects_ShouldShortCircuit()
    {
        var (ctx, rejection) = await _bashDefenseService
            .Begin("cmd >nul", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStopAtFirstRejection()
    {
        var secondStepCalled = false;

        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >nul", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .Then((_, _) => { secondStepCalled = true; return ValueTask.FromResult<ToolResult?>(null); })
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        secondStepCalled.Should().BeFalse("第一个步骤拒绝时应短路，不调用第二个步骤");
    }

    #endregion

    #region RetainedDeviceNode 检测

    [Theory]
    [InlineData("cmd >nul", "nul")]
    [InlineData("cmd 2>nul", "nul")]
    [InlineData("cmd >>nul", "nul")]
    [InlineData("cmd >con", "con")]
    [InlineData("cmd >prn", "prn")]
    [InlineData("cmd >aux", "aux")]
    [InlineData("cmd >com1", "com1")]
    [InlineData("cmd >lpt1", "lpt1")]
    public void FindRetainedDevice_ShouldDetect(string command, string expectedDevice)
    {
        _retainedDeviceNode.FindRetainedDevice(command).Should().Be(expectedDevice);
    }

    [Theory]
    [InlineData("cmd >/dev/null")]
    [InlineData("echo hello")]
    [InlineData("touch nul")]
    [InlineData("cmd > output.txt")]
    public void FindRetainedDevice_ShouldNotDetect(string command)
    {
        _retainedDeviceNode.FindRetainedDevice(command).Should().BeNull();
    }

    #endregion

    #region BashDefenseService.CheckRetainedDevice 拒绝消息封死替代路径

    [Fact]
    public async Task CheckRetainedDevice_RejectMessage_ShouldContainAlternativePath()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >nul", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        var text = rejection!.GetFirstText();
        text.Should().Contain(">NUL", "应封死替代路径，列出同类禁止写法");
        text.Should().Contain(">nul.", "应封死替代路径 nul. 变体");
        text.Should().Contain(">con", "应封死替代路径 con 变体");
    }

    [Fact]
    public async Task CheckRetainedDevice_RejectMessage_ShouldContainMtpHint()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >nul", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.GetFirstText().Should().Contain("MTP", "应提示疑似 MTP 扰动");
        rejection.GetFirstText().Should().Contain("重新生成", "应建议完整重新生成而非局部修补");
    }

    [Fact]
    public async Task CheckRetainedDevice_RejectMessage_ShouldContainCorrectWrite()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >nul", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.GetFirstText().Should().Contain("/dev/null", "应给出正确写法 > /dev/null");
    }

    [Fact]
    public async Task CheckRetainedDevice_SafeCommand_ShouldReturnNull()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >/dev/null", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull();
    }

    #endregion

    #region 边界 case（计划第五节）

    [Fact]
    public async Task BoundaryCase1_CmdRedirectNul_ShouldReject()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >nul", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);
        rejection.Should().NotBeNull();
    }

    [Fact]
    public async Task BoundaryCase10_CmdRedirectDevNull_ShouldPass()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd >/dev/null 2>&1", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRetainedDevice)
            .ExecuteAsync(CancellationToken.None);
        rejection.Should().BeNull();
    }

    #endregion
}
