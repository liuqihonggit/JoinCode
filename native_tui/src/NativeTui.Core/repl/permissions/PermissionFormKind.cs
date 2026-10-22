namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框形态种类（S8 per-tool 分发）— 对齐基准 PermissionRequest.tsx:47-82 的
/// permissionComponentForTool 十三种分发收敛为三档：Bash（命令全文+危险警告）/
/// File（路径+patch 摘要）/ Fallback（现单一形态 — 工具行+描述行）。
/// 形态数据不可得（命令/路径提取失败）时降级 Fallback，保证既有行为不变。
/// </summary>
public enum PermissionFormKind
{
    /// <summary>兜底形态 — "Tool use" 标题 + 工具行 + 描述行（M7-3 原单一形态，零行为变化）。</summary>
    Fallback,

    /// <summary>Bash 形态 — 命令全文多行等宽展示 + 危险命令警告行（基准 BashPermissionRequest.tsx:34-70）。</summary>
    Bash,

    /// <summary>文件形态 — 文件路径 + patch 摘要行数统计（基准 FilesystemPermissionRequest / FilePermissionDialog）。</summary>
    File,
}
