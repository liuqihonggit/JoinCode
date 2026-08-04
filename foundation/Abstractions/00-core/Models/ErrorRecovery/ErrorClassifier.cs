namespace JoinCode.Abstractions.Models.ErrorRecovery;

/// <summary>
/// 错误分类器 — 从错误消息中提取关键词，映射到 ErrorCategory 枚举
/// 统一 DiagnoseErrorAsync 和 FixShellErrorAsync 的分类逻辑
/// </summary>
public static class ErrorClassifier
{
    public static ToolErrorCategory Classify(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return ToolErrorCategory.Unknown;

        if (errorMessage.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("权限", StringComparison.OrdinalIgnoreCase))
            return ToolErrorCategory.Permission;

        if (errorMessage.Contains("command not found", StringComparison.OrdinalIgnoreCase))
            return ToolErrorCategory.CommandNotFound;

        if (errorMessage.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase))
            return ToolErrorCategory.AccessDenied;

        if (errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("找不到", StringComparison.OrdinalIgnoreCase))
            return ToolErrorCategory.NotFound;

        if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("超时", StringComparison.OrdinalIgnoreCase))
            return ToolErrorCategory.Timeout;

        return ToolErrorCategory.Unknown;
    }
}
