namespace Services.Notebook.ToolHandlers;

[McpToolDispatch(ToolCategory.Notebook, Optional = true)]
public class NotebookToolHandlers
{
    private readonly INotebookService _notebookService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileStateCache _fileStateCache;
    private readonly IFileSystem _fs;
    private readonly IToolPermissionManager? _permissionManager;

    public NotebookToolHandlers(INotebookService notebookService, IFileOperationService fileOperationService, IFileStateCache fileStateCache, IFileSystem fs, IToolPermissionManager? permissionManager = null)
    {
        _notebookService = notebookService ?? throw new ArgumentNullException(nameof(notebookService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fileStateCache = fileStateCache ?? throw new ArgumentNullException(nameof(fileStateCache));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _permissionManager = permissionManager;
    }

    [McpTool(NotebookToolNameConstants.NotebookEdit, "Replace the contents of a specific cell in a Jupyter notebook (.ipynb)", "notebook")]
    public async Task<ToolResult> NotebookEditAsync(
        [McpToolParameter("The absolute path to the Jupyter notebook file to edit")] string notebook_path,
        [McpToolParameter("The new source for the cell")] string new_source,
        [McpToolParameter("The ID of the cell to edit (optional for insert mode)", Required = false)] string? cell_id = null,
        [McpToolParameter("The type of the cell: code or markdown (required for insert)", Required = false)] string? cell_type = null,
        [McpToolParameter("The type of edit: replace, insert, or delete (default: replace)", Required = false)] string? edit_mode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notebook_path))
        {
            var diag = BuildNotebookPathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 对齐 TS: 相对路径自动转绝对路径
        if (!Path.IsPathRooted(notebook_path))
            notebook_path = Path.GetFullPath(notebook_path);

        // 对齐 TS validateInput: UNC 路径安全检查，防止 NTLM 凭据泄露
        if (notebook_path.StartsWith(@"\\", StringComparison.Ordinal) ||
            (notebook_path.Length >= 2 && notebook_path[0] == '/' && notebook_path[1] == '/'))
        {
            var diag = BuildUncPathNotAllowedDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build();
        }

        if (!notebook_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            var diag = BuildNotIpynbFileDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var modeStr = edit_mode ?? NotebookEditModeConstants.Replace;
        var mode = NotebookEditModeExtensions.FromValue(modeStr) ?? NotebookEditMode.Replace;
        if (!NotebookEditModeExtensions.IsDefined(mode))
        {
            var diag = BuildEditModeInvalidDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (mode == NotebookEditMode.Insert && string.IsNullOrWhiteSpace(cell_type))
        {
            var diag = BuildCellTypeRequiredForInsertDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (mode != NotebookEditMode.Insert && string.IsNullOrWhiteSpace(cell_id))
        {
            var diag = BuildCellIdRequiredDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 对齐 TS checkPermissions: 写入权限检查
        // Plan 模式下写入操作需要确认，Ask 模式下每个操作都需要确认
        if (_permissionManager != null)
        {
            var currentMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            if (currentMode == PermissionMode.Plan)
            {
                var diag = BuildPlanModeForbiddenDiagnostic();
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }
        }

        // Read-before-Edit 校验：必须先读取文件才能编辑，防止模型编辑从未见过的文件
        if (!_fileStateCache.HasBeenRead(notebook_path))
        {
            var diag = BuildFileNotReadDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 并发修改检测：检查文件是否在读取后被外部修改
        var readTimestamp = _fileStateCache.GetReadTimestampMs(notebook_path);
        if (readTimestamp.HasValue && _fs.FileExists(notebook_path))
        {
            var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(notebook_path)).ToUnixTimeMilliseconds();
            if (lastWriteMs > readTimestamp.Value + 1000) // 1s tolerance
            {
                var diag = BuildFileModifiedSinceReadDiagnostic(notebook_path, lastWriteMs, readTimestamp.Value);
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
        }

        var fileResult = await _fileOperationService.ReadFileAsync(notebook_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
            return ToolResultBuilder.Error().WithText($"Notebook file does not exist: {notebook_path}")
                .WithDiagnostic(ToolDiagnostic.Create("FileNotFound", $"Notebook file does not exist: {notebook_path}",
                    [new DiagnosticDetail("filePath", notebook_path)],
                    ["检查路径拼写、大小写，或使用 Read 工具确认文件是否存在。"])).Build();

        var notebook = await _notebookService.LoadAsync(notebook_path, cancellationToken).ConfigureAwait(false);
        if (notebook == null)
        {
            var diag = BuildNotebookInvalidJsonDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        int cellIndex;
        if (string.IsNullOrWhiteSpace(cell_id))
        {
            cellIndex = 0;
        }
        else
        {
            cellIndex = ResolveCellIndex(notebook, cell_id);
            if (cellIndex < 0)
            {
                var cellMsg = BuildCellNotFoundMessage(notebook, cell_id);
                return ToolResultBuilder.Error().WithText(cellMsg)
                    .WithDiagnostic(ToolDiagnostic.Create("CellNotFound", cellMsg,
                        [new DiagnosticDetail("cellId", cell_id), new DiagnosticDetail("cellCount", notebook.Cells.Count.ToString())],
                        ["cell_id 支持三种格式 — 自定义 ID、\"cell-N\" 格式、数字索引。"])).Build();
            }
        }

        if (mode == NotebookEditMode.Insert)
            cellIndex += 1;

        if (mode == NotebookEditMode.Replace && cellIndex == notebook.Cells.Count)
        {
            mode = NotebookEditMode.Insert;
            cell_type ??= NotebookCellTypeConstants.Code;
        }

        if (mode == NotebookEditMode.Delete)
        {
            var deleteResult = _notebookService.DeleteCell(notebook, cellIndex);
            if (!deleteResult.Success)
            {
                var diag = BuildCellOperationFailedDiagnostic("DeleteCell", deleteResult.ErrorMessage, "Failed to delete cell");
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            notebook = deleteResult.GetNotebook();
        }
        else if (mode == NotebookEditMode.Insert)
        {
            var ct = NotebookCellTypeExtensions.FromValue(cell_type) ?? NotebookCellType.Code;
            var addResult = _notebookService.AddCell(notebook, ct, new_source, cellIndex);
            if (!addResult.Success)
            {
                var diag = BuildCellOperationFailedDiagnostic("InsertCell", addResult.ErrorMessage, "Failed to insert cell");
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            notebook = addResult.GetNotebook();
        }
        else
        {
            // 对齐 TS: replace 模式下支持修改 cell_type
            var editResult = _notebookService.EditCell(notebook, cellIndex, new_source, cell_type);
            if (!editResult.Success)
            {
                var diag = BuildCellOperationFailedDiagnostic("EditCell", editResult.ErrorMessage, "Failed to edit cell");
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            notebook = editResult.GetNotebook();
        }

        var saved = await _notebookService.SaveAsync(notebook_path, notebook, cancellationToken).ConfigureAwait(false);
        if (!saved)
        {
            var diag = BuildSaveNotebookFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 写入后更新 FileStateCache，确保后续读取不会返回过时的缓存内容
        if (_fs.FileExists(notebook_path))
        {
            var postWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(notebook_path)).ToUnixTimeMilliseconds();
            _fileStateCache.RecordRead(notebook_path, "", postWriteMs);
        }

        var outputMessage = mode switch
        {
            NotebookEditMode.Replace => $"Updated cell {cell_id ?? cellIndex.ToString()} with {new_source}",
            NotebookEditMode.Insert => $"Inserted cell {cell_id ?? cellIndex.ToString()} with {new_source}",
            NotebookEditMode.Delete => $"Deleted cell {cell_id ?? cellIndex.ToString()}",
            _ => "Unknown edit mode"
        };

        return ToolResultBuilder.Success().WithText(outputMessage).Build();
    }

    private static int ResolveCellIndex(NotebookDocument notebook, string cellId)
    {
        for (int i = 0; i < notebook.Cells.Count; i++)
        {
            if (notebook.Cells[i].Id == cellId)
                return i;
        }

        if (cellId.StartsWith("cell-", StringComparison.OrdinalIgnoreCase) && int.TryParse(cellId.AsSpan(5), out var idx))
        {
            if (idx >= 0 && idx < notebook.Cells.Count)
                return idx;
        }

        if (int.TryParse(cellId, out var numericIdx))
        {
            if (numericIdx >= 0 && numericIdx < notebook.Cells.Count)
                return numericIdx;
        }

        return -1;
    }

    /// <summary>
    /// cell 未找到时的诊断消息 — 列出可用的 cell ID 和合法格式提示。
    /// 仅在失败路径调用，不影响正常操作性能。
    /// </summary>
    internal static string BuildCellNotFoundMessage(NotebookDocument notebook, string cellId)
    {
        var sb = new StringBuilder(256);
        sb.Append($"Cell with ID \"{cellId}\" not found in notebook.");
        sb.Append($"\n[诊断] notebook 共 {notebook.Cells.Count} 个 cell，可用 ID:");

        var maxList = Math.Min(notebook.Cells.Count, 20);
        for (int i = 0; i < maxList; i++)
        {
            var id = notebook.Cells[i].Id ?? $"cell-{i}";
            sb.Append($"\n  - \"{id}\" (index {i})");
        }
        if (notebook.Cells.Count > maxList)
        {
            sb.Append($"\n  ... 还有 {notebook.Cells.Count - maxList} 个 cell");
        }

        sb.Append("\n提示: cell_id 支持三种格式 — 自定义 ID、\"cell-N\" 格式、数字索引。");
        return sb.ToString();
    }

    [McpTool(NotebookToolNameConstants.NotebookCreate, "Create a new Jupyter Notebook file", "notebook")]
    public async Task<ToolResult> NotebookCreateAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Kernel name (e.g. python3)", Required = false)] string? kernel_name = null,
        [McpToolParameter("Programming language (e.g. python)", Required = false)] string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (!file_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            file_path += ".ipynb";
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (fileResult.Success)
        {
            var diag = BuildFileAlreadyExistsDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = _notebookService.Create(kernel_name, language);
        var saved = await _notebookService.SaveAsync(file_path, notebook, cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCreatedSuccess)}");
        response.AppendLine(L.T(StringKey.NotebookPathLabel, file_path));
        response.AppendLine(L.T(StringKey.NotebookFormatVersion, notebook.NbFormat, notebook.NbFormatMinor));

        if (notebook.Metadata.KernelSpec != null)
        {
            response.AppendLine(L.T(StringKey.NotebookKernelLabel, notebook.Metadata.KernelSpec.DisplayName));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 加载并查看Notebook
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookRead, "Read a Jupyter Notebook file", "notebook", ConcurrencySafe = true)]
    public async Task<ToolResult> NotebookReadAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Whether to show cell contents", Required = false, DefaultValue = "false")] bool? show_content = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 对齐 TS: 读取后记录到 FileStateCache，确保后续 Edit 的 Read-before-Edit 检查能通过
        if (_fs.FileExists(file_path))
        {
            var readMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
            _fileStateCache.RecordRead(file_path, fileResult.Content, readMs);
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.NotebookInfoHeader));
        response.AppendLine(L.T(StringKey.NotebookPathLabel, file_path));
        response.AppendLine(L.T(StringKey.NotebookFormatVersion, notebook.NbFormat, notebook.NbFormatMinor));
        response.AppendLine(L.T(StringKey.NotebookTotalCells, notebook.CellCount));
        response.AppendLine(L.T(StringKey.NotebookCodeCells, notebook.CodeCellCount));
        response.AppendLine(L.T(StringKey.NotebookMarkdownCells, notebook.MarkdownCellCount));

        if (notebook.Metadata.KernelSpec != null)
        {
            response.AppendLine(L.T(StringKey.NotebookKernelLabel, $"{notebook.Metadata.KernelSpec.DisplayName} ({notebook.Metadata.KernelSpec.Language})"));
        }

        response.AppendLine();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.NotebookCellListHeader)}");

        var cells = _notebookService.ListCells(notebook);
        response.Append(string.Join(Environment.NewLine,
            cells.Select(c =>
                $"{c.Type switch { NotebookCellType.Code => ObjectSymbol.DiamondFilled.ToValue(), NotebookCellType.Markdown => ObjectSymbol.Pencil.ToValue(), _ => ObjectSymbol.File.ToValue() }} [{c.Index}] {c.Type,-10} {c.Preview}")));
        response.AppendLine();

        if (show_content == true && cells.Count > 0)
        {
            response.AppendLine();
            response.AppendLine($"{ObjectSymbol.File.ToValue()} {L.T(StringKey.NotebookCellContentHeader)}");
            response.AppendLine();

            for (int i = 0; i < notebook.Cells.Count; i++)
            {
                var cell = notebook.Cells[i];
                response.AppendLine(L.T(StringKey.NotebookCellSeparator, i, cell.Type));
                response.AppendLine(cell.SourceText);
                response.AppendLine();
            }
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 添加单元格
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookAddCell, "Add a cell to a notebook", "notebook")]
    public async Task<ToolResult> NotebookAddCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell type (code/markdown/raw)")] string cell_type,
        [McpToolParameter("Cell content")] string content,
        [McpToolParameter("Insert position index (optional, default end)", Required = false)] int? index = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var cellType = NotebookCellTypeExtensions.FromValue(cell_type);
        if (cellType is null)
        {
            var diag = BuildInvalidCellTypeDiagnostic(cell_type);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.AddCell(notebook, cellType.Value, content, index);

        if (!result.Success)
        {
            var diag = BuildCellOperationFailedDiagnostic("AddCell", result.ErrorMessage, L.T(StringKey.NotebookAddCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellAddedSuccess, result.AffectedCellIndex, cellType)}")
            .Build();
    }

    /// <summary>
    /// 删除单元格
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookDeleteCell, "Delete a cell from a notebook", "notebook")]
    public async Task<ToolResult> NotebookDeleteCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.DeleteCell(notebook, index);

        if (!result.Success)
        {
            var diag = BuildCellOperationFailedDiagnostic("DeleteCell", result.ErrorMessage, L.T(StringKey.NotebookDeleteCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellDeleted, index)}")
            .Build();
    }

    /// <summary>
    /// 编辑单元格内容
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookEditCell, "Edit a notebook cell's content", "notebook")]
    public async Task<ToolResult> NotebookEditCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        [McpToolParameter("New content")] string content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.EditCell(notebook, index, content, null);

        if (!result.Success)
        {
            var diag = BuildCellOperationFailedDiagnostic("EditCell", result.ErrorMessage, L.T(StringKey.NotebookEditCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellUpdated, index)}")
            .Build();
    }

    /// <summary>
    /// 移动单元格
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookMoveCell, "Move a notebook cell to a new position", "notebook")]
    public async Task<ToolResult> NotebookMoveCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Source position index")] int from_index,
        [McpToolParameter("Target position index")] int to_index,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.MoveCell(notebook, from_index, to_index);

        if (!result.Success)
        {
            var diag = BuildCellOperationFailedDiagnostic("MoveCell", result.ErrorMessage, L.T(StringKey.NotebookMoveCellFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellMoved, from_index, to_index)}")
            .Build();
    }

    /// <summary>
    /// 更改单元格类型
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookChangeCellType, "Change a notebook cell's type", "notebook")]
    public async Task<ToolResult> NotebookChangeCellTypeAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        [McpToolParameter("New type (code/markdown/raw)")] string new_type,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var newType = NotebookCellTypeExtensions.FromValue(new_type);
        if (newType is null)
        {
            var diag = BuildInvalidTypeDiagnostic(new_type);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.ChangeCellType(notebook, index, newType.Value);

        if (!result.Success)
        {
            var diag = BuildCellOperationFailedDiagnostic("ChangeCellType", result.ErrorMessage, L.T(StringKey.NotebookChangeCellTypeFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCellTypeChanged, index, newType)}")
            .Build();
    }

    /// <summary>
    /// 清除所有输出
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookClearOutputs, "Clear outputs of all notebook cells", "notebook")]
    public async Task<ToolResult> NotebookClearOutputsAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var result = _notebookService.ClearAllOutputs(notebook);

        if (!result.Success)
        {
            var diag = BuildCellOperationFailedDiagnostic("ClearAllOutputs", result.ErrorMessage, L.T(StringKey.NotebookClearOutputsFailed));
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            var diag = BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookOutputsCleared)}")
            .Build();
    }

    /// <summary>
    /// 获取单元格内容
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookGetCell, "Get the content of a specific notebook cell", "notebook", ConcurrencySafe = true)]
    public async Task<ToolResult> NotebookGetCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            var diag = BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            var diag = BuildFileNotExistDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            var diag = BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (index < 0 || index >= notebook.Cells.Count)
        {
            var diag = BuildInvalidCellIndexDiagnostic(index);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var cell = notebook.Cells[index];

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.File.ToValue()} {L.T(StringKey.NotebookCellHeader, index)}");
        response.AppendLine(L.T(StringKey.NotebookCellTypeLabel, cell.Type));

        if (cell.ExecutionCount.HasValue)
        {
            response.AppendLine(L.T(StringKey.NotebookExecutionCountLabel, cell.ExecutionCount));
        }

        response.AppendLine();
        response.AppendLine(L.T(StringKey.NotebookContentLabel));
        response.AppendLine("```");
        response.AppendLine(cell.SourceText);
        response.AppendLine("```");

        if (cell.Outputs != null && cell.Outputs.Count > 0)
        {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.NotebookOutputLabel));

            response.Append(string.Join(Environment.NewLine,
                cell.Outputs.Where(o => o.Text != null).Select(o => string.Join("", o.Text ?? []))));
            response.AppendLine();
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Diagnostics

    /// <summary>
    /// 构建 notebook_path 为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookPathEmptyDiagnostic()
    {
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
    internal static ToolDiagnostic BuildUncPathNotAllowedDiagnostic()
    {
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
    internal static ToolDiagnostic BuildNotIpynbFileDiagnostic()
    {
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
                "如需编辑其他文件类型，使用 FileEdit 工具。",
            ]);
    }

    /// <summary>
    /// 构建 edit_mode 无效的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildEditModeInvalidDiagnostic()
    {
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
    internal static ToolDiagnostic BuildCellTypeRequiredForInsertDiagnostic()
    {
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
    internal static ToolDiagnostic BuildCellIdRequiredDiagnostic()
    {
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
    internal static ToolDiagnostic BuildPlanModeForbiddenDiagnostic()
    {
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
    internal static ToolDiagnostic BuildFileNotReadDiagnostic()
    {
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
    internal static ToolDiagnostic BuildFileModifiedSinceReadDiagnostic(string filePath, long lastWriteMs, long readTimestampMs)
    {
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
    private static string FormatIsoUtc(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// 构建 notebook JSON 解析失败的结构化诊断（NotebookEditAsync 内硬编码英文消息）。
    /// </summary>
    internal static ToolDiagnostic BuildNotebookInvalidJsonDiagnostic()
    {
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
    internal static ToolDiagnostic BuildCellOperationFailedDiagnostic(string operation, string? errorMessage, string fallbackMessage)
    {
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
    internal static ToolDiagnostic BuildSaveNotebookFailedDiagnostic()
    {
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
    internal static ToolDiagnostic BuildFilePathEmptyDiagnostic()
    {
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
    internal static ToolDiagnostic BuildFileNotExistDiagnostic(string filePath)
    {
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
    internal static ToolDiagnostic BuildNotebookParseFailedDiagnostic()
    {
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
    internal static ToolDiagnostic BuildNotebookSaveFailedDiagnostic()
    {
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
    internal static ToolDiagnostic BuildFileAlreadyExistsDiagnostic(string filePath)
    {
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
                "如需编辑已有文件，使用 NotebookEdit 工具。",
            ]);
    }

    /// <summary>
    /// 构建无效 cell 类型的结构化诊断（NotebookAddCellAsync）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidCellTypeDiagnostic(string cellType)
    {
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
    internal static ToolDiagnostic BuildInvalidTypeDiagnostic(string newType)
    {
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
    internal static ToolDiagnostic BuildInvalidCellIndexDiagnostic(int index)
    {
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

    #endregion
}
