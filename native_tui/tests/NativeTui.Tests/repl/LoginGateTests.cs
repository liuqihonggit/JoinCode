// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Repl;

/// <summary>
/// 未登录友好提示测试（TASK060 Step2 — 用户痛点：未登录时输入仍调引擎傻等 40s，
/// 对照基准 `⎿ Not logged in · Please run /login` 即时反馈）—
/// SessionInfo.LoggedIn 字段链（引擎 ApiKey 判定 → driver 门禁态 + footer 真实化）+
/// 未登录提交本地 ⎿ 反馈行（ToolResultItem 复用）不调引擎。
/// </summary>
public sealed class LoginGateTests
{
    /// <summary>构造外壳（SeedContent 对齐 Run 首步）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(IReadOnlyList<CommandSuggestion>? commands = null) {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter(), commands: commands);
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>注入登录态事件（模拟引擎 session_info 握手）。</summary>
    private static void ApplyLoginState(ReplShell shell, FakeReplEngine engine, bool loggedIn) {
        engine.Events.Enqueue(new BridgeEvent.SessionInfo("1.0.0", "m", "auto", BypassAvailable: true, "C:\\", LoggedIn: loggedIn));
        shell.DrainEngine();
    }

    /// <summary>未登录事件 — driver 门禁关闭 + footer 显示未登录提示（真实化替换硬编码种子）。</summary>
    [Fact]
    public void SessionInfo_NotLoggedIn_ClosesGateAndShowsFooterHint()
    {
        var (shell, engine) = CreateShell();

        ApplyLoginState(shell, engine, loggedIn: false);

        Assert.False(shell.LoginGateOpen);
        Assert.NotNull(shell.View.Footer.LoginError);
        Assert.Contains("未登录", shell.View.Footer.LoginError);
    }

    /// <summary>已登录事件 — 门禁开 + footer 未登录提示清除。</summary>
    [Fact]
    public void SessionInfo_LoggedIn_OpensGateAndClearsFooterHint()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);

        ApplyLoginState(shell, engine, loggedIn: true);

        Assert.True(shell.LoginGateOpen);
        Assert.Null(shell.View.Footer.LoginError);
    }

    /// <summary>旧引擎兼容 — 不带 LoggedIn 字段（默认 true）门禁开不误拦。</summary>
    [Fact]
    public void SessionInfo_WithoutLoggedInField_DefaultsOpen()
    {
        var (shell, engine) = CreateShell();

        ApplyLoginState(shell, engine, loggedIn: true);

        Assert.True(shell.LoginGateOpen);
    }

    /// <summary>未登录提交 — 用户输入上屏（基准 `> 1` 留痕）+ ⎿ 未登录反馈行，不调引擎（不傻等）。</summary>
    [Fact]
    public void NotLoggedInSubmit_LocalFeedbackWithoutEngineCall()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, "112");

        shell.Submit();

        Assert.Empty(engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem u && u.Text == "112");
        Assert.Contains(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>已登录提交 — 正常直发（门禁不误拦闲态对话）。</summary>
    [Fact]
    public void LoggedInSubmit_SendsNormally()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: true);
        TypeText(shell, "你好");

        shell.Submit();

        Assert.Equal(["你好"], engine.Submits);
    }

    /// <summary>未登录 /exit — 客户端本地退出命令必须可用，不被登录门吞掉（TASK106）。
    /// 基准 commands.ts meetsAvailabilityRequirement：无 availability 的命令 universal，/exit 直接退出。</summary>
    [Fact]
    public void NotLoggedIn_ExitCommand_Exits()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, "/exit");

        shell.Submit();

        Assert.False(shell.IsRunning);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>未登录 /quit — 同 /exit，退出别名同样不受登录门拦截（TASK106）。</summary>
    [Fact]
    public void NotLoggedIn_QuitCommand_Exits()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, "/quit");

        shell.Submit();

        Assert.False(shell.IsRunning);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>未登录 /login 必须透传到引擎（TASK106 — footer 明写"运行 /login"，
    /// 若被登录门吞掉则形成"叫你登录却登不了"的死锁；宿主 LoginCommand 全权处理交互
    /// （CommandDialogCoordinator 提供密码框），成功后 RefreshSessionInfo 开门）。</summary>
    [Fact]
    public void NotLoggedIn_LoginCommand_ForwardedToEngine()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, "/login");

        shell.Submit();

        Assert.Equal(["/login"], engine.Submits);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>未登录带参 /login provider --oauth 同样透传（参数原样送宿主解析）。</summary>
    [Fact]
    public void NotLoggedIn_LoginWithArgs_ForwardedVerbatim()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, "/login openai --oauth");

        shell.Submit();

        Assert.Equal(["/login openai --oauth"], engine.Submits);
    }

    /// <summary>未登录时本地诊断/会话命令必须透传，命令可用性由目录元数据声明。</summary>
    [Theory]
    [InlineData("/version")]
    [InlineData("/help")]
    [InlineData("/session")]
    [InlineData("/resume")]
    [InlineData("/sessions")]
    [InlineData("/continue")]
    public void NotLoggedIn_LocalCommand_ForwardedToEngine(string command)
    {
        CommandSuggestion[] commands =
        [
            new("version", "版本", [], IsHidden: false, IsAvailableWithoutLogin: true),
            new("help", "帮助", ["?"], IsHidden: false, IsAvailableWithoutLogin: true),
            new("session", "会话", ["sessions"], IsHidden: false, IsAvailableWithoutLogin: true),
            new("resume", "恢复", ["continue"], IsHidden: false, IsAvailableWithoutLogin: true),
            new("model", "模型", [], IsHidden: false),
        ];
        var (shell, engine) = CreateShell(commands);
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, command);

        shell.Submit();

        Assert.Equal([command], engine.Submits);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>未登录时未声明免登录的斜杠命令仍被拦，不能被名称猜测放行。</summary>
    [Theory]
    [InlineData("/model")]
    [InlineData("/versionfoo")]
    public void NotLoggedIn_NonLocalSlashCommand_StillGated(string command)
    {
        CommandSuggestion[] commands =
        [
            new("version", "版本", [], IsHidden: false, IsAvailableWithoutLogin: true),
            new("model", "模型", [], IsHidden: false),
        ];
        var (shell, engine) = CreateShell(commands);
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, command);

        shell.Submit();

        Assert.Empty(engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>未登录普通提示词仍被拦（零回归 — 登录门对非命令文本照旧）。</summary>
    [Fact]
    public void NotLoggedIn_PlainText_StillGated()
    {
        var (shell, engine) = CreateShell();
        ApplyLoginState(shell, engine, loggedIn: false);
        TypeText(shell, "写段代码");

        shell.Submit();

        Assert.True(shell.IsRunning);
        Assert.Contains(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("未登录"));
    }

    /// <summary>逐字符输入文本（HandleKey 真实按键链）。</summary>
    private static void TypeText(ReplShell shell, string text) {
        foreach (var ch in text)
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
    }
}
