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
        Assert.Equal("{\"type\":\"token_delta\",\"length\":4}", lines[3]);
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
        var errorLine = stdout.ToString().Split('\n').Single(l => l.Contains("\"error\""));
        Assert.Contains("LLM 连接失败", errorLine);
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
}
