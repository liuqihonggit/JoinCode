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

        var queue = new CommandQueue();

        var titleLabel = new Label
        {
            Text = "JoinCode - AI 智能体命令行工具 (TUI 模式)",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        var modelLabel = new Label
        {
            Text = $"模型: {config.CurrentModelId}",
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
        };

        var hintLabel = new Label
        {
            Text = "输入命令并按 Enter 发送。输入 /exit 退出。",
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = 1,
        };

        var separatorLabel = new Label
        {
            Text = new string('-', 60),
            X = 0,
            Y = 3,
            Width = Dim.Fill(),
            Height = 1,
        };

        var outputLabel = new Label
        {
            Text = "",
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = 10,
        };

        var promptLabel = new Label
        {
            Text = "> ",
            X = 0,
            Y = 15,
            Width = 2,
            Height = 1,
        };

        var textField = new TextField
        {
            X = 2,
            Y = 15,
            Width = Dim.Fill(),
            Height = 1,
        };

        textField.KeyDown += (sender, key) =>
        {
            if (key == TuiKey.Enter)
            {
                var text = textField.Text.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (string.Equals(text, "/exit", StringComparison.OrdinalIgnoreCase))
                    {
                        app.RequestStop();
                        return;
                    }
                    outputLabel.Text += $"{Environment.NewLine}> {text}";
                    queue.Enqueue(new QueuedCommand(text, CommandOrigin.User, QueuePriority.Next));
                    textField.Text = "";
                }
            }
        };

        var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        window.Add(titleLabel, modelLabel, hintLabel, separatorLabel, outputLabel, promptLabel, textField);

        app.Invoke(() => textField.SetFocus());

        await Task.Run(() => app.Run(window), cancellationToken).ConfigureAwait(false);
    }
}
