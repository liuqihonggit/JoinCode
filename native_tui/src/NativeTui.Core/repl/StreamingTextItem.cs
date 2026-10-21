namespace NativeTui.Core.Repl;

/// <summary>
/// 流式正文项（M8-1 逐字流式 — TASK064 方案 A）— 回合进行中 assistant 正文逐字上屏：
/// 对齐基准 Messages.tsx 尾部 ⎿ 前缀行渲染 StreamingMarkdown（方案 A 先纯文本逐字追加，
/// OnDone 由 Driver 原位替换为完整 Markdown/Assistant 项 — ⏺ 形态）。
/// ⎿ 前缀整体 dim（对齐 ToolResultItem "  ⎿  " 先例）；续行缩进对齐内容列；
/// ScrollBox 每帧全量重渲（MeasureHeight/Render 每帧调用）— 追加后自然逐帧增长，无需失效通知。
/// 接口按方案 B（块级增量 Markdown）的增量契约设计（Append 语义不变），后续升级替换实现不破消费方。
/// </summary>
public sealed class StreamingTextItem : IScrollItem
{
    private readonly StringBuilder _buffer = new();

    /// <summary>当前累积文本（测试断言面 + Driver 替换/定格语义消费）。</summary>
    public string Text => _buffer.ToString();

    /// <summary>追加一段增量文本（引擎 OnText 粒度 — 空串无操作）。</summary>
    public void Append(string text) {
        if (text.Length > 0) {
            _buffer.Append(text);
        }
    }

    /// <summary>内容高度 = margin 1 + 文本行数（\n 分行；超宽由 Screen 裁剪 — 对齐现有消息项先例）。</summary>
    public int MeasureHeight(int width) => 1 + LineCount();

    /// <summary>渲染：margin 行留空 → 首行 "  ⎿  " dim 前缀 + 默认正文 → 续行空格缩进对齐内容列。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // 续行缩进 = 首行内容列的显示对齐（"  ⎿  " = 5 rune 但 ⎿ 宽字符占 2 列 = 6 显示列，正文视觉对齐取 6）
        const string continuation = "      ";
        var row = frame.Y + 1;
        var first = true;
        foreach (var line in Lines()) {
            var prefix = first ? Figures.ToolResultIndent : continuation;
            screen.WriteText(row, frame.X, prefix, dim, frame.Right);
            if (line.Length > 0) {
                screen.WriteText(row, frame.X + prefix.Length, line, 0, frame.Right);
            }
            first = false;
            row++;
        }
    }

    private int LineCount() => Lines().Length;

    private string[] Lines() => _buffer.Length == 0
        ? [string.Empty]
        : _buffer.ToString().Split('\n');
}
