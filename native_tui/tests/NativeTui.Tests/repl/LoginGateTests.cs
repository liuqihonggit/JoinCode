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
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell() {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter());
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

    /// <summary>逐字符输入文本（HandleKey 真实按键链）。</summary>
    private static void TypeText(ReplShell shell, string text) {
        foreach (var ch in text)
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
    }
}
