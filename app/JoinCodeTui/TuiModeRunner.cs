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

        // 底层命令系统 — 转发斜杠命令到底层 CmdMap，不自己实现一套
        var commandRegistry = services.GetService<ChatCommandRegistry>() ?? new ChatCommandRegistry();
        if (services.GetService<ChatCommandRegistry>() is null)
            GeneratedCommandRegistration.RegisterAllChatCommands(commandRegistry);
        var toolRegistry = services.GetService<IToolRegistry>();
        var cmdMap = toolRegistry is not null ? new CmdMap(commandRegistry, toolRegistry) : null;

        // CommandServices — 从 DI 获取所有服务，供底层命令使用
        var commandServices = BuildCommandServices(services, commandRegistry, toolRegistry);
        var commandServiceProvider = new CommandServiceProvider(commandServices, services);

        // Tab 补全命令列表 — 从 ISlashCommandCatalog 获取（源码生成器生成的命令元数据）
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
        var processingTask = ProcessQueueAsync(queue, mainPipe, outputView, queryEngine, chatHistory, app.RequestStop, painter, permissionDialog, permissionManager, cmdMap, commandRegistry, commandServiceProvider, statusBar, toolBar, currentQueryCts, processingCts.Token);

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
        CmdMap? cmdMap,
        ChatCommandRegistry commandRegistry,
        IServiceProvider commandServiceProvider,
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

            // 斜杠命令 — 转发到底层 CmdMap，不自己实现一套
            if (cmd.Content.Length > 0 && cmd.Content[0] == '/')
            {
                await HandleSlashCommandAsync(cmd.Content, cmdMap, commandRegistry, commandServiceProvider, outputView, requestStop, painter, permissionDialog, cancellationToken).ConfigureAwait(false);
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
    /// 转发斜杠命令到底层 CmdMap — 解析、路由、执行、捕获输出。
    /// </summary>
    private static async Task HandleSlashCommandAsync(
        string input,
        CmdMap? cmdMap,
        ChatCommandRegistry commandRegistry,
        IServiceProvider commandServiceProvider,
        OutputView outputView,
        Action requestStop,
        TerminalPainter painter,
        PermissionDialogView permissionDialog,
        CancellationToken cancellationToken)
    {
        var parseResult = commandRegistry.Parse(input);
        if (!parseResult.IsSuccess)
        {
            outputView.AppendLine($"  ❌ 解析失败: {parseResult.ErrorMessage}");
            return;
        }

        var commandName = parseResult.CommandName;
        if (commandName is null) return;

        // 路由 — 先查斜杠命令，再查 MCP 工具
        CmdDescriptor? descriptor = null;
        if (cmdMap is not null)
        {
            try
            {
                descriptor = await cmdMap.ResolveAsync(commandName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outputView.AppendLine($"  ❌ 命令路由失败: {ex.Message}");
                return;
            }
        }

        var command = descriptor?.SlashCommand;
        if (command is null)
        {
            outputView.AppendLine($"  ❌ 未知命令: /{commandName}（输入 /help 查看可用命令）");
            return;
        }

        // 构造上下文 — 注入 TUI 的 UI 回调
        var context = new ChatCommandContext
        {
            Arguments = parseResult.Arguments,
            CancellationToken = cancellationToken,
            Services = commandServiceProvider,
            ClearScreen = () => painter.Invoke(() => outputView.Clear()),
            Confirm = msg =>
            {
                // 用 TUI 权限对话框做确认
                Task<bool>? dialogTask = null;
                painter.Invoke(() => dialogTask = permissionDialog.ShowAsync("确认", msg, cancellationToken));
                var result = dialogTask!.GetAwaiter().GetResult();
                painter.Invoke(() => permissionDialog.Hide());
                return result;
            },
            Prompt = msg =>
            {
                // TUI 简化输入：非交互环境返回 null
                return null;
            },
            ReadPassword = _ => string.Empty,
        };

        // 捕获命令输出 — 重定向 TerminalHelper.Out 到 StringBuilder
        var commandOutput = new StringBuilder();
        var originalOut = System.Console.Out;
        using var commandWriter = new System.IO.StringWriter(commandOutput);
        try
        {
            System.Console.SetOut(commandWriter);
            var result = await command.ExecuteAsync(context).ConfigureAwait(false);
            if (!result.ShouldContinue)
                requestStop();
        }
        catch (Exception ex)
        {
            outputView.AppendLine($"  ❌ 命令执行失败: {ex.Message}");
        }
        finally
        {
            System.Console.SetOut(originalOut);
            commandWriter.Flush();
        }

        var outputText = commandOutput.ToString();
        if (!string.IsNullOrWhiteSpace(outputText))
            outputView.AppendLine(outputText.TrimEnd());
    }

    /// <summary>
    /// 从 DI 构造 CommandServices — 供底层命令获取服务。
    /// </summary>
    private static CommandServices BuildCommandServices(
        IServiceProvider services,
        ChatCommandRegistry commandRegistry,
        IToolRegistry? toolRegistry)
    {
        var fs = services.GetService<IFileSystem>() ?? IO.FileSystem.FileSystemFactory.Create();
        var chatService = services.GetService<IChatService>()!;
        var codeService = services.GetService<ICodeService>()!;
        var planService = services.GetService<IPlanService>()!;

        return new CommandServices
        {
            ChatService = chatService,
            CodeService = codeService,
            PlanService = planService,
            FileSystem = fs,
            ServiceProvider = services,
            ToolRegistry = toolRegistry,
            CommandRegistry = commandRegistry,
            GoalEngine = services.GetService<IGoalEngine>(),
            GoalRegistry = services.GetService<IGoalRegistry>(),
            CronTaskStore = services.GetService<ICronTaskStore>(),
            SimpleModeService = services.GetService<ISimpleModeService>(),
            BriefModeService = services.GetService<IBriefModeService>(),
            HookConfigurationManager = services.GetService<IHookConfigurationManager>(),
            PluginManager = services.GetService<IPluginManager>(),
            WorkflowConfig = services.GetService<WorkflowConfig>(),
            ExecutionSettingsProvider = services.GetService<IExecutionSettingsProvider>(),
            MemoryManagementService = services.GetService<IMemoryManagementService>(),
            TaskService = services.GetService<ITaskService>(),
            TodoService = services.GetService<ITodoService>(),
            UsageTracker = services.GetService<IUsageTracker>(),
            PermissionManager = services.GetService<IAgentPermissionManager>(),
            ThinkingStore = services.GetService<IThinkingStore>(),
            RateLimitTracker = services.GetService<IRateLimitTracker>(),
            WorkflowTaskExecutor = services.GetService<IWorkflowTaskExecutor>(),
            ClipboardService = services.GetService<IClipboardService>(),
            WorkspaceService = services.GetService<IWorkspaceService>(),
            FileOperationTracker = services.GetService<IFileOperationTracker>(),
            SessionTagService = services.GetService<ISessionTagService>(),
            WebService = services.GetService<IWebService>(),
            CostTracker = services.GetService<Core.CostTracking.CostTracker>(),
            TokenStorage = services.GetService<ITokenStorage>(),
            PkceGenerator = services.GetService<IPkceGenerator>(),
            WorktreeService = services.GetService<IAgentWorktreeService>(),
            BridgeClient = services.GetService<BridgeClient>(),
        };
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
