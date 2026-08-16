namespace JoinCode.Entry;

/// <summary>
/// TUI 模式启动器 — 使用 Terminal.Gui 渲染层替代纯 CLI。
/// --tui 参数触发，创建 RootView + 所有 TUI 组件，运行 MainLoop。
/// </summary>
internal static class TuiModeRunner
{
    internal static async Task RunAsync(WorkflowConfig config, CommandLineOptions options, IHost host, CancellationToken cancellationToken = default)
    {
        using var app = Application.Create();
        app.Init();

        var painter = new TerminalPainter(app);
        var queue = new CommandQueue();
        var root = new RootView(painter, queue);

        var promptView = new PromptView(queue);
        var outputView = new OutputView();
        var queuedCommandsView = new QueuedCommandsView(queue);
        var statusBarView = new StatusBarView();
        var agentPanesView = new AgentPanesView();
        var permissionDialogView = new PermissionDialogView();

        root.AddComponent(promptView);
        root.AddComponent(outputView);
        root.AddComponent(queuedCommandsView);
        root.AddComponent(statusBarView);
        root.AddComponent(agentPanesView);
        root.AddComponent(permissionDialogView);

        outputView.AppendLine("JoinCode - AI 智能体命令行工具 (TUI 模式)");
        outputView.AppendLine($"模型: {config.CurrentModelId}");
        outputView.AppendLine("输入命令并按 Enter 发送。输入 /exit 退出。");

        var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        window.Add(root);

        app.Invoke(() => promptView.SetFocus());

        await Task.Run(() => app.Run(window), cancellationToken).ConfigureAwait(false);
    }
}
