namespace NativeTui.Core.Repl;

/// <summary>
/// 注入上下文折叠项（TASK079 用户指令 — 首次注入的提示词/工具提示词在 TUI 上可见可展开）：
/// 塌缩态单行摘要（📎 首次注入 · 系统提示词 N 字符 · 工具总数（完整/按组）· 点击展开），
/// **鼠标左键点击展开/收起**（HitTestId = prompt-context — ReplView 帧序注册 rect，
/// ReplShell.HandleMouseEvent 单击分派 Toggle）；展开态 = 摘要行 + 快照全文逐行（系统提示词
/// 各节 + 工具清单按类目分组）。快照由宿主惰性提供（首次展开才求值 — 引擎会话服务真实数据）。
/// </summary>
public sealed class PromptContextItem : IScrollItem
{
    /// <summary>命中标识（ReplShell.TryDispatchHitTestClick 分派依据）。</summary>
    public const string HitIdValue = "prompt-context";

    /// <summary>快照全文（宿主构建 — 系统提示词全文 + 工具清单文本）。</summary>
    private readonly string _snapshot;

    /// <summary>系统提示词字符数（摘要行展示）。</summary>
    public int PromptChars { get; }

    /// <summary>工具清单惰性求值器（宿主注入 — 首次展开才调注册表：shell 构造早于工具装载，
    /// 急切求值得 0 — TASK079 用户实测修正）。</summary>
    private readonly Func<(string Text, int Total, int System)> _inventoryProvider;

    /// <summary>已求值的工具统计（缓存）。</summary>
    private (string Text, int Total, int System)? _inventory;

    /// <summary>展开态（false = 塌缩摘要行；true = 摘要行 + 快照全文）。</summary>
    public bool Expanded { get; set; }

    /// <summary>摘要行统计追加（首回合 TokenUsage 到达后更新真实 prompt tokens — null = 未更新）。</summary>
    public int? PromptTokens { get; set; }

    public PromptContextItem(string snapshot, int promptChars, Func<(string Text, int Total, int System)> inventoryProvider)
    {
        _snapshot = snapshot;
        PromptChars = promptChars;
        _inventoryProvider = inventoryProvider;
    }

    /// <summary>工具统计（首次展开求值并缓存 — 彼时工具注册表已装载完毕）。</summary>
    private (string Text, int Total, int System) Inventory => _inventory ??= _inventoryProvider();

    /// <summary>命中标识 — 非空即参与鼠标单击命中（左键 Toggle 展开/收起）。</summary>
    public string? HitTestId => HitIdValue;

    /// <summary>切换展开/收起（鼠标单击入口）。</summary>
    public void Toggle() => Expanded = !Expanded;

    /// <summary>塌缩单行摘要；展开态 = 1 + 快照行数（按宽度硬折行）。</summary>
    public int MeasureHeight(int width) =>
        Expanded ? 1 + SnapshotLines(width).Count : 1;

    /// <summary>渲染：塌缩摘要行（📎 前缀 + 统计 + "点击展开"）；
    /// 展开态摘要行变 "点击收起" + 快照全文逐行（dim 缩进 2）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var accent = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var tokenNote = PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
        var summary = Expanded
            ? $"📎 首次注入 · 系统提示词 {PromptChars} 字符{tokenNote} · {Inventory.Total} 工具（{Inventory.System} 完整注入）· /prompt 收起"
            : $"📎 首次注入 · 系统提示词 {PromptChars} 字符{tokenNote} · /prompt 展开查看（含工具清单）";
        var row = frame.Y;
        screen.WriteText(row, frame.X, summary, accent, frame.Right);

        if (!Expanded)
        {
            return;
        }

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var bodyRow = row + 1;
        foreach (var line in SnapshotLines(frame.Width))
        {
            screen.WriteText(bodyRow, frame.X + 2, line, dim, frame.Right);
            bodyRow++;
        }
    }

    /// <summary>快照全文行（按宽度硬折行；展开态 = 提示词全文 + 工具清单）。</summary>
    private List<string> SnapshotLines(int width)
    {
        var text = _snapshot + System.Environment.NewLine + System.Environment.NewLine + Inventory.Text;
        var lines = new List<string>();
        var bodyWidth = Math.Max(20, width - 2);
        foreach (var raw in text.Split('\n'))
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
}
