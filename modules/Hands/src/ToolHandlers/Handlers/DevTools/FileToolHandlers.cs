

namespace Tools.Handlers;

[McpToolHandler(ToolCategory.File)]
public class FileToolHandlers : IDisposable
{
    private static readonly FrozenSet<string> BinaryExtensions = CreateBinaryExtensionSet();
    private static readonly FrozenSet<string> BlockedDevicePaths = CreateBlockedDevicePathSet();
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// LSP 通知完成信号量 — 测试中用于等待 fire-and-forget 操作完成，替代 Task.Delay
    /// 初始计数 0，NotifyLspFileChange 完成后释放 1 次
    /// </summary>
    private readonly SemaphoreSlim _lspNotificationCompleted = new(0, int.MaxValue);

    /// <summary>
    /// 图像扩展名集合（不作为二进制拒绝，而是读取为图像）
    /// 对齐 TS: IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp'])
    /// </summary>
    private static readonly FrozenSet<string> ImageExtensions = FrozenSet.ToFrozenSet(
        ImageMediaTypeHelper.Extensions, StringComparer.OrdinalIgnoreCase);

    private const string MalwareReminder = """

        <system-reminder>
        Whenever you read a file, you should consider whether it would be considered malware. You CAN and SHOULD provide analysis of malware, what it is doing. But you MUST refuse to improve or augment the code. You can still analyze existing code, write reports, or behavior about the code.
        </system-reminder>
        """;

    private readonly IFileOperationService _fileOperationService;
    private readonly IScratchpadSandbox? _scratchpadSandbox;
    private readonly ITelemetryService? _telemetryService;
    private readonly FileEditLogic? _fileEditLogic;
    private readonly SnipLogic? _snipLogic;
    private readonly IFileStateCache? _fileStateCache;
    private readonly IFileHistoryService? _fileHistoryService;
    private readonly ILspFileSync? _lspFileSync;
    private readonly FileOperationConfig _fileOperationConfig;
    private readonly ITeamMemSecretGuard? _teamMemSecretGuard;
    private readonly IFileReadListenerRegistry? _fileReadListenerRegistry;
    private readonly ILspDiagnosticProvider? _lspDiagnosticProvider;
    private readonly IFileSystem _fs;

    /// <summary>
    /// Default max read tokens (matches TS: DEFAULT_MAX_OUTPUT_TOKENS = 25000)
    /// </summary>
    private const int DefaultMaxReadTokens = 25000;

    public FileToolHandlers(
        IFileOperationService fileOperationService,
        IFileSystem fs,
        FileToolHandlersContext? context = null)
    {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _scratchpadSandbox = context?.ScratchpadSandbox;
        _telemetryService = context?.TelemetryService;
        _fileEditLogic = context?.FileEditLogic;
        _snipLogic = context?.SnipLogic;
        _fileStateCache = context?.FileStateCache;
        _fileHistoryService = context?.FileHistoryService;
        _lspFileSync = context?.LspFileSync;
        _fileOperationConfig = context?.FileOperationConfig ?? new FileOperationConfig();
        _teamMemSecretGuard = context?.TeamMemSecretGuard;
        _fileReadListenerRegistry = context?.FileReadListenerRegistry;
        _lspDiagnosticProvider = context?.LspDiagnosticProvider;
    }

    [McpTool(FileToolNameConstants.FileRead, "Read a file from the local filesystem", "file")]
    public async Task<ToolResult> FileReadAsync(
        [McpToolParameter("The absolute path to the file to read")] string file_path,
        [McpToolParameter("The line number to start reading from (1-based). Only use for large files.", Required = false)] int? offset = null,
        [McpToolParameter("The number of lines to read. Only use for large files.", Required = false)] int? limit = null,
        [McpToolParameter("Page range for PDF files (e.g., \"1-5\", \"3\", \"10-20\"). Only applicable to PDF files. Maximum 20 pages per request.", Required = false)] string? pages = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"),
            ValidationHelper.ValidateRange(offset, 1, int.MaxValue, "offset"),
            ValidationHelper.ValidateRange(limit, 1, int.MaxValue, "limit"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        if (IsUncPath(file_path))
        {
            return ResultBuilder.Error().WithText("Cannot read UNC path files (starting with \\\\), this may lead to credential leakage").Build();
        }

        if (IsBlockedDevicePath(file_path))
        {
            return ResultBuilder.Error().WithText($"Cannot read '{file_path}': this device file would block or produce infinite output.").Build();
        }

        var ext = Path.GetExtension(file_path).ToLowerInvariant();
        var extWithoutDot = ext.Length > 0 ? ext[1..] : string.Empty;

        // 图像文件特殊处理（不作为二进制拒绝，而是读取为图像）
        if (ImageExtensions.Contains(extWithoutDot))
        {
            return await ReadImageFileAsync(file_path, extWithoutDot, cancellationToken).ConfigureAwait(false);
        }

        // 对齐 TS: FileReadTool — PDF 文件特殊处理（不作为二进制拒绝，而是读取为 base64）
        if (PdfReader.IsPdfExtension(file_path))
        {
            return await ReadPdfFileAsync(file_path, pages, cancellationToken).ConfigureAwait(false);
        }

        // 对齐 TS: FileReadTool — Notebook 文件特殊处理（不作为二进制拒绝，而是格式化输出）
        if (NotebookReader.IsNotebookExtension(file_path))
        {
            return await ReadNotebookFileAsync(file_path, cancellationToken).ConfigureAwait(false);
        }

        if (HasBinaryExtension(ext))
        {
            return ResultBuilder.Error().WithText($"This tool cannot read binary files. The file appears to be a binary {ext} file. Use an appropriate tool for analysis.").Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: readFileState dedup — 检查文件是否已读取且未修改
        // 约 18% 的 Read 调用是同文件碰撞，去重可节省 cache_creation token
        var existingState = _fileStateCache?.GetReadState(file_path);
        if (existingState is not null && !existingState.IsPartialView && existingState.Offset.HasValue)
        {
            var rangeMatch = existingState.Offset == (offset.HasValue ? offset.Value - 1 : (int?)null)
                && existingState.Limit == limit;
            if (rangeMatch)
            {
                try
                {
                    var currentMtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
                    if (currentMtimeMs == existingState.TimestampMs)
                    {
                        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
                        return ResultBuilder.Success()
                            .WithText("File unchanged since last read. The content from the earlier Read tool_result in this conversation is still current — refer to that instead of re-reading.")
                            .Build();
                    }
                }
                catch (Exception ex)
                {
                    // stat 失败（文件可能被删除），降级为完整读取
                    System.Diagnostics.Trace.WriteLine($"文件stat检查失败，降级为完整读取: {ex.Message}");
                }
            }
        }

        var fileOffset = offset.HasValue ? offset.Value - 1 : (int?)null;

        var result = await _fileOperationService.ReadFileAsync(
            file_path,
            fileOffset,
            limit,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to read file").Build();
        }

        if (result.TotalLines == 0)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            return ResultBuilder.Success().WithText("<system-reminder>Warning: the file exists but the contents are empty.</system-reminder>").Build();
        }

        if (result.NumLines == 0 && offset.HasValue && offset.Value > result.TotalLines)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            return ResultBuilder.Success().WithText($"<system-reminder>Warning: the file exists but is shorter than the provided offset ({offset.Value}). The file has {result.TotalLines} lines.</system-reminder>").Build();
        }

        // Token limit check (matches TS: validateContentTokens)
        // Prevents reading files that would consume too much context
        var maxTokens = _fileOperationConfig.MaxReadTokens > 0
            ? _fileOperationConfig.MaxReadTokens
            : DefaultMaxReadTokens;
        var estimatedTokens = EstimateTokenCount(result.Content, file_path);
        if (estimatedTokens > maxTokens)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.TokenExceeded);
            return ResultBuilder.Error().WithText(
                $"File content ({estimatedTokens} tokens) exceeds maximum allowed tokens ({maxTokens}). " +
                "Use offset and limit parameters to read specific portions of the file, " +
                "or search for specific content instead of reading the whole file.").Build();
        }

        var numberedContent = AddLineNumbers(result.Content, result.StartLine);

        var response = new StringBuilder(256);
        response.Append(numberedContent);

        // 对齐 TS: FileReadTool — 记忆文件新鲜度提示
        if (MemoryFreshnessNote.IsMemoryFile(file_path))
        {
            var mtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
            var freshnessNote = MemoryFreshnessNote.FreshnessNote(mtimeMs);
            if (!string.IsNullOrEmpty(freshnessNote))
            {
                response.Append(freshnessNote);
            }
        }

        response.Append(MalwareReminder);

        // Record read state for write-before-read validation
        // 对齐 TS: timestamp 使用文件 mtime 而非当前时间，用于去重判断
        long recordTimestampMs;
        try
        {
            recordTimestampMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(result.FilePath)).ToUnixTimeMilliseconds();
        }
        catch
        {
            recordTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        _fileStateCache?.RecordRead(
            result.FilePath,
            result.Content,
            recordTimestampMs,
            offset.HasValue ? offset.Value - 1 : null,
            limit);

        // 对齐 TS: FileReadTool — 通知文件读取监听器
        // 仅在文本文件读取成功后触发，PDF/Notebook/图像等特殊文件不触发
        _fileReadListenerRegistry?.Notify(new FileReadEventArgs
        {
            FilePath = result.FilePath,
            Content = result.Content,
        });

        // 对齐 TS: tengu_session_file_read + tengu_file_operation — 详细遥测
        RecordFileReadTelemetry(result.FilePath, result.Content, result.TotalLines, result.NumLines, offset, limit);

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileWrite, "Write a file to the local filesystem", "file")]
    public async Task<ToolResult> FileWriteAsync(
        [McpToolParameter("The absolute path to the file to write (must be absolute, not relative)")] string file_path,
        [McpToolParameter("The content to write to the file")] string content,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        if (IsUncPath(file_path))
        {
            return ResultBuilder.Error().WithText("Cannot write UNC path files (starting with \\\\), this may lead to credential leakage").Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: FileWriteTool.ts L156-160 — 拒绝写入团队记忆文件中的密钥
        if (_teamMemSecretGuard is not null)
        {
            var secretError = _teamMemSecretGuard.CheckTeamMemSecrets(file_path, content);
            if (secretError is not null)
            {
                return ResultBuilder.Error().WithText(secretError).Build();
            }
        }

        // Write-before-read validation: existing files must be read first
        if (_fileStateCache is not null && _fs.FileExists(file_path))
        {
            if (!_fileStateCache.HasBeenRead(file_path))
            {
                RecordFileMetrics(FileOperationType.Write, FileOperationResult.Rejected);
                return ResultBuilder.Error().WithText("File has not been read yet. Read it first before writing to it. Use the Read tool to examine the file, then write your changes.").Build();
            }

            // Stale-write guard: check if file was modified after we read it
            var readTimestamp = _fileStateCache.GetReadTimestampMs(file_path);
            if (readTimestamp.HasValue)
            {
                var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
                if (lastWriteMs > readTimestamp.Value + 1000) // 1s tolerance
                {
                    // Timestamp indicates modification, but on Windows timestamps can change
                    // without content changes (cloud sync, antivirus, etc.). Compare content
                    // as a fallback to avoid false positives (mirrors TS behavior).
                    var readContent = _fileStateCache.GetReadContent(file_path);
                    var isFullRead = readContent is not null;
                    if (isFullRead)
                    {
                        // 对齐 TS: 用检测到的编码读取文件，避免 UTF-16LE 文件内容比对错误
                        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(file_path, _fs, cancellationToken).ConfigureAwait(false);
                        var currentContent = await _fs.ReadAllTextAsync(file_path, detectedEncoding, cancellationToken).ConfigureAwait(false);
                        if (currentContent == readContent)
                        {
                            // Content unchanged, safe to proceed
                        }
                        else
                        {
                            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Stale);
                            return ResultBuilder.Error().WithText("File has been modified since it was last read. The file may have been changed by another process. Read it again before writing to ensure you have the latest content.").Build();
                        }
                    }
                    else
                    {
                        RecordFileMetrics(FileOperationType.Write, FileOperationResult.Stale);
                        return ResultBuilder.Error().WithText("File has been modified since it was last read. The file may have been changed by another process. Read it again before writing to ensure you have the latest content.").Build();
                    }
                }
            }
        }

        // Backup file before writing (if file exists)
        if (_fileHistoryService is not null && _fs.FileExists(file_path))
        {
            await _fileHistoryService.BackupBeforeWriteAsync(file_path, cancellationToken).ConfigureAwait(false);
        }

        var result = await _fileOperationService.WriteFileAsync(
            file_path,
            content,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to write file").Build();
        }

        var response = result.Operation == FileOperationTypeConstants.Create
            ? $"File created successfully at: {result.FilePath}"
            : $"The file {result.FilePath} has been updated successfully.";

        // 附加 structuredPatch 到 ToolResult — 对齐 TS FileWriteTool 返回 structuredPatch
        var toolResult = ResultBuilder.Success().WithText(response).Build();
        if (result.StructuredPatch.Length > 0)
        {
            toolResult.StructuredPatch = result.StructuredPatch;
        }

        // 对齐 TS: clearDeliveredDiagnosticsForFile — 写入后清除已投递诊断，让新诊断能重新展示
        _lspDiagnosticProvider?.ClearDeliveredForFile($"file://{result.FilePath}");

        // 对齐 TS: 写入成功后通知 LSP 服务器（fire-and-forget）
        // 1. changeFile（含 didOpen 自动回退）→ 2. saveFile
        NotifyLspFileChange(result.FilePath, content);

        RecordFileMetrics(FileOperationType.Write, FileOperationResult.Ok);
        return toolResult;
    }

    [McpTool(FileToolNameConstants.FileEdit, "Edit file contents by search-and-replace", "file")]
    public async Task<ToolResult> FileEditAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("String to replace (must match exactly)")] string old_string,
        [McpToolParameter("Replacement string")] string new_string,
        [McpToolParameter("Replace all occurrences, default false", Required = false, DefaultValue = "false")] bool replace_all = false,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        if (IsUncPath(file_path))
        {
            return ResultBuilder.Error().WithText("Cannot edit UNC path files (starting with \\\\), this may lead to credential leakage").Build();
        }

        if (file_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            return ResultBuilder.Error().WithText("This is a Jupyter Notebook file. Use the notebook_edit tool to edit this file.").Build();
        }

        if (old_string == new_string)
        {
            return ResultBuilder.Error().WithText("old_string and new_string are identical, no changes needed").Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: FileEditTool.ts L143-147 — 拒绝编辑团队记忆文件时引入密钥
        if (_teamMemSecretGuard is not null)
        {
            var secretError = _teamMemSecretGuard.CheckTeamMemSecrets(file_path, new_string);
            if (secretError is not null)
            {
                return ResultBuilder.Error().WithText(secretError).Build();
            }
        }

        // 对齐 TS: FileEditTool.ts L345-359 — settings 文件编辑校验
        // 只阻止"从合法变非法"的降级编辑，不阻止修复
        if (SettingsEditValidator.IsJccSettingsPath(file_path) && _fs.FileExists(file_path))
        {
            var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(file_path, _fs, cancellationToken).ConfigureAwait(false);
            var originalContent = await _fs.ReadAllTextAsync(file_path, detectedEncoding, cancellationToken).ConfigureAwait(false);
            // 对齐 TS: 预模拟编辑 — 使用与 FileEditTool 相同的替换逻辑
            var updatedContent = replace_all
                ? originalContent.Replace(old_string, new_string)
                : ReplaceFirst(originalContent, old_string, new_string);
            var settingsError = SettingsEditValidator.ValidateEdit(file_path, originalContent, updatedContent);
            if (settingsError is not null)
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Rejected);
                return ResultBuilder.Error().WithText(settingsError).Build();
            }
        }

        // Write-before-read validation for edits too
        if (_fileStateCache is not null && _fs.FileExists(file_path))
        {
            if (!_fileStateCache.HasBeenRead(file_path))
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Rejected);
                return ResultBuilder.Error().WithText("File has not been read yet. Read it first before editing it. Use the Read tool to examine the file, then make your edits.").Build();
            }

            var readTimestamp = _fileStateCache.GetReadTimestampMs(file_path);
            if (readTimestamp.HasValue)
            {
                var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
                if (lastWriteMs > readTimestamp.Value + 1000) // 1s tolerance
                {
                    // Timestamp indicates modification, but on Windows timestamps can change
                    // without content changes (cloud sync, antivirus, etc.). Compare content
                    // as a fallback to avoid false positives (mirrors TS behavior).
                    var readContent = _fileStateCache.GetReadContent(file_path);
                    var isFullRead = readContent is not null;
                    if (isFullRead)
                    {
                        // 对齐 TS: 用检测到的编码读取文件，避免 UTF-16LE 文件内容比对错误
                        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(file_path, _fs, cancellationToken).ConfigureAwait(false);
                        var currentContent = await _fs.ReadAllTextAsync(file_path, detectedEncoding, cancellationToken).ConfigureAwait(false);
                        if (currentContent == readContent)
                        {
                            // Content unchanged, safe to proceed
                        }
                        else
                        {
                            RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Stale);
                            return ResultBuilder.Error().WithText("File has been modified since it was last read. The file may have been changed by another process. Read it again before editing to ensure you have the latest content.").Build();
                        }
                    }
                    else
                    {
                        RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Stale);
                        return ResultBuilder.Error().WithText("File has been modified since it was last read. The file may have been changed by another process. Read it again before editing to ensure you have the latest content.").Build();
                    }
                }
            }
        }

        // Backup file before editing
        if (_fileHistoryService is not null && _fs.FileExists(file_path))
        {
            await _fileHistoryService.BackupBeforeWriteAsync(file_path, cancellationToken).ConfigureAwait(false);
        }

        var result = await _fileOperationService.EditFileAsync(
            file_path,
            old_string,
            new_string,
            replace_all,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to edit file").Build();
        }

        var response = replace_all
            ? $"The file {result.FilePath} has been updated. All {result.ReplaceCount} occurrences were successfully replaced."
            : $"The file {result.FilePath} has been updated successfully.";

        // 附加 structuredPatch 到 ToolResult — 对齐 TS FileEditTool 返回 structuredPatch
        var toolResult = ResultBuilder.Success().WithText(response).Build();
        if (result.StructuredPatch.Length > 0)
        {
            toolResult.StructuredPatch = result.StructuredPatch;
        }

        // 对齐 TS: clearDeliveredDiagnosticsForFile — 编辑后清除已投递诊断，让新诊断能重新展示
        _lspDiagnosticProvider?.ClearDeliveredForFile($"file://{result.FilePath}");

        // 对齐 TS: 编辑成功后通知 LSP 服务器（fire-and-forget）
        // 1. changeFile（含 didOpen 自动回退）→ 2. saveFile
        NotifyLspFileChange(result.FilePath, null);

        RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Ok);
        return toolResult;
    }

    [McpTool(FileToolNameConstants.FileDelete, "Delete the specified file", "file")]
    public async Task<ToolResult> FileDeleteAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        var success = await _fileOperationService.DeleteFileAsync(
            file_path,
            cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText("Failed to delete file, file may not exist").Build();
        }

        RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText($"File deleted: {file_path}").Build();
    }

    [McpTool(FileToolNameConstants.DirectoryList, "List directory contents including files and subdirectories", "file")]
    public async Task<ToolResult> DirectoryListAsync(
        [McpToolParameter("Directory path, relative or absolute")] string directory_path,
        [McpToolParameter("Recursively list subdirectory contents, default false", Required = false, DefaultValue = "false")] bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(directory_path, "directory_path"),
            ValidationHelper.ValidateStringLength(directory_path, 4096, "directory_path"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        directory_path = await ResolveSandboxPathAsync(directory_path, cancellationToken).ConfigureAwait(false);

        var result = await _fileOperationService.ListDirectoryAsync(
            directory_path,
            recursive,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.List, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to list directory").Build();
        }

        var response = new StringBuilder(512);
        response.AppendLine($"Directory: {result.DirectoryPath}");
        response.AppendLine($"Subdirectories: {result.Directories.Count}");
        response.AppendLine($"Files: {result.Files.Count}");

        if (result.Directories.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("[Subdirectories]");
            foreach (var dir in result.Directories.Take(50))
            {
                response.AppendLine($"  {ObjectSymbol.Directory.ToValue()} {dir.Name}/");
            }
            if (result.Directories.Count > 50)
            {
                response.AppendLine($"  ... and {result.Directories.Count - 50} more subdirectories");
            }
        }

        if (result.Files.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("[Files]");
            foreach (var file in result.Files.Take(100))
            {
                var size = ContentReplacementConstants.FormatFileSize(file.Size);
                response.AppendLine($"  {ObjectSymbol.File.ToValue()} {file.Name} ({size})");
            }
            if (result.Files.Count > 100)
            {
                response.AppendLine($"  ... and {result.Files.Count - 100} more files");
            }
        }

        RecordFileMetrics(FileOperationType.List, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileEditRegex, "Edit file using regex pattern to replace matched text", "file")]
    public async Task<ToolResult> FileEditRegexAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Regex pattern")] string pattern,
        [McpToolParameter("Replacement string")] string replacement,
        [McpToolParameter("Replace all matches, default true", Required = false, DefaultValue = "true")] bool replace_all = true,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
            return ResultBuilder.Error().WithText("File edit service is not initialized").Build();

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRequired(pattern, "pattern"));
        if (validationError != null)
            return ResultBuilder.Error().WithText(validationError).Build();

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        var result = await _fileEditLogic.EditWithRegexAsync(file_path, pattern, replacement, replace_all, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.EditRegex, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Regex edit failed").Build();
        }

        var response = new StringBuilder(128);
        response.AppendLine($"File edited: {result.FilePath}");
        response.AppendLine($"Replaced {result.ReplaceCount} occurrence(s)");

        RecordFileMetrics(FileOperationType.EditRegex, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileInsertLines, "Insert new content after a specified line in the file", "file")]
    public async Task<ToolResult> FileInsertLinesAfterAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Line number after which to insert (0 for file beginning)")] int after_line,
        [McpToolParameter("New content to insert")] string new_content,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
            return ResultBuilder.Error().WithText("File edit service is not initialized").Build();

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRequired(new_content, "new_content"),
            ValidationHelper.ValidateRange(after_line, 0, int.MaxValue, "after_line"));
        if (validationError != null)
            return ResultBuilder.Error().WithText(validationError).Build();

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        var result = await _fileEditLogic.InsertLinesAfterAsync(file_path, after_line, new_content, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.InsertLines, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to insert lines").Build();
        }

        var response = new StringBuilder(128);
        response.AppendLine($"Content inserted: {result.FilePath}");
        response.AppendLine($"Inserted {result.ReplacedLinesCount} line(s) after line {after_line}");

        RecordFileMetrics(FileOperationType.InsertLines, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileDeleteLines, "Delete a range of lines from the file", "file")]
    public async Task<ToolResult> FileDeleteLinesAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Start line number (1-based)")] int start_line,
        [McpToolParameter("End line number (1-based)")] int end_line,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
            return ResultBuilder.Error().WithText("File edit service is not initialized").Build();

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(start_line, 1, int.MaxValue, "start_line"),
            ValidationHelper.ValidateRange(end_line, 1, int.MaxValue, "end_line"));
        if (validationError != null)
            return ResultBuilder.Error().WithText(validationError).Build();

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        var result = await _fileEditLogic.DeleteLinesAsync(file_path, start_line, end_line, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.DeleteLines, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to delete lines").Build();
        }

        var response = new StringBuilder(128);
        response.AppendLine($"Lines deleted: {result.FilePath}");
        response.AppendLine($"Deleted {result.ReplacedLinesCount} line(s) ({result.StartLine}-{result.EndLine})");

        RecordFileMetrics(FileOperationType.DeleteLines, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileBatchEdit, "Batch edit multiple files with the same search-and-replace", "file")]
    public async Task<ToolResult> FileBatchEditAsync(
        [McpToolParameter("String to replace (must match exactly)")] string old_string,
        [McpToolParameter("Replacement string")] string new_string,
        [McpToolParameter("List of file paths")] string[]? file_paths = null,
        [McpToolParameter("Replace all matches, default true", Required = false, DefaultValue = "true")] bool replace_all = true,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
            return ResultBuilder.Error().WithText("File edit service is not initialized").Build();

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(old_string, "old_string"));
        if (validationError != null)
            return ResultBuilder.Error().WithText(validationError).Build();

        if (file_paths == null || file_paths.Length == 0)
            return ResultBuilder.Error().WithText("At least one file path is required").Build();

        var resolvedPaths = await Task.WhenAll(
            file_paths.Select(path => ResolveSandboxPathAsync(path, cancellationToken))).ConfigureAwait(false);

        var results = await _fileEditLogic.BatchEditAsync(resolvedPaths, old_string, new_string, replace_all, cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder(512);
        response.AppendLine($"Batch edit completed: {results.Count} file(s)");
        response.AppendLine();

        var successCount = 0;
        var failureCount = 0;
        foreach (var item in results)
        {
            if (item.Result.Success)
            {
                successCount++;
                response.AppendLine($"  {StatusSymbol.Tick.ToValue()} {item.FilePath} ({item.Result.ReplaceCount} replacement(s))");
            }
            else
            {
                failureCount++;
                response.AppendLine($"  {StatusSymbol.Cross.ToValue()} {item.FilePath}: {item.Result.ErrorMessage}");
            }
        }

        response.AppendLine();
        response.AppendLine($"Succeeded: {successCount}, Failed: {failureCount}");

        RecordFileMetrics(FileOperationType.BatchEdit, failureCount == 0 ? FileOperationResult.Ok : FileOperationResult.Partial);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileSnipLines, "Read a range of lines from the file (snip read)", "file")]
    public async Task<ToolResult> FileSnipLinesAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Start line number (0-based)", Required = false, DefaultValue = "0")] int start_line = 0,
        [McpToolParameter("Line count limit", Required = false, DefaultValue = "100")] int line_count = 100,
        CancellationToken cancellationToken = default)
    {
        if (_snipLogic == null)
            return ResultBuilder.Error().WithText("File chunking service is not initialized").Build();

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(start_line, 0, int.MaxValue, "start_line"),
            ValidationHelper.ValidateRange(line_count, 1, int.MaxValue, "line_count"));
        if (validationError != null)
            return ResultBuilder.Error().WithText(validationError).Build();

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        string content;
        try
        {
            content = await _snipLogic.SnipLinesAsync(file_path, start_line, line_count, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            RecordFileMetrics(FileOperationType.SnipLines, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(ex.Message).Build();
        }

        var response = new StringBuilder(256);
        response.AppendLine($"File: {file_path}");
        response.AppendLine($"Line range: {start_line}-{start_line + line_count - 1}");
        response.AppendLine();
        response.AppendLine("```");
        response.Append(content.TrimEnd());
        response.AppendLine();
        response.AppendLine("```");

        RecordFileMetrics(FileOperationType.SnipLines, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(FileToolNameConstants.FileSnipPreview, "Get file preview info (size, line count, first N lines)", "file")]
    public async Task<ToolResult> FileSnipPreviewAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Max preview lines", Required = false, DefaultValue = "20")] int max_preview_lines = 20,
        CancellationToken cancellationToken = default)
    {
        if (_snipLogic == null)
            return ResultBuilder.Error().WithText("File chunking service is not initialized").Build();

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(max_preview_lines, 1, int.MaxValue, "max_preview_lines"));
        if (validationError != null)
            return ResultBuilder.Error().WithText(validationError).Build();

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        SnipPreview preview;
        try
        {
            preview = await _snipLogic.GetPreviewAsync(file_path, max_preview_lines, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            RecordFileMetrics(FileOperationType.SnipPreview, FileOperationResult.Failed);
            return ResultBuilder.Error().WithText(ex.Message).Build();
        }

        var response = new StringBuilder(256);
        response.AppendLine($"File: {preview.FilePath}");
        response.AppendLine($"Size: {ContentReplacementConstants.FormatFileSize(preview.FileSize)}");
        response.AppendLine($"Total lines: {preview.TotalLines}");
        response.AppendLine();
        response.AppendLine("--- Preview ---");
        response.Append(preview.PreviewContent.TrimEnd());
        if (preview.TotalLines > max_preview_lines)
            response.AppendLine("...");

        RecordFileMetrics(FileOperationType.SnipPreview, FileOperationResult.Ok);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Private Methods

    /// <summary>
    /// 通知 LSP 服务器文件变更（fire-and-forget）
    /// 对齐 TS FileWriteTool/FileEditTool: changeFile + saveFile
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">
    /// 文件完整内容。FileWrite 传入写入内容，FileEdit 传入 null（由 LspFileSync 从文件读取）
    /// </param>
    private void NotifyLspFileChange(string filePath, string? content)
    {
        if (_lspFileSync is null)
        {
            // LspFileSync 为 null 时立即释放信号量，避免测试等待超时
            _lspNotificationCompleted.Release();
            return;
        }

        var ct = _disposeCts.Token;
        // fire-and-forget: 不阻塞主流程，错误在 LspFileSync 内部处理
        _ = Task.Run(async () =>
        {
            try
            {
                // 对齐 TS: lspManager.changeFile(path, content)
                // LspFileSync.ChangeDocumentAsync 内部处理 didOpen 自动回退
                var changeContent = content;
                if (changeContent is null)
                {
                    // FileEdit 场景：从磁盘读取编辑后的内容（检测编码）
                    var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs).ConfigureAwait(false);
                    changeContent = await _fs.ReadAllTextAsync(filePath, encoding).ConfigureAwait(false);
                }

                await _lspFileSync.ChangeDocumentAsync(
                    filePath,
                    [new TextDocumentContentChangeEvent { Text = changeContent }],
                    ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

                // 对齐 TS: lspManager.saveFile(path)
                await _lspFileSync.SaveDocumentAsync(filePath, ct)
                    .WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消时静默退出
            }
            catch (Exception ex)
            {
                // 对齐 TS: .catch() 处理错误，不阻塞主流程
                _telemetryService?.RecordCount("file.lsp_notify.error", new Dictionary<string, string>
                {
                    ["operation"] = "change_and_save",
                    ["error"] = ex.GetType().Name
                }, description: "LSP file change notification error");
            }
            finally
            {
                // 通知完成，释放信号量供测试等待
                _lspNotificationCompleted.Release();
            }
        }, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try { _disposeCts.Cancel(); } catch (ObjectDisposedException ex) { System.Diagnostics.Trace.WriteLine($"Dispose时取消CancellationTokenSource失败: {ex.Message}"); }
        try { _disposeCts.Dispose(); } catch (ObjectDisposedException ex) { System.Diagnostics.Trace.WriteLine($"Dispose时释放CancellationTokenSource失败: {ex.Message}"); }
        try { _lspNotificationCompleted.Dispose(); } catch (ObjectDisposedException ex) { System.Diagnostics.Trace.WriteLine($"Dispose时释放LSP通知信号量失败: {ex.Message}"); }
    }

    private static FrozenSet<string> CreateBinaryExtensionSet()
    {
        return FrozenSet.ToFrozenSet(
        [
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v", ".mpeg", ".mpg",
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".aiff", ".opus",
            ".zip", ".tar", ".gz", ".bz2", ".7z", ".rar", ".xz", ".z", ".tgz", ".iso",
            ".exe", ".dll", ".so", ".dylib", ".bin", ".o", ".a", ".obj", ".lib", ".app", ".msi", ".deb", ".rpm",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            ".pyc", ".pyo", ".class", ".jar", ".war", ".ear", ".node", ".wasm", ".rlib",
            ".sqlite", ".sqlite3", ".db", ".mdb", ".idx",
            ".psd", ".ai", ".eps", ".sketch", ".fig", ".xd", ".blend", ".3ds", ".max",
            ".swf", ".fla",
            ".lockb", ".dat", ".data"
        ], StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasBinaryExtension(string ext)
    {
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    private static FrozenSet<string> CreateBlockedDevicePathSet()
    {
        return FrozenSet.ToFrozenSet(
        [
            "/dev/zero", "/dev/random", "/dev/urandom", "/dev/full",
            "/dev/stdin", "/dev/tty", "/dev/console",
            "/dev/stdout", "/dev/stderr",
            "/dev/fd/0", "/dev/fd/1", "/dev/fd/2"
        ], StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBlockedDevicePath(string filePath)
    {
        if (BlockedDevicePaths.Contains(filePath))
            return true;

        if (filePath.StartsWith("/proc/", StringComparison.OrdinalIgnoreCase)
            && (filePath.EndsWith("/fd/0", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith("/fd/1", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith("/fd/2", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static bool IsUncPath(string filePath)
    {
        return filePath.StartsWith("\\\\", StringComparison.Ordinal)
               || filePath.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>
    /// 替换字符串中第一个匹配项（用于预模拟编辑）。
    /// 对齐 TS: file.replace(actualOldString, new_string) — 非替换所有时只替换第一个匹配
    /// </summary>
    private static string ReplaceFirst(string text, string search, string replace)
    {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + search.Length));
    }

    private static string AddLineNumbers(string content, int startLine)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var lines = content.Split(['\n'], StringSplitOptions.None);
        var maxLineNum = startLine + lines.Length - 1;
        var maxDigits = maxLineNum.ToString().Length;
        var padWidth = Math.Max(maxDigits, 6);

        var sb = new StringBuilder(content.Length + lines.Length * (padWidth + 2));
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNum = startLine + i;
            sb.Append(lineNum.ToString().PadLeft(padWidth));
            sb.Append('\u2192');
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result)
        => _telemetryService?.RecordCount("file.operation.count", new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() }, description: "File operation count");

    /// <summary>
    /// 记录文件读取详细遥测。
    /// 对齐 TS: tengu_session_file_read — 文本文件读取详情（行数/字节数/扩展名/会话文件类型）
    /// 对齐 TS: tengu_file_operation — 通用文件操作（路径哈希脱敏）
    /// </summary>
    private void RecordFileReadTelemetry(
        string filePath, string content, int totalLines, int readLines,
        int? offset, int? limit)
    {
        if (_telemetryService is null) return;

        // 对齐 TS: getFileExtensionForAnalytics — 脱敏扩展名（超过10字符替换为"other"）
        var ext = Path.GetExtension(filePath).TrimStart('.');
        var analyticsExt = string.IsNullOrEmpty(ext) ? null
            : ext.Length > 10 ? "other"
            : ext;

        // 对齐 TS: detectSessionFileType — 检测会话文件类型
        var isSessionMemory = MemoryFreshnessNote.IsMemoryFile(filePath);
        var isSessionTranscript = filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            && filePath.Contains("projects", StringComparison.OrdinalIgnoreCase);

        // 对齐 TS: tengu_session_file_read
        var tags = new Dictionary<string, string>
        {
            ["total_lines"] = totalLines.ToString(CultureInfo.InvariantCulture),
            ["read_lines"] = readLines.ToString(CultureInfo.InvariantCulture),
            ["total_bytes"] = content.Length.ToString(CultureInfo.InvariantCulture),
            ["read_bytes"] = content.Length.ToString(CultureInfo.InvariantCulture),
            ["offset"] = offset?.ToString(CultureInfo.InvariantCulture) ?? "0",
            ["is_session_memory"] = isSessionMemory.ToString(),
            ["is_session_transcript"] = isSessionTranscript.ToString(),
        };
        if (limit.HasValue)
        {
            tags["limit"] = limit.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (analyticsExt is not null)
        {
            tags["ext"] = analyticsExt;
        }

        _telemetryService.RecordCount("file.read.detail", tags, description: "File read detail telemetry");

        // 对齐 TS: tengu_file_operation — 路径哈希脱敏
        var pathHash = SecurityPatterns.ComputeShortHash(filePath);
        _telemetryService.RecordCount("file.operation",
            new Dictionary<string, string> { ["operation"] = FileOperationTypeConstants.Read, ["path_hash"] = pathHash },
            description: "File operation with path hash");
    }

    /// <summary>
    /// 记录 PDF 读取遥测。
    /// 对齐 TS: tengu_pdf_page_extraction — PDF 页面提取事件
    /// </summary>
    private void RecordPdfReadTelemetry(string filePath, long fileSize, bool success)
    {
        if (_telemetryService is null) return;

        var tags = new Dictionary<string, string>
        {
            ["success"] = success.ToString(),
            ["file_size"] = fileSize.ToString(CultureInfo.InvariantCulture),
        };

        _telemetryService.RecordCount("file.read.pdf", tags, description: "PDF read telemetry");
    }

    /// <summary>
    /// 记录文件操作遥测（路径哈希脱敏）。
    /// 对齐 TS: tengu_file_operation — 通用文件操作事件
    /// </summary>
    private void RecordFileOperationTelemetry(string filePath, string operation)
    {
        if (_telemetryService is null) return;

        var pathHash = SecurityPatterns.ComputeShortHash(filePath);
        _telemetryService.RecordCount("file.operation.hash",
            new Dictionary<string, string> { ["operation"] = operation, ["path_hash"] = pathHash },
            description: "File operation with path hash");
    }

    private async Task<string> ResolveSandboxPathAsync(string path, CancellationToken cancellationToken)
    {
        if (_scratchpadSandbox == null)
        {
            return path;
        }

        // 沙箱未创建时 GetSandboxInfo 抛出 KeyNotFoundException
        // 生产环境 IScratchpadSandbox 通过 DI 注入（非 null），但未必调用过 CreateSandboxAsync
        // 此时直接返回原路径，不进行沙箱路径解析
        SandboxInfo? sandboxInfo;
        try
        {
            sandboxInfo = _scratchpadSandbox.GetSandboxInfo(_scratchpadSandbox.GetType().Name);
        }
        catch (KeyNotFoundException)
        {
            return path;
        }

        if (sandboxInfo == null)
        {
            return path;
        }

        var resolvedPath = await _scratchpadSandbox.ResolveSandboxPathAsync(path, sandboxInfo.SandboxId, cancellationToken).ConfigureAwait(false);
        var isInSandbox = await _scratchpadSandbox.IsPathInSandboxAsync(resolvedPath, sandboxInfo.SandboxId, cancellationToken).ConfigureAwait(false);
        if (!isInSandbox)
        {
            throw new UnauthorizedAccessException($"Path '{path}' is outside the sandbox scope");
        }

        return resolvedPath;
    }

    /// <summary>
    /// 估算文本内容的Token数量。
    /// 对齐 TS: roughTokenCountEstimationForFileType — JSON/JSONL/JSONC 密集格式用 2 字节/token，
    /// 其他文件用 4 字节/token（密集格式的单字符 token 如 { } : , " 导致实际比率更低）。
    /// </summary>
    private static int EstimateTokenCount(string text, string? filePath = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var bytesPerToken = BytesPerTokenForFileType(filePath);
        return text.Length / bytesPerToken + (text.Length % bytesPerToken > 0 ? 1 : 0);
    }

    /// <summary>
    /// 根据文件扩展名返回字节/token比率。
    /// 对齐 TS: bytesPerTokenForFileType — JSON 密集格式用 2，其他用 4。
    /// </summary>
    private static int BytesPerTokenForFileType(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return 4;
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return ext is "json" or "jsonl" or "jsonc" ? 2 : 4;
    }

    /// <summary>
    /// 估算 base64 编码图像的 Token 数量。
    /// 对齐 TS: base64.length * 0.125（1 token ≈ 8 base64 字符 ≈ 6 原始字节）。
    /// TS 使用 compressImageBufferWithTokenLimit: maxBase64Chars = maxTokens / 0.125
    /// </summary>
    private static int EstimateImageTokenCount(int base64Length)
        => (int)(base64Length * 0.125);

    /// <summary>
    /// 读取图像文件并返回base64编码结果。
    /// 对齐 TS: readImageWithTokenBudget + maybeResizeAndDownsampleImageBuffer
    /// 流程：读取 → magic bytes检测 → 缩放/压缩 → base64编码 → token预算检查
    /// </summary>
    private async Task<ToolResult> ReadImageFileAsync(
        string filePath, string extension, CancellationToken cancellationToken)
    {
        filePath = await ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (!_fs.FileExists(filePath))
        {
            // 对齐 TS: findSimilarFile + suggestPathUnderCwd — 文件未找到时建议相似文件
            var message = FileSuggestionHelper.BuildFileNotFoundMessage(filePath, _fs);
            return ResultBuilder.Error().WithText(message).Build();
        }

        var originalSize = _fs.GetFileLength(filePath);

        if (originalSize == 0)
        {
            return ResultBuilder.Error().WithText($"Image file is empty: {filePath}").Build();
        }

        // 读取原始图像字节
        byte[] imageBytes;
        try
        {
            imageBytes = await _fs.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ResultBuilder.Error().WithText($"Failed to read image file: {ex.Message}").Build();
        }

        // 用 magic bytes 检测实际格式（对齐 TS: detectImageFormatFromBuffer）
        var detectedType = ImageMediaTypeHelper.DetectFromMagicBytes(imageBytes);
        var effectiveExtension = detectedType is not null
            ? detectedType.Value.ToValue()
            : extension;

        // 缩放/压缩图像（对齐 TS: maybeResizeAndDownsampleImageBuffer）
        ImageResizeResult resizeResult;
        try
        {
            resizeResult = await ImageResizer.ResizeAsync(imageBytes, originalSize, effectiveExtension).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.ResizeFailed);
            return ResultBuilder.Error().WithText(ex.Message).Build();
        }

        // Base64 编码
        var base64Data = Convert.ToBase64String(resizeResult.Buffer);

        // 检查 base64 大小是否超过 API 限制（5MB）
        if (base64Data.Length > FileOperationConfig.ApiImageMaxBase64Size)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.ApiLimitExceeded);
            return ResultBuilder.Error().WithText(
                $"Image base64 size ({base64Data.Length} bytes) exceeds API limit ({FileOperationConfig.ApiImageMaxBase64Size} bytes). " +
                "Please use a smaller image.").Build();
        }

        // Token 预算检查（对齐 TS: base64.length * 0.125）
        var estimatedTokens = EstimateImageTokenCount(base64Data.Length);
        var maxTokens = _fileOperationConfig.MaxReadTokens > 0
            ? _fileOperationConfig.MaxReadTokens
            : DefaultMaxReadTokens;
        if (estimatedTokens > maxTokens)
        {
            // 对齐 TS: compressImageBufferWithTokenLimit — Token 驱动的激进压缩
            var compressedResult = await ImageResizer.CompressWithTokenBudgetAsync(
                resizeResult.Buffer, maxTokens, effectiveExtension).ConfigureAwait(false);

            if (compressedResult is not null)
            {
                // 激进压缩成功，重新编码
                base64Data = Convert.ToBase64String(compressedResult.Buffer);
                resizeResult = compressedResult;
                estimatedTokens = EstimateImageTokenCount(base64Data.Length);
            }
            else
            {
                // 所有压缩策略都无法满足预算
                RecordFileMetrics(FileOperationType.Read, FileOperationResult.TokenExceeded);
                return ResultBuilder.Error().WithText(
                    $"Image content ({estimatedTokens} tokens, {resizeResult.Buffer.Length} bytes) exceeds maximum allowed tokens ({maxTokens}). " +
                    "Try reading a smaller image or use offset/limit on text files instead.").Build();
            }
        }

        // 记录读取状态
        var dimensionInfo = resizeResult.OriginalWidth is not null
            ? $" [{resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} → {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight}]"
            : string.Empty;
        _fileStateCache?.RecordRead(
            filePath,
            $"[image:{resizeResult.MediaType}:{originalSize}{dimensionInfo}]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        // 返回图像结果：base64数据 + 文本摘要 + 元数据文本
        var summaryText = resizeResult.OriginalWidth is not null && resizeResult.OriginalWidth != resizeResult.DisplayWidth
            ? $"Read image: {filePath} (resized from {resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} to {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight}, {resizeResult.MediaType})"
            : $"Read image: {filePath} ({ContentReplacementConstants.FormatFileSize(resizeResult.Buffer.Length)}, {resizeResult.MediaType})";

        // 对齐 TS: createImageMetadataText — 缩放比例+坐标映射提示
        var metadataText = ImageResizer.CreateImageMetadataText(resizeResult, filePath);
        if (metadataText is not null)
            summaryText += $"\n{metadataText}";

        return ResultBuilder.Success()
            .WithImage(base64Data, resizeResult.MediaType)
            .WithText(summaryText)
            .Build();
    }

    /// <summary>
    /// 读取 PDF 文件。
    /// 对齐 TS: FileReadTool — PDF 读取决策树：
    /// 1. 用户指定 pages → 提取指定页面为 JPEG 图片（extractPDFPages）
    /// 2. 未指定 pages，页数 > 10 → 报错要求指定页码
    /// 3. 未指定 pages，页数 ≤ 10 → 检查 shouldExtractPages：
    ///    a. 文件 > 3MB → 提取全部页面为图片
    ///    b. 否则 → 直接发送 base64 PDF（readPDF）
    /// </summary>
    private async Task<ToolResult> ReadPdfFileAsync(
        string filePath, string? pages, CancellationToken cancellationToken)
    {
        // 对齐 TS: validateInput — 验证 pages 参数格式
        PdfPageRange? parsedRange = null;
        if (pages is not null)
        {
            parsedRange = PdfReader.ParsePageRange(pages);
            if (parsedRange is null)
            {
                return ResultBuilder.Error()
                    .WithText($"Invalid pages parameter: \"{pages}\". Use formats like \"1-5\", \"3\", or \"10-20\". Pages are 1-indexed.")
                    .Build();
            }

            var rangePageCount = parsedRange.LastPage == int.MaxValue
                ? FileOperationConfig.PdfMaxPagesPerRead
                : parsedRange.LastPage - parsedRange.FirstPage + 1;
            if (rangePageCount > FileOperationConfig.PdfMaxPagesPerRead)
            {
                return ResultBuilder.Error()
                    .WithText($"Page range \"{pages}\" exceeds maximum of {FileOperationConfig.PdfMaxPagesPerRead} pages per request. Please use a smaller range.")
                    .Build();
            }
        }

        filePath = await ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: 决策1 — 用户指定了 pages 参数 → 提取页面为图片
        if (parsedRange is not null)
        {
            return await ExtractPdfPagesAsync(filePath, parsedRange, cancellationToken).ConfigureAwait(false);
        }

        // 先获取 PDF 基本信息（大小 + 页数）
        var result = await PdfReader.ReadPdfAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to read PDF file").Build();
        }

        // 对齐 TS: 决策2 — 超过 PdfMaxInlinePageCount 页必须使用 pages 参数
        if (result.PageCount is not null &&
            result.PageCount > FileOperationConfig.PdfMaxInlinePageCount)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
            return ResultBuilder.Error()
                .WithText($"This PDF has {result.PageCount} pages, which is too many to read at once. " +
                          $"Use the pages parameter to read specific page ranges (e.g., pages: \"1-5\"). " +
                          $"Maximum {FileOperationConfig.PdfMaxPagesPerRead} pages per request.")
                .Build();
        }

        // 对齐 TS: 决策3 — shouldExtractPages: 文件 > 3MB 时提取页面为图片
        // TS: shouldExtractPages = !isPDFSupported() || fileSize > PDF_EXTRACT_SIZE_THRESHOLD
        // C# 简化：始终支持 PDF base64 发送，仅文件过大时走提取路径
        if (result.OriginalSize > FileOperationConfig.PdfExtractSizeThreshold)
        {
            return await ExtractPdfPagesAsync(filePath, null, cancellationToken).ConfigureAwait(false);
        }

        // 对齐 TS: 决策3b — 文件较小，直接发送 base64 PDF
        // 记录读取状态
        _fileStateCache?.RecordRead(
            filePath,
            $"[pdf:{result.OriginalSize}bytes]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        RecordPdfReadTelemetry(filePath, result.OriginalSize ?? 0, success: true);
        RecordFileOperationTelemetry(filePath, FileOperationTypeConstants.Read);
        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        var pageCountInfo = result.PageCount is not null ? $", {result.PageCount} pages" : string.Empty;
        var summaryText = $"Read PDF: {filePath} ({ContentReplacementConstants.FormatFileSize(result.GetOriginalSize())}{pageCountInfo})";

        return ResultBuilder.Success()
            .WithPdf(result.GetBase64(), result.GetOriginalSize())
            .WithText(summaryText)
            .Build();
    }

    /// <summary>
    /// 提取 PDF 页面为 JPEG 图片。
    /// 对齐 TS: extractPDFPages — 使用 PDFium 渲染页面为 JPEG，再缩放/压缩
    /// </summary>
    private async Task<ToolResult> ExtractPdfPagesAsync(
        string filePath, PdfPageRange? range, CancellationToken cancellationToken)
    {
        // 检查 PDFium 渲染功能是否可用
        if (!PdfPageRenderer.IsAvailable())
        {
            // 对齐 TS: pdftoppm 不可用时降级为 base64 发送
            var fallbackResult = await PdfReader.ReadPdfAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
            if (!fallbackResult.Success)
            {
                RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
                return ResultBuilder.Error().WithText(fallbackResult.ErrorMessage ?? "Failed to read PDF file").Build();
            }

            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            var fallbackInfo = fallbackResult.PageCount is not null ? $", {fallbackResult.PageCount} pages" : string.Empty;
            return ResultBuilder.Success()
                .WithPdf(fallbackResult.GetBase64(), fallbackResult.GetOriginalSize())
                .WithText($"Read PDF (rendering unavailable, sent as document): {filePath} ({ContentReplacementConstants.FormatFileSize(fallbackResult.GetOriginalSize())}{fallbackInfo})")
                .Build();
        }

        var firstPage = range?.FirstPage;
        var lastPage = range?.LastPage == int.MaxValue ? (int?)null : range?.LastPage;

        var extractResult = await PdfPageRenderer.ExtractPagesAsync(
            filePath, _fs, firstPage, lastPage, cancellationToken).ConfigureAwait(false);

        if (!extractResult.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
            return ResultBuilder.Error().WithText(extractResult.ErrorMessage ?? "Failed to extract PDF pages").Build();
        }

        // 记录读取状态
        _fileStateCache?.RecordRead(
            filePath,
            $"[pdf-extract:{extractResult.GetPages().Count}pages]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        RecordPdfReadTelemetry(filePath, extractResult.OriginalSize ?? 0, success: true);
        RecordFileOperationTelemetry(filePath, FileOperationTypeConstants.Read);

        // 对齐 TS: extractPDFPages → 读取输出目录中的 .jpg 文件 → maybeResizeAndDownsampleImageBuffer
        var builder = ResultBuilder.Success();
        var pageDescriptions = new List<string>();

        foreach (var page in extractResult.GetPages())
        {
            // 对齐 TS: maybeResizeAndDownsampleImageBuffer — 缩放/压缩每页图片
            ImageResizeResult resizeResult;
            try
            {
                resizeResult = await ImageResizer.ResizeAsync(
                    page.JpegBytes, page.JpegBytes.Length, "jpg").ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // 缩放失败，使用原始 JPEG
                resizeResult = new ImageResizeResult
                {
                    Buffer = page.JpegBytes,
                    MediaType = "image/jpeg",
                    OriginalWidth = page.Width,
                    OriginalHeight = page.Height,
                    DisplayWidth = page.Width,
                    DisplayHeight = page.Height,
                };
            }

            var base64Data = Convert.ToBase64String(resizeResult.Buffer);

            // 检查 base64 大小是否超过 API 限制
            if (base64Data.Length > FileOperationConfig.ApiImageMaxBase64Size)
            {
                pageDescriptions.Add($"Page {page.PageNumber}: too large to include ({ContentReplacementConstants.FormatFileSize(resizeResult.Buffer.Length)})");
                continue;
            }

            builder.WithImage(base64Data, resizeResult.MediaType);

            var dimensionInfo = resizeResult.OriginalWidth != resizeResult.DisplayWidth
                ? $" (resized {resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} → {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight})"
                : $" ({page.Width}x{page.Height})";
            var pageDesc = $"Page {page.PageNumber}{dimensionInfo}";

            // 对齐 TS: createImageMetadataText — PDF 页面缩放时也附加坐标映射提示
            var pageMetadata = ImageResizer.CreateImageMetadataText(resizeResult);
            var fullPageDesc = pageMetadata is not null
                ? string.Concat(pageDesc, "\n", pageMetadata)
                : pageDesc;

            pageDescriptions.Add(fullPageDesc);
        }

        var rangeText = range is not null ? $" pages {range.FirstPage}-{(range.LastPage == int.MaxValue ? "end" : range.LastPage.ToString())}" : string.Empty;
        var totalInfo = extractResult.TotalPageCount is not null ? $", {extractResult.TotalPageCount} total pages" : string.Empty;
        var summaryText = $"Read PDF: {filePath}{rangeText} — extracted {extractResult.GetPages().Count} page(s){totalInfo}\n{string.Join("\n", pageDescriptions)}";

        builder.WithText(summaryText);

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
        return builder.Build();
    }

    /// <summary>
    /// 读取 Notebook 文件并格式化输出
    /// 对齐 TS: FileReadTool → notebook.ts readNotebook + mapNotebookCellsToToolResult
    /// 支持 cell 输出中的图像作为 ImageBlock 发送
    /// </summary>
    private async Task<ToolResult> ReadNotebookFileAsync(
        string filePath, CancellationToken cancellationToken)
    {
        filePath = await ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: readFileState dedup — Notebook 也需要去重检查
        var existingState = _fileStateCache?.GetReadState(filePath);
        if (existingState is not null && !existingState.IsPartialView)
        {
            try
            {
                var currentMtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
                if (currentMtimeMs == existingState.TimestampMs)
                {
                    RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
                    return ResultBuilder.Success()
                        .WithText("File unchanged since last read. The content from the earlier Read tool_result in this conversation is still current — refer to that instead of re-reading.")
                        .Build();
                }
            }
            catch (Exception ex)
            {
                // stat 失败，降级为完整读取
                System.Diagnostics.Trace.WriteLine($"Notebook文件stat检查失败，降级为完整读取: {ex.Message}");
            }
        }

        var result = await NotebookReader.ReadNotebookAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.NotebookFailed);
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to read notebook file").Build();
        }

        // 记录读取状态
        _fileStateCache?.RecordRead(
            filePath,
            $"[notebook:{result.Text?.Length ?? 0}chars]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // 对齐 TS: tengu_file_operation — Notebook 遥测
        RecordFileOperationTelemetry(filePath, FileOperationTypeConstants.Read);

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        // 对齐 TS: mapNotebookCellsToToolResult — 文本 + 图像块
        var builder = ResultBuilder.Success().WithText(result.GetText());

        // 对齐 TS: cellOutputToToolResult — 将 cell 输出中的图像作为 ImageBlock 发送
        if (result.Images is { Count: > 0 })
        {
            foreach (var image in result.Images)
            {
                builder.WithImage(image.Base64Data, image.MediaType);
            }
        }

        return builder.Build();
    }

    #endregion
}
