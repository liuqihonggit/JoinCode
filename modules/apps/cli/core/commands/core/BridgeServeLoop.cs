namespace JoinCode.CliCommands;

/// <summary>
/// bridge 服务循环 — jcc bridge_serve 子命令核心（ADR 0141）：
/// stdin 逐行读 BridgeRequest，stdout 逐行写 BridgeEvent；回合任务与读循环并发
/// （interrupt 可随时到达）。submit → turn_started + 回合执行 + turn_completed；
/// interrupt → 取消回合 → turn_interrupted；忙时 submit → error 诱导提示；
/// 未知 type → stderr 诊断不中断；quit/Eof → 取消在途回合收尾后退出。
/// 回合内部事件（thinking/tool/token/assistant_message）由 BridgeEventConsumer 实时推送，
/// 回合生命周期事件（started/completed/interrupted/error）由本循环统一发出。
/// stdout 写出经 TextWriter.Synchronized + 单次 Write(json+"\n") 保证行原子性（协议统一 LF）。
/// </summary>
internal static class BridgeServeLoop {
    /// <summary>回合执行器 — 生产侧绑定 SessionController.StreamResponseAsync，测试侧注入假执行器模拟 LLM 流。</summary>
    /// <param name="input">用户提交文本</param>
    /// <param name="consumer">回合事件消费器（executor 内触发回调实时推送事件行）</param>
    /// <param name="ct">回合取消令牌（interrupt 触发）</param>
    public delegate Task TurnExecutor(string input, IEventConsumer consumer, CancellationToken ct);

    /// <summary>支持的退出码 — 正常退出（quit/Eof/外部取消均收尾后返回 0）。</summary>
    private const int ExitOk = 0;

    /// <summary>运行协议主循环直到 quit/Eof/外部取消。</summary>
    public static async Task<int> RunAsync(
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        TurnExecutor executeTurn,
        CancellationToken ct) {
        var synced = TextWriter.Synchronized(stdout);
        var consumer = new BridgeEventConsumer(synced);
        CancellationTokenSource? turnCts = null;
        Task? turnTask = null;
        var exit = false;

        while (!exit && !ct.IsCancellationRequested) {
            var result = Cli.Bridge.BridgeProtocol.TryReadRequest(stdin, out var request, out var diagnostic);
            switch (result) {
                case Cli.Bridge.BridgeProtocol.ReadResult.Eof:
                    exit = true;
                    break;
                case Cli.Bridge.BridgeProtocol.ReadResult.Skipped:
                    stderr.WriteLine(diagnostic);
                    break;
                default:
                    if (request is not null)
                        (exit, turnCts, turnTask) = DispatchRequest(request, synced, consumer, executeTurn, turnCts, turnTask);
                    break;
            }
        }

        turnCts?.Cancel();
        if (turnTask is not null)
            await turnTask.ConfigureAwait(false);
        turnCts?.Dispose();
        return ExitOk;
    }

    /// <summary>分派单条请求：submit 起新回合（忙时拒绝）/ interrupt 取消 / quit 退出。</summary>
    private static (bool Exit, CancellationTokenSource? TurnCts, Task? TurnTask) DispatchRequest(
        Cli.Bridge.BridgeRequest request,
        TextWriter stdout,
        BridgeEventConsumer consumer,
        TurnExecutor executeTurn,
        CancellationTokenSource? turnCts,
        Task? turnTask) {
        switch (request) {
            case Cli.Bridge.BridgeRequest.Submit(var text):
                if (turnTask is { IsCompleted: false }) {
                    WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error(
                        "回合进行中，本次提交已忽略。如需提交新输入，请先发送 interrupt 打断当前回合。"));
                    return (false, turnCts, turnTask);
                }

                turnCts?.Dispose();
                var newCts = new CancellationTokenSource();
                var newTask = RunTurnAsync(stdout, consumer, executeTurn, text, newCts.Token);
                return (false, newCts, newTask);
            case Cli.Bridge.BridgeRequest.Interrupt:
                turnCts?.Cancel();
                return (false, turnCts, turnTask);
            case Cli.Bridge.BridgeRequest.Quit:
                return (true, turnCts, turnTask);
            default:
                return (false, turnCts, turnTask);
        }
    }

    /// <summary>执行一个回合：turn_started → executor（事件实时推送）→ completed/interrupted/error。</summary>
    private static async Task RunTurnAsync(
        TextWriter stdout,
        BridgeEventConsumer consumer,
        TurnExecutor executeTurn,
        string input,
        CancellationToken ct) {
        WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnStarted());
        consumer.Reset();
        try {
            await executeTurn(input, consumer, ct).ConfigureAwait(false);
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnCompleted());
        } catch (OperationCanceledException) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.TurnInterrupted());
        } catch (Exception ex) {
            WriteEvent(stdout, new Cli.Bridge.BridgeEvent.Error($"回合执行失败：{ex.Message}"));
        }
    }

    /// <summary>写一条事件行（单次 Write 保证行原子性 + LF + Flush，协议统一 \n）。</summary>
    private static void WriteEvent(TextWriter stdout, Cli.Bridge.BridgeEvent bridgeEvent) {
        stdout.Write(Cli.Bridge.BridgeProtocol.SerializeEvent(bridgeEvent) + "\n");
        stdout.Flush();
    }
}
