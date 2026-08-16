namespace JoinCode.Entry;

/// <summary>
/// TUI 模式启动器 — 多 Agent 管道架构 + 轮询拉取 + Terminal.Gui v2 渲染。
/// --tui 参数触发，创建 PipeRegistry + PollingService + 5 区域布局。
/// 布局：状态栏(行1) + 工具栏(行2) + 输出区(中间) + 输入区(底部) + 底部状态(最后1行)。
/// </summary>
internal static class TuiModeRunner
{
    internal static async Task RunAsync(WorkflowConfig config, CommandLineOptions options, IHost host, CancellationToken cancellationToken = default)
    {
        using var app = Application.Create();
        app.Init();

        var queue = new CommandQueue();
        var registry = new PipeRegistry();
        var mainPipe = new MessagePipe("main", "AI Assistant", isMain: true);
        registry.Register(mainPipe);

        var cardManager = new SubAgentCardManager();
        var polling = new PollingService(registry, 200);

        var startTime = DateTime.UtcNow;

        var statusBar = CreateStatusBar(config, startTime);
        var toolbar = CreateToolbar(queue);
        var (outputView, outputContent) = CreateOutputView();
        var (inputField, inputHint) = CreateInputArea(queue);
        var bottomBar = CreateBottomBar();

        polling.OnMessagesReceived += (agentId, messages) =>
        {
            app.Invoke(() =>
            {
                foreach (var msg in messages)
                {
                    var rendered = MessageRenderer.Render(msg);
                    outputContent.Add(rendered);
                }
            });
        };

        inputField.KeyDown += (sender, key) =>
        {
            if (key == TuiKey.Enter)
            {
                var text = inputField.Text.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase))
                    {
                        app.RequestStop();
                        return;
                    }

                    queue.Enqueue(new QueuedCommand(text, CommandOrigin.User, QueuePriority.Next));

                    mainPipe.AddMessage(new TuiMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        AgentId = "main",
                        Type = TuiMessageType.User,
                        Content = text,
                        Style = MessageStyle.User,
                    });

                    inputField.Text = "";
                    UpdateToolbarQueueCount(toolbar, queue);
                    UpdateInputHint(inputHint, queue);
                }
            }
        };

        var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        window.Add(statusBar, toolbar, outputView, inputField, inputHint, bottomBar);

        window.KeyDown += (sender, key) =>
        {
            if (key == TuiKey.F5)
            {
                polling.PollOnce();
            }
        };

        app.Invoke(() => inputField.SetFocus());

        polling.Start();
        try
        {
            await Task.Run(() => app.Run(window), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await polling.StopAsync().ConfigureAwait(false);
        }
    }

    private static Label CreateStatusBar(WorkflowConfig config, DateTime startTime)
    {
        var model = config.CurrentModelId;
        var label = new Label
        {
            Text = $" ⚡ Agent TUI  │  Model: {model}  │  Agents: 1  │  ⏱ 00:00:00",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };
        label.SetAttribute(ColorMapper.ToAttribute(MessageStyle.Content));
        return label;
    }

    private static Label CreateToolbar(CommandQueue queue)
    {
        var label = new Label
        {
            Text = $" [📤 Send Queue: {queue.Count}]  [🔄 Refresh F5]  [📋 Clear Ctrl+L]  [⚙️ Settings F10]",
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
        };
        label.SetAttribute(ColorMapper.ToAttribute(MessageStyle.ToolCall));
        return label;
    }

    private static (View container, View content) CreateOutputView()
    {
        var container = new View
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 5,
        };
        return (container, container);
    }

    private static (TextField field, Label hint) CreateInputArea(CommandQueue queue)
    {
        var field = new TextField
        {
            X = 2,
            Y = Pos.AnchorEnd() - 2,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };

        var hint = new Label
        {
            Text = " Enter发送 · Ctrl+Enter换行 · /exit退出 · 队列: 0条",
            X = 0,
            Y = Pos.AnchorEnd() - 1,
            Width = Dim.Fill(),
            Height = 1,
        };
        hint.SetAttribute(ColorMapper.ToAttribute(MessageStyle.Separator));
        return (field, hint);
    }

    private static Label CreateBottomBar()
    {
        var label = new Label
        {
            Text = " [📋 Log]  [📁 Files]  [🧠 Memory]  [📊 Stats]",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            Height = 1,
        };
        label.SetAttribute(ColorMapper.ToAttribute(MessageStyle.Separator));
        return label;
    }

    private static void UpdateToolbarQueueCount(Label toolbar, CommandQueue queue)
    {
        var text = toolbar.Text.ToString() ?? "";
        var newCount = queue.Count;
        var newText = text.Replace($"Send Queue: {newCount - 1}", $"Send Queue: {newCount}");
        toolbar.Text = newText;
    }

    private static void UpdateInputHint(Label hint, CommandQueue queue)
    {
        hint.Text = $" Enter发送 · Ctrl+Enter换行 · /exit退出 · 队列: {queue.Count}条";
    }
}
