namespace Tools.Handlers;

/// <summary>
/// 文件遥测记录器 — 封装文件操作的遥测上报
/// 从 FileToolHandlers 提取,统一管理 4 类遥测: 操作指标/读取详情/PDF读取/操作哈希
/// </summary>
internal sealed class FileTelemetryRecorder {
    private readonly ITelemetryService? _telemetry;

    /// <summary>构造 FileTelemetryRecorder</summary>
    public FileTelemetryRecorder(ITelemetryService? telemetry = null) => _telemetry = telemetry;

    /// <summary>记录文件操作指标 — 操作类型+结果</summary>
    public void RecordFileMetrics(FileOperationType operation, FileOperationResult result)
        => ToolTelemetryHelper.RecordToolCount(_telemetry, "file.operation.count", new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() });

    /// <summary>
    /// 记录文件读取详细遥测。
    /// 对齐 TS: tengu_session_file_read — 文本文件读取详情（行数/字节数/扩展名/会话文件类型）
    /// 对齐 TS: tengu_file_operation — 通用文件操作（路径哈希脱敏）
    /// </summary>
    public void RecordFileReadTelemetry(
        string filePath, string content, int totalLines, int readLines,
        int? offset, int? limit) {
        if (_telemetry is null) return;

        var ext = Path.GetExtension(filePath).TrimStart('.');
        var analyticsExt = string.IsNullOrEmpty(ext) ? null
            : ext.Length > 10 ? "other"
            : ext;

        var isSessionMemory = MemoryFreshnessNote.IsMemoryFile(filePath);
        var isSessionTranscript = filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && filePath.Contains("projects", StringComparison.OrdinalIgnoreCase);

        var tags = new Dictionary<string, string> {
            ["total_lines"] = totalLines.ToString(CultureInfo.InvariantCulture),
            ["read_lines"] = readLines.ToString(CultureInfo.InvariantCulture),
            ["total_bytes"] = content.Length.ToString(CultureInfo.InvariantCulture),
            ["read_bytes"] = content.Length.ToString(CultureInfo.InvariantCulture),
            ["offset"] = offset?.ToString(CultureInfo.InvariantCulture) ?? "0",
            ["is_session_memory"] = isSessionMemory.ToString(),
            ["is_session_transcript"] = isSessionTranscript.ToString(),
        };
        if (limit.HasValue) {
            tags["limit"] = limit.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (analyticsExt is not null) {
            tags["ext"] = analyticsExt;
        }

        _telemetry.RecordCount("file.read.detail", tags, description: "File read detail telemetry");

        var pathHash = SecurityPatterns.ComputeShortHash(filePath);
        _telemetry.RecordCount("file.operation",
            new Dictionary<string, string> { ["operation"] = FileOperationTypeEnumConstants.Read, ["path_hash"] = pathHash },
            description: "File operation with path hash");
    }

    /// <summary>
    /// 记录 PDF 读取遥测。
    /// 对齐 TS: tengu_pdf_page_extraction — PDF 页面提取事件
    /// </summary>
    public void RecordPdfReadTelemetry(string filePath, long fileSize, bool success) {
        if (_telemetry is null) return;

        var tags = new Dictionary<string, string> {
            ["success"] = success.ToString(),
            ["file_size"] = fileSize.ToString(CultureInfo.InvariantCulture),
        };

        _telemetry.RecordCount("file.read.pdf", tags, description: "PDF read telemetry");
    }

    /// <summary>
    /// 记录文件操作遥测（路径哈希脱敏）。
    /// 对齐 TS: tengu_file_operation — 通用文件操作事件
    /// </summary>
    public void RecordFileOperationTelemetry(string filePath, string operation) {
        if (_telemetry is null) return;

        var pathHash = SecurityPatterns.ComputeShortHash(filePath);
        _telemetry.RecordCount("file.operation.hash",
            new Dictionary<string, string> { ["operation"] = operation, ["path_hash"] = pathHash },
            description: "File operation with path hash");
    }
}