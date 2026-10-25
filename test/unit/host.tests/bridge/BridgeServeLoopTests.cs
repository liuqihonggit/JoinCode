namespace Host.Tests.Bridge;

/// <summary>
/// bridge 服务循环测试 — stdio 协议主循环（jcc bridge_serve 子命令核心，注入内存流可测）：
/// submit → turn_started + 回合事件 + turn_completed；interrupt → turn_interrupted；
/// quit/Eof → 退出；忙时 submit → error 诱导提示；未知 type → stderr 诊断不中断。
/// </summary>
public sealed class BridgeServeLoopTests {
    private static string WriteRequest(CliBridge.BridgeRequest request) =>
        JsonSerializer.Serialize(request, CliBridge.BridgeJsonContext.Default.BridgeRequest);

    [Fact]
    public async Task Submit_RunsTurn_WithLifecycleEventsInOrder() {
        using var stdin = new StringReader(WriteRequest(new CliBridge.BridgeRequest.Submit("你好")) + "\n");
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, stderr, (input, consumer, ct) => {
            Assert.Equal("你好", input);
            consumer.OnThinking("思考");
            consumer.OnText("回复正文");
            consumer.OnDone(null, null);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        // durationMs 为真实墙钟差（非确定），thinking_ended 行按前缀匹配
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
        Assert.Equal("{\"type\":\"turn_started\"}", lines[0]);
        Assert.Equal("{\"type\":\"thinking_started\"}", lines[1]);
        Assert.StartsWith("{\"type\":\"thinking_ended\",\"durationMs\":", lines[2]);
        Assert.EndsWith("}", lines[2]);
        Assert.Equal("{\"type\":\"token_delta\",\"length\":4,\"text\":\"回复正文\"}", lines[3]);
        Assert.Equal("{\"type\":\"assistant_message\",\"text\":\"回复正文\"}", lines[4]);
        Assert.Equal("{\"type\":\"turn_completed\"}", lines[5]);
    }

    [Fact]
    public async Task Interrupt_CancelsTurn_EmitsTurnInterrupted() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("长任务")),
            WriteRequest(new CliBridge.BridgeRequest.Interrupt())));
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, stderr, async (input, consumer, ct) => {
            await Task.Delay(Timeout.Infinite, ct);
        }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "{\"type\":\"turn_started\"}\n" +
            "{\"type\":\"turn_interrupted\"}\n",
            stdout.ToString());
    }

    [Fact]
    public async Task Quit_ExitsImmediately_WithZero() {
        using var stdin = new StringReader(WriteRequest(new CliBridge.BridgeRequest.Quit()) + "\n");
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task Eof_ExitsWithZero_AfterTurnCompletes() {
        // submit 后无 quit，stdin 直接结束：回合收尾 + 退出
        using var stdin = new StringReader(WriteRequest(new CliBridge.BridgeRequest.Submit("x")) + "\n");
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => {
                consumer.OnText("ok");
                consumer.OnDone(null, null);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.EndsWith("{\"type\":\"turn_completed\"}\n", stdout.ToString());
    }

    [Fact]
    public async Task SubmitWhileBusy_EmitsErrorWithInductiveMessage() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("第一回合")),
            WriteRequest(new CliBridge.BridgeRequest.Submit("第二回合")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            async (input, consumer, ct) => await Task.Delay(Timeout.Infinite, ct),
            CancellationToken.None);

        // 忙时第二 submit 被拒（诱导提示），interrupt 未发出 → 第一回合仍挂起 → quit 取消收尾后退出
        Assert.Equal(0, exitCode);
        Assert.Contains("turn_started", stdout.ToString());
        var errorLine = stdout.ToString().Split('\n').Single(l => l.Contains("\"error\""));
        Assert.Contains("回合进行中", errorLine);
        Assert.Contains("interrupt", errorLine);
    }

    [Fact]
    public async Task UnknownType_WritesStderrDiagnostic_AndLoopContinues() {
        using var stdin = new StringReader(string.Join('\n',
            "{\"type\":\"warp_drive\"}",
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, stderr,
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("warp_drive", stderr.ToString());
        Assert.Contains("submit", stderr.ToString());
    }

    [Fact]
    public async Task ExecutorThrows_EmitsErrorEvent_AndLoopContinues() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("触发异常")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => throw new InvalidOperationException("LLM 连接失败"),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        var errorLine = output.Split('\n').Single(l => l.Contains("\"error\""));
        Assert.Contains("LLM 连接失败", errorLine);
        // 回合失败也必须发 turn_completed，否则消费方（TUI/E2E）永远等不到回合结束 → 超时
        Assert.Contains("\"type\":\"turn_completed\"", output);
    }

    [Fact]
    public async Task InterruptWhenIdle_IsNoOp() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Interrupt()),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task SetPermissionMode_ValidMode_EmitsChangedWithAppliedValue() {
        var applied = new List<string>();
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.SetPermissionMode("plan")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None,
            mode => {
                applied.Add(mode);
                return PermissionMode.Plan;
            });

        Assert.Equal(0, exitCode);
        Assert.Equal(["plan"], applied);
        Assert.Equal("{\"type\":\"permission_mode_changed\",\"mode\":\"plan\"}\n", stdout.ToString());
    }

    [Fact]
    public async Task SetPermissionMode_AppliedValueDiffersFromRequest_EmitsActualValue() {
        // 安全闸场景：请求 bypass 被闸拦截 → 应用器返回实际生效模式（如 auto），回执以实际值为准
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.SetPermissionMode("bypass")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None,
            _ => PermissionMode.Auto);

        Assert.Equal(0, exitCode);
        Assert.Equal("{\"type\":\"permission_mode_changed\",\"mode\":\"auto\"}\n", stdout.ToString());
    }

    [Fact]
    public async Task SetPermissionMode_UnknownMode_EmitsErrorWithValidList() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.SetPermissionMode("warp-drive")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None,
            _ => null);

        Assert.Equal(0, exitCode);
        var errorLine = stdout.ToString().Split('\n').Single(l => l.Contains("\"error\""));
        Assert.Contains("warp-drive", errorLine);
        Assert.Contains("plan, auto, ask, bypass, unattended", errorLine);
        Assert.Contains("disableBypassPermissionsMode", errorLine);
    }

    [Fact]
    public async Task SetPermissionMode_WithoutApplier_EmitsErrorInductive() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.SetPermissionMode("plan")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var errorLine = stdout.ToString().Split('\n').Single(l => l.Contains("\"error\""));
        Assert.Contains("引擎未提供权限系统", errorLine);
        Assert.Contains("plan, auto, ask, bypass, unattended", errorLine);
    }

    /// <summary>submit 带图片附件（TASK066 板块 G G4）— 执行器收到降级投影输入：文本 + 空行 +
    /// 逐附件 [Image #N: filename (mediaType)] 引用段（与进程内直连版 EngineTurnClient.
    /// ProjectImageAttachments 同一降级语义 — 引擎回合链 string-only，base64 不内嵌）。</summary>
    [Fact]
    public async Task Submit_WithImages_ExecutorReceivesProjectedInput() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("看这张图",
                [
                    new CliBridge.BridgeImageAttachment("aGk=", "image/png", "a.png"),
                    new CliBridge.BridgeImageAttachment("eQ==", "image/jpeg", "b.jpg"),
                ])),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => {
                Assert.Equal("看这张图\n\n[Image #1: a.png (image/png)]\n[Image #2: b.jpg (image/jpeg)]", input);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("turn_completed", stdout.ToString());
    }

    /// <summary>submit 空文本 + 图片 — 投影段兜底非空（TUI 剥离 [Image #N] 占位后空文本场景，
    /// 附件即提交内容 — 对齐 EngineTurnClient 投影同一兜底分支）。</summary>
    [Fact]
    public async Task Submit_EmptyTextWithImages_ProjectsSummaryOnly() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.Submit("",
                [new CliBridge.BridgeImageAttachment("aGk=", "image/png", "shot.png")])),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => {
                Assert.Equal("[Image #1: shot.png (image/png)]", input);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    /// <summary>submit 空图片数组 — 投影零改动（执行器收原文；null/空附件等价无附件提交）。</summary>
    [Fact]
    public async Task Submit_EmptyImages_ExecutorReceivesTextAsIs() {
        using var stdin = new StringReader(string.Join('\n',
            "{\"type\":\"submit\",\"text\":\"纯文本\",\"images\":[]}",
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => {
                Assert.Equal("纯文本", input);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    /// <summary>旧 TUI（G4 前）不输出 images 字段 — 执行器收原文（Images=null 投影零改动，旧客户端零破坏）。</summary>
    [Fact]
    public async Task Submit_LegacyWithoutImages_ExecutorReceivesTextAsIs() {
        using var stdin = new StringReader(string.Join('\n',
            "{\"type\":\"submit\",\"text\":\"legacy\"}",
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => {
                Assert.Equal("legacy", input);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    /// <summary>stop_task 分派（M8-8 后续）— 泵停止 fork 任务：CancelForkAsync 被调 +
    /// cancelled 终态 background_task 事件行回推（请求循环不等待 — fire-and-forget 后立即处理 quit）。</summary>
    [Fact]
    public async Task StopTask_DispatchesToPump_EmitsCancelledBackgroundTask() {
        var forkManager = new Mock<IForkSubAgentManager>();
        forkManager.Setup(m => m.CancelForkAsync("fork-123", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        await using var pump = new BackgroundTaskEventPump(forkManager.Object, teammateExecutor: null);
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.StopTask("fork", "fork-123")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None,
            backgroundPump: pump);

        Assert.Equal(0, exitCode);
        forkManager.VerifyAll();
        Assert.Contains("\"type\":\"background_task\",\"kind\":\"fork\",\"taskId\":\"fork-123\",\"description\":\"\",\"state\":\"cancelled\"", stdout.ToString());
    }
    /// <summary>stop_task 泵未注入（引擎无后台能力）— 静默跳过不中断循环（面板无条目，请求实际不可达）。</summary>
    [Fact]
    public async Task StopTask_WithoutPump_SkipsSilently() {
        using var stdin = new StringReader(string.Join('\n',
            WriteRequest(new CliBridge.BridgeRequest.StopTask("fork", "fork-404")),
            WriteRequest(new CliBridge.BridgeRequest.Quit())));
        await using var stdout = new StringWriter();

        var exitCode = await BridgeServeLoop.RunAsync(stdin, stdout, new StringWriter(),
            (input, consumer, ct) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
    }
}
