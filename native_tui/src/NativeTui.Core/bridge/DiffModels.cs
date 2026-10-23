namespace NativeTui.Core.Bridge;

/// <summary>
/// diff 行种类 — 与引擎 PatchLineType 数值对齐（Context=0/Added=1/Removed=2，投影可直转 byte），
/// 渲染前缀：context=' '、added='+'、removed='-'。
/// </summary>
public enum DiffLineKind : byte
{
    /// <summary>上下文行（前缀 ' '，dim 渲染）。</summary>
    Context = 0,

    /// <summary>新增行（前缀 '+'，DiffAddedWord 前景）。</summary>
    Added = 1,

    /// <summary>删除行（前缀 '-'，DiffRemovedWord 前景）。</summary>
    Removed = 2,
}

/// <summary>
/// diff 单行 — Kind 决定渲染前缀与着色，Content 不含前缀。
/// 行号（TASK066 板块 F 基建 F1）：OldLineNumber/NewLineNumber 为引擎 PatchLine 投影真值
/// （1 基：Removed/Context 带 old、Added/Context 带 new — 渲染单列 gutter 按行种取侧，
/// 对齐基准 color-diff/index.ts ColorDiff.render entries L877-895 行号流语义）；
/// null = 旧引擎 JSON 无字段（WhenWritingNull 不序列化，旧 golden 零破坏），
/// 渲染侧降级从 hunk 头 "@@ -a,b +c,d @@" 解析流推进推导（DiffGutter.AssignLineNumbers）。
/// </summary>
public sealed record DiffLine
{
    /// <summary>行种类（前缀与着色依据）。</summary>
    public required DiffLineKind Kind { get; init; }

    /// <summary>行内容（不含 +/-/空格前缀）。</summary>
    public required string Content { get; init; }

    /// <summary>旧文件行号（1 基 — Removed/Context 行有值；null 不序列化）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OldLineNumber { get; init; }

    /// <summary>新文件行号（1 基 — Added/Context 行有值；null 不序列化）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NewLineNumber { get; init; }
}

/// <summary>
/// 结构化 diff hunk — 引擎 StructuredPatchHunk 的 Core 零依赖渲染投影（ADR 0140：Core 不引引擎库）。
/// 渲染所需字段子集（Header + Lines）；Header 兼作行号降级数据源（行字段缺失时
/// DiffGutter 从 "@@ -a,b +c,d @@" 解析起始行号流推进）。
/// </summary>
public sealed record DiffHunk
{
    /// <summary>Hunk 头（如 "@@ -1,3 +1,4 @@"）。</summary>
    public required string Header { get; init; }

    /// <summary>Diff 行数组（渲染顺序）。</summary>
    public required DiffLine[] Lines { get; init; }
}
