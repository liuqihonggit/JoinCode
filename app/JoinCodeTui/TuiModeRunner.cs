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
        var queuedCommands = new QueuedCommandsView(queue);
        var agentPanes = new AgentPanesView();
        var resizeMonitor = new TerminalResizeMonitor();

        statusBar.SetMode("auto");
        statusBar.SetAgentStatus("● Running");
        statusBar.SetSessionId(1);

        root.SetStatusBar(statusBar);
        root.SetToolBar(toolBar);
        root.AddComponent(outputView);
        root.SetPrompt(promptView);
        root.SetFooter(footerTab);
        root.AddComponent(permissionDialog);
        root.AddComponent(queuedCommands);
        root.AddComponent(agentPanes);

        resizeMonitor.SizeChanged += (cols, rows) => painter.NotifyResize(cols, rows);
        resizeMonitor.SizeTooSmall += (w, h, minW, minH) =>
            painter.Invoke(() => outputView.AppendLine($"⚠️ 终端太小 {w}x{h}，建议至少 {minW}x{minH}"));

        outputView.AppendLine($"⚡ AgentOS v1.0  │  Model: {config.CurrentModelId}");
        outputView.AppendLine(new string('─', 60));
        outputView.AppendLine("输入消息后按 Enter 发送，/exit 退出，F1-F5 快捷键");
        outputView.AppendLine("");

        var queryEngine = services.GetService<IQueryEngine>();
        var permissionManager = services.GetService<IToolPermissionManager>();
        var chatHistory = new MessageList();

        var registry = new PipeRegistry();
        var mainPipe = new MessagePipe("main", "AI Assistant", isMain: true);
        registry.Register(mainPipe);

        var polling = new PollingService(registry, 200);
        polling.OnMessagesReceived += (_, messages) =>
        {
            painter.Invoke(() =>
            {
                foreach (var msg in messages)
                    outputView.AppendLine(msg.Content);
            });
        };

        var startTime = DateTime.UtcNow;
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
                        app.RequestStop();
                        break;
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
            painter.Invoke(() =>
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
            });
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
        var processingTask = ProcessQueueAsync(queue, mainPipe, outputView, queryEngine, chatHistory, app.RequestStop, painter, permissionDialog, permissionManager, processingCts.Token);

        var focusSet = false;
        var lastQueueCount = -1;
        app.Iteration += (_, _) =>
        {
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
                queuedCommands.OnQueueChanged(snapshot);
            }
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

            if (string.Equals(cmd.Content, "/exit", StringComparison.OrdinalIgnoreCase))
            {
                requestStop();
                return;
            }

            mainPipe.AddMessage(new TuiMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                AgentId = "main",
                Type = TuiMessageType.User,
                Content = cmd.Content,
                Style = MessageStyle.User,
            });

            painter.Invoke(() => outputView.AppendLine($"👤 {cmd.Content}"));

            if (queryEngine is null)
            {
                painter.Invoke(() => outputView.AppendLine("🤖 (未配置LLM) 请设置 API Key 后使用"));
                continue;
            }

            try
            {
                await foreach (var chunk in queryEngine.QueryAsync(cmd.Content, chatHistory, cancellationToken).ConfigureAwait(false))
                {
                    var text = ChunkToText(chunk);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var capturedText = text;
                        painter.Invoke(() => outputView.AppendLine(capturedText));
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (PermissionPendingConfirmationException ex)
            {
                Task<bool>? dialogTask = null;
                painter.Invoke(() =>
                {
                    dialogTask = permissionDialog.ShowAsync(ex.ToolName, ex.ConfirmationPrompt, cancellationToken);
                });
                var allowed = dialogTask!.GetAwaiter().GetResult();
                painter.Invoke(() => permissionDialog.Hide());

                if (allowed)
                {
                    permissionManager?.ApproveToolTemporarily(ex.ToolName, TimeSpan.FromMinutes(5));
                    painter.Invoke(() => outputView.AppendLine($"  [允许] {ex.ToolName}"));
                    queue.Enqueue(new QueuedCommand(cmd.Content, CommandOrigin.User, QueuePriority.Now));
                }
                else
                {
                    painter.Invoke(() => outputView.AppendLine($"  [拒绝] {ex.ToolName}"));
                }
            }
            catch (Exception ex)
            {
                painter.Invoke(() => outputView.AppendLine($"  [错误] {ex.Message}"));
            }
        }
    }

    /// <summary>
    /// 将 QueryStreamChunk 映射为 OutputView 显示文本行。返回 null 表示该 chunk 无需显示。
    /// 对齐 GUI MainViewModel 的事件处理（7种事件）和 CLI CliEventConsumer（8种事件）。
    /// </summary>
    /// <param name="chunk">查询流式输出块。</param>
    /// <returns>显示文本行，或 null。</returns>
    internal static string? ChunkToText(QueryStreamChunk chunk)
    {
        return chunk.Type switch
        {
            AgentStreamChunkType.Content => chunk.Content,
            AgentStreamChunkType.ThinkingStart => "  [思考开始]",
            AgentStreamChunkType.Thinking => $"  [思考] {chunk.ThinkingContent}",
            AgentStreamChunkType.ThinkingEnd => "  [思考结束]",
            AgentStreamChunkType.ToolCallStart => $"  [工具] {chunk.ToolName}",
            AgentStreamChunkType.ToolCallEnd => FormatToolResult(chunk),
            AgentStreamChunkType.ToolProgress => $"  [进度] {chunk.ProgressMessage}",
            AgentStreamChunkType.LoopDetected => $"  ⚠️ [循环检测] 触发 {chunk.LoopTriggerCount} 次",
            AgentStreamChunkType.TimingSummary => $"  ⏱️ {chunk.Content}",
            AgentStreamChunkType.Complete => FormatComplete(chunk),
            AgentStreamChunkType.Error => $"  [错误] {chunk.Content}",
            _ => null,
        };
    }

    private static string FormatToolResult(QueryStreamChunk chunk)
    {
        var status = chunk.IsToolError ? "❌" : "✅";
        var result = TruncateText(chunk.ToolResultText, 200);
        return $"  [工具] {chunk.ToolName} {status} {result}";
    }

    private static string FormatComplete(QueryStreamChunk chunk)
    {
        if (chunk.Usage is not null)
            return $"  ✅ 完成 │ Token: {chunk.Usage.TotalTokens} │ 模型: {chunk.ModelId}";
        return "  ✅ 完成";
    }

    private static string TruncateText(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length > maxLen ? string.Concat(text.AsSpan(0, maxLen - 3), "...") : text;
    }

    private static void WriteDiag(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcctui_diag");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "run.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch (Exception logEx) { Console.Error.WriteLine($"[diag] WriteDiag failed: {logEx.Message}"); }
    }
}
