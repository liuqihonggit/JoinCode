namespace JoinCode.Tui.Views;

/// <summary>
/// 投递中预览组件 — 可视化 CommandQueue 中待处理的命令。
/// 队列空时自动隐藏，窄终端（&lt;40列）时隐藏预览。
/// 对齐 TS 原版 的 PromptInputQueuedCommands 组件。
/// </summary>
public sealed class QueuedCommandsView : ITuiComponent
{
    private readonly CommandQueue _queue;
    private readonly View _container;
    private readonly Label _headerLabel;
    private readonly ListView _listView;
    private int _lastCols;
    private int _lastRows;
    private const int NarrowThreshold = 40;

    /// <summary>
    /// 创建 QueuedCommandsView。
    /// </summary>
    /// <param name="queue">命令队列（驱动预览内容）。</param>
    public QueuedCommandsView(CommandQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));

        _container = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Auto(),
            Visible = false,
        };

        _headerLabel = new Label
        {
            Text = "投递中 (0)",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        _listView = new ListView
        {
            X = 0,
            Y = Pos.Bottom(_headerLabel),
            Width = Dim.Fill(),
            Height = Dim.Auto(),
        };

        _container.Add(_headerLabel, _listView);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
        var pending = snapshot.All;
        var hasPending = pending.Count > 0;
        var isNarrow = _lastCols > 0 && _lastCols < NarrowThreshold;

        _container.Visible = hasPending && !isNarrow;

        if (!hasPending)
        {
            _headerLabel.Text = "投递中 (0)";
            _listView.SetSource(new ObservableCollection<string>());
            return;
        }

        _headerLabel.Text = $"投递中 ({pending.Count})";

        var displayItems = new string[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            var cmd = pending[i];
            var priorityMark = cmd.Priority switch
            {
                QueuePriority.Now => "⚡",
                QueuePriority.Next => "→",
                QueuePriority.Later => "⏳",
                _ => " ",
            };
            var originMark = cmd.Origin switch
            {
                CommandOrigin.User => "",
                CommandOrigin.TaskNotification => "[task] ",
                CommandOrigin.PermissionResponse => "[perm] ",
                _ => "",
            };
            var maxLen = Math.Max(_lastCols - 8, 10);
            var content = cmd.Content.Length > maxLen
                ? string.Concat(cmd.Content.AsSpan(0, maxLen - 3), "...")
                : cmd.Content;
            displayItems[i] = $"{priorityMark} {originMark}{content}";
        }

        _listView.SetSource(new ObservableCollection<string>(displayItems));
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
        _lastCols = cols;
        _lastRows = rows;

        var snapshot = _queue.GetSnapshot();
        OnQueueChanged(snapshot);
    }
}
