namespace Services.Notebook.ToolHandlers;

/// <summary>
/// Jupyter Notebook 单元格操作处理器 — 提供添加、删除、编辑、移动、更改类型、
/// 清除输出、获取单元格内容等 MCP 工具能力。
/// 从 NotebookToolHandlers 拆分而来，职责单一：仅操作单元格，不涉及文件级编辑防御链。
/// </summary>
[McpToolDispatch(ToolCategory.Notebook, Optional = true)]
public class NotebookCellOperations {
    private readonly INotebookService _notebookService;
    private readonly IFileOperationService _fileOperationService;

    /// <summary>
    /// 构造 Notebook 单元格操作处理器。
    /// </summary>
    /// <param name="notebookService">Notebook 服务（加载/保存/单元格操作）。</param>
    /// <param name="fileOperationService">文件操作服务（读取/写入/元数据）。</param>
    public NotebookCellOperations(INotebookService notebookService, IFileOperationService fileOperationService) {
        _notebookService = notebookService ?? throw new ArgumentNullException(nameof(notebookService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
    }

    /// <summary>
    /// 添加单元格
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="cell_type">单元格类型（code/markdown/raw）。</param>
    /// <param name="content">单元格内容。</param>
    /// <param name="index">插入位置索引（可选，默认追加到末尾）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookAddCell, "Add a cell to a notebook", "notebook")]
    public async Task<ToolResult> NotebookAddCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell type (code/markdown/raw)")] string cell_type,
        [McpToolParameter("Cell content")] string content,
        [McpToolParameter("Insert position index (optional, default end)", Required = false)] int? index = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var cellType = NotebookCellTypeExtensions.FromValue(cell_type);
        if (cellType is null) {
            var diag = NotebookDiagnostics.BuildInvalidCellTypeDiagnostic(cell_type);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.AddCell(notebook, cellType.Value, content, index);

        if (!result.Success) {
            var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("AddCell", result.ErrorMessage, L.T(StringKey.NotebookAddCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellAddedSuccess, result.AffectedCellIndex, cellType)}")
            .Build();
    }

    /// <summary>
    /// 删除单元格
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="index">单元格索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookDeleteCell, "Delete a cell from a notebook", "notebook")]
    public async Task<ToolResult> NotebookDeleteCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.DeleteCell(notebook, index);

        if (!result.Success) {
            var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("DeleteCell", result.ErrorMessage, L.T(StringKey.NotebookDeleteCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellDeleted, index)}")
            .Build();
    }

    /// <summary>
    /// 编辑单元格内容
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="index">单元格索引。</param>
    /// <param name="content">新内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookEditCell, "Edit a notebook cell's content", "notebook")]
    public async Task<ToolResult> NotebookEditCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        [McpToolParameter("New content")] string content,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.EditCell(notebook, index, content, null);

        if (!result.Success) {
            var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("EditCell", result.ErrorMessage, L.T(StringKey.NotebookEditCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellUpdated, index)}")
            .Build();
    }

    /// <summary>
    /// 移动单元格
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="from_index">源位置索引。</param>
    /// <param name="to_index">目标位置索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookMoveCell, "Move a notebook cell to a new position", "notebook")]
    public async Task<ToolResult> NotebookMoveCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Source position index")] int from_index,
        [McpToolParameter("Target position index")] int to_index,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.MoveCell(notebook, from_index, to_index);

        if (!result.Success) {
            var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("MoveCell", result.ErrorMessage, L.T(StringKey.NotebookMoveCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellMoved, from_index, to_index)}")
            .Build();
    }

    /// <summary>
    /// 更改单元格类型
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="index">单元格索引。</param>
    /// <param name="new_type">新类型（code/markdown/raw）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookChangeCellType, "Change a notebook cell's type", "notebook")]
    public async Task<ToolResult> NotebookChangeCellTypeAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        [McpToolParameter("New type (code/markdown/raw)")] string new_type,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var newType = NotebookCellTypeExtensions.FromValue(new_type);
        if (newType is null) {
            var diag = NotebookDiagnostics.BuildInvalidTypeDiagnostic(new_type);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.ChangeCellType(notebook, index, newType.Value);

        if (!result.Success) {
            var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("ChangeCellType", result.ErrorMessage, L.T(StringKey.NotebookChangeCellTypeFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellTypeChanged, index, newType)}")
            .Build();
    }

    /// <summary>
    /// 清除所有输出
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookClearOutputs, "Clear outputs of all notebook cells", "notebook")]
    public async Task<ToolResult> NotebookClearOutputsAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.ClearAllOutputs(notebook);

        if (!result.Success) {
            var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("ClearAllOutputs", result.ErrorMessage, L.T(StringKey.NotebookClearOutputsFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookOutputsCleared)}")
            .Build();
    }

    /// <summary>
    /// 获取单元格内容
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="index">单元格索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookGetCell, "Get the content of a specific notebook cell", "notebook", ConcurrencySafe = true)]
    public async Task<ToolResult> NotebookGetCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (index < 0 || index >= notebook.Cells.Count) {
            var diag = NotebookDiagnostics.BuildInvalidCellIndexDiagnostic(index);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var cell = notebook.Cells[index];

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.File.ToValue()} {L.T(StringKey.NotebookCellHeader, index)}");
        response.AppendLine(L.T(StringKey.NotebookCellTypeLabel, cell.Type));

        if (cell.ExecutionCount.HasValue) {
            response.AppendLine(L.T(StringKey.NotebookExecutionCountLabel, cell.ExecutionCount));
        }

        response.AppendLine();
        response.AppendLine(L.T(StringKey.NotebookContentLabel));
        response.AppendLine("```");
        response.AppendLine(cell.SourceText);
        response.AppendLine("```");

        if (cell.Outputs != null && cell.Outputs.Count > 0) {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.NotebookOutputLabel));

            response.Append(string.Join(Environment.NewLine,
                cell.Outputs.Where(o => o.Text != null).Select(o => string.Join("", o.Text ?? []))));
            response.AppendLine();
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
