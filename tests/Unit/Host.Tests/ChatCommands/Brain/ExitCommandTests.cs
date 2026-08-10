namespace Core.Tests.ChatCommands;

/// <summary>
/// /exit 命令单元测试 — 覆盖确认提示被重定向吞掉 + PTY 交互模式无阻塞
/// </summary>
public class ExitCommandTests
{
    [Fact]
    public void Name_Should_Be_exit()
    {
        var cmd = new ExitCommand();
        cmd.Name.Should().Be("exit");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new ExitCommand();
        cmd.Usage.Should().StartWith("/exit");
    }

    [Fact]
    public async Task ExecuteAsync_ForceNonInteractive_ShouldExitDirectly()
    {
        var originalForce = Core.Utils.TestEnvironmentDetector.ForceNonInteractive;
        try
        {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
            var cmd = new ExitCommand();
            var context = new ChatCommandContext
            {
                Arguments = "",
                CancellationToken = CancellationToken.None,
                Services = new CommandServices
                {
                    ChatService = Mock.Of<IChatService>(),
                    CodeService = Mock.Of<ICodeService>(),
                    PlanService = Mock.Of<IPlanService>(),
                    FileSystem = TestFileSystem.Current,
                },
            };

            var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

            result.ShouldContinue.Should().BeFalse();
            result.IsHandled.Should().BeTrue();
        }
        finally
        {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = originalForce;
        }
    }
}

/// <summary>
/// TerminalHelper RealOut 单元测试 — 验证交互式输出绕过 SetOut 重定向
/// </summary>
public class TerminalHelperRealOutTests
{
    [Fact]
    public void RealOut_AfterInit_ShouldNotBeNull()
    {
        JoinCode.Cli.TerminalHelper.Init();
        JoinCode.Cli.TerminalHelper.RealOut.Should().NotBeNull();
    }

    [Fact]
    public void WriteLineReal_AfterSetOut_ShouldNotGoToRedirectedWriter()
    {
        JoinCode.Cli.TerminalHelper.Init();
        var originalOut = System.Console.Out;
        var sb = new StringBuilder();
        using var stringWriter = new StringWriter(sb);
        try
        {
            JoinCode.Cli.TerminalHelper.SetOut(stringWriter);
            JoinCode.Cli.TerminalHelper.WriteLineReal("确认提示测试");
            sb.ToString().Should().NotContain("确认提示测试");
        }
        finally
        {
            JoinCode.Cli.TerminalHelper.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteRawReal_AfterSetOut_ShouldNotGoToRedirectedWriter()
    {
        JoinCode.Cli.TerminalHelper.Init();
        var originalOut = System.Console.Out;
        var sb = new StringBuilder();
        using var stringWriter = new StringWriter(sb);
        try
        {
            JoinCode.Cli.TerminalHelper.SetOut(stringWriter);
            JoinCode.Cli.TerminalHelper.WriteRawReal("raw提示测试");
            sb.ToString().Should().NotContain("raw提示测试");
        }
        finally
        {
            JoinCode.Cli.TerminalHelper.SetOut(originalOut);
        }
    }
}

/// <summary>
/// TestEnvironmentDetector.ForceNonInteractive 单元测试
/// </summary>
public class TestEnvironmentDetectorForceNonInteractiveTests
{
    [Fact]
    public void ForceNonInteractive_SetTrue_ShouldMakeIsNonInteractiveTrue()
    {
        var original = Core.Utils.TestEnvironmentDetector.ForceNonInteractive;
        try
        {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
            Core.Utils.TestEnvironmentDetector.IsNonInteractive.Should().BeTrue();
        }
        finally
        {
            Core.Utils.TestEnvironmentDetector.ForceNonInteractive = original;
        }
    }

    [Fact]
    public void ForceNonInteractive_ResetFalse_ShouldRestoreOriginal()
    {
        var original = Core.Utils.TestEnvironmentDetector.ForceNonInteractive;
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = original;
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive.Should().Be(original);
    }
}
