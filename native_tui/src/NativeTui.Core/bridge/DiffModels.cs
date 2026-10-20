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

/// <summary>diff 单行 — Kind 决定渲染前缀与着色，Content 不含前缀。</summary>
public sealed record DiffLine
{
    /// <summary>行种类（前缀与着色依据）。</summary>
    public required DiffLineKind Kind { get; init; }

    /// <summary>行内容（不含 +/-/空格前缀）。</summary>
    public required string Content { get; init; }
}

/// <summary>
/// 结构化 diff hunk — 引擎 StructuredPatchHunk 的 Core 零依赖渲染投影（ADR 0140：Core 不引引擎库）。
/// 渲染所需字段子集（Header + Lines）；行号/行数计数由引擎侧 summary 文本承载。
/// </summary>
public sealed record DiffHunk
{
    /// <summary>Hunk 头（如 "@@ -1,3 +1,4 @@"）。</summary>
    public required string Header { get; init; }

    /// <summary>Diff 行数组（渲染顺序）。</summary>
    public required DiffLine[] Lines { get; init; }
}
