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
            Cli.TerminalHelper.WriteLine($"供应商: {p.Vendor} | 模型: {p.ModelId} | 流式: {(context.Config.ToolExecution.UseStreamingToolExecution ? "是" : "否")}" +
                (context.Config.CurrentProfile is not null ? $" | 预设: {context.Config.CurrentProfile}" : ""));
            Cli.TerminalHelper.WriteLine($"  端点: {p.Endpoint ?? "(默认)"} | API Key: {(string.IsNullOrEmpty(p.ApiKey) ? "未配置" : "已配置")}");
        }
        Cli.TerminalHelper.WriteLine("JoinCode CLI - 输入消息或 /help 查看命令");
        Cli.TerminalHelper.WriteLine();
        Diag.WriteLifecycle("[AI助手] 就绪");

        var session = context.Session ?? throw new InvalidOperationException("Session not initialized");

        var inputChannel = System.Threading.Channels.Channel.CreateUnbounded<string>();

        var readTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (System.Console.IsInputRedirected && !Cli.TerminalHelper.ForceInteractive)
                    {
                        Diag.WriteLine("[DIAG-REPL] stdin redirected, ForceInteractive=false, returning empty");
                        inputChannel.Writer.TryWrite(string.Empty);
                        return;
                    }

                    Diag.WriteLine("[DIAG-REPL] ReadLineAsync calling...");
                    string? input;
                    try
                    {
                        input = await System.Console.In.ReadLineAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Diag.WriteLine("[DIAG-REPL] ReadLineAsync canceled");
                        break;
                    }

                    Diag.WriteLine($"[DIAG-REPL] ReadLineAsync returned: '{(input is not null && input.Length > 60 ? input[..60] + "..." : input)}', IsNull={input is null}");

                    if (input is null)
                    {
                        Diag.WriteLine("[DIAG-REPL] ReadLineAsync returned null (EOF), exiting read loop");
                        break;
                    }

                    if (ConfirmationGate.Pending && ConfirmationGate.Source is not null)
                    {
                        Diag.WriteLine($"[DIAG-REPL] routing input to confirmation: '{input}'");
                        ConfirmationGate.Source.TrySetResult(input);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        inputChannel.Writer.TryWrite(input);
                        continue;
                    }

                    Diag.WriteLine("[DIAG-REPL] TryWrite to channel");
                    if (!inputChannel.Writer.TryWrite(input)) return;
                    Diag.WriteLine("[DIAG-REPL] TryWrite succeeded");
                }
            }
            catch (OperationCanceledException) { }
            finally { inputChannel.Writer.TryComplete(); }
        }, ct);

        while (session.IsRunning && !ct.IsCancellationRequested)
        {
            using (Cli.TerminalHelper.SetColor(ConsoleColor.Green))
                Cli.TerminalHelper.WriteRaw("[用户] ");

            string combined;
            try
            {
                Diag.WriteLine("[DIAG-REPL] waiting for input...");
                combined = await inputChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
                Diag.WriteLine($"[DIAG-REPL] received input: '{(combined.Length > 80 ? combined[..80] + "..." : combined)}'");
            }
            catch (System.Threading.Channels.ChannelClosedException) { break; }

            if (string.IsNullOrWhiteSpace(combined))
                continue;

            while (inputChannel.Reader.TryRead(out var more))
                combined = string.Concat(combined, "\n", more);

            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var aliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                stepCts.Cancel();
            }
            Console.CancelKeyPress += OnCancelKeyPress;

            var aliveTask = RunAliveLoopAsync(aliveCts.Token);
            try
            {
                Diag.WriteLine("[DIAG-REPL] calling ProcessUserInputAsync");
                await session.ProcessUserInputAsync(combined, stepCts.Token);
                Diag.WriteLine("[DIAG-REPL] ProcessUserInputAsync returned");
            }
            catch (OperationCanceledException) when (stepCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                Cli.TerminalHelper.WriteLine();
                Cli.TerminalHelper.WriteLine("(已中断)");
                    Diag.WriteLine("[DIAG-REPL] OperationCanceledException (Ctrl+C)");
            }
            catch (TimeoutException ex)
            {
                Diag.WriteLine($"[DIAG-REPL] TimeoutException: {ex.Message}");
                using var _ = Cli.TerminalHelper.SetColor(ConsoleColor.Yellow);
                Cli.TerminalHelper.WriteLine();
                Cli.TerminalHelper.WriteLine($"{ex.Message}。请检查：");
                Cli.TerminalHelper.WriteLine("  1. 是否已配置 API Key");
                Cli.TerminalHelper.WriteLine("  2. 网络连接是否正常");
                Cli.TerminalHelper.WriteLine("  3. API 服务是否可用");
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
                Diag.WriteLine($"[DIAG-REPL] Exception: {ex.GetType().Name}: {ex.Message}");
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
                Cli.TerminalHelper.WriteLine();
                Diag.WriteLifecycle("[AI对话结束]");
                using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray))
                    Cli.TerminalHelper.WriteLine(new string('─', Cli.TerminalHelper.GetWidth()));
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
                if (Diag.IsDebugLog) Diag.WriteLifecycle("[ALIVE]");
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
