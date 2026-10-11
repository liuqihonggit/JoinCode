namespace JoinCode.Gui.Views;

/// <summary>
/// 子代理回放窗口 — 展示单个 subAgent 的完整活动历史（引擎层 ActivityHistory 全量留痕），
/// 对齐 TS 原版 ctrl+o transcript 模式的 GUI 版。只读快照：打开时定格，不实时刷新。
/// </summary>
public partial class TranscriptWindow : Window {
    private readonly BackgroundAgentItemVm _run;

    /// <summary>初始化 TranscriptWindow 实例</summary>
    public TranscriptWindow() {
        _run = new BackgroundAgentItemVm(new BackgroundAgentInfo(
            AgentId: "design", Name: "design", Description: string.Empty,
            State: AgentStatus.Completed, StartedAt: null, ToolUseCount: 0, TokenCount: 0,
            Activities: Array.Empty<AgentActivityEntry>(), LastActivityText: null,
            FinalOutput: null, IsSuccess: null, ExecutionTimeMs: null, Role: null));
        InitializeComponent();
    }

    /// <summary>以指定子代理运行记录构建回放窗口</summary>
    public TranscriptWindow(BackgroundAgentItemVm run) {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        InitializeComponent();
        Title = $"{run.Name} — 子代理回放";
        TitleText.Text = $"{run.Name}{(string.IsNullOrEmpty(run.Description) ? "" : $" — {run.Description}")}";
        TitleGlyph.Text = run.State == AgentStatus.Failed ? "✗" : run.State == AgentStatus.Completed ? "✓" : "▶";
        StatsText.Text = $"{run.ToolUseCount} 次工具调用 · {run.Activities.Count} 条记录";
        TranscriptItems.ItemsSource = run.Activities;
        Loaded += (_, _) => TranscriptScroll.ScrollToEnd();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>复制全部时间线为终端式纯文本（含角色 glyph 与时间戳）</summary>
    private void OnCopyAllClick(object? sender, RoutedEventArgs e) {
        var sb = new StringBuilder();
        sb.AppendLine($"[{_run.Name}] {_run.Description}");
        foreach (var item in _run.Activities)
            sb.Append('[').Append(item.Timestamp.ToString("HH:mm:ss")).Append("] ")
              .Append(item.Glyph).Append(' ').AppendLine(item.Text);
        SetClipboardTextSafe(sb.ToString());
        StatsText.Text = "已复制到剪贴板";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void SetClipboardTextSafe(string text) {
        try {
            Clipboard?.SetTextAsync(text);
        } catch (Exception ex) {
            App.LogDiag($"[TranscriptWindow] 剪贴板写入失败: {ex.Message}");
        }
    }
}
