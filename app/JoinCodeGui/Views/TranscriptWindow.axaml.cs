namespace JoinCode.Gui.Views;

/// <summary>
/// 子代理回放窗口 — 展示单个 subAgent 的完整时间线（tracker Transcript 全量留痕），
/// 对齐 TS 原版 ctrl+o transcript 模式的 GUI 版。只读快照：打开时定格，不实时刷新。
/// </summary>
public partial class TranscriptWindow : Window
{
    private readonly SubAgentRun _run;

    public TranscriptWindow()
    {
        // 设计器/无参场景
        _run = new SubAgentRun { AgentId = "design", Name = "design", Description = string.Empty };
        InitializeComponent();
    }

    public TranscriptWindow(SubAgentRun run)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        InitializeComponent();
        Title = $"{run.Name} — 子代理回放";
        TitleText.Text = $"{run.Name}{(string.IsNullOrEmpty(run.Description) ? "" : $" — {run.Description}")}";
        TitleGlyph.Text = run.State == SubAgentRunState.Failed ? "✗" : run.State == SubAgentRunState.Completed ? "✓" : "▶";
        StatsText.Text = $"{run.ToolUseCount} 次工具调用 · {run.Transcript.Count} 条记录";
        TranscriptItems.ItemsSource = run.Transcript;
        Loaded += (_, _) => TranscriptScroll.ScrollToEnd();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>复制全部时间线为终端式纯文本（含角色 glyph 与时间戳）</summary>
    private void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{_run.Name}] {_run.Description}");
        foreach (var item in _run.Transcript)
            sb.Append('[').Append(item.At.ToString("HH:mm:ss")).Append("] ")
              .Append(item.Glyph).Append(' ').AppendLine(item.Text);
        SetClipboardTextSafe(sb.ToString());
        StatsText.Text = "已复制到剪贴板";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void SetClipboardTextSafe(string text)
    {
        try
        {
            Clipboard?.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            App.LogDiag($"[TranscriptWindow] 剪贴板写入失败: {ex.Message}");
        }
    }
}
