namespace JoinCode.Tui;

/// <summary>
/// TUI 模式启动器 — 用 RootView 5层组件架构 + TerminalPainter 统一渲染入口。
/// jcctui.exe 入口，组装 StatusBar/ToolBar/Output/Prompt/FooterTab 五层布局。
/// 接入真实 LLM（IQueryEngine.QueryAsync 流式响应）+ 底部Tab面板（Log/Files/Memory/Settings）。
/// 布局由 RootView 用 Pos.Bottom 链式垂直排列，组件内部用 Pos.Right 链式水平排列。
/// </summary>
internal static class TuiModeRunner
{
    internal static async Task RunAsync(WorkflowConfig config, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var app = Application.Create();
        Application.MaximumIterationsPerSecond = 60;
        app.Init();
        WriteDiag($"[TUI] app.Init done, Initialized={app.Initialized}");

        var queue = new CommandQueue();
        var painter = new TerminalPainter(app.Invoke);
        var root = new RootView(painter, queue);

        var statusBar = new StatusBarView();
        var toolBar = new ToolBarView();
        var outputView = new OutputView();
        var promptView = new PromptView(queue);
        var footerTab = new FooterTabView();
        var permissionDialog = new PermissionDialogView();
        var askUserDialog = new AskUserDialogView();
        var queuedCommands = new QueuedCommandsView(queue);
        var agentPanes = new AgentPanesView();
        var resizeMonitor = new TerminalResizeMonitor();

        statusBar.SetMode("auto");
        statusBar.SetAgentStatus("● Running");
        statusBar.SetSessionId(1);
        statusBar.SetModel(Environment.GetEnvironmentVariable("JCC_MODEL_ID") ?? "(未配置)");

        root.SetStatusBar(statusBar);
        root.SetToolBar(toolBar);
        root.AddComponent(outputView);
        root.SetPrompt(promptView);
        root.SetFooter(footerTab);
        root.AddComponent(permissionDialog);
        root.AddComponent(askUserDialog);
        root.AddComponent(queuedCommands);
        root.AddComponent(agentPanes);

        resizeMonitor.SizeChanged += (cols, rows) => painter.NotifyResize(cols, rows);
        resizeMonitor.SizeTooSmall += (w, h, minW, minH) =>
            outputView.AppendLine($"⚠️ 终端太小 {w}x{h}，建议至少 {minW}x{minH}");

        outputView.AppendLine($"⚡ AgentOS v1.0  │  Model: {config.CurrentModelId}");
        outputView.AppendLine(new string('─', 60));
        outputView.AppendLine("输入消息后按 Enter 发送，/exit 退出，F1-F5 快捷键");
        outputView.AppendLine("");

        var queryEngine = services.GetService<IQueryEngine>();
        var permissionManager = services.GetService<IToolPermissionManager>();
        var chatHistory = new MessageList();

        // T6：会话元信息 — transcript 消息落盘已下沉到引擎 TranscriptPersistMiddleware（三端统一），
        // TUI 此处只写 meta.json 元数据；sessionId 由引擎 IChatContextManager 管理
        var transcriptService = services.GetService<ITranscriptService>();
        if (transcriptService is not null)
        {
            var sessionStore = new Session.TuiSessionStore(transcriptService);
            try
            {
                await sessionStore.SaveMetaAsync(config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteDiag($"[TUI] SaveMetaAsync failed: {ex.Message}");
            }
        }

        // T2：绑定 TUI 问答通道 — ask_user_question 工具经此在 TUI 主循环弹对话框
        services.GetService<Interaction.TerminalGuiInteractiveService>()?
            .Attach(painter, askUserDialog);

        statusBar.SetConnected(queryEngine is not null);

        // Tab 补全命令列表 — 从 ISlashCommandCatalog 获取（源码生成器生成的命令元数据）
        // 斜杠命令执行链路已收敛到共享 SlashCommandRunner（按需自行解析/注册命令表）
        var slashCatalog = services.GetService<ISlashCommandCatalog>();
        var slashCommands = slashCatalog?.Commands
            .Where(c => !c.IsHidden && c.IsEnabled)
            .Select(c => c.Name)
            .OrderBy(n => n)
            .ToArray() as IReadOnlyList<string> ?? [];
        promptView.SetSlashCommands(slashCommands);

        var registry = new PipeRegistry();
        var mainPipe = new MessagePipe("main", "AI Assistant", isMain: true);
        registry.Register(mainPipe);

        var polling = new PollingService(registry, 200);
        polling.OnMessagesReceived += (_, messages) =>
        {
            foreach (var msg in messages)
                outputView.AppendLine(msg.Content);
        };

        var startTime = DateTime.UtcNow;
        // 当前正在执行的查询 CTS 容器 — 工具栏 Stop 与处理循环共享（B6：停止当前生成而非退出程序）
        var currentQueryCts = new System.Runtime.CompilerServices.StrongBox<CancellationTokenSource?>(null);
        toolBar.ActionRequested += action =>
        {
            painter.Invoke(() =>
            {
                switch (action)
                {
                    case ToolBarAction.New:
                        outputView.Clear();
                        chatHistory.Clear();
                        outputView.AppendLine("⚡ 新会话已创建");
                        break;
                    case ToolBarAction.Pause:
                        outputView.AppendLine("⏸ 暂停/恢复 — 轮询切换");
                        _ = Task.Run(async () =>
                        {
                            await polling.StopAsync().ConfigureAwait(false);
                            await Task.Delay(100).ConfigureAwait(false);
                            polling.Start();
                        });
                        break;
                    case ToolBarAction.Stop:
                    {
                        var cts = currentQueryCts.Value;
                        if (cts is not null && !cts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                cts.Cancel();
                                outputView.AppendLine("⏹ 已请求停止当前生成");
                            }
                            catch (ObjectDisposedException)
                            {
                                // 命令恰在此时完成并释放 CTS — 视为无进行中任务
                                outputView.AppendLine("⏹ 当前没有正在进行的生成（/exit 退出程序）");
                            }
                        }
                        else
                        {
                            outputView.AppendLine("⏹ 当前没有正在进行的生成（/exit 退出程序）");
                        }
                        break;
                    }
                    case ToolBarAction.Chat:
                        outputView.AppendLine("💬 Chat 模式 — 对话输出在此显示");
                        break;
                    case ToolBarAction.Stats:
                        var elapsed = DateTime.UtcNow - startTime;
                        outputView.AppendLine($"📊 Stats │ 消息数: {chatHistory.Count} │ 运行时长: {elapsed:hh\\:mm\\:ss}");
                        break;
                }
            });
        };

        footerTab.TabSwitched += tab =>
        {
            switch (tab)
            {
                case FooterTab.Log:
                    outputView.AppendLine("📋 [Log] 日志模式 — 输出实时显示在此区域");
                    break;
                case FooterTab.Files:
                    outputView.AppendLine($"📁 [Files] 当前目录: {Environment.CurrentDirectory}");
                    try
                    {
                        var files = Directory.GetFiles(Environment.CurrentDirectory);
                        var dirs = Directory.GetDirectories(Environment.CurrentDirectory);
                        foreach (var d in dirs.Take(5))
                            outputView.AppendLine($"  📂 {Path.GetFileName(d)}/");
                        foreach (var f in files.Take(10))
                            outputView.AppendLine($"  📄 {Path.GetFileName(f)}");
                        var total = files.Length + dirs.Length;
                        if (total > 15)
                            outputView.AppendLine($"  ... 共 {total} 项");
                    }
                    catch (Exception ex) { outputView.AppendLine($"  [错误] {ex.Message}"); }
                    break;
                case FooterTab.Memory:
                    outputView.AppendLine($"🧠 [Memory] 对话消息数: {chatHistory.Count}");
                    break;
                case FooterTab.Settings:
                    outputView.AppendLine($"⚙️ [Settings] 模型: {config.CurrentModelId}");
                    outputView.AppendLine("  TUI模式: True");
                    break;
            }
        };

        using var timer = new System.Threading.Timer(_ =>
        {
            painter.Invoke(() => footerTab.SetElapsedTime(DateTime.UtcNow - startTime));
        }, null, 1000, 1000);

        var top = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            BorderStyle = LineStyle.None,
        };
        top.Add(root);

        root.KeyDown += (_, key) =>
        {
            if (key == TuiKey.F1) toolBar.TriggerAction(ToolBarAction.New);
            else if (key == TuiKey.F2) toolBar.TriggerAction(ToolBarAction.Pause);
            else if (key == TuiKey.F3) toolBar.TriggerAction(ToolBarAction.Stop);
            else if (key == TuiKey.F4) toolBar.TriggerAction(ToolBarAction.Chat);
            else if (key == TuiKey.F5) { toolBar.TriggerAction(ToolBarAction.Stats); polling.PollOnce(); }
        };

        var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingTask = ProcessQueueAsync(queue, mainPipe, outputView, queryEngine, chatHistory, app.RequestStop, painter, permissionDialog, permissionManager, services, statusBar, toolBar, currentQueryCts, processingCts.Token);

        var focusSet = false;
        var lastQueueCount = -1;
        var iterCount = 0;
        var iterSw = System.Diagnostics.Stopwatch.StartNew();
        var lastIterMs = 0L;
        app.Iteration += (_, _) =>
        {
            var iterSw2 = System.Diagnostics.Stopwatch.StartNew();
            iterCount++;
            var nowMs = iterSw.ElapsedMilliseconds;
            var gap = nowMs - lastIterMs;
            lastIterMs = nowMs;
            if (gap > 50)
                PerfTap.Log("Iteration.slow", gap, $"#{iterCount}");
            if (iterCount % 100 == 0)
                PerfTap.Log("Iteration.stats", gap, $"#{iterCount} avg={nowMs / iterCount}ms");
            outputView.Flush();
            if (!focusSet)
            {
                focusSet = true;
                promptView.SetFocus();
            }
            try
            {
                resizeMonitor.CheckAndNotify(Console.WindowWidth, Console.WindowHeight);
            }
            catch (Exception ex) { WriteDiag($"[TUI] resize check failed: {ex.Message}"); }
            var snapshot = queue.GetSnapshot();
            if (snapshot.All.Count != lastQueueCount)
            {
                lastQueueCount = snapshot.All.Count;
                // 经 painter 广播给全部注册组件（状态栏"队列:N"段/工具栏等），
                // 直调单个视图会绕过其余组件导致死路径（曾致状态栏队列计数永不更新）
                painter.NotifyQueueChanged(snapshot);
            }
            iterSw2.Stop();
            if (iterSw2.ElapsedMilliseconds > 5)
                PerfTap.Log("Iteration.body", iterSw2.ElapsedMilliseconds, $"#{iterCount}");
        };

        polling.Start();
        try
        {
            using var ctReg = cancellationToken.Register(() => app.RequestStop());
            WriteDiag("[TUI] app.Run start");
            app.Run(top);
            WriteDiag("[TUI] app.Run returned");
        }
        finally
        {
            processingCts.Cancel();
            await polling.StopAsync().ConfigureAwait(false);
            try { await processingTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private static async Task ProcessQueueAsync(
        CommandQueue queue,
        MessagePipe mainPipe,
        OutputView outputView,
        IQueryEngine? queryEngine,
        MessageList chatHistory,
        Action requestStop,
        TerminalPainter painter,
        PermissionDialogView permissionDialog,
        IToolPermissionManager? permissionManager,
        IServiceProvider services,
        StatusBarView statusBar,
        ToolBarView toolBar,
        System.Runtime.CompilerServices.StrongBox<CancellationTokenSource?> currentQueryCts,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var cmd = queue.Dequeue();
            if (cmd is null)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // 斜杠命令 — 转发到共享 SlashCommandRunner（与 GUI 同一执行链路）
            if (cmd.Content.Length > 0 && cmd.Content[0] == '/')
            {
                await HandleSlashCommandAsync(cmd.Content, services, outputView, chatHistory, requestStop, painter, permissionDialog, cancellationToken).ConfigureAwait(false);
                continue;
            }

            mainPipe.AddMessage(new TuiMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                AgentId = "main",
                Type = TuiMessageType.User,
                Content = cmd.Content,
                Style = MessageStyle.User,
            });

            outputView.AppendLine($"👤 {cmd.Content}");

            if (queryEngine is null)
            {
                outputView.AppendLine("🤖 (未配置LLM) 请设置 API Key 后使用");
                continue;
            }

            // 每条命令独立 CTS（链接到处理器令牌）— Stop 只取消当前生成，不杀队列循环
            // 记录命令执行前历史快照 — QueryAsync 会先 AddUserMessage 再跑管道，
            // 权限批准后需裁剪回此点再重发（B7 防上下文重复）
            var historySnapshotCount = chatHistory.Count;
            var permissionRetryCount = 0;
            var cmdCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            currentQueryCts.Value = cmdCts;
            try
            {
                painter.Invoke(() => toolBar.SetRunning(true));
                var chunkCount = 0;
                long totalTokens = 0;
                var chunkSw = System.Diagnostics.Stopwatch.StartNew();
                await foreach (var chunk in queryEngine.QueryAsync(cmd.Content, chatHistory, cmdCts.Token).ConfigureAwait(false))
                {
                    chunkCount++;
                    var text = ChunkFormatter.ChunkToText(chunk);
                    if (!string.IsNullOrEmpty(text))
                    {
                        outputView.AppendText(text);
                    }
                    if (chunk.Usage is not null)
                        totalTokens += chunk.Usage.TotalTokens;
                }
                chunkSw.Stop();
                PerfTap.Log("chunk-loop-total", chunkSw.ElapsedMilliseconds, $"chunks={chunkCount} cmd={cmd.Content[..Math.Min(50, cmd.Content.Length)]}");
                if (totalTokens > 0)
                    painter.Invoke(() => statusBar.SetTokenCount(totalTokens));
            }
            catch (OperationCanceledException)
            {
                // 用户主动停止当前生成 — 队列继续处理后续命令，不退出程序
                outputView.AppendLine("  ⏹ 已停止当前生成");
            }
            catch (PermissionPendingConfirmationException ex)
            {
                // T3 重试上限 — 超限不再弹窗，报错终止本轮（对齐 GUI MaxPermissionRetries）
                if (permissionRetryCount >= MaxPermissionRetries)
                {
                    outputView.AppendLine($"  [错误] 权限确认重试次数超限: {ex.ToolName}");
                    continue;
                }

                PermissionConfirmAction? decision = null;
                painter.Invoke(() =>
                {
                    decision = permissionDialog.ShowWithDecisionAsync(ex.ToolName, ex.ConfirmationPrompt, cancellationToken).GetAwaiter().GetResult();
                });
                painter.Invoke(() => permissionDialog.Hide());

                if (decision is { } d && d != PermissionConfirmAction.Deny)
                {
                    var duration = GetApprovalDuration(d);
                    permissionManager?.ApproveToolTemporarily(ex.ToolName, duration);
                    // 撤回本轮（用户消息+部分回复已入历史）再重发，避免上下文重复
                    RewindToSnapshot(chatHistory, historySnapshotCount);
                    permissionRetryCount++;
                    var label = d == PermissionConfirmAction.AlwaysAllow ? "始终允许" : "允许";
                    outputView.AppendLine($"  [{label}] {ex.ToolName}（{duration.TotalMinutes:N0} 分钟）");
                    queue.Enqueue(new QueuedCommand(cmd.Content, CommandOrigin.User, QueuePriority.Now));
                }
                else
                {
                    outputView.AppendLine($"  [拒绝] {ex.ToolName}");
                }
            }
            catch (Exception ex)
            {
                outputView.AppendLine($"  [错误] {ex.Message}");
            }
            finally
            {
                // 先清空引用再释放 — Stop 按钮读到 null 即报"没有正在进行的生成"，避免对已释放实例 Cancel
                currentQueryCts.Value = null;
                cmdCts.Dispose();
                painter.Invoke(() => toolBar.SetRunning(false));
            }
        }
    }

    /// <summary>
    /// 裁剪 chatHistory 回快照点 — 权限批准后重发前调用，撤回本轮已入历史的
    /// 用户消息+部分助手/工具消息，避免重发造成上下文重复（对齐 GUI RewindLastTurnAsync 语义）。
    /// </summary>
    internal static void RewindToSnapshot(MessageList history, int snapshotCount)
    {
        while (history.Count > snapshotCount)
            history.RemoveAt(history.Count - 1);
    }

    /// <summary>权限重试上限（T3 对齐 GUI MaxPermissionRetries）— 超限不再弹窗重发，防止无限循环</summary>
    internal const int MaxPermissionRetries = 3;

    /// <summary>
    /// 权限决策 → 批准时长映射（T3 对齐 GUI JccChatSession 语义）：
    /// 允许一次 = 5 分钟临时批准；始终允许 = 24 小时会话级；拒绝 = 零时长。
    /// </summary>
    internal static TimeSpan GetApprovalDuration(PermissionConfirmAction decision) => decision switch
    {
        PermissionConfirmAction.AlwaysAllow => TimeSpan.FromHours(24),
        PermissionConfirmAction.Allow => TimeSpan.FromMinutes(5),
        _ => TimeSpan.Zero,
    };

    /// <summary>
    /// 从引擎消息记录重建 TUI 本地历史（T1）— 斜杠命令可能改变引擎上下文
    /// （/resume 装入历史、/clear 清空、/compact 压缩摘要），本地 chatHistory 需与引擎
    /// 保持一致，否则后续对话 LLM 收不到恢复的历史。角色字符串经生成的 FromValue 映射，
    /// 未识别角色回退 Tool（与 GUI ReloadMessagesFromEngineAsync 的 User 回退互补覆盖）。
    /// </summary>
    internal static void SyncHistoryFromEngine(MessageList history, IReadOnlyList<ApiMessageRecord> records)
    {
        var rebuilt = new List<ApiMessage>(records.Count);
        foreach (var record in records)
        {
            var role = MessageRoleExtensions.FromValue(record.Role) ?? MessageRole.Tool;
            rebuilt.Add(new ApiMessage(role, record.Content));
        }
        history.ReplaceAll(rebuilt);
    }

    /// <summary>
    /// 转发斜杠命令到底层 CmdMap — 委托共享 <see cref="SlashCommandRunner"/>（与 GUI 同一执行链路）。
    /// </summary>
    private static async Task HandleSlashCommandAsync(
        string input,
        IServiceProvider services,
        OutputView outputView,
        MessageList history,
        Action requestStop,
        TerminalPainter painter,
        PermissionDialogView permissionDialog,
        CancellationToken cancellationToken)
    {
        var result = await SlashCommandRunner.RunAsync(
            input,
            services,
            clearScreen: () => painter.Invoke(() => outputView.Clear()),
            confirm: msg =>
            {
                Task<bool>? dialogTask = null;
                painter.Invoke(() => dialogTask = permissionDialog.ShowAsync("确认", msg, cancellationToken));
                var confirmed = dialogTask!.GetAwaiter().GetResult();
                painter.Invoke(() => permissionDialog.Hide());
                return confirmed;
            },
            prompt: _ => null,
            readPassword: _ => string.Empty,
            onExitRequested: requestStop,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.Output))
            outputView.AppendLine(result.Output);

        // T1：命令可能改变引擎上下文（/resume 装入历史、/clear 清空、/compact 压缩），
        // 重读引擎消息重建本地 chatHistory，保证后续对话 LLM 收到正确上下文
        if (result.Handled)
        {
            try
            {
                var chat = services.GetService<Abstractions.Interfaces.IChatService>();
                if (chat is not null)
                {
                    var records = await chat.GetMessageListAsync(cancellationToken).ConfigureAwait(false);
                    painter.Invoke(() => SyncHistoryFromEngine(history, records));
                }
            }
            catch (Exception syncEx)
            {
                WriteDiag($"[T1] history sync failed: {syncEx.Message}");
            }
        }
    }

    private static void WriteDiag(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcctui_diag");
            System.IO.Directory.CreateDirectory(dir);
            SafeFileIO.AppendAllText(
                System.IO.Path.Combine(dir, "run.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch (Exception logEx) { Console.Error.WriteLine($"[diag] WriteDiag failed: {logEx.Message}"); }
    }
}
