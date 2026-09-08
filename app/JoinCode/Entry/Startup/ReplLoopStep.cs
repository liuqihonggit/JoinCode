namespace JoinCode.Entry;

/// <summary>
/// REPL 循环中间件 — 读取用户输入并处理
/// 生命周期标记（始终输出到 stderr，供 E2E 测试事件驱动等待）：
///   [READY] — REPL 循环就绪，等待用户输入
///   [ALIVE] — 处理用户输入期间每 2s 心跳
///   [DONE]  — 单次用户输入处理完成
///   [EXIT]  — 进程即将退出
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
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
        if (!Cli.TerminalHelper.IsHeadless)
        {
            using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray))
            {
                var hints = string.Join(" | ",
                    L.T(StringKey.FooterBashModeHint),
                    L.T(StringKey.FooterSilentBashModeHint),
                    L.T(StringKey.FooterShortcutsHint));
                Cli.TerminalHelper.WriteLine(hints);
            }
        }
        Cli.TerminalHelper.WriteLine();
        Diag.WriteLifecycle("[AI助手] 就绪");

        var session = context.Session ?? throw new InvalidOperationException("Session not initialized");

        var agentService = context.Host.Services.GetService<JoinCode.Abstractions.Interfaces.IAgentService>();
        var confirmationGate = context.Host.Services.GetService<IConfirmationGate>();
        var proactiveState = context.Host.Services.GetService<IProactiveStateService>();
        var proactiveLogger = context.Host.Services.GetService<ILogger<ProactiveTickScheduler>>();
        TerminalFocusDetector? focusDetector = null;
        ProactiveTickScheduler? tickScheduler = null;
        if (proactiveState is not null)
        {
            focusDetector = new TerminalFocusDetector();
            tickScheduler = new ProactiveTickScheduler(proactiveState, focusDetector, proactiveLogger);
        }

        var isProcessing = 0;

        var commandQueue = new CommandQueue();
        var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var readTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (System.Console.IsInputRedirected && !Cli.TerminalHelper.ForceInteractive)
                    {
                        Diag.WriteLine("[DIAG-REPL] stdin redirected, ForceInteractive=false, signaling loop exit");
                        loopCts.Cancel();
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

                    if (confirmationGate is not null && confirmationGate.Pending && confirmationGate.Source is not null)
                    {
                        Diag.WriteLine($"[DIAG-REPL] routing input to confirmation: '{input}'");
                        confirmationGate.Source.TrySetResult(input);
                        continue;
                    }

                    if (input.Length > 0 && input[0] == '@' && agentService is not null)
                    {
                        var parsed = SubAgentMentionParser.Parse(input);
                        if (parsed is not null)
                        {
                            var (agentName, message) = parsed.Value;
                            await TryForwardToMentionedAgentAsync(agentName, message).ConfigureAwait(false);
                            continue;
                        }

                        using (Cli.TerminalHelper.SetColor(ConsoleColor.Yellow))
                            Cli.TerminalHelper.WriteLine("@语法格式错误，正确格式: @agentName 消息内容（必须用空格分隔）");
                        continue;
                    }

                    if (Interlocked.CompareExchange(ref isProcessing, 0, 0) == 1 && agentService is not null)
                    {
                        if (await TryAutoForwardToRunningAgentAsync(input).ConfigureAwait(false))
                            continue;
                    }

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        commandQueue.Enqueue(new QueuedCommand(input, CommandOrigin.User, QueuePriority.Next));
                        continue;
                    }

                    Diag.WriteLine("[DIAG-REPL] Enqueue to commandQueue");
                    commandQueue.Enqueue(new QueuedCommand(input, CommandOrigin.User, QueuePriority.Next));
                    Diag.WriteLine("[DIAG-REPL] Enqueue succeeded");
                }
            }
            catch (OperationCanceledException) { }
            finally { loopCts.Cancel(); }

            async Task TryForwardToMentionedAgentAsync(string agentName, string message)
            {
                try
                {
                    var agentId = await agentService!.FindAgentIdByNameAsync(agentName, ct).ConfigureAwait(false);
                    if (agentId is not null)
                    {
                        await agentService.ForwardUserInputToAgentAsync(agentId, message, ct).ConfigureAwait(false);
                        Diag.WriteLine($"[DIAG-REPL] forwarded @{agentName} -> agent {agentId}");
                        using (Cli.TerminalHelper.SetColor(ConsoleColor.Cyan))
                            Cli.TerminalHelper.WriteLine($"[已转发给 @{agentName}]");
                    }
                    else
                    {
                        var runningAgents = await agentService.GetRunningAgentsAsync(ct).ConfigureAwait(false);
                        var list = string.Join(", ", runningAgents.Select(a => a.DisplayName ?? a.Id));
                        using (Cli.TerminalHelper.SetColor(ConsoleColor.Yellow))
                            Cli.TerminalHelper.WriteLine($"未找到子代理 @{agentName}，当前运行中: [{list}]");
                    }
                }
                catch (Exception ex)
                {
                    Diag.WriteLine($"[DIAG-REPL] @mention forward failed: {ex.Message}");
                }
            }

            async Task<bool> TryAutoForwardToRunningAgentAsync(string input)
            {
                try
                {
                    var runningAgents = await agentService!.GetRunningAgentsAsync(ct).ConfigureAwait(false);
                    var runningList = runningAgents.ToList();
                    if (runningList.Count == 1)
                    {
                        var agent = runningList[0];
                        await agentService.ForwardUserInputToAgentAsync(agent.Id, input, ct).ConfigureAwait(false);
                        Diag.WriteLine($"[DIAG-REPL] auto-forwarded to single running agent {agent.Id}");
                        using (Cli.TerminalHelper.SetColor(ConsoleColor.Cyan))
                            Cli.TerminalHelper.WriteLine($"[已转发给 @{agent.DisplayName ?? agent.Description ?? agent.Id}]");
                        return true;
                    }
                    else if (runningList.Count > 1)
                    {
                        var list = string.Join(", ", runningAgents.Select(a => a.DisplayName ?? a.Id));
                        using (Cli.TerminalHelper.SetColor(ConsoleColor.Yellow))
                            Cli.TerminalHelper.WriteLine($"多个子代理运行中，输入已缓存，请用 @agentName 指定目标。运行中: [{list}]");
                        commandQueue.Enqueue(new QueuedCommand(input, CommandOrigin.User, QueuePriority.Next));
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Diag.WriteLine($"[DIAG-REPL] auto-forward check failed: {ex.Message}");
                }
                return false;
            }
        }, ct);

        var outputChannelManager = context.Host.Services.GetService<JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager>();
        outputChannelManager?.Register("main", "AI助手");
        var outputDisplayTask = Task.Run(async () =>
        {
            if (outputChannelManager is null) return;
            try
            {
                await foreach (var chunk in outputChannelManager.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    if (!outputChannelManager.ShouldDisplay(chunk.AgentId)) continue;
                    var prefix = chunk.AgentName is not null ? $"[{chunk.AgentName}] " : $"[{chunk.AgentId}] ";
                    using (Cli.TerminalHelper.SetColor(ConsoleColor.Magenta))
                        Cli.TerminalHelper.WriteRaw(prefix);
                    using (Cli.TerminalHelper.SetColor(ConsoleColor.Cyan))
                        Cli.TerminalHelper.WriteRaw(chunk.Content);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        try
        {
            while (session.IsRunning && !loopCts.Token.IsCancellationRequested)
            {
                using (Cli.TerminalHelper.SetColor(ConsoleColor.Green))
                    Cli.TerminalHelper.WriteRaw("[用户] ");

                QueuedCommand queued;
                try
                {
                    Diag.WriteLine("[DIAG-REPL] waiting for input...");
                    queued = await commandQueue.DequeueAsync(loopCts.Token).ConfigureAwait(false);
                    Diag.WriteLine($"[DIAG-REPL] received input: '{(queued.Content.Length > 80 ? queued.Content[..80] + "..." : queued.Content)}'");
                }
                catch (OperationCanceledException) { break; }

                var combined = queued.Content;
                if (string.IsNullOrWhiteSpace(combined))
                    continue;

                while (commandQueue.TryDequeue(out var more))
                    combined = string.Concat(combined, "\n", more.Content);

                Diag.WriteLine("[DIAG-REPL] dispatching to ProcessUserInputAsync");

                await using (var scope = new ReplStepScope(loopCts.Token, x => Interlocked.Exchange(ref isProcessing, x)))
                {
                    await ProcessSingleInputAsync(combined, scope).ConfigureAwait(false);
                }

                if (tickScheduler is not null && commandQueue.Count == 0)
                {
                    var tick = tickScheduler;
                    var tickContent = tick.GenerateTick();
                    if (tickContent is not null)
                    {
                        commandQueue.Enqueue(new QueuedCommand(tickContent, CommandOrigin.ProactiveTick, QueuePriority.Later));
                    }
                }
            }
        }
        catch (OperationCanceledException) { }

        loopCts.Cancel();
        await Task.WhenAll(
            Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2))),
            Task.WhenAny(outputDisplayTask, Task.Delay(TimeSpan.FromSeconds(2)))
        ).ConfigureAwait(false);
        loopCts.Dispose();

        Diag.WriteLifecycle("[EXIT]");
        await next(context, ct);

        async Task ProcessSingleInputAsync(string combined, ReplStepScope scope)
        {
            try
            {
                Diag.WriteLine("[DIAG-REPL] calling ProcessUserInputAsync");
                await session.ProcessUserInputAsync(combined, scope.CancellationToken).ConfigureAwait(false);
                Diag.WriteLine("[DIAG-REPL] ProcessUserInputAsync returned");
            }
            catch (OperationCanceledException) when (scope.IsStepCancellation && !loopCts.Token.IsCancellationRequested)
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
        }
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
            SafeFileIO.WriteAllText(errorLog, errorContent);
        }
        catch (Exception logEx)
        {
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
    }

    /// <summary>
    /// REPL 单步作用域 — 封装单次用户输入处理的"前-后"配对(CTS+事件+心跳任务+状态标记+UI收尾)
    /// 构造时进入(创建CTS+注册Ctrl+C+启动心跳+标记处理中),DisposeAsync 时退出(还原状态+注销事件+取消心跳+等待+UI收尾+释放CTS)
    /// 用 await using var scope = new ReplStepScope(...) 管理生命周期,消除散落的 try-finally 配对
    /// </summary>
    private sealed class ReplStepScope : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stepCts;
        private readonly CancellationTokenSource _aliveCts;
        private readonly Task _aliveTask;
        private readonly Action<int> _setProcessing;
        private readonly ConsoleCancelEventHandler _onCancelKeyPress;
        private int _disposed;

        /// <summary>单步取消令牌 — 传给 ProcessUserInputAsync,Ctrl+C 触发取消</summary>
        public CancellationToken CancellationToken => _stepCts.Token;

        /// <summary>是否因单步(Ctrl+C)取消而非外层取消 — 用于 catch when 区分中断来源</summary>
        public bool IsStepCancellation => _stepCts.IsCancellationRequested;

        public ReplStepScope(CancellationToken loopCt, Action<int> setProcessing)
        {
            _setProcessing = setProcessing;
            _stepCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
            _aliveCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
            _onCancelKeyPress = (_, e) => { e.Cancel = true; _stepCts.Cancel(); };
            Console.CancelKeyPress += _onCancelKeyPress;
            _aliveTask = RunAliveLoopAsync(_aliveCts.Token);
            _setProcessing(1);
        }

        public async ValueTask DisposeAsync()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _setProcessing(0);
            Console.CancelKeyPress -= _onCancelKeyPress;
            _aliveCts.Cancel();
#pragma warning disable VSTHRD003
            try { await _aliveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
#pragma warning restore VSTHRD003
            await Console.Out.FlushAsync().ConfigureAwait(false);
            Cli.TerminalHelper.WriteLine();
            Diag.WriteLifecycle("[AI对话结束]");
            using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray))
                Cli.TerminalHelper.WriteLine(new string('─', Cli.TerminalHelper.GetWidth()));
            _stepCts.Dispose();
            _aliveCts.Dispose();
        }
    }
}
