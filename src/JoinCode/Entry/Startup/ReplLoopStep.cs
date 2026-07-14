namespace JoinCode.Entry;

/// <summary>
/// REPL 循环中间件 — 读取用户输入并处理
/// 生命周期标记（通过 Diag.WriteLine 输出，JCC_VERBOSE=1 时显示，供 E2E 测试事件驱动等待）：
///   [READY] — REPL 循环就绪，等待用户输入
///   [ALIVE] — 处理用户输入期间每 2s 心跳
///   [DONE]  — 单次用户输入处理完成
///   [EXIT]  — 进程即将退出
/// </summary>
[Register]
internal sealed class ReplLoopStep : IMiddleware<StartupContext>
{
    private static readonly TimeSpan AliveInterval = TimeSpan.FromSeconds(2);

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Cli.TerminalHelper.WriteLine("JoinCode CLI - 输入消息或 /help 查看命令");
        Cli.TerminalHelper.WriteLine();
        Diag.WriteLine("[READY]");

        var session = context.Session!;

        while (session.IsRunning && !ct.IsCancellationRequested)
        {
            Cli.TerminalHelper.WriteRaw("> ");
            var input = Cli.TerminalHelper.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                if (Cli.TerminalHelper.IsInputRedirected && !Cli.TerminalHelper.ForceInteractive) break;
                continue;
            }

            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var aliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Ctrl+C 中断当前 LLM 调用而非终止进程
            // 仅在处理期间注册，提示符状态下 Ctrl+C 保持默认退出行为
            void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true; // 阻止默认进程终止
                stepCts.Cancel(); // 中断当前处理
            }
            Console.CancelKeyPress += OnCancelKeyPress;

            var aliveTask = RunAliveLoopAsync(aliveCts.Token);
            try
            {
                await session.ProcessUserInputAsync(input, stepCts.Token);
            }
            catch (OperationCanceledException) when (stepCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Ctrl+C 中断 — 返回提示符继续 REPL 循环
                Cli.TerminalHelper.WriteLine();
                Cli.TerminalHelper.WriteLine("(已中断)");
            }
            catch (Exception ex)
            {
                Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                aliveCts.Cancel();
                try { await aliveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                await Console.Out.FlushAsync().ConfigureAwait(false);
                Diag.WriteLine("[DONE]");
            }
        }

        Diag.WriteLine("[EXIT]");
        await next(context, ct);
    }

    /// <summary>
    /// 心跳循环 — 每 2s 输出 [ALIVE]，通过 Diag.WriteLine 受 JCC_VERBOSE 控制，供 E2E 测试检测进程存活
    /// </summary>
    private static async Task RunAliveLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AliveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                Diag.WriteLine("[ALIVE]");
            }
        }
        catch (OperationCanceledException) { }
    }
}
