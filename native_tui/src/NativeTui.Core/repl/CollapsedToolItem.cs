namespace NativeTui.Core.Repl;

/// <summary>
/// 工具调用聚合项（M8-4）— 连续同名可聚合工具调用（Read/Grep/Glob）的单行摘要投影，
/// 对齐基准 CollapsedReadSearchContent 的 "Read N files" 折叠形态（中文文案）：
/// 单次（Count=1）渲染与 ToolCallItem 像素一致（● bold 工具名 + (参数) — 单次视觉零变化）；
/// 聚合态（Count≥2）摘要行 "● 读取 N 个文件"（● bullet + 动词 bold + 数量段默认样式）+
/// 最新操作提示行 "  ⎿  {LastArgs}" 整行 dim（基准组下方 latest file_path 行）。
/// 高度随 Count 变化（2 → 3）由 ScrollBox 每帧全量重渲自动生效（无需失效通知）。
/// 消费方：ReplBridgeDriver 聚合路由（第二次同名调用替换链头 ToolCallItem + 后续 RecordCall 原地更新）。
/// </summary>
public sealed class CollapsedToolItem(string toolName, IReadOnlyList<string> calls) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>历史调用参数清单（全量保留 — transcript 搜索匹配任一，历史路径不丢）。</summary>
    private readonly List<string> _calls = [.. calls];

    /// <summary>聚合的工具名（同名合并键 — Driver 链路由判定）。</summary>
    public string ToolName => toolName;

    /// <summary>聚合调用次数。</summary>
    public int Count => _calls.Count;

    /// <summary>最新一次调用参数（聚合态提示行正文）。</summary>
    public string LastArgs => _calls[^1];

    /// <summary>聚合摘要文案（断言面）— "读取 N 个文件" / "搜索 N 次" / "查找 N 个模式" / 兜底 "Tool × N"。</summary>
    public string SummaryText
    {
        get
        {
            var (verb, countPhrase) = Summarize(toolName, Count);
            return $"{verb} {countPhrase}";
        }
    }

    /// <summary>是否可聚合工具 — Read/Grep/Glob（基准语义：高频批量读/搜/找才折叠；Edit/Bash/Write 等独立上屏）。</summary>
    public static bool IsAggregatable(string toolName) => toolName is "Read" or "Grep" or "Glob";

    /// <summary>后续同名调用到达 — 计数与最新提示行原地更新（下帧渲染自动反映）。</summary>
    public void RecordCall(string args) => _calls.Add(args);

    /// <summary>内容高度 = margin 1 + 摘要 1 +（聚合态）最新提示行 1。</summary>
    public int MeasureHeight(int width) => _calls.Count > 1 ? 3 : 2;

    /// <summary>渲染：margin 留空 → 单次 = ToolCall 形态 / 聚合态 = 摘要行 + 最新提示行。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (_calls.Count == 1)
        {
            MessageRenderer.RenderToolCall(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, toolName, _calls[0]);
            return;
        }

        var (verb, countPhrase) = Summarize(toolName, Count);
        MessageRenderer.RenderCollapsedToolHeader(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, verb, countPhrase);
        MessageRenderer.RenderCollapsedToolLastHint(screen, frame with { Y = frame.Y + 2, Height = 1 }, theme, LastArgs);
    }

    /// <summary>摘要分段派生 — 动词段（bold 渲染）+ 数量段（默认样式渲染）；未知工具兜底 "Tool × N"（Driver 白名单外的防御形态）。</summary>
    private static (string Verb, string CountPhrase) Summarize(string toolName, int count) => toolName switch
    {
        "Read" => ("读取", $"{count} 个文件"),
        "Grep" => ("搜索", $"{count} 次"),
        "Glob" => ("查找", $"{count} 个模式"),
        _ => (toolName, $"× {count}"),
    };

    /// <summary>工具名 + 摘要 + 任一调用参数匹配（transcript 搜索 — 大小写不敏感）。</summary>
    public bool ContainsText(string query) =>
        toolName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || SummaryText.Contains(query, StringComparison.OrdinalIgnoreCase)
        || _calls.Exists(args => args.Contains(query, StringComparison.OrdinalIgnoreCase));
}
