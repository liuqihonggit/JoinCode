namespace NativeTui.Core.Markdown;

/// <summary>代码高亮 token 色段 — Start/Length 为行内字符区间，R/G/B 为真彩前景。</summary>
public readonly record struct HighlightSpan(int Start, int Length, int R, int G, int B);

/// <summary>
/// 代码高亮器插件接口（M7-5）— 实现方隔离在卫星项目（ADR 0140：TextMateSharp/Onigwrap 原生依赖不进 Core），
/// 宿主组装注入（null = 高亮禁用降级，对齐基准 settings.syntaxHighlightingDisabled 语义）。
/// 整块一次性高亮（内部会话承载跨行语法状态 — 块注释/模板字符串等）；
/// null 返回 = 语言不支持，调用方降级纯文本（对齐基准五层降级链的"语言不支持"层）。
/// </summary>
public interface ICodeHighlighter
{
    /// <summary>按语言整块高亮 — 返回每行的 token 色段列表（索引对齐代码行；空行 = 空列表）；null = 语言不支持。</summary>
    IReadOnlyList<IReadOnlyList<HighlightSpan>>? Highlight(string language, IReadOnlyList<string> lines);
}

/// <summary>单行代码高亮结果 — token 色段（Start 已含行首前缀宽度偏移，坐标系 = 布局行完整段文本）。</summary>
public sealed record CodeLineHighlight(IReadOnlyList<HighlightSpan> Spans);

/// <summary>
/// 链接区间（DSG043 P2）— 行内 OSC 8 注入依据：StartCol/EndCol 为布局行物理显示列区间
///（含前缀偏移，半开区间 [StartCol, EndCol)），Url 为链接目标（P3 终流注入消费）。
/// 折行拆分后每行区间独立（链接 token 跨行时各行的区间只覆盖本行物理列）。
/// </summary>
public readonly record struct LinkSpan(int StartCol, int EndCol, string Url);

/// <summary>布局产物 — 物理行模型 + 逐行高亮上下文（与 Lines 等长；null = 非代码行或无色段）
/// + 逐行链接区间表（与 Lines 等长；空表 = 该行无链接 — P3 渲染注入零判断成本）。</summary>
public sealed record MarkdownLayoutResult(
    IReadOnlyList<IReadOnlyList<MarkdownSegment>> Lines,
    IReadOnlyList<CodeLineHighlight?> Highlights,
    IReadOnlyList<IReadOnlyList<LinkSpan>> Links);
