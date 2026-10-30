namespace NativeTui.Core.Repl;

/// <summary>
/// 注入提示词详情页对话框消息项（TASK079 用户指令 — "我要一个新页面，让我看见提示词"）：
/// /prompt 或点击折叠项挂载的只读全量视图 — 标题栏（统计：系统提示词字符数 + 工具总数/完整注入数）
/// + 正文（系统提示词全文逐节 + 工具清单按类目分组，词边界折行）。作为消息项插入滚动区
/// （紧邻输入框 — 立即可见），终端滚动回看全文，esc 关闭移除。
/// 数据 = 构造快照（snapshot 字符串 + inventoryProvider 惰性工具统计 — 页面打开时注册表已装载）。
/// 消费方：ReplShell.ShowPromptPage（AddMessage）+ HandlePromptPageKey（esc 移除）。
/// </summary>
public sealed class PromptDetailDialog : IScrollItem
{
    /// <summary>标题文案。</summary>
    private const string TitleText = "首次注入的提示词";

    /// <summary>底部提示行（esc 关闭引导）。</summary>
    private const string HintText = $"esc 关闭{Figures.MiddotSeparator}终端滚动回看全文";

    /// <summary>系统提示词全文（宿主快照 — 真实 sections 渲染结果）。</summary>
    private readonly string _snapshot;

    /// <summary>系统提示词字符数（标题统计）。</summary>
    private readonly int _promptChars;

    /// <summary>工具清单惰性求值器（页面打开时注册表已装载 — 文本 + 总数 + 完整注入数）。</summary>
    private readonly Func<(string Text, int Total, int System)> _inventoryProvider;

    /// <summary>工具统计缓存（页面生命周期内一次求值）。</summary>
    private (string Text, int Total, int System)? _inventory;

    /// <summary>注入快照与统计。</summary>
    public PromptDetailDialog(string snapshot, int promptChars, Func<(string Text, int Total, int System)> inventoryProvider)
    {
        _snapshot = snapshot;
        _promptChars = promptChars;
        _inventoryProvider = inventoryProvider;
    }

    /// <summary>工具统计（首次访问求值缓存）。</summary>
    private (string Text, int Total, int System) Inventory => _inventory ??= _inventoryProvider();

    /// <summary>系统提示词全文（测试断言面）。</summary>
    internal string SnapshotForTest => _snapshot;

    /// <summary>正文行（快照逐行 + 工具清单逐行 — MeasureHeight/Render 同参一致）。</summary>
    private List<string> BodyLines(int width)
    {
        var bodyWidth = Math.Max(20, width - 2);
        var lines = new List<string>();
        foreach (var raw in (_snapshot + "\n" + Inventory.Text).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length <= bodyWidth)
            {
                lines.Add(line);
                continue;
            }

            for (var i = 0; i < line.Length; i += bodyWidth)
            {
                lines.Add(line.Substring(i, Math.Min(bodyWidth, line.Length - i)));
            }
        }

        return lines;
    }

    /// <summary>内容高度 = margin 1 + 标题 1 + 空行 1 + 统计 1 + 空行 1 + 正文行数 + 空行 1 + 提示 1。</summary>
    public int MeasureHeight(int width) => 7 + BodyLines(width).Count;

    /// <summary>渲染：标题栏（统计）+ 正文逐行（1 缩进）+ 提示行。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var row = frame.Y + 1;
        screen.WriteText(row, frame.X, $" {TitleText}", bold, frame.Right);
        row++;
        var inventory = Inventory;
        screen.WriteText(row, frame.X,
            $" 系统提示词 {_promptChars} 字符{tokenNote()} · 工具 {inventory.Total}（{inventory.System} 完整注入）",
            dim, frame.Right);
        row += 2;
        foreach (var line in BodyLines(frame.Width))
        {
            screen.WriteText(row, frame.X + 1, line, 0, frame.Right);
            row++;
        }

        row++;
        screen.WriteText(row, frame.X, $" {HintText}", dim, frame.Right);
    }

    /// <summary>tokens 备注（PromptTokens 回填后追加 — 折叠项同源）。</summary>
    public int? PromptTokens { get; set; }

    private string tokenNote() => PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
}
