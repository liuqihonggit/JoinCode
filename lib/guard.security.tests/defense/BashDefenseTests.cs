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
    private readonly ArgvHashNode _argvHashNode = new();
    private readonly RedirectWhitelistNode _redirectWhitelistNode = new();
    private readonly MtpPerturbationNode _mtpPerturbationNode = new();
    private readonly BashDefenseService _bashDefenseService;

    public BashDefenseTests()
    {
        _bashDefenseService = new BashDefenseService(_retainedDeviceNode, _argvHashNode, _redirectWhitelistNode);
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

    #region ArgvHashNode 防意图反推

    [Fact]
    public void ComputeArgvHash_SameCommand_ShouldReturnSameHash()
    {
        var hash1 = _argvHashNode.ComputeArgvHash("rm -rf ./build");
        var hash2 = _argvHashNode.ComputeArgvHash("rm -rf ./build");
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeArgvHash_DifferentCommand_ShouldReturnDifferentHash()
    {
        var hash1 = _argvHashNode.ComputeArgvHash("rm -rf ./build");
        var hash2 = _argvHashNode.ComputeArgvHash("rm -rf ./buld");
        hash1.Should().NotBe(hash2, "MTP 扰动 ./build→./buld 应产生不同 hash");
    }

    [Fact]
    public void ComputeArgvHash_ShouldReturn6CharHex()
    {
        var hash = _argvHashNode.ComputeArgvHash("echo hello");
        hash.Should().HaveLength(6);
        hash.Should().MatchRegex("^[0-9A-F]{6}$", "应为 6 位大写 hex");
    }

    [Fact]
    public void ValidateArgvHash_CorrectHash_ShouldReturnTrue()
    {
        var command = "rm -rf ./build";
        var hash = _argvHashNode.ComputeArgvHash(command);
        _argvHashNode.ValidateArgvHash(command, hash).Should().BeTrue();
    }

    [Fact]
    public void ValidateArgvHash_WrongHash_ShouldReturnFalse()
    {
        _argvHashNode.ValidateArgvHash("rm -rf ./build", "000000").Should().BeFalse();
    }

    [Fact]
    public void ValidateArgvHash_NullHash_ShouldReturnFalse()
    {
        _argvHashNode.ValidateArgvHash("rm -rf ./build", null).Should().BeFalse();
    }

    #endregion

    #region RequireArgvHash 二次确认

    [Fact]
    public async Task RequireArgvHash_NoConfirmMode_ShouldPass()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("echo hello", "/tmp", SystemActuatorKind.Bash)
            .Then(_bashDefenseService.RequireArgvHash)
            .ExecuteAsync(CancellationToken.None);
        rejection.Should().BeNull("默认 None 模式不要求确认");
    }

    [Fact]
    public async Task RequireArgvHash_FirstRound_ShouldRejectWithHash()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("rm -rf ./build", "/tmp", SystemActuatorKind.Bash, GuardConfirmMode.AntiCharLossConfirm)
            .Then(_bashDefenseService.RequireArgvHash)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.GetFirstText().Should().Contain("确认码", "第一轮应返回确认码");
        rejection.GetFirstText().Should().Contain("#", "确认码应以 # 开头");
    }

    [Fact]
    public async Task RequireArgvHash_SecondRoundCorrectHash_ShouldPass()
    {
        var command = "rm -rf ./build";
        var hash = _argvHashNode.ComputeArgvHash(command);

        var (_, rejection) = await _bashDefenseService
            .Begin(command, "/tmp", SystemActuatorKind.Bash, GuardConfirmMode.AntiCharLossConfirm, command, hash)
            .Then(_bashDefenseService.RequireArgvHash)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull("第二轮命令匹配 + hash 匹配应通过");
    }

    [Fact]
    public async Task RequireArgvHash_SecondRoundWrongHash_ShouldReject()
    {
        var command = "rm -rf ./build";

        var (_, rejection) = await _bashDefenseService
            .Begin(command, "/tmp", SystemActuatorKind.Bash, GuardConfirmMode.AntiCharLossConfirm, command, "WRONG0")
            .Then(_bashDefenseService.RequireArgvHash)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.GetFirstText().Should().Contain("确认码不匹配", "hash 不匹配应拒绝");
    }

    [Fact]
    public async Task RequireArgvHash_SecondRoundCommandMismatch_ShouldReject()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("rm -rf ./buld", "/tmp", SystemActuatorKind.Bash, GuardConfirmMode.AntiCharLossConfirm, "rm -rf ./build", null)
            .Then(_bashDefenseService.RequireArgvHash)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.GetFirstText().Should().Contain("不匹配", "命令不匹配应拒绝");
    }

    #endregion

    #region RedirectWhitelistNode 重定向白名单

    private static readonly string WorkDir = AppContext.BaseDirectory;

    [Theory]
    [InlineData("cmd >/dev/null")]
    [InlineData("cmd >/dev/stderr")]
    [InlineData("cmd > output.txt")]
    [InlineData("cmd >> output.txt")]
    [InlineData("cmd 2>error.txt")]
    [InlineData("cmd >/dev/null 2>&1")]
    public void CheckWhitelist_SafeTargets_ShouldPass(string command)
    {
        var result = _redirectWhitelistNode.CheckWhitelist(command, WorkDir);
        result.IsWhitelisted.Should().BeTrue();
    }

    [Theory]
    [InlineData("cmd > ../../etc/passwd")]
    [InlineData("cmd > /etc/passwd")]
    public void CheckWhitelist_OutsideWorkspace_ShouldReject(string command)
    {
        var result = _redirectWhitelistNode.CheckWhitelist(command, WorkDir);
        result.IsWhitelisted.Should().BeFalse();
    }

    [Fact]
    public void CheckWhitelist_NoRedirect_ShouldPass()
    {
        var result = _redirectWhitelistNode.CheckWhitelist("echo hello", WorkDir);
        result.IsWhitelisted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRedirectWhitelist_ViolatingTarget_ShouldRejectWithMessage()
    {
        var (_, rejection) = await _bashDefenseService
            .Begin("cmd > ../../etc/passwd", WorkDir, SystemActuatorKind.Bash)
            .Then(_bashDefenseService.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull();
        rejection!.GetFirstText().Should().Contain("/dev/null", "应给出正确写法");
        rejection.GetFirstText().Should().Contain("MTP", "应提示 MTP 扰动");
    }

    #endregion

    #region MtpPerturbationNode 扰动统计

    [Fact]
    public void Record_SuccessCommand_ShouldNotTriggerAdaptive()
    {
        var report = _mtpPerturbationNode.Record("echo hello", 0, null);
        report.ShouldTriggerAdaptive.Should().BeFalse();
        report.TotalRecords.Should().Be(1);
    }

    [Fact]
    public void Record_SingleAnomaly_ShouldNotTriggerAdaptive()
    {
        _mtpPerturbationNode.Record("cmd >nul", 1, "The system cannot find the file specified");
        var report = _mtpPerturbationNode.Record("cmd >nul", 1, "not found");

        report.ShouldTriggerAdaptive.Should().BeFalse("单次异常不触发，需连续3次");
        report.ConsecutiveAnomalies.Should().Be(2);
    }

    [Fact]
    public void Record_ThreeConsecutiveAnomalies_ShouldTriggerAdaptive()
    {
        _mtpPerturbationNode.Record("cmd >nul", 1, "not found");
        _mtpPerturbationNode.Record("cmd >nul", 1, "not found");
        var report = _mtpPerturbationNode.Record("cmd >nul", 1, "not found");

        report.ShouldTriggerAdaptive.Should().BeTrue("连续3次异常应触发自适应开关");
        report.ConsecutiveAnomalies.Should().Be(3);
    }

    [Fact]
    public void Record_SuccessAfterAnomaly_ShouldResetCounter()
    {
        _mtpPerturbationNode.Record("cmd >nul", 1, "not found");
        _mtpPerturbationNode.Record("cmd >nul", 1, "not found");
        var report = _mtpPerturbationNode.Record("echo hello", 0, null);

        report.ShouldTriggerAdaptive.Should().BeFalse();
        report.ConsecutiveAnomalies.Should().Be(0, "成功调用应重置连续异常计数");
    }

    [Fact]
    public void Record_PathErrorInStderr_ShouldBeAnomaly()
    {
        var report = _mtpPerturbationNode.Record("cat ./buld/file", 1, "No such file or directory");
        report.ConsecutiveAnomalies.Should().Be(1, "stderr 包含路径错误应为异常");
    }

    #endregion
}
