namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框形态数据（S8）— per-tool 分发的不可变渲染契约：
/// PermissionRequestItem.Render/MeasureHeight 按 Kind 分支消费（标题/命令区/警告行/路径/patch 统计），
/// ToolPermissionForms.Derive 是唯一构造入口（Fallback 常量形态保证既有渲染零变化）。
/// </summary>
public sealed record ToolPermissionForm
{
    /// <summary>Fallback 常量形态 — 标题 "Tool use"、无命令区/警告/路径（M7-3 原单一形态）。</summary>
    public static readonly ToolPermissionForm Fallback = new()
    {
        Kind = PermissionFormKind.Fallback,
        Title = "Tool use",
        CommandLines = [],
    };

    /// <summary>形态种类（渲染分支依据）。</summary>
    public required PermissionFormKind Kind { get; init; }

    /// <summary>对话框标题（Fallback="Tool use" / Bash="Bash command" / File="Read|Write|Edit file"）。</summary>
    public required string Title { get; init; }

    /// <summary>命令全文折行行集（Bash 形态 — 等宽 dim 渲染；空数组 = 无命令区）。</summary>
    public required string[] CommandLines { get; init; }

    /// <summary>危险命令警告文本（Bash 形态 — warning 色渲染；null = 无危险命中）。</summary>
    public string? DangerWarning { get; init; }

    /// <summary>目标文件路径（File 形态 — 路径行渲染；null = 无路径）。</summary>
    public string? FilePath { get; init; }

    /// <summary>patch 新增行数统计（File 形态 — +N 渲染；0 = 无统计）。</summary>
    public int PatchAdded { get; init; }

    /// <summary>patch 删除行数统计（File 形态 — -M 渲染；0 = 无统计）。</summary>
    public int PatchRemoved { get; init; }

    /// <summary>是否携带 patch 摘要（+N 或 -M 任一非零 — 渲染 patch 行依据）。</summary>
    public bool HasPatchSummary => PatchAdded > 0 || PatchRemoved > 0;
}
