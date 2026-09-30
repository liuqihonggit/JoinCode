namespace Services.Notebook.ToolHandlers;

/// <summary>
/// Jupyter Notebook (.ipynb) 文件级工具处理器 — 提供编辑（NotebookEdit）、创建（NotebookCreate）、
/// 读取（NotebookRead）三个 MCP 工具能力，并集成权限校验、Read-before-Edit 校验、
/// 并发修改检测、团队密钥检测与写前备份等防御链。
/// 单元格级操作（Add/Delete/Move/ChangeType/ClearOutputs/GetCell）见 <see cref="NotebookCellOperations"/>。
/// 诊断消息构建见 <see cref="NotebookDiagnostics"/>。
/// </summary>
[McpToolDispatch(ToolCategory.Notebook, Optional = true)]
public class NotebookToolHandlers {
    private readonly INotebookService _notebookService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileStateCache _fileStateCache;
    private readonly IFileSystem _fs;
    private readonly IToolPermissionManager? _permissionManager;
    private readonly WriteDefenseService? _writeDefense;

    /// <summary>
    /// 构造 Notebook 工具处理器。
    /// </summary>
    /// <param name="notebookService">Notebook 服务（加载/保存/单元格操作）。</param>
    /// <param name="fileOperationService">文件操作服务（读取/写入/元数据）。</param>
    /// <param name="fileStateCache">文件状态缓存（Read-before-Edit 校验）。</param>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="permissionManager">权限管理器（可选，Plan 模式拦截）。</param>
    /// <param name="writeDefense">写入防御服务（可选，团队密钥检测与写前备份）。</param>
    public NotebookToolHandlers(INotebookService notebookService, IFileOperationService fileOperationService, IFileStateCache fileStateCache, IFileSystem fs, IToolPermissionManager? permissionManager = null, WriteDefenseService? writeDefense = null) {
        _notebookService = notebookService ?? throw new ArgumentNullException(nameof(notebookService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fileStateCache = fileStateCache ?? throw new ArgumentNullException(nameof(fileStateCache));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _permissionManager = permissionManager;
        _writeDefense = writeDefense;
    }

    /// <summary>
    /// 替换、插入或删除 Jupyter notebook 中指定单元格的内容。
    /// </summary>
    /// <param name="notebook_path">Jupyter notebook 文件的绝对路径。</param>
    /// <param name="new_source">单元格的新源内容。</param>
    /// <param name="cell_id">目标单元格 ID（insert 模式可选，其余模式必填）。</param>
    /// <param name="cell_type">单元格类型 code 或 markdown（insert 模式必填）。</param>
    /// <param name="edit_mode">编辑模式：replace、insert 或 delete（默认 replace）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookEdit, "Replace the contents of a specific cell in a Jupyter notebook (.ipynb)", "notebook")]
    public async Task<ToolResult> NotebookEditAsync(
        [McpToolParameter("The absolute path to the Jupyter notebook file to edit")] string notebook_path,
        [McpToolParameter("The new source for the cell")] string new_source,
        [McpToolParameter("The ID of the cell to edit (optional for insert mode)", Required = false)] string? cell_id = null,
        [McpToolParameter("The type of the cell: code or markdown (required for insert)", Required = false)] string? cell_type = null,
        [McpToolParameter("The type of edit: replace, insert, or delete (default: replace)", Required = false)] string? edit_mode = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(notebook_path)) {
            var diag = NotebookDiagnostics.BuildNotebookPathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 对齐 TS: 相对路径自动转绝对路径
        if (!Path.IsPathRooted(notebook_path))
            notebook_path = Path.GetFullPath(notebook_path);

        // 对齐 TS validateInput: UNC 路径安全检查，防止 NTLM 凭据泄露
        if (notebook_path.StartsWith(@"\\", StringComparison.Ordinal) ||
            (notebook_path.Length >= 2 && notebook_path[0] == '/' && notebook_path[1] == '/')) {
            var diag = NotebookDiagnostics.BuildUncPathNotAllowedDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(diag.FormattedMessage)
                .WithDiagnostic(diag)
                .Build();
        }

        if (!notebook_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase)) {
            var diag = NotebookDiagnostics.BuildNotIpynbFileDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var modeStr = edit_mode ?? NotebookEditModeEnumConstants.Replace;
        var mode = NotebookEditModeExtensions.FromValue(modeStr) ?? NotebookEditMode.Replace;
        if (!NotebookEditModeExtensions.IsDefined(mode)) {
            var diag = NotebookDiagnostics.BuildEditModeInvalidDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (mode == NotebookEditMode.Insert && string.IsNullOrWhiteSpace(cell_type)) {
            var diag = NotebookDiagnostics.BuildCellTypeRequiredForInsertDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (mode != NotebookEditMode.Insert && string.IsNullOrWhiteSpace(cell_id)) {
            var diag = NotebookDiagnostics.BuildCellIdRequiredDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 对齐 TS checkPermissions: 写入权限检查
        // Plan 模式下写入操作需要确认，Ask 模式下每个操作都需要确认
        if (_permissionManager != null) {
            var currentMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            if (currentMode == PermissionMode.Plan) {
                var diag = NotebookDiagnostics.BuildPlanModeForbiddenDiagnostic();
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }
        }

        // Read-before-Edit 校验：跨进程时 FileStateCache 不共享，自动读取文件并记录
        if (!_fileStateCache.HasBeenRead(notebook_path) && _fs.FileExists(notebook_path)) {
            var autoReadResult = await _fileOperationService.ReadFileAsync(notebook_path, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (autoReadResult.Success) {
                var autoReadMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(notebook_path)).ToUnixTimeMilliseconds();
                _fileStateCache.RecordRead(notebook_path, autoReadResult.Content, autoReadMs);
            }
        }

        // 并发修改检测：检查文件是否在读取后被外部修改
        var readTimestamp = _fileStateCache.GetReadTimestampMs(notebook_path);
        if (readTimestamp.HasValue && _fs.FileExists(notebook_path)) {
            var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(notebook_path)).ToUnixTimeMilliseconds();
            if (lastWriteMs > readTimestamp.Value + 1000) // 1s tolerance
            {
                var diag = NotebookDiagnostics.BuildFileModifiedSinceReadDiagnostic(notebook_path, lastWriteMs, readTimestamp.Value);
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
        }

        var fileResult = await _fileOperationService.ReadFileAsync(notebook_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
            return ToolResultBuilder.Error().WithText($"Notebook file does not exist: {notebook_path}")
                .WithDiagnostic(ToolDiagnostic.Create("FileNotFound", $"Notebook file does not exist: {notebook_path}",
                    [new DiagnosticDetail("filePath", notebook_path)],
                    [$"检查路径拼写、大小写，或使用 {FileToolNameEnumConstants.FileRead} 工具确认文件是否存在。"])).Build();

        var notebook = await _notebookService.LoadAsync(notebook_path, cancellationToken).ConfigureAwait(false);
        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookInvalidJsonDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        int cellIndex;
        if (string.IsNullOrWhiteSpace(cell_id)) {
            cellIndex = 0;
        } else {
            cellIndex = ResolveCellIndex(notebook, cell_id);
            if (cellIndex < 0) {
                var cellMsg = NotebookDiagnostics.BuildCellNotFoundMessage(notebook, cell_id);
                return ToolResultBuilder.Error().WithText(cellMsg)
                    .WithDiagnostic(ToolDiagnostic.Create("CellNotFound", cellMsg,
                        [new DiagnosticDetail("cellId", cell_id), new DiagnosticDetail("cellCount", notebook.Cells.Count.ToString())],
                        ["cell_id 支持三种格式 — 自定义 ID、\"cell-N\" 格式、数字索引。"])).Build();
            }
        }

        if (mode == NotebookEditMode.Insert)
            cellIndex += 1;

        if (mode == NotebookEditMode.Replace && cellIndex == notebook.Cells.Count) {
            mode = NotebookEditMode.Insert;
            cell_type ??= NotebookCellTypeEnumConstants.Code;
        }

        if (mode == NotebookEditMode.Delete) {
            var deleteResult = _notebookService.DeleteCell(notebook, cellIndex);
            if (!deleteResult.Success) {
                var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("DeleteCell", deleteResult.ErrorMessage, "Failed to delete cell");
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            notebook = deleteResult.GetNotebook();
        } else if (mode == NotebookEditMode.Insert) {
            var ct = NotebookCellTypeExtensions.FromValue(cell_type) ?? NotebookCellType.Code;
            var addResult = _notebookService.AddCell(notebook, ct, new_source, cellIndex);
            if (!addResult.Success) {
                var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("InsertCell", addResult.ErrorMessage, "Failed to insert cell");
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            notebook = addResult.GetNotebook();
        } else {
            // 对齐 TS: replace 模式下支持修改 cell_type
            var editResult = _notebookService.EditCell(notebook, cellIndex, new_source, cell_type);
            if (!editResult.Success) {
                var diag = NotebookDiagnostics.BuildCellOperationFailedDiagnostic("EditCell", editResult.ErrorMessage, "Failed to edit cell");
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            notebook = editResult.GetNotebook();
        }

        // ── 团队密钥检测 + 写前备份 — 对齐 FileWriteTool/FileEditTool 防御链 ──
        if (_writeDefense is not null) {
            var safety = await _writeDefense
                .Begin(notebook_path, new_source, FileOperationType.Edit, "notebook-editing")
                .Then(_writeDefense.CheckTeamMemSecrets)     // 团队密钥检测（new_source 可能含密钥）
                .Then(_writeDefense.BackupBeforeWriteAsync)  // 写前备份
                .ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (safety.Rejection is not null) return safety.Rejection;
        }

        var saved = await _notebookService.SaveAsync(notebook_path, notebook, cancellationToken).ConfigureAwait(false);
        if (!saved) {
            var diag = NotebookDiagnostics.BuildSaveNotebookFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 写入后更新 FileStateCache，确保后续读取不会返回过时的缓存内容
        if (_fs.FileExists(notebook_path)) {
            var postWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(notebook_path)).ToUnixTimeMilliseconds();
            _fileStateCache.RecordRead(notebook_path, "", postWriteMs);
        }

        // ── LSP 通知 + 遥测 + 写入监听器 — 对齐 FileWriteTool/FileEditTool 通知链 ──
        _writeDefense?.NotifyWriteComplete(notebook_path, null, "notebook-edit", FileOperationType.Edit);

        var outputMessage = mode switch {
            NotebookEditMode.Replace => $"Updated cell {cell_id ?? cellIndex.ToString()} with {new_source}",
            NotebookEditMode.Insert => $"Inserted cell {cell_id ?? cellIndex.ToString()} with {new_source}",
            NotebookEditMode.Delete => $"Deleted cell {cell_id ?? cellIndex.ToString()}",
            _ => "Unknown edit mode"
        };

        return ToolResultBuilder.Success().WithText(outputMessage).Build();
    }

    private static int ResolveCellIndex(NotebookDocument notebook, string cellId) {
        for (var i = 0; i < notebook.Cells.Count; i++) {
            if (notebook.Cells[i].Id == cellId)
                return i;
        }

        if (cellId.StartsWith("cell-", StringComparison.OrdinalIgnoreCase) && int.TryParse(cellId.AsSpan(5), out var idx)) {
            if (idx >= 0 && idx < notebook.Cells.Count)
                return idx;
        }

        if (int.TryParse(cellId, out var numericIdx)) {
            if (numericIdx >= 0 && numericIdx < notebook.Cells.Count)
                return numericIdx;
        }

        return -1;
    }

    /// <summary>
    /// 创建新的 Jupyter Notebook 文件。
    /// </summary>
    /// <param name="file_path">文件路径（若不以 .ipynb 结尾则自动追加）。</param>
    /// <param name="kernel_name">内核名称（如 python3，可选）。</param>
    /// <param name="language">编程语言（如 python，可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookCreate, "Create a new Jupyter Notebook file", "notebook")]
    public async Task<ToolResult> NotebookCreateAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Kernel name (e.g. python3)", Required = false)] string? kernel_name = null,
        [McpToolParameter("Programming language (e.g. python)", Required = false)] string? language = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            var diag = NotebookDiagnostics.BuildFilePathEmptyDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (!file_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase)) {
            file_path += ".ipynb";
        }

        var fileResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (fileResult.Success) {
            var diag = NotebookDiagnostics.BuildFileAlreadyExistsDiagnostic(file_path);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var notebook = _notebookService.Create(kernel_name, language);
        var saved = await _notebookService.SaveAsync(file_path, notebook, cancellationToken).ConfigureAwait(false);

        if (!saved) {
            var diag = NotebookDiagnostics.BuildNotebookSaveFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.NotebookCreatedSuccess)}");
        response.AppendLine(L.T(StringKey.NotebookPathLabel, file_path));
        response.AppendLine(L.T(StringKey.NotebookFormatVersion, notebook.NbFormat, notebook.NbFormatMinor));

        if (notebook.Metadata.KernelSpec != null) {
            response.AppendLine(L.T(StringKey.NotebookKernelLabel, notebook.Metadata.KernelSpec.DisplayName));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 加载并查看Notebook
    /// </summary>
    /// <param name="file_path">Notebook 文件路径。</param>
    /// <param name="show_content">是否显示各单元格的完整内容（默认 false）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具执行结果。</returns>
    [McpTool(NotebookToolNameEnumConstants.NotebookRead, "Read a Jupyter Notebook file", "notebook", ConcurrencySafe = true)]
    public async Task<ToolResult> NotebookReadAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Whether to show cell contents", Required = false, DefaultValue = "false")] bool? show_content = null,
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

        // 对齐 TS: 读取后记录到 FileStateCache，确保后续 Edit 的 Read-before-Edit 检查能通过
        if (_fs.FileExists(file_path)) {
            var readMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
            _fileStateCache.RecordRead(file_path, fileResult.Content, readMs);
        }

        var notebook = await _notebookService.LoadAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (notebook == null) {
            var diag = NotebookDiagnostics.BuildNotebookParseFailedDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.NotebookInfoHeader));
        response.AppendLine(L.T(StringKey.NotebookPathLabel, file_path));
        response.AppendLine(L.T(StringKey.NotebookFormatVersion, notebook.NbFormat, notebook.NbFormatMinor));
        response.AppendLine(L.T(StringKey.NotebookTotalCells, notebook.CellCount));
        response.AppendLine(L.T(StringKey.NotebookCodeCells, notebook.GetCodeCellCount()));
        response.AppendLine(L.T(StringKey.NotebookMarkdownCells, notebook.GetMarkdownCellCount()));

        if (notebook.Metadata.KernelSpec != null) {
            response.AppendLine(L.T(StringKey.NotebookKernelLabel, $"{notebook.Metadata.KernelSpec.DisplayName} ({notebook.Metadata.KernelSpec.Language})"));
        }

        response.AppendLine();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.NotebookCellListHeader)}");

        var cells = _notebookService.ListCells(notebook);
        response.Append(string.Join(Environment.NewLine,
            cells.Select(c =>
                $"{c.Type switch { NotebookCellType.Code => ObjectSymbol.DiamondFilled.ToValue(), NotebookCellType.Markdown => ObjectSymbol.Pencil.ToValue(), _ => ObjectSymbol.File.ToValue() }} [{c.Index}] {c.Type,-10} {c.Preview}")));
        response.AppendLine();

        if (show_content == true && cells.Count > 0) {
            response.AppendLine();
            response.AppendLine($"{ObjectSymbol.File.ToValue()} {L.T(StringKey.NotebookCellContentHeader)}");
            response.AppendLine();

            for (var i = 0; i < notebook.Cells.Count; i++) {
                var cell = notebook.Cells[i];
                response.AppendLine(L.T(StringKey.NotebookCellSeparator, i, cell.Type));
                response.AppendLine(cell.SourceText);
                response.AppendLine();
            }
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
