namespace NativeTui.Core.Repl;

/// <summary>
/// 工具流式输出项（TASK065 P3 — Bash 工具运行中输出实时显示）：
/// 工具执行期间输出尾巴上屏（ToolCallItem 之后落位），对齐基准 Bash 流式 tail 形态 —
/// 最近 <see cref="ToolFoldingPolicy.MaxToolOutputLines"/> 行 dim 输出环形保留（超出行数
/// "… +N lines" 溢出提示行前置 — 上方被截语义，尾部行恒可见），消息为引擎侧每秒 stdout
/// 快照（非增量）→ Update 整体替换尾部行；单行超长截断（对齐 ToolResultSummaryLimit=200 先例）。
/// tool_result 到达时 Driver 原位替换为 ToolResultItem（对齐 StreamingTextItem 替换先例 —
/// 位置不变视觉无跳变）；打断（无 tool_result）时项残留定格为已见输出（EndTurn 清引用不删项）。
/// 首行 "  ⎿  " 前缀 + 续行等宽缩进（对齐 ToolResultItem 输出块形态），整体 dim（流态弱化 —
/// 终态 ToolResultItem 替换后恢复正式形态）。ToolCallItem 专属（MessageItems.cs 并行独占禁改）
/// — 工具标题行由既有 ToolCallItem 渲染，本项只承载输出尾巴，不重复工具行。
/// </summary>
public sealed class ToolStreamingItem : IScrollItem, ITextMatchable
{
    /// <summary>单行输出字符截断上限（对齐 ToolResultSummaryLimit=200 显示截断先例 — 防单行 dump 刷宽）。</summary>
    private const int LineCharLimit = 200;

    /// <summary>溢出提示前缀（… 表示上方还有被截的输出行）。</summary>
    private const string OverflowPrefix = "… +";

    /// <summary>尾部显示行（最近行在前 — 溢出提示行 + 尾行；Update 整体重建）。</summary>
    private string[] _displayLines = [];

    /// <summary>上方被截的输出行数（0 = 无溢出不渲染提示行）。</summary>
    private int _hiddenLines;

    /// <summary>更新输出快照（引擎侧每秒发的当前 stdout 最近行 — 整体替换非增量；空串清空尾巴）。</summary>
    public void Update(string message)
    {
        var lines = SplitLines(message);
        _hiddenLines = Math.Max(0, lines.Length - ToolFoldingPolicy.MaxToolOutputLines);
        var tail = lines.Length > ToolFoldingPolicy.MaxToolOutputLines
            ? lines[^ToolFoldingPolicy.MaxToolOutputLines..]
            : lines;
        var display = new List<string>(tail.Length + 1);
        if (_hiddenLines > 0)
        {
            display.Add($"{OverflowPrefix}{_hiddenLines} lines");
        }

        foreach (var line in tail)
        {
            display.Add(TruncateLine(line));
        }

        _displayLines = [.. display];
    }

    /// <summary>上方被截的输出行数（测试断言面 — 溢出提示数据源）。</summary>
    public int HiddenLines => _hiddenLines;

    /// <summary>当前尾部显示行（测试断言面 — 溢出提示行 + 尾行原序）。</summary>
    public IReadOnlyList<string> DisplayLines => _displayLines;

    /// <summary>内容高度 = margin 1 + 显示行数（空态仅 margin — 无尾巴零占屏）。</summary>
    public int MeasureHeight(int width) => 1 + _displayLines.Length;

    /// <summary>渲染：margin 行留空 → 首行 "  ⎿  " dim 前缀 → 各行从内容列起写
    ///（续行 ToolResultIndent 等宽空格缩进对齐内容列 — 对齐 ToolResultItem 输出续行形态；
    /// 整行 dim — 流态弱化语义）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (_displayLines.Length == 0)
        {
            return;
        }

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var continuation = new string(' ', Figures.ToolResultIndent.Length);
        var row = frame.Y + 1;
        var first = true;
        foreach (var line in _displayLines)
        {
            var prefix = first ? Figures.ToolResultIndent : continuation;
            screen.WriteText(row, frame.X, prefix, dim, frame.Right);
            if (line.Length > 0)
            {
                screen.WriteText(row, frame.X + prefix.Length, line, dim, frame.Right);
            }

            first = false;
            row++;
        }
    }

    /// <summary>transcript 搜索匹配 — 尾部显示行内容。</summary>
    public bool ContainsText(string query) =>
        _displayLines.Any(line => line.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>快照拆行（\n 分行 + \r 清洗；空快照明行保留 — 空行 reset 语义）。</summary>
    private static string[] SplitLines(string message)
    {
        if (message.Length == 0)
        {
            return [];
        }

        return message.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
    }

    /// <summary>单行超长截断（200 上限 + … 尾 — 对齐结果摘要截断先例）。</summary>
    private static string TruncateLine(string line) =>
        line.Length > LineCharLimit
            ? string.Concat(line.AsSpan(0, LineCharLimit - 1), "…")
            : line;
}
