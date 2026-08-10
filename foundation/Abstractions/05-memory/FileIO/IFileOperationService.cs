namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件操作服务接口，提供高层文件读写功能（搜索替换、行号编辑、元数据读写）
/// 关系: 本接口的底层方法（FileExists/DirectoryExists/GetCurrentDirectory 等）委托给 IFileSystem 实现
/// 调用方对简单文件操作应优先注入 IFileSystem，仅需要编辑/元数据功能时才使用本接口
/// </summary>
public interface IFileOperationService
{
    /// <summary>
    /// 读取文件内容（含行号范围支持）
    /// </summary>
    Task<FileReadResult> ReadFileAsync(
        string filePath,
        int? offset = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入文件内容
    /// </summary>
    Task<FileWriteResult> WriteFileAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 编辑文件内容（搜索替换）
    /// </summary>
    Task<FileEditResult> EditFileAsync(
        string filePath,
        string oldString,
        string newString,
        bool replaceAll = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于行号范围编辑文件内容
    /// </summary>
    Task<FileLineEditResult> EditByLineRangeAsync(
        LineRangeEditRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取文件并返回元数据（编码 + 换行符） — 对齐 TS: readFileSyncWithMetadata
    /// </summary>
    Task<FileMetadataResult> ReadFileWithMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以指定编码和换行符写入文件 — 对齐 TS: writeTextContent
    /// </summary>
    Task<FileWriteResult> WriteFileWithEncodingAsync(
        string filePath,
        string content,
        System.Text.Encoding? encoding = null,
        string? lineEndings = null,
        CancellationToken cancellationToken = default);

    // --- 以下方法委托给 IFileSystem，仅保留以兼容现有消费方 ---

    /// <summary>委托 IFileSystem.FileExists — 优先直接使用 IFileSystem</summary>
    bool FileExists(string filePath);

    /// <summary>委托 IFileSystem.FileExistsAsync — 优先直接使用 IFileSystem</summary>
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem.DirectoryExists — 优先直接使用 IFileSystem</summary>
    bool DirectoryExists(string directoryPath);

    /// <summary>委托 IFileSystem.DirectoryExistsAsync — 优先直接使用 IFileSystem</summary>
    Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem.CreateDirectory — 优先直接使用 IFileSystem</summary>
    DirectoryInfo CreateDirectory(string directoryPath);

    /// <summary>委托 IFileSystem — 优先直接使用 IFileSystem</summary>
    Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem — 高层封装，含 FileEntry 元数据</summary>
    Task<DirectoryListResult> ListDirectoryAsync(string directoryPath, bool recursive = false, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem.CopyFileAsync — 优先直接使用 IFileSystem</summary>
    Task<bool> CopyFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem.MoveFileAsync — 优先直接使用 IFileSystem</summary>
    Task<bool> MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem.CreateSymbolicLink — 优先直接使用 IFileSystem</summary>
    bool CreateSymbolicLink(string linkPath, string targetPath);

    /// <summary>委托 IFileSystem — 优先直接使用 IFileSystem</summary>
    DateTime GetDirectoryLastWriteTimeUtc(string directoryPath);

    /// <summary>委托 IFileSystem — 优先直接使用 IFileSystem</summary>
    void SetDirectoryLastWriteTimeUtc(string directoryPath, DateTime utcTime);

    /// <summary>委托 IFileSystem — 优先直接使用 IFileSystem</summary>
    DateTime GetFileLastWriteTime(string filePath);

    /// <summary>委托 IFileSystem — 优先直接使用 IFileSystem</summary>
    Task<DateTime> GetLastWriteTimeUtcAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>委托 IFileSystem.GetCurrentDirectory — 优先直接使用 IFileSystem</summary>
    string GetCurrentDirectory();

    /// <summary>委托 IFileSystem.GetFullPath — 优先直接使用 IFileSystem</summary>
    string GetFullPath(string path);

    /// <summary>委托 IFileSystem.CombinePath — 优先直接使用 IFileSystem</summary>
    string CombinePath(params string[] paths);

    /// <summary>委托 IFileSystem.EnumerateFiles — 优先直接使用 IFileSystem</summary>
    IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern, SearchOption searchOption);

    /// <summary>委托 IFileSystem.EnumerateDirectories — 优先直接使用 IFileSystem</summary>
    IEnumerable<string> EnumerateDirectories(string directoryPath, string searchPattern, SearchOption searchOption);

    /// <summary>委托 IFileSystem.GetFiles — 优先直接使用 IFileSystem</summary>
    string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption);

    /// <summary>委托 IFileSystem.GetDirectories — 优先直接使用 IFileSystem</summary>
    string[] GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption);
}

// Result records
public sealed record FileReadResult
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public required int NumLines { get; init; }
    public required int StartLine { get; init; }
    public required int TotalLines { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static FileReadResult SuccessResult(string filePath, string content, int numLines, int startLine, int totalLines)
        => new()
        {
            FilePath = filePath,
            Content = content,
            NumLines = numLines,
            StartLine = startLine,
            TotalLines = totalLines,
            Success = true
        };

    public static FileReadResult FailureResult(string filePath, string errorMessage)
        => new()
        {
            FilePath = filePath,
            Content = string.Empty,
            NumLines = 0,
            StartLine = 0,
            TotalLines = 0,
            Success = false,
            ErrorMessage = errorMessage
        };
}

public sealed record FileWriteResult
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public required string Operation { get; init; }
    public string? OriginalContent { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 结构化 Patch — 对齐 TS FileWriteOutput.structuredPatch
    /// 更新文件时从 OriginalContent/Content 生成，创建文件时为空数组
    /// </summary>
    public IEnumerable<StructuredPatchHunk> StructuredPatch { get; init; } = Array.Empty<StructuredPatchHunk>();

    public static FileWriteResult SuccessResult(
        string filePath,
        string content,
        string operation,
        string? originalContent = null,
        IEnumerable<StructuredPatchHunk>? structuredPatch = null)
        => new()
        {
            FilePath = filePath,
            Content = content,
            Operation = operation,
            OriginalContent = originalContent,
            Success = true,
            StructuredPatch = structuredPatch ?? []
        };

    public static FileWriteResult FailureResult(string filePath, string errorMessage)
        => new()
        {
            FilePath = filePath,
            Content = string.Empty,
            Operation = string.Empty,
            Success = false,
            ErrorMessage = errorMessage
        };
}

public sealed record FileEditResult
{
    public required string FilePath { get; init; }
    public required string OldString { get; init; }
    public required string NewString { get; init; }
    public required string OriginalContent { get; init; }
    public required string UpdatedContent { get; init; }
    public required int ReplaceCount { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 结构化诊断信息 — 匹配失败时填充，GUI 可分区域渲染。
    /// </summary>
    public ToolDiagnostic? Diagnostic { get; init; }

    /// <summary>
    /// 结构化 Patch — 对齐 TS FileEditOutput.structuredPatch
    /// 由 StructuredPatchGenerator 从 OriginalContent/UpdatedContent 生成
    /// </summary>
    public IEnumerable<StructuredPatchHunk> StructuredPatch { get; init; } = Array.Empty<StructuredPatchHunk>();

    public static FileEditResult SuccessResult(
        string filePath,
        string oldString,
        string newString,
        string originalContent,
        string updatedContent,
        int replaceCount,
        IEnumerable<StructuredPatchHunk>? structuredPatch = null)
        => new()
        {
            FilePath = filePath,
            OldString = oldString,
            NewString = newString,
            OriginalContent = originalContent,
            UpdatedContent = updatedContent,
            ReplaceCount = replaceCount,
            Success = true,
            StructuredPatch = structuredPatch ?? []
        };

    public static FileEditResult FailureResult(string filePath, string oldString, string newString, string errorMessage)
        => new()
        {
            FilePath = filePath,
            OldString = oldString,
            NewString = newString,
            OriginalContent = string.Empty,
            UpdatedContent = string.Empty,
            ReplaceCount = 0,
            Success = false,
            ErrorMessage = errorMessage
        };

    public static FileEditResult FailureResult(string filePath, string oldString, string newString, ToolDiagnostic diagnostic)
        => new()
        {
            FilePath = filePath,
            OldString = oldString,
            NewString = newString,
            OriginalContent = string.Empty,
            UpdatedContent = string.Empty,
            ReplaceCount = 0,
            Success = false,
            ErrorMessage = diagnostic.FormattedMessage,
            Diagnostic = diagnostic
        };
}

public sealed record LineRangeEditRequest
{
    public string FilePath { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public string NewContent { get; }

    public LineRangeEditRequest(string filePath, int startLine, int endLine, string newContent)
    {
        FilePath = filePath;
        StartLine = startLine;
        EndLine = endLine;
        NewContent = newContent;
    }
}

public sealed record FileLineEditResult
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string OriginalContent { get; init; }
    public required string NewContent { get; init; }
    public required string UpdatedFileContent { get; init; }
    public required int ReplacedLinesCount { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 结构化诊断信息 — 失败时填充。
    /// </summary>
    public ToolDiagnostic? Diagnostic { get; init; }

    public static FileLineEditResult SuccessResult(
        string filePath,
        int startLine,
        int endLine,
        string originalContent,
        string newContent,
        string updatedFileContent,
        int replacedLinesCount)
        => new()
        {
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            OriginalContent = originalContent,
            NewContent = newContent,
            UpdatedFileContent = updatedFileContent,
            ReplacedLinesCount = replacedLinesCount,
            Success = true
        };

    public static FileLineEditResult FailureResult(
        string filePath,
        int startLine,
        int endLine,
        string errorMessage)
        => new()
        {
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            OriginalContent = string.Empty,
            NewContent = string.Empty,
            UpdatedFileContent = string.Empty,
            ReplacedLinesCount = 0,
            Success = false,
            ErrorMessage = errorMessage
        };

    public static FileLineEditResult FailureResult(
        string filePath,
        int startLine,
        int endLine,
        ToolDiagnostic diagnostic)
        => new()
        {
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            OriginalContent = string.Empty,
            NewContent = string.Empty,
            UpdatedFileContent = string.Empty,
            ReplacedLinesCount = 0,
            Success = false,
            ErrorMessage = diagnostic.FormattedMessage,
            Diagnostic = diagnostic
        };
}

public sealed record DirectoryListResult
{
    public required string DirectoryPath { get; init; }
    public required IReadOnlyList<FileEntry> Files { get; init; }
    public required IReadOnlyList<DirectoryEntry> Directories { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DirectoryListResult SuccessResult(
        string directoryPath,
        IReadOnlyList<FileEntry> files,
        IReadOnlyList<DirectoryEntry> directories)
        => new()
        {
            DirectoryPath = directoryPath,
            Files = files,
            Directories = directories,
            Success = true
        };

    public static DirectoryListResult FailureResult(string directoryPath, string errorMessage)
        => new()
        {
            DirectoryPath = directoryPath,
            Files = Array.Empty<FileEntry>(),
            Directories = Array.Empty<DirectoryEntry>(),
            Success = false,
            ErrorMessage = errorMessage
        };
}

public sealed record FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required long Size { get; init; }
    public required DateTime LastModified { get; init; }
}

public sealed record DirectoryEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required DateTime LastModified { get; init; }
}

/// <summary>
/// 文件元数据读取结果 — 对齐 TS: readFileSyncWithMetadata 返回值
/// </summary>
public sealed record FileMetadataResult
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    /// <summary>
    /// 检测到的文件编码 — 对齐 TS: encoding (BufferEncoding)
    /// </summary>
    public required System.Text.Encoding Encoding { get; init; }
    /// <summary>
    /// 检测到的换行符类型 — "LF" 或 "CRLF"，对齐 TS: LineEndingType
    /// </summary>
    public required string LineEndings { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static FileMetadataResult SuccessResult(
        string filePath,
        string content,
        System.Text.Encoding encoding,
        string lineEndings)
        => new()
        {
            FilePath = filePath,
            Content = content,
            Encoding = encoding,
            LineEndings = lineEndings,
            Success = true
        };

    public static FileMetadataResult FailureResult(string filePath, string errorMessage)
        => new()
        {
            FilePath = filePath,
            Content = string.Empty,
            Encoding = System.Text.Encoding.UTF8,
            LineEndings = "LF",
            Success = false,
            ErrorMessage = errorMessage
        };
}
