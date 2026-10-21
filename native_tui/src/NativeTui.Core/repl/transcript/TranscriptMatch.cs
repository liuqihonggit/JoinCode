namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 搜索匹配区间 — 纯数据（行号 + 列 + 长度），供渲染层高亮消费。
/// 行号指向冻结快照行（进入 transcript 时拍摄，会话内不变 — 匹配与冻结行号绑定）。
/// </summary>
/// <param name="LineIndex">行号（0-based，冻结快照行索引）。</param>
/// <param name="Column">匹配起始列（0-based）。</param>
/// <param name="Length">匹配长度（= 查询长度）。</param>
public sealed record TranscriptMatch(int LineIndex, int Column, int Length);
