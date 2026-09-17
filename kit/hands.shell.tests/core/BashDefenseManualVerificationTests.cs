namespace Hands.Tests.Shell;

/// <summary>
/// BashDefense 链手动验证 — 实际执行 bash 命令验证防御链行为（非 mock）
/// </summary>
public class BashDefenseManualVerificationTests
{
    private readonly BashDefenseService _service;
    private readonly RedirectWhitelistNode _redirectNode = new();
    private readonly RetainedDeviceNode _retainedNode = new();
    private readonly ArgvHashNode _argvHashNode = new();
    private readonly StrictParseNode _strictParseNode = new();

    public BashDefenseManualVerificationTests()
    {
        _service = new BashDefenseService(_retainedNode, _argvHashNode, _redirectNode, _strictParseNode);
    }

    private static string WorkDir => AppContext.BaseDirectory;

    [Fact]
    public async Task Verify_NormalEcho_PassesDefense()
    {
        var (_, rejection) = await _service
            .Begin("echo hello", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull("echo hello 应通过防御链");
    }

    [Fact]
    public async Task Verify_RetainedDeviceNul_Rejected()
    {
        var (_, rejection) = await _service
            .Begin("echo test >nul", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull(">nul 应被保留设备名检测拦截");
        rejection!.GetFirstText().Should().Contain("nul");
        rejection.GetFirstText().Should().Contain("/dev/null");
        rejection.GetFirstText().Should().Contain("MTP");
    }

    [Fact]
    public async Task Verify_UnclosedQuote_Rejected()
    {
        var (_, rejection) = await _service
            .Begin("echo \"hello >nul", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull("未闭合引号应被 StrictParse 拦截");
        rejection!.GetFirstText().Should().Contain("未闭合");
    }

    [Fact]
    public async Task Verify_RedirectOutsideWorkspace_Rejected()
    {
        var (_, rejection) = await _service
            .Begin("echo test > ../../etc/passwd", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull("重定向到工作区外应被白名单拦截");
        rejection!.GetFirstText().Should().Contain("白名单");
    }

    [Fact]
    public async Task Verify_RedirectToDevNull_Passes()
    {
        var (_, rejection) = await _service
            .Begin("echo test >/dev/null", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull(">/dev/null 应通过白名单");
    }

    [Fact]
    public async Task Verify_RedirectWithinWorkspace_Passes()
    {
        var (_, rejection) = await _service
            .Begin("echo test > ./output.txt", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull("> ./output.txt 应通过白名单（工作区内）");
    }

    [Fact]
    public async Task Verify_QuotedDangerousString_PassesDefense()
    {
        var (_, rejection) = await _service
            .Begin("echo \"rm -rf /\"", WorkDir, SystemActuatorKind.Bash)
            .Then(_service.StrictParse)
            .Then(_service.CheckRetainedDevice)
            .Then(_service.CheckRedirectWhitelist)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().BeNull("echo \"rm -rf /\" 是 echo 的字符串参数，应通过防御链");
    }

    [Fact]
    public async Task Verify_ArgvHash_FirstRound_RejectsAndReturnsHash()
    {
        var (_, rejection) = await _service
            .Begin("rm -rf /tmp/test", WorkDir, SystemActuatorKind.Bash, GuardConfirmMode.AntiCharLossConfirm)
            .Then(_service.RequireArgvHash)
            .ExecuteAsync(CancellationToken.None);

        rejection.Should().NotBeNull("第一轮应要求确认");
        rejection!.GetFirstText().Should().Contain("确认码");
        rejection.GetFirstText().Should().Contain("#");
    }
}
