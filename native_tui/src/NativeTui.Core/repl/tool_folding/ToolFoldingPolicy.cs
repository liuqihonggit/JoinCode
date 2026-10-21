namespace NativeTui.Core.Repl;

/// <summary>
/// 工具折叠策略常量与判定 — 全量对齐基准折叠先例（纯函数集合，TUI 侧零状态）：
/// Bash 结果 3 行折叠（terminal.ts MAX_LINES_TO_SHOW=3 + 差 1 行特例 L44-54）；
/// Write 10 行预览（FileWriteTool/UI.tsx MAX_LINES_TO_RENDER=10）；
/// 聚合提示防闪烁 700ms（CollapsedReadSearchContent.tsx MIN_HINT_DISPLAY_MS=29）；
/// 小终端塌缩（AgentTool/UI.tsx ESTIMATED_LINES_PER_TOOL=9 + TERMINAL_BUFFER_LINES=7，L181-182）。
/// HiddenLines 由引擎侧算好（S1 契约），TUI 只做显示行数换算与提示行判定。
/// </summary>
public static class ToolFoldingPolicy
{
    /// <summary>工具输出折叠行数上限（Bash 结果形态 — terminal.ts MAX_LINES_TO_SHOW）。</summary>
    public const int MaxToolOutputLines = 3;

    /// <summary>Write 结果预览行数上限（FileWriteTool/UI.tsx MAX_LINES_TO_RENDER）。</summary>
    public const int MaxWritePreviewLines = 10;

    /// <summary>聚合 hint 最短展示时长 ms（CollapsedReadSearchContent.tsx MIN_HINT_DISPLAY_MS — 防快完成工具一帧闪过）。</summary>
    public const int MinHintDisplayMs = 700;

    /// <summary>单工具渲染行数估值（小终端塌缩估算 — AgentTool/UI.tsx ESTIMATED_LINES_PER_TOOL）。</summary>
    public const int EstimatedLinesPerTool = 9;

    /// <summary>终端缓冲行数（小终端塌缩估算的固定头部余量 — AgentTool/UI.tsx TERMINAL_BUFFER_LINES）。</summary>
    public const int TerminalBufferLines = 7;

    /// <summary>无输出占位文案（BashToolResultMessage.tsx L156 — stdout/stderr 皆空且非预期完成时）。</summary>
    public const string NoOutputText = "(No output)";

    /// <summary>折叠提示行文案 — "… +N lines (ctrl+o)"（terminal.ts L107 ctrlOToExpand 展开引导）。</summary>
    public static string HiddenLinesHint(int hiddenLines) => $"… +{hiddenLines} lines (ctrl+o)";

    /// <summary>
    /// 折叠态可见输出行数 — HiddenLines 引擎侧算好后的 TUI 换算：
    /// 0 = 全量；1 = 差 1 行特例（直接显示剩余全部，terminal.ts L44-54 不出 "+1 line" 提示）；
    /// ≥2 = 预览上限行。hidden ≥ 总量（引擎计数与 TUI 拆行口径偏差/荒谬值）钳制全显防 0 行黑洞。
    /// </summary>
    public static int VisibleOutputLines(int totalLines, int hiddenLines, int previewLimit)
    {
        if (hiddenLines <= 0 || hiddenLines >= totalLines)
        {
            return totalLines;
        }

        return hiddenLines == 1
            ? Math.Min(totalLines - 1, previewLimit + 1)
            : Math.Min(totalLines - hiddenLines, previewLimit);
    }

    /// <summary>是否渲染折叠提示行 — 差 1 行特例直接显示不出提示（terminal.ts L44-54），≥2 行才提示。</summary>
    public static bool ShowsCollapseHint(int hiddenLines) => hiddenLines > 1;

    /// <summary>小终端塌缩判定 — 渲染行数估值超可用行数时整块进度折叠为单行（AgentTool/UI.tsx L469-470）。
    /// 塌缩单行文案由 MessageRenderer.RenderCondensedToolProgress 唯一持有（渲染拆段 bold 计数）。</summary>
    public static bool ShouldCondense(int rows, int inProgressToolCallCount) =>
        rows < inProgressToolCallCount * EstimatedLinesPerTool + TerminalBufferLines;
}
