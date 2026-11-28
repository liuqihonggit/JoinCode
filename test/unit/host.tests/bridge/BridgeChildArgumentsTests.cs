namespace Host.Tests.Bridge;

/// <summary>
/// TASK138 — bridge 子进程拼参门禁：<see cref="BridgeSubprocessSpawner.BuildChildArguments"/>
/// 的每一项都必须能被本仓 <c>jcc</c> 真实解析。
/// <para>背景：改造前这里照搬 TS 端拼了 <c>--print</c> / <c>--sdk-url</c> / <c>--session-id</c> /
/// <c>--replay-user-messages</c> / <c>--debug-file</c>，五个都不是本仓登记的选项，子进程启动
/// 第一行就报 <c>Unknown option</c> 死掉 —— 而单测/CI 全绿（没有任何用例碰这条路径）。
/// 判据只能是「拿同一份 <see cref="CliArgParser"/> 去解析」，不能靠肉眼看参数名像不像。</para>
/// </summary>
public sealed class BridgeChildArgumentsTests {
    /// <summary>全字段都填满的子进程选项 — 尽量把可选分支都激活，门禁才有效。</summary>
    private static BridgeSubprocessOptions FullOptions() => new() {
        SessionId = "cse_abc123",
        SdkUrl = "wss://ingress.example/sdk/cse_abc123",
        AccessToken = "token-xyz",
        Dir = "/tmp/work",
        PermissionMode = "auto",
        DebugFile = "/tmp/bridge-session-cse_abc123.log",
        DebugLog = true,
        Sandbox = true,
    };

    /// <summary>拼出来的参数必须整体可解析 —— 任一未知选项都会让子进程当场退出。</summary>
    [Fact]
    public void BuildChildArguments_ProducesOnlyParseableOptions() {
        var args = BridgeSubprocessSpawner.BuildChildArguments(FullOptions());

        var parsed = CliArgParser.Parse([.. args]);

        parsed.Error.Should().BeNull(
            $"子进程参数必须全部是 jcc 已登记的选项，实际拼出：{string.Join(' ', args)}");
    }

    /// <summary>输入/输出两侧都必须是 stream-json —— 一条进程内多次查询的协议前提（TASK138）。</summary>
    [Fact]
    public void BuildChildArguments_RequestsStreamJsonOnBothSides() {
        var parsed = CliArgParser.Parse([.. BridgeSubprocessSpawner.BuildChildArguments(FullOptions())]);

        parsed.InputFormat.Should().Be("stream-json");
        parsed.OutputFormat.Should().Be("stream-json");
    }

    /// <summary>
    /// 回归锁：本仓无消费方的 TS 选项不得再出现。
    /// <para><c>--print</c> 曾让子进程 100% 起不来；<c>--sdk-url</c> 在本仓无消费方
    /// （事件由父进程从 stdout 转发到 ingress）；<c>--session-id</c> 语义也不同
    /// （本仓 <c>--resume</c> 认的是本地会话 ID，不是 bridge 的 <c>cse_*</c>）；
    /// <c>--debug-file</c> 无消费方，文件回溯由父进程落 bridge-transcript-*.json。
    /// <c>--replay-user-messages</c> 已由 TASK142-A 补上真实消费方（回显用户消息供父进程派生会话标题），
    /// 所以它从"不得出现"清单里移出，由下一条断言锁住。</para>
    /// </summary>
    [Fact]
    public void BuildChildArguments_OmitsTsOnlyOptionsWithoutConsumer() {
        var args = BridgeSubprocessSpawner.BuildChildArguments(FullOptions());

        string[] tsOnly = [
            JccCliArgEnumConstants.Print,
            JccCliArgEnumConstants.SdkUrl,
            JccCliArgEnumConstants.SessionId,
            JccCliArgEnumConstants.DebugFile,
        ];
        args.Should().NotIntersectWith(tsOnly);
    }

    /// <summary>
    /// TASK142-A：bridge 子进程必须要求回显用户消息 —— 父进程的 OnFirstUserMessage
    /// （会话标题派生）依赖 stdout 里的 type=user 行；此前删掉该选项只是堵了 Unknown option，
    /// 却把标题派生链一起切断了。
    /// </summary>
    [Fact]
    public void BuildChildArguments_RequestsReplayUserMessages() {
        BridgeSubprocessSpawner.BuildChildArguments(FullOptions())
            .Should().Contain(JccCliArgEnumConstants.ReplayUserMessages);
    }

    /// <summary>
    /// 调试开关只传 <c>--debuglog</c>（stderr 开关），且仅在 DebugLog=true 时传 ——
    /// 即便给了 DebugFile 路径也不拼 <c>--debug-file</c>（无消费方，拼了就是「声明≠支持」）。
    /// </summary>
    [Fact]
    public void BuildChildArguments_EmitsDebugLogSwitchOnlyWhenRequested() {
        BridgeSubprocessSpawner.BuildChildArguments(FullOptions())
            .Should().Contain(JccCliArgEnumConstants.DebugLog);

        BridgeSubprocessSpawner.BuildChildArguments(new BridgeSubprocessOptions { SessionId = "cse_quiet" })
            .Should().NotContain(JccCliArgEnumConstants.DebugLog);
    }

    /// <summary>ScriptArgs 是调用方透传的额外参数，原样前置（不参与本仓选项校验）。</summary>
    [Fact]
    public void BuildChildArguments_PassesScriptArgsThroughFirst() {
        var args = BridgeSubprocessSpawner.BuildChildArguments(new BridgeSubprocessOptions {
            SessionId = "cse_script",
            ScriptArgs = ["--vendor", "test-provider"],
        });

        args.Take(2).Should().Equal("--vendor", "test-provider");
    }
}
