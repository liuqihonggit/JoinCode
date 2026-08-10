namespace JoinCode.Entry;

/// <summary>
/// REPL 循环中间件 — 读取用户输入并处理
/// 生命周期标记（始终输出到 stderr，供 E2E 测试事件驱动等待）：
///   [READY] — REPL 循环就绪，等待用户输入
///   [ALIVE] — 处理用户输入期间每 2s 心跳
///   [DONE]  — 单次用户输入处理完成
///   [EXIT]  — 进程即将退出
/// </summary>
[Register]
internal sealed partial class ReplLoopStep : ServiceEntity, IMiddleware<StartupContext>
{
    private static readonly TimeSpan AliveInterval = TimeSpan.FromSeconds(2);

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var p = context.Config.Provider;
        using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray))
        {
            Cli.TerminalHelper.WriteLine($"当前配置: 供应商={p.Vendor}, 模型={p.ModelId}");
            Cli.TerminalHelper.WriteLine($"  端点={p.Endpoint ?? "(默认)"}, API Key={(string.IsNullOrEmpty(p.ApiKey) ? "未配置" : "已配置")}");
        }
        Cli.TerminalHelper.WriteLine("JoinCode CLI - 输入消息或 /help 查看命令");
        Cli.TerminalHelper.WriteLine();
        Diag.WriteLifecycle("[READY]");

        var session = context.Session ?? throw new InvalidOperationException("Session not initialized");

        var inputChannel = System.Threading.Channels.Channel.CreateUnbounded<string>();

        var readTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var _ = Cli.TerminalHelper.SetColor(ConsoleColor.Green);
                    Cli.TerminalHelper.WriteRaw("> ");
                    var input = Cli.TerminalHelper.ReadLine();
                    if (string.IsNullOrWhiteSpace(input))
                    {
                        if (Cli.TerminalHelper.IsInputRedirected && !Cli.TerminalHelper.ForceInteractive) return;
                        continue;
                    }
                    if (!inputChannel.Writer.TryWrite(input)) return;
                }
            }
            catch (OperationCanceledException) { }
            finally { inputChannel.Writer.TryComplete(); }
        }, ct);

        while (session.IsRunning && !ct.IsCancellationRequested)
        {
            string combined;
            try
            {
                combined = await inputChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException) { break; }

            while (inputChannel.Reader.TryRead(out var more))
                combined = string.Concat(combined, "\n", more);

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
                await session.ProcessUserInputAsync(combined, stepCts.Token);
            }
            catch (OperationCanceledException) when (stepCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Ctrl+C 中断 — 返回提示符继续 REPL 循环
                Cli.TerminalHelper.WriteLine();
                Cli.TerminalHelper.WriteLine("(已中断)");
            }
            catch (Exception ex)
            {
                // 单轮失败不应杀死 REPL — 记录错误日志 + 分类提示后继续循环
                WriteErrorLog(ex);
                using var _ = Cli.TerminalHelper.SetColor(ConsoleColor.Red);
                Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
                if (ex is JoinCode.Abstractions.Exceptions.ApiException apiEx && apiEx.IsRetryable)
                    Cli.TerminalHelper.WriteLine("  此错误通常可重试，请稍后再试。");
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                aliveCts.Cancel();
                try { await aliveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                await Console.Out.FlushAsync().ConfigureAwait(false);
                Diag.WriteLifecycle("[DONE]");
            }
        }

        inputChannel.Writer.TryComplete();
        await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

        Diag.WriteLifecycle("[EXIT]");
        await next(context, ct);
    }

    /// <summary>
    /// 心跳循环 — 每 2s 输出 [ALIVE] 到 stderr，供 E2E 测试检测进程存活
    /// </summary>
    private static async Task RunAliveLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AliveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (Diag.IsVerbose) Diag.WriteLifecycle("[ALIVE]");
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 写入错误日志到临时目录的 jcc_error.log — 与 Program.WriteErrorLog 一致
    /// </summary>
    private static void WriteErrorLog(Exception ex, ILogger? logger = null)
    {
        var errorLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcc_error.log");
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try
        {
            System.IO.File.WriteAllText(errorLog, errorContent);
        }
        catch (Exception logEx)
        {
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
    }
}
