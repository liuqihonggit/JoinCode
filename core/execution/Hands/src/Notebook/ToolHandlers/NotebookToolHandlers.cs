namespace Services.Notebook.ToolHandlers;

[McpToolHandler(ToolCategory.Notebook, Optional = true)]
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
            return McpResultBuilder.Error().WithText("notebook_path cannot be empty").Build();

        // 对齐 TS: 相对路径自动转绝对路径
        if (!Path.IsPathRooted(notebook_path))
            notebook_path = Path.GetFullPath(notebook_path);

        // 对齐 TS validateInput: UNC 路径安全检查，防止 NTLM 凭据泄露
        if (notebook_path.StartsWith(@"\\", StringComparison.Ordinal) ||
            (notebook_path.Length >= 2 && notebook_path[0] == '/' && notebook_path[1] == '/'))
        {
            return McpResultBuilder.Error()
                .WithText("UNC paths are not allowed for security reasons (potential NTLM credential leakage). Use a local path instead.")
                .Build();
        }

        if (!notebook_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
            return McpResultBuilder.Error().WithText("File must be a Jupyter notebook (.ipynb file). For editing other file types, use the FileEdit tool.").Build();

        var modeStr = edit_mode ?? NotebookEditModeConstants.Replace;
        var mode = NotebookEditModeExtensions.FromValue(modeStr) ?? NotebookEditMode.Replace;
        if (!NotebookEditModeExtensions.IsDefined(mode))
            return McpResultBuilder.Error().WithText("edit_mode must be replace, insert, or delete").Build();

        if (mode == NotebookEditMode.Insert && string.IsNullOrWhiteSpace(cell_type))
            return McpResultBuilder.Error().WithText("cell_type is required when using edit_mode=insert").Build();

        if (mode != NotebookEditMode.Insert && string.IsNullOrWhiteSpace(cell_id))
            return McpResultBuilder.Error().WithText("cell_id must be specified when not inserting a new cell").Build();

        // 对齐 TS checkPermissions: 写入权限检查
        // Plan 模式下写入操作需要确认，Ask 模式下每个操作都需要确认
        if (_permissionManager != null)
        {
            var currentMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            if (currentMode == PermissionMode.Plan)
            {
                return McpResultBuilder.Error()
                    .WithText("Cannot edit notebook in plan mode. Exit plan mode first before editing files.")
                    .Build();
            }
        }

        // Read-before-Edit 校验：必须先读取文件才能编辑，防止模型编辑从未见过的文件
        if (!_fileStateCache.HasBeenRead(notebook_path))
            return McpResultBuilder.Error().WithText("File has not been read yet. Read it first before writing to it.").Build();

        // 并发修改检测：检查文件是否在读取后被外部修改
        var readTimestamp = _fileStateCache.GetReadTimestampMs(notebook_path);
        if (readTimestamp.HasValue && _fs.FileExists(notebook_path))
        {
            var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(notebook_path)).ToUnixTimeMilliseconds();
            if (lastWriteMs > readTimestamp.Value + 1000) // 1s tolerance
                return McpResultBuilder.Error().WithText("File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.").Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(notebook_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
            return McpResultBuilder.Error().WithText("Notebook file does not exist").Build();

        var notebook = await _notebookService.LoadAsync(notebook_path, cancellationToken).ConfigureAwait(false);
        if (notebook == null)
            return McpResultBuilder.Error().WithText("Notebook is not valid JSON").Build();

        int cellIndex;
        if (string.IsNullOrWhiteSpace(cell_id))
        {
            cellIndex = 0;
        }
        else
        {
            cellIndex = ResolveCellIndex(notebook, cell_id);
            if (cellIndex < 0)
                return McpResultBuilder.Error().WithText($"Cell with ID \"{cell_id}\" not found in notebook").Build();
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
                return McpResultBuilder.Error().WithText(deleteResult.ErrorMessage ?? "Failed to delete cell").Build();
            notebook = deleteResult.GetNotebook();
        }
        else if (mode == NotebookEditMode.Insert)
        {
            var ct = NotebookCellTypeExtensions.FromValue(cell_type) ?? NotebookCellType.Code;
            var addResult = _notebookService.AddCell(notebook, ct, new_source, cellIndex);
            if (!addResult.Success)
                return McpResultBuilder.Error().WithText(addResult.ErrorMessage ?? "Failed to insert cell").Build();
            notebook = addResult.GetNotebook();
        }
        else
        {
            // 对齐 TS: replace 模式下支持修改 cell_type
            var editResult = _notebookService.EditCell(notebook, cellIndex, new_source, cell_type);
            if (!editResult.Success)
                return McpResultBuilder.Error().WithText(editResult.ErrorMessage ?? "Failed to edit cell").Build();
            notebook = editResult.GetNotebook();
        }

        var saved = await _notebookService.SaveAsync(notebook_path, notebook, cancellationToken).ConfigureAwait(false);
        if (!saved)
            return McpResultBuilder.Error().WithText("Failed to save notebook").Build();

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

        return McpResultBuilder.Success().WithText(outputMessage).Build();
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

    [McpTool(NotebookToolNameConstants.NotebookCreate, "Create a new Jupyter Notebook file", "notebook")]
    public async Task<ToolResult> NotebookCreateAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Kernel name (e.g. python3)", Required = false)] string? kernel_name = null,
        [McpToolParameter("Programming language (e.g. python)", Required = false)] string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        if (!file_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            file_path += ".ipynb";
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileAlreadyExists, file_path)).Build();
        }

        var notebook = _notebookService.Create(kernel_name, language);
        var saved = await _notebookService.SaveAsync(file_path, notebook, cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCreatedSuccess)}");
        response.AppendLine(L.T(StringKey.NotebookPathLabel, file_path));
        response.AppendLine(L.T(StringKey.NotebookFormatVersion, notebook.NbFormat, notebook.NbFormatMinor));

        if (notebook.Metadata.KernelSpec != null)
        {
            response.AppendLine(L.T(StringKey.NotebookKernelLabel, notebook.Metadata.KernelSpec.DisplayName));
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 加载并查看Notebook
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookRead, "Read a Jupyter Notebook file", "notebook")]
    public async Task<ToolResult> NotebookReadAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Whether to show cell contents", Required = false, DefaultValue = "false")] bool? show_content = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
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

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var cellType = NotebookCellTypeExtensions.FromValue(cell_type);
        if (cellType is null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookInvalidCellType, cell_type)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        var result = _notebookService.AddCell(notebook, cellType.Value, content, index);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.NotebookAddCellFailed)).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        return McpResultBuilder.Success()
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        var result = _notebookService.DeleteCell(notebook, index);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.NotebookDeleteCellFailed)).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        return McpResultBuilder.Success()
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        var result = _notebookService.EditCell(notebook, index, content, null);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.NotebookEditCellFailed)).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        return McpResultBuilder.Success()
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        var result = _notebookService.MoveCell(notebook, from_index, to_index);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.NotebookMoveCellFailed)).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        return McpResultBuilder.Success()
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var newType = NotebookCellTypeExtensions.FromValue(new_type);
        if (newType is null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookInvalidType, new_type)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        var result = _notebookService.ChangeCellType(notebook, index, newType.Value);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.NotebookChangeCellTypeFailed)).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        return McpResultBuilder.Success()
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
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        var result = _notebookService.ClearAllOutputs(notebook);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.NotebookClearOutputsFailed)).Build();
        }

        var saved = await _notebookService.SaveAsync(file_path, result.GetNotebook(), cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookSaveFailed)).Build();
        }

        return McpResultBuilder.Success()
            .WithText($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookOutputsCleared)}")
            .Build();
    }

    /// <summary>
    /// 获取单元格内容
    /// </summary>
    [McpTool(NotebookToolNameConstants.NotebookGetCell, "Get the content of a specific notebook cell", "notebook")]
    public async Task<ToolResult> NotebookGetCellAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Cell index")] int index,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFilePathCannotBeEmpty)).Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookFileNotExist, file_path)).Build();
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookParseFailed)).Build();
        }

        if (index < 0 || index >= notebook.Cells.Count)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.NotebookInvalidCellIndex, index)).Build();
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

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
