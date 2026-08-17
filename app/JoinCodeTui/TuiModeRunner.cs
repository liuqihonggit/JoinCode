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
        Application.MaximumIterationsPerSecond = 200;
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
        statusBar.SetModel(Environment.GetEnvironmentVariable("JCC_MODEL_ID") ?? "(未配置)");

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
            outputView.AppendLine($"⚠️ 终端太小 {w}x{h}，建议至少 {minW}x{minH}");

        outputView.AppendLine($"⚡ AgentOS v1.0  │  Model: {config.CurrentModelId}");
        outputView.AppendLine(new string('─', 60));
        outputView.AppendLine("输入消息后按 Enter 发送，/exit 退出，F1-F5 快捷键");
        outputView.AppendLine("");

        var queryEngine = services.GetService<IQueryEngine>();
        var permissionManager = services.GetService<IToolPermissionManager>();
        var chatHistory = new MessageList();

        statusBar.SetConnected(queryEngine is not null);

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
        var processingTask = ProcessQueueAsync(queue, mainPipe, outputView, queryEngine, chatHistory, app.RequestStop, painter, permissionDialog, permissionManager, processingCts.Token);

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
                queuedCommands.OnQueueChanged(snapshot);
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

            var slashResult = TuiCommandProcessor.Process(cmd.Content, chatHistory);
            if (slashResult.IsHandled)
            {
                if (!string.IsNullOrEmpty(slashResult.Output))
                {
                    outputView.AppendLine(slashResult.Output);
                }
                switch (slashResult.Action)
                {
                    case TuiCommandAction.Exit:
                        requestStop();
                        return;
                    case TuiCommandAction.ClearOutput:
                        painter.Invoke(() => outputView.Clear());
                        break;
                    case TuiCommandAction.ExecuteShell:
                        await ExecuteShellAsync(slashResult.ShellCommand!, outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteBuild:
                        await ExecuteShellAsync("dotnet build", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteTest:
                        await ExecuteShellAsync("dotnet test", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.SaveSession:
                        SaveSession(chatHistory, outputView, painter);
                        break;
                    case TuiCommandAction.ExecuteGrep:
                        await ExecuteShellAsync($"findstr /s /i /n \"{slashResult.ShellCommand}\" *.cs *.md *.json", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteDiff:
                        await ExecuteShellAsync("git diff", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteFiles:
                        await ExecuteShellAsync($"dir /s /b {slashResult.ShellCommand}", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteOpen:
                        OpenFile(slashResult.ShellCommand!, outputView, painter);
                        break;
                    case TuiCommandAction.ExecutePatch:
                        OpenFile(slashResult.ShellCommand!, outputView, painter);
                        break;
                    case TuiCommandAction.ExecuteApply:
                        await ExecuteShellAsync($"git apply {slashResult.ShellCommand}", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteUndo:
                        await ExecuteShellAsync("git checkout .", outputView, cancellationToken).ConfigureAwait(false);
                        break;
                    case TuiCommandAction.ExecuteLoad:
                        OpenFile(slashResult.ShellCommand!, outputView, painter);
                        break;
                    case TuiCommandAction.ShowConfig:
                        ShowConfig(outputView, painter);
                        break;
                    case TuiCommandAction.ShowModel:
                        ShowModel(outputView, painter);
                        break;
                    case TuiCommandAction.SetModel:
                        outputView.AppendLine($"  ⚠️ 运行时切换模型需重启 jcctui，请设置环境变量 JCC_MODEL_ID={slashResult.ShellCommand} 后重新启动");
                        break;
                    case TuiCommandAction.ListSessions:
                        ListSessions(outputView, painter);
                        break;
                    case TuiCommandAction.ShowTokens:
                        ShowTokens(chatHistory, outputView, painter);
                        break;
                    case TuiCommandAction.ClearHistory:
                        chatHistory.Clear();
                        outputView.AppendLine("  🗑️ 聊天历史已清空");
                        break;
                }
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

            try
            {
                var chunkCount = 0;
                var chunkSw = System.Diagnostics.Stopwatch.StartNew();
                await foreach (var chunk in queryEngine.QueryAsync(cmd.Content, chatHistory, cancellationToken).ConfigureAwait(false))
                {
                    chunkCount++;
                    var text = ChunkFormatter.ChunkToText(chunk);
                    if (!string.IsNullOrEmpty(text))
                    {
                        outputView.AppendLine(text);
                    }
                }
                chunkSw.Stop();
                PerfTap.Log("chunk-loop-total", chunkSw.ElapsedMilliseconds, $"chunks={chunkCount} cmd={cmd.Content[..Math.Min(50, cmd.Content.Length)]}");
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
                    outputView.AppendLine($"  [允许] {ex.ToolName}");
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
        }
    }

    /// <summary>
    /// 执行 shell 命令并将输出显示到 OutputView。
    /// </summary>
    private static async Task ExecuteShellAsync(
        string command,
        OutputView outputView,
        CancellationToken cancellationToken)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();
            var outputTask = ReadStreamAsync(proc.StandardOutput, outputView, cancellationToken);
            var errorTask = ReadStreamAsync(proc.StandardError, outputView, cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            var exitCode = proc.ExitCode;
            outputView.AppendLine($"  [退出码 {exitCode}]");
        }
        catch (Exception ex)
        {
            outputView.AppendLine($"  [执行失败] {ex.Message}");
        }
    }

    private static async Task ReadStreamAsync(
        System.IO.StreamReader reader,
        OutputView outputView,
        CancellationToken cancellationToken)
    {
        var lineCount = 0;
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            lineCount++;
            outputView.AppendLine($"  {line}");
        }
        totalSw.Stop();
        PerfTap.Log("shell-read-total", totalSw.ElapsedMilliseconds, $"lines={lineCount}");
    }

    /// <summary>
    /// 保存聊天历史到文件。
    /// </summary>
    private static void SaveSession(MessageList history, OutputView outputView, TerminalPainter painter)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".jcctui");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var sb = new StringBuilder();
            for (var i = 0; i < history.Count; i++)
            {
                var msg = history[i];
                var role = msg.Role switch
                {
                    MessageRole.User => "User",
                    MessageRole.Assistant => "Assistant",
                    MessageRole.System => "System",
                    _ => "Tool",
                };
                sb.Append($"[{role}] {msg.Content}\n");
            }
            System.IO.File.WriteAllText(path, sb.ToString());
            outputView.AppendLine($"  💾 已保存到 {path}");
        }
        catch (Exception ex)
        {
            outputView.AppendLine($"  [保存失败] {ex.Message}");
        }
    }

    /// <summary>
    /// 显示文件内容到 OutputView。
    /// </summary>
    private static void OpenFile(string filePath, OutputView outputView, TerminalPainter painter)
    {
        try
        {
            if (!System.IO.File.Exists(filePath))
            {
                outputView.AppendLine($"  [文件不存在] {filePath}");
                return;
            }
            var content = System.IO.File.ReadAllText(filePath);
            var lines = content.Split('\n');
            var maxLines = 200;
            var displayCount = Math.Min(lines.Length, maxLines);
            for (var i = 0; i < displayCount; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var captured = line;
                outputView.AppendLine($"  {captured}");
            }
            if (lines.Length > maxLines)
                outputView.AppendLine($"  ... ({lines.Length} 行，仅显示前 {maxLines} 行)");
        }
        catch (Exception ex)
        {
            outputView.AppendLine($"  [打开失败] {ex.Message}");
        }
    }

    /// <summary>
    /// 显示当前配置信息。
    /// </summary>
    private static void ShowConfig(OutputView outputView, TerminalPainter painter)
    {
        var endpoint = Environment.GetEnvironmentVariable("JCC_ENDPOINT") ?? "(未设置)";
        var vendor = Environment.GetEnvironmentVariable("JCC_VENDOR") ?? "(未设置)";
        var model = Environment.GetEnvironmentVariable("JCC_MODEL_ID") ?? "(未设置)";
        var apiKey = Environment.GetEnvironmentVariable("JCC_API_KEY");
        var keyDisplay = string.IsNullOrEmpty(apiKey) ? "(未设置)" : $"{apiKey[..Math.Min(4, apiKey.Length)]}****";
        outputView.AppendLine("  ⚙️ 当前配置:");
        outputView.AppendLine($"    Endpoint: {endpoint}");
        outputView.AppendLine($"    Vendor:   {vendor}");
        outputView.AppendLine($"    Model:    {model}");
        outputView.AppendLine($"    API Key:  {keyDisplay}");
    }

    /// <summary>
    /// 显示当前模型信息。
    /// </summary>
    private static void ShowModel(OutputView outputView, TerminalPainter painter)
    {
        var model = Environment.GetEnvironmentVariable("JCC_MODEL_ID") ?? "(未设置)";
        var vendor = Environment.GetEnvironmentVariable("JCC_VENDOR") ?? "(未设置)";
        outputView.AppendLine($"  🤖 模型: {model}");
        outputView.AppendLine($"  🏷️ 供应商: {vendor}");
    }

    /// <summary>
    /// 列出 .jcctui/ 目录下的已保存会话。
    /// </summary>
    private static void ListSessions(OutputView outputView, TerminalPainter painter)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".jcctui");
            if (!System.IO.Directory.Exists(dir))
            {
                outputView.AppendLine("  📋 无已保存会话（.jcctui/ 目录不存在）");
                return;
            }
            var files = System.IO.Directory.GetFiles(dir, "session_*.txt");
            if (files.Length == 0)
            {
                outputView.AppendLine("  📋 无已保存会话");
                return;
            }
            outputView.AppendLine("  📋 已保存会话:");
            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileName(file);
                var time = System.IO.File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm");
                var capturedName = name;
                var capturedTime = time;
                outputView.AppendLine($"    {capturedName} ({capturedTime})");
            }
        }
        catch (Exception ex)
        {
            outputView.AppendLine($"  [列出失败] {ex.Message}");
        }
    }

    /// <summary>
    /// 显示聊天历史中的 Token 用量统计。
    /// </summary>
    private static void ShowTokens(MessageList history, OutputView outputView, TerminalPainter painter)
    {
        long totalTokens = 0;
        var msgCount = 0;
        foreach (var msg in history)
        {
            msgCount++;
            if (msg.TokenUsage is not null)
                totalTokens += msg.TokenUsage.TotalTokens;
        }
        outputView.AppendLine("  🔢 Token 用量:");
        outputView.AppendLine($"    消息数: {msgCount}");
        outputView.AppendLine($"    总 Token: {totalTokens}");
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
