namespace Tools.Handlers;

/// <summary>
/// FileToolHandlers 错误诊断方法 — partial class，分离诊断构建逻辑以控制文件长度。
/// </summary>
public partial class FileToolHandlers
{
    /// <summary>
    /// 构建参数校验失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildValidationErrorDiagnostic(string validationError)
    {
        return ToolDiagnostic.Create(
            reason: "FileValidationError",
            formattedMessage: validationError,
            details: [new DiagnosticDetail("Error", validationError)],
            suggestions: ["修正参数使其满足校验要求。"]);
    }

    /// <summary>
    /// 构建 FileWrite UNC 路径拒绝的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildUncPathWriteRejectedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "UncPathWriteRejected",
            formattedMessage: "Cannot write UNC path files (starting with \\\\), this may lead to credential leakage",
            details: [new DiagnosticDetail("reason", "UncPath")],
            suggestions: ["使用本地文件路径替代 UNC 路径。"]);
    }

    /// <summary>
    /// 构建 FileEdit UNC 路径拒绝的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildUncPathEditRejectedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "UncPathEditRejected",
            formattedMessage: "Cannot edit UNC path files (starting with \\\\), this may lead to credential leakage",
            details: [new DiagnosticDetail("reason", "UncPath")],
            suggestions: ["使用本地文件路径替代 UNC 路径。"]);
    }

    /// <summary>
    /// 构建团队记忆密钥写入拒绝的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildTeamMemSecretRejectedDiagnostic(string secretError)
    {
        return ToolDiagnostic.Create(
            reason: "TeamMemSecretRejected",
            formattedMessage: secretError,
            details: [new DiagnosticDetail("Error", secretError)],
            suggestions: ["移除密钥内容或使用允许的写入方式。"]);
    }

    /// <summary>
    /// 构建 FileWrite 写前读校验失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileNotReadBeforeWriteDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "FileNotReadBeforeWrite",
            formattedMessage: "File has not been read yet. Read it first before writing to it. Use the Read tool to examine the file, then write your changes.",
            details: [new DiagnosticDetail("operation", "write")],
            suggestions: ["先使用 Read 工具读取文件内容，再执行写入。"]);
    }

    /// <summary>
    /// 构建 FileEdit 写前读校验失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileNotReadBeforeEditDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "FileNotReadBeforeEdit",
            formattedMessage: "File has not been read yet. Read it first before editing it. Use the Read tool to examine the file, then make your edits.",
            details: [new DiagnosticDetail("operation", "edit")],
            suggestions: ["先使用 Read 工具读取文件内容，再执行编辑。"]);
    }

    /// <summary>
    /// 构建文件自上次读取后已被修改的脏写保护诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileModifiedSinceReadDiagnostic(string operation)
    {
        return ToolDiagnostic.Create(
            reason: "FileModifiedSinceRead",
            formattedMessage: $"File has been modified since it was last read. The file may have been changed by another process. Read it again before {operation} to ensure you have the latest content.",
            details: [new DiagnosticDetail("operation", operation)],
            suggestions: ["重新读取文件以获取最新内容后再操作。"]);
    }

    /// <summary>
    /// 构建 Notebook 文件编辑拒绝的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookEditRejectedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "NotebookEditRejected",
            formattedMessage: "This is a Jupyter Notebook file. Use the notebook_edit tool to edit this file.",
            details: [new DiagnosticDetail("reason", "NotebookFile")],
            suggestions: ["使用 notebook_edit 工具编辑 .ipynb 文件。"]);
    }

    /// <summary>
    /// 构建 old_string 与 new_string 相同的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildIdenticalStringsDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "IdenticalStrings",
            formattedMessage: "old_string and new_string are identical, no changes needed",
            details: [new DiagnosticDetail("reason", "Identical")],
            suggestions: ["提供与 old_string 不同的 new_string。"]);
    }

    /// <summary>
    /// 构建 settings 文件编辑校验失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildSettingsEditRejectedDiagnostic(string settingsError)
    {
        return ToolDiagnostic.Create(
            reason: "SettingsEditRejected",
            formattedMessage: settingsError,
            details: [new DiagnosticDetail("Error", settingsError)],
            suggestions: ["修正编辑内容使其保持 settings 文件合法。"]);
    }

    /// <summary>
    /// 构建 keyword-sections.json 编辑权限拒绝的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildKeywordSectionsEditRejectedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "KeywordSectionsEditRejected",
            formattedMessage: "keyword-sections.json 只能由 keywordMaintenance Agent 编辑",
            details: [new DiagnosticDetail("reason", "PermissionDenied")],
            suggestions: ["使用 keywordMaintenance Agent 编辑此文件。"]);
    }

    /// <summary>
    /// 构建 doctor Agent 编辑路径拒绝的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildDoctorAgentEditRejectedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "DoctorAgentEditRejected",
            formattedMessage: "doctor Agent 只能编辑 .jcc/diag/、.jcc/reflexion/ 和 worktree 内文件",
            details: [new DiagnosticDetail("reason", "PermissionDenied")],
            suggestions: ["仅编辑 doctor Agent 允许的路径范围。"]);
    }

    /// <summary>
    /// 构建列目录失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildListDirectoryFailedDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create(
            reason: "ListDirectoryFailed",
            formattedMessage: errorMessage,
            details: [new DiagnosticDetail("Error", errorMessage)],
            suggestions: ["检查目录是否存在且有访问权限。"]);
    }

    /// <summary>
    /// 构建 FileEdit 服务未初始化的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileEditServiceNotInitializedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "FileEditServiceNotInitialized",
            formattedMessage: "File edit service is not initialized",
            details: [new DiagnosticDetail("reason", "NotInitialized")],
            suggestions: ["确认 FileEditLogic 已通过构造函数注入。"]);
    }

    /// <summary>
    /// 构建 FileSnip 服务未初始化的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileChunkingServiceNotInitializedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "FileChunkingServiceNotInitialized",
            formattedMessage: "File chunking service is not initialized",
            details: [new DiagnosticDetail("reason", "NotInitialized")],
            suggestions: ["确认 SnipLogic 已通过构造函数注入。"]);
    }

    /// <summary>
    /// 构建批量编辑文件路径为空的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFilePathRequiredDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "FilePathRequired",
            formattedMessage: "At least one file path is required",
            details: [new DiagnosticDetail("reason", "EmptyPaths")],
            suggestions: ["提供至少一个文件路径。"]);
    }

    /// <summary>
    /// 构建图像 base64 大小超过 API 限制的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildImageBase64TooLargeDiagnostic(int base64Length, int limit)
    {
        return ToolDiagnostic.Create(
            reason: "ImageBase64TooLarge",
            formattedMessage: $"Image base64 size ({base64Length} bytes) exceeds API limit ({limit} bytes). Please use a smaller image.",
            details: [new DiagnosticDetail("base64Length", base64Length.ToString()), new DiagnosticDetail("limit", limit.ToString())],
            suggestions: ["使用更小的图像文件。"]);
    }

    /// <summary>
    /// 构建图像 Token 超过最大允许值的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildImageTokenExceededDiagnostic(int estimatedTokens, int bufferSize, int maxTokens)
    {
        return ToolDiagnostic.Create(
            reason: "ImageTokenExceeded",
            formattedMessage: $"Image content ({estimatedTokens} tokens, {bufferSize} bytes) exceeds maximum allowed tokens ({maxTokens}). Try reading a smaller image or use offset/limit on text files instead.",
            details: [new DiagnosticDetail("estimatedTokens", estimatedTokens.ToString()), new DiagnosticDetail("bufferSize", bufferSize.ToString()), new DiagnosticDetail("maxTokens", maxTokens.ToString())],
            suggestions: ["使用更小的图像或对文本文件使用 offset/limit 参数。"]);
    }

    /// <summary>
    /// 构建 PDF pages 参数格式无效的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPdfInvalidPagesDiagnostic(string pages)
    {
        return ToolDiagnostic.Create(
            reason: "PdfInvalidPages",
            formattedMessage: $"Invalid pages parameter: \"{pages}\". Use formats like \"1-5\", \"3\", or \"10-20\". Pages are 1-indexed.",
            details: [new DiagnosticDetail("pages", pages)],
            suggestions: ["使用 \"1-5\"、\"3\"、\"10-20\" 等格式指定页范围。"]);
    }

    /// <summary>
    /// 构建 PDF 页范围超过最大页数的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPdfPageRangeExceedsMaxDiagnostic(string pages, int maxPages)
    {
        return ToolDiagnostic.Create(
            reason: "PdfPageRangeExceedsMax",
            formattedMessage: $"Page range \"{pages}\" exceeds maximum of {maxPages} pages per request. Please use a smaller range.",
            details: [new DiagnosticDetail("pages", pages), new DiagnosticDetail("maxPages", maxPages.ToString())],
            suggestions: [$"使用更小的页范围，每次最多 {maxPages} 页。"]);
    }

    /// <summary>
    /// 构建 PDF fallback 读取失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPdfFallbackReadFailedDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create(
            reason: "PdfFallbackReadFailed",
            formattedMessage: errorMessage,
            details: [new DiagnosticDetail("Error", errorMessage)],
            suggestions: ["检查文件是否为有效的 PDF。"]);
    }

    /// <summary>
    /// 构建 PDF 页面提取失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPdfExtractFailedDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create(
            reason: "PdfExtractFailed",
            formattedMessage: errorMessage,
            details: [new DiagnosticDetail("Error", errorMessage)],
            suggestions: ["检查 PDF 页码范围是否有效，或 PDFium 是否可用。"]);
    }

    /// <summary>
    /// 构建 ApplyPatchLogic 未初始化的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildApplyPatchNotAvailableDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "ApplyPatchNotAvailable",
            formattedMessage: "ApplyPatchLogic is not available",
            details: [new DiagnosticDetail("reason", "NotInitialized")],
            suggestions: ["确认 ApplyPatchLogic 已通过构造函数注入。"]);
    }

    /// <summary>
    /// 构建 ApplyPatch 失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildApplyPatchFailedDiagnostic(string errorText)
    {
        return ToolDiagnostic.Create(
            reason: "ApplyPatchFailed",
            formattedMessage: errorText,
            details: [new DiagnosticDetail("Error", errorText)],
            suggestions: ["检查 patch 格式是否正确，目标文件是否存在。"]);
    }
}
