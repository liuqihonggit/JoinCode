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

        statusBar.SetMode("auto");
        statusBar.SetAgentStatus("● Running");
        statusBar.SetSessionId(1);

        root.SetStatusBar(statusBar);
        root.SetToolBar(toolBar);
        root.AddComponent(outputView);
        root.SetPrompt(promptView);
        root.SetFooter(footerTab);

        outputView.AppendLine($"⚡ AgentOS v1.0  │  Model: {config.CurrentModelId}");
        outputView.AppendLine(new string('─', 60));
        outputView.AppendLine("输入消息后按 Enter 发送，/exit 退出，F1-F5 快捷键");
        outputView.AppendLine("");

        var queryEngine = services.GetService<IQueryEngine>();
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

        var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingTask = ProcessQueueAsync(queue, mainPipe, outputView, queryEngine, chatHistory, app.RequestStop, painter, processingCts.Token);

        var focusSet = false;
        app.Iteration += (_, _) =>
        {
            if (!focusSet)
            {
                focusSet = true;
                promptView.SetFocus();
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
                    var text = chunk.Type switch
                    {
                        AgentStreamChunkType.Content => chunk.Content,
                        AgentStreamChunkType.Thinking => $"  [思考] {chunk.ThinkingContent}",
                        AgentStreamChunkType.ToolCallStart => $"  [工具] {chunk.ToolName}",
                        AgentStreamChunkType.Error => $"  [错误] {chunk.Content}",
                        _ => null,
                    };
                    if (!string.IsNullOrEmpty(text))
                    {
                        var capturedText = text;
                        painter.Invoke(() => outputView.AppendLine(capturedText));
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                painter.Invoke(() => outputView.AppendLine($"  [错误] {ex.Message}"));
            }
        }
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
