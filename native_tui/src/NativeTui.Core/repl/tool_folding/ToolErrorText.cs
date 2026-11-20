namespace NativeTui.Core.Repl;

/// <summary>
/// 工具错误文本归一（TASK069-M5 — 对齐基准 components/FallbackToolUseErrorMessage.tsx L38-56）：
/// trim 后已有 `Error: ` / `Cancelled: ` 前缀则原样保留，否则补 `Error: `；空内容得到裸前缀
/// （基准 trim 空串同样走补前缀分支）。
/// 基准另有三条本仓库无产生点的分支未移植（已 grep 确认 .cs 零产生）：`&lt;tool_use_error&gt;` 标签提取、
/// sandbox 违规标签剔除、非 verbose 下 `InputValidationError: ` → `Invalid tool parameters`。
/// </summary>
public static class ToolErrorText
{
    /// <summary>错误前缀（基准 `error = \`Error: ${trimmed}\``）。</summary>
    public const string ErrorPrefix = "Error: ";

    /// <summary>取消前缀 — 已带此前缀视为已归一，不再叠加（基准 `Cancelled: `）。</summary>
    public const string CancelledPrefix = "Cancelled: ";

    /// <summary>归一为带前缀的错误文本（多行内容前缀只落在首行）。</summary>
    public static string Normalize(string? content)
    {
        var trimmed = (content ?? string.Empty).Trim();
        return trimmed.StartsWith(ErrorPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(CancelledPrefix, StringComparison.Ordinal)
            ? trimmed
            : ErrorPrefix + trimmed;
    }
}
