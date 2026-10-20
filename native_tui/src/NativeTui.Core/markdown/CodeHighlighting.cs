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
