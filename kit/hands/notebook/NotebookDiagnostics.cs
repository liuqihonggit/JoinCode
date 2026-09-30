namespace Services.Notebook.ToolHandlers;

/// <summary>
/// Notebook 工具诊断消息构建器 — 集中管理所有结构化诊断（ToolDiagnostic）的创建，
/// 供 NotebookToolHandlers 与 NotebookCellOperations 共享。
/// 对齐 Rust 编译器风格报错：含 reason + formattedMessage + details + suggestions 四要素。
/// </summary>
internal static class NotebookDiagnostics {

    /// <summary>
    /// 构建 notebook_path 为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookPathEmptyDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "NotebookPathEmpty",
            formattedMessage: "notebook_path cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "notebook_path"),
            ],
            suggestions:
            [
                "提供 Jupyter notebook 文件的绝对路径。",
            ]);
    }

    /// <summary>
    /// 构建 UNC 路径不允许的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildUncPathNotAllowedDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "UncPathNotAllowed",
            formattedMessage: "UNC paths are not allowed for security reasons (potential NTLM credential leakage). Use a local path instead.",
            details:
            [
                new DiagnosticDetail("Reason", "NTLM credential leakage risk"),
            ],
            suggestions:
            [
                "使用本地路径（如 C:\\path\\to\\notebook.ipynb）替代 UNC 路径。",
            ]);
    }

    /// <summary>
    /// 构建非 .ipynb 文件的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildNotIpynbFileDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "NotIpynbFile",
            formattedMessage: "File must be a Jupyter notebook (.ipynb file). For editing other file types, use the FileEdit tool.",
            details:
            [
                new DiagnosticDetail("ExpectedExtension", ".ipynb"),
            ],
            suggestions:
            [
                "确认文件扩展名为 .ipynb。",
                $"如需编辑其他文件类型，使用 {FileToolNameEnumConstants.FileEdit} 工具。",
            ]);
    }

    /// <summary>
    /// 构建 edit_mode 无效的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildEditModeInvalidDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "EditModeInvalid",
            formattedMessage: "edit_mode must be replace, insert, or delete",
            details:
            [
                new DiagnosticDetail("Param", "edit_mode"),
                new DiagnosticDetail("ValidValues", "replace, insert, delete"),
            ],
            suggestions:
            [
                "将 edit_mode 设置为 replace、insert 或 delete 之一。",
            ]);
    }

    /// <summary>
    /// 构建 insert 模式缺少 cell_type 的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildCellTypeRequiredForInsertDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "CellTypeRequiredForInsert",
            formattedMessage: "cell_type is required when using edit_mode=insert",
            details:
            [
                new DiagnosticDetail("Param", "cell_type"),
                new DiagnosticDetail("EditMode", "insert"),
            ],
            suggestions:
            [
                "插入新 cell 时必须指定 cell_type（code 或 markdown）。",
            ]);
    }

    /// <summary>
    /// 构建非 insert 模式缺少 cell_id 的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildCellIdRequiredDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "CellIdRequired",
            formattedMessage: "cell_id must be specified when not inserting a new cell",
            details:
            [
                new DiagnosticDetail("Param", "cell_id"),
            ],
            suggestions:
            [
                "replace 或 delete 模式下必须指定要操作的 cell_id。",
            ]);
    }

    /// <summary>
    /// 构建 Plan 模式禁止编辑的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPlanModeForbiddenDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "PlanModeForbidden",
            formattedMessage: "Cannot edit notebook in plan mode. Exit plan mode first before editing files.",
            details:
            [
                new DiagnosticDetail("CurrentMode", "Plan"),
            ],
            suggestions:
            [
                "退出 Plan 模式后再执行编辑操作。",
            ]);
    }

    /// <summary>
    /// 构建文件未读取的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileNotReadDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "FileNotRead",
            formattedMessage: "File has not been read yet. Read it first before writing to it.",
            details:
            [
                new DiagnosticDetail("Requirement", "Read-before-Edit"),
            ],
            suggestions:
            [
                "先使用 NotebookRead 读取文件，再执行编辑操作。",
            ]);
    }

    /// <summary>
    /// 构建文件已被外部修改的结构化诊断。
    /// 对齐 openCode 报错格式：包含具体文件路径与 Last modification/Last read ISO 时间戳，便于排查并发修改。
    /// </summary>
    internal static ToolDiagnostic BuildFileModifiedSinceReadDiagnostic(string filePath, long lastWriteMs, long readTimestampMs) {
        var lastModification = FormatIsoUtc(lastWriteMs);
        var lastRead = FormatIsoUtc(readTimestampMs);
        return ToolDiagnostic.Create(
            reason: "FileModifiedSinceRead",
            formattedMessage: $"File {filePath} has been modified since it was last read.\nLast modification: {lastModification}\nLast read: {lastRead}\nPlease read the file again before modifying it.",
            details:
            [
                new DiagnosticDetail("filePath", filePath),
                new DiagnosticDetail("lastModification", lastModification),
                new DiagnosticDetail("lastRead", lastRead),
                new DiagnosticDetail("Tolerance", "1s"),
            ],
            suggestions:
            [
                "重新读取文件以获取最新内容后再编辑。",
            ]);
    }

    /// <summary>
    /// 将 Unix 毫秒时间戳格式化为 ISO 8601 UTC 字符串（毫秒精度，Z 后缀），例如 2026-08-11T12:03:09.950Z。
    /// </summary>
    internal static string FormatIsoUtc(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// 构建 notebook JSON 解析失败的结构化诊断（NotebookEditAsync 内硬编码英文消息）。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookInvalidJsonDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "NotebookInvalidJson",
            formattedMessage: "Notebook is not valid JSON",
            details:
            [
                new DiagnosticDetail("Expectation", "Valid .ipynb JSON structure"),
            ],
            suggestions:
            [
                "确认文件是有效的 Jupyter notebook JSON 格式。",
                "使用 NotebookCreate 创建新的 notebook。",
            ]);
    }

    /// <summary>
    /// 构建 cell 操作失败的通用结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildCellOperationFailedDiagnostic(string operation, string? errorMessage, string fallbackMessage) {
        var message = errorMessage ?? fallbackMessage;
        return ToolDiagnostic.Create(
            reason: $"{operation}Failed",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("Operation", operation),
                new DiagnosticDetail("ErrorMessage", message),
            ],
            suggestions:
            [
                "检查错误消息以获取详细信息后重试。",
            ]);
    }

    /// <summary>
    /// 构建 notebook 保存失败的结构化诊断（NotebookEditAsync 内硬编码英文消息）。
    /// </summary>
    internal static ToolDiagnostic BuildSaveNotebookFailedDiagnostic() {
        return ToolDiagnostic.Create(
            reason: "SaveNotebookFailed",
            formattedMessage: "Failed to save notebook",
            details:
            [
                new DiagnosticDetail("Operation", "SaveAsync"),
            ],
            suggestions:
            [
                "检查文件路径是否有写入权限。",
                "确认磁盘空间充足。",
            ]);
    }

    /// <summary>
    /// 构建 file_path 为空的结构化诊断（多方法共享）。
    /// </summary>
    internal static ToolDiagnostic BuildFilePathEmptyDiagnostic() {
        var message = L.T(StringKey.NotebookFilePathCannotBeEmpty);
        return ToolDiagnostic.Create(
            reason: "NotebookFilePathEmpty",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("Param", "file_path"),
            ],
            suggestions:
            [
                "提供 notebook 文件路径。",
            ]);
    }

    /// <summary>
    /// 构建文件不存在的结构化诊断（多方法共享）。
    /// </summary>
    internal static ToolDiagnostic BuildFileNotExistDiagnostic(string filePath) {
        var message = L.T(StringKey.NotebookFileNotExist, filePath);
        return ToolDiagnostic.Create(
            reason: "NotebookFileNotExist",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("FilePath", filePath),
            ],
            suggestions:
            [
                "检查路径拼写和大小写。",
                "使用 NotebookCreate 创建新的 notebook 文件。",
            ]);
    }

    /// <summary>
    /// 构建 notebook 解析失败的结构化诊断（多方法共享，本地化消息）。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookParseFailedDiagnostic() {
        var message = L.T(StringKey.NotebookParseFailed);
        return ToolDiagnostic.Create(
            reason: "NotebookParseFailed",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("Expectation", "Valid .ipynb JSON structure"),
            ],
            suggestions:
            [
                "确认文件是有效的 Jupyter notebook JSON 格式。",
            ]);
    }

    /// <summary>
    /// 构建 notebook 保存失败的结构化诊断（多方法共享，本地化消息）。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookSaveFailedDiagnostic() {
        var message = L.T(StringKey.NotebookSaveFailed);
        return ToolDiagnostic.Create(
            reason: "NotebookSaveFailed",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("Operation", "SaveAsync"),
            ],
            suggestions:
            [
                "检查文件路径是否有写入权限。",
                "确认磁盘空间充足。",
            ]);
    }

    /// <summary>
    /// 构建文件已存在的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildFileAlreadyExistsDiagnostic(string filePath) {
        var message = L.T(StringKey.NotebookFileAlreadyExists, filePath);
        return ToolDiagnostic.Create(
            reason: "NotebookFileAlreadyExists",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("FilePath", filePath),
            ],
            suggestions:
            [
                "使用不同的文件名创建新的 notebook。",
                $"如需编辑已有文件，使用 {NotebookToolNameEnumConstants.NotebookEdit} 工具。",
            ]);
    }

    /// <summary>
    /// 构建无效 cell 类型的结构化诊断（NotebookAddCellAsync）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidCellTypeDiagnostic(string cellType) {
        var message = L.T(StringKey.NotebookInvalidCellType, cellType);
        return ToolDiagnostic.Create(
            reason: "NotebookInvalidCellType",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("CellType", cellType),
                new DiagnosticDetail("ValidValues", "code, markdown, raw"),
            ],
            suggestions:
            [
                "将 cell_type 设置为 code、markdown 或 raw 之一。",
            ]);
    }

    /// <summary>
    /// 构建无效类型的结构化诊断（NotebookChangeCellTypeAsync）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidTypeDiagnostic(string newType) {
        var message = L.T(StringKey.NotebookInvalidType, newType);
        return ToolDiagnostic.Create(
            reason: "NotebookInvalidType",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("NewType", newType),
                new DiagnosticDetail("ValidValues", "code, markdown, raw"),
            ],
            suggestions:
            [
                "将 new_type 设置为 code、markdown 或 raw 之一。",
            ]);
    }

    /// <summary>
    /// 构建无效 cell 索引的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidCellIndexDiagnostic(int index) {
        var message = L.T(StringKey.NotebookInvalidCellIndex, index);
        return ToolDiagnostic.Create(
            reason: "NotebookInvalidCellIndex",
            formattedMessage: message,
            details:
            [
                new DiagnosticDetail("Index", index.ToString()),
            ],
            suggestions:
            [
                "使用 NotebookRead 查看有效的 cell 索引范围。",
            ]);
    }

    /// <summary>
    /// cell 未找到时的诊断消息 — 列出可用的 cell ID 和合法格式提示。
    /// 仅在失败路径调用，不影响正常操作性能。
    /// </summary>
    internal static string BuildCellNotFoundMessage(NotebookDocument notebook, string cellId) {
        var sb = new StringBuilder(256);
        sb.Append($"Cell with ID \"{cellId}\" not found in notebook.");
        sb.Append($"\n[诊断] notebook 共 {notebook.Cells.Count} 个 cell，可用 ID:");

        var maxList = Math.Min(notebook.Cells.Count, 20);
        for (var i = 0; i < maxList; i++) {
            var id = notebook.Cells[i].Id ?? $"cell-{i}";
            sb.Append($"\n  - \"{id}\" (index {i})");
        }
        if (notebook.Cells.Count > maxList) {
            sb.Append($"\n  ... 还有 {notebook.Cells.Count - maxList} 个 cell");
        }

        sb.Append("\n提示: cell_id 支持三种格式 — 自定义 ID、\"cell-N\" 格式、数字索引。");
        return sb.ToString();
    }
}
