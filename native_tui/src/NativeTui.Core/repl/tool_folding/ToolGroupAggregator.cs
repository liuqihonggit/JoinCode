namespace NativeTui.Core.Repl;

/// <summary>
/// 工具分组聚合状态机 — 连续同类工具调用（Read/Grep/Glob，ToolCall+ToolResult 成对到达）聚合为
/// 单行摘要的纯组件（基准 CollapsedReadSearchContent.tsx L142-396 非 verbose 形态）：
/// 计数单调不回退（max-ref 防回抖 — 基准 L171-184 maxReadCountRef/maxSearchCountRef，流式窗口
/// 计数瞬时回落不抖动）；hint 最短展示 700ms（MIN_HINT_DISPLAY_MS — 帧拍时间戳结算，禁 Task.Delay，
/// 基准 useMinDisplayTime 语义：每个不同 hint 值至少展示 700ms 才允许被替换）。
/// 分组边界（开组/收组时机）由 Driver 判定路由 — 本组件只管计数与 hint（S1 接线）。
/// </summary>
public sealed class ToolGroupAggregator
{
    /// <summary>待切换 hint（最新到达值 — 等待 700ms 窗口结算）。</summary>
    private string? _pendingHint;

    /// <summary>当前展示 hint（700ms 窗口结算后的值）。</summary>
    private string? _displayedHint;

    /// <summary>当前展示 hint 的起始帧拍时间戳。</summary>
    private long _displayedSinceMs;

    /// <summary>是否已展示过任一 hint（首个 hint 立即显示 — 基准 lastShownAtRef=0 墙钟语义）。</summary>
    private bool _hasDisplayedHint;

    /// <summary>读取文件计数（Read 调用 — 只增不减的单调计数，max-ref 语义）。</summary>
    public int ReadCount { get; private set; }

    /// <summary>搜索模式计数（Grep/Glob 调用 — 只增不减的单调计数）。</summary>
    public int SearchCount { get; private set; }

    /// <summary>是否有未完成的 ToolCall（组活动态 — Driver 在 ToolCall 置位 / ToolResult 复位）。</summary>
    public bool HasPendingToolCall { get; private set; }

    /// <summary>
    /// 聚合摘要单行 — "读取 N 个文件 · 搜索 M 个模式"（基准 "Read N files, searched M patterns"
    /// 汉化对齐；空组返回空串由渲染方跳过）。
    /// </summary>
    public string SummaryText => string.Join(Figures.MiddotSeparator, BuildParts());

    /// <summary>当前展示 hint（null = 无；最新一次操作路径/模式，700ms 防闪烁结算后）。</summary>
    public string? HintText => _displayedHint;

    /// <summary>判定工具是否纳入聚合 — Read/Grep/Glob 三类（基准 collapseReadSearch 收录口径）。</summary>
    public static bool Accepts(string toolName) =>
        toolName is ToolTitleFormatter.ReadTool or ToolTitleFormatter.GrepTool or ToolTitleFormatter.GlobTool;

    /// <summary>
    /// 工具调用到达 — 分类计数（单调递增不回退）+ hint 派生（Read 取路径 / Grep/Glob 取
    /// 引号包裹 pattern，基准 L194-199 latestDisplayHint 口径）。首个 hint 立即展示
    /// （基准 lastShownAtRef=0 墙钟语义 — 未展示过直接落位，展示起点待首帧校准）。
    /// </summary>
    public void OnToolCall(string toolName, string args)
    {
        if (toolName is ToolTitleFormatter.ReadTool)
        {
            ReadCount++;
        }
        else
        {
            SearchCount++;
        }

        HasPendingToolCall = true;
        var hint = DeriveHint(toolName, args);
        if (!_hasDisplayedHint && hint is not null)
        {
            _displayedHint = hint;
            _displayedSinceMs = -1;
            _hasDisplayedHint = true;
        }

        _pendingHint = hint;
    }

    /// <summary>工具结果到达 — 清除待完成态（hint 保留最新值继续展示，基准 latestDisplayHint 定格语义）。</summary>
    public void OnToolResult()
    {
        HasPendingToolCall = false;
    }

    /// <summary>
    /// 帧拍结算（唯一时间入口 — item 渲染时用 AnimTimeMs 推进，50ms 帧拍）：
    /// 首帧校准首个 hint 的展示起点（OnToolCall 无时间参数 — 到达时刻未知，以首帧近似）；
    /// 待切换 hint ≠ 当前展示且展示时长已满 700ms → 切换并记起始时间戳。
    /// 幂等 — 同帧内多次调用无副作用（ScrollBox 先测量后渲染两阶段均调用安全）。
    /// </summary>
    public void OnFrame(long nowMs)
    {
        if (_displayedSinceMs < 0)
        {
            _displayedSinceMs = nowMs;
        }

        if (_pendingHint != _displayedHint
            && nowMs - _displayedSinceMs >= ToolFoldingPolicy.MinHintDisplayMs)
        {
            _displayedHint = _pendingHint;
            _displayedSinceMs = nowMs;
        }
    }

    /// <summary>摘要分段 — 读取计数段 + 搜索计数段（零计数控件不出现，基准 L345-366 条件 pushPart）。</summary>
    private IEnumerable<string> BuildParts()
    {
        if (ReadCount > 0)
        {
            yield return $"读取 {ReadCount} 个文件";
        }

        if (SearchCount > 0)
        {
            yield return $"搜索 {SearchCount} 个模式";
        }
    }

    /// <summary>hint 派生 — Read 路径 / Grep/Glob 引号包裹 pattern（基准 L195-198 lastSearch `"p"` 优先级：pattern 在前）。</summary>
    private static string? DeriveHint(string toolName, string args) =>
        toolName is ToolTitleFormatter.ReadTool
            ? ToolTitleFormatter.Format(ToolTitleFormatter.ReadTool, args)
            : QuoteHintPattern(args);

    /// <summary>搜索 hint — 提取 pattern 引号包裹（JSON 与纯文本双形态，提取失败降级原文无引号）。</summary>
    private static string? QuoteHintPattern(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return null;
        }

        var pattern = ToolTitleFormatter.TryGetStringField(args, "pattern");
        return pattern is null ? args : $"\"{pattern}\"";
    }
}
