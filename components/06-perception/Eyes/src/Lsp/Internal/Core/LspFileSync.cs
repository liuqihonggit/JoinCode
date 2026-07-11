

namespace Services.Lsp.Internal;

/// <summary>
/// LSP 文件同步实现
/// 对齐 TS LSPServerManager 的 changeFile/saveFile/openFile/closeFile
/// </summary>
[Register]
public sealed partial class LspFileSync : ILspFileSync
{
    [Inject] private readonly ILspManager _lspManager;
    [Inject] private readonly ILogger<LspFileSync>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, OpenDocumentInfo> _openDocuments = new();

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    /// <inheritdoc />
    public async Task OpenDocumentAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default)
    {
        // 对齐 TS openFile: 如果文件已打开则跳过（幂等性）
        if (_openDocuments.ContainsKey(filePath))
        {
            _logger?.LogDebug("File already open, skipping didOpen for: {FilePath}", filePath);
            return;
        }

        var documentInfo = new OpenDocumentInfo
        {
            FilePath = filePath,
            LanguageId = languageId,
            Version = 1,
            Content = content
        };

        _openDocuments[filePath] = documentInfo;

        try
        {
            await _lspManager.OpenFileAsync(filePath, content, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Opened document: {FilePath} ({LanguageId})", filePath, languageId);
            OnDocumentChanged(filePath, DocumentChangeKind.Opened);
            RecordFileSyncMetrics("open", true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open document for: {FilePath}", filePath);
            RecordFileSyncMetrics("open", false);
        }
    }

    /// <inheritdoc />
    public async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!_openDocuments.TryRemove(filePath, out _))
        {
            return;
        }

        try
        {
            await _lspManager.CloseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Closed document: {FilePath}", filePath);
            OnDocumentChanged(filePath, DocumentChangeKind.Closed);
            RecordFileSyncMetrics("close", true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to close document for: {FilePath}", filePath);
            RecordFileSyncMetrics("close", false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 对齐 TS changeFile: 若文件未打开，自动回退为 OpenDocumentAsync（didOpen 回退逻辑）
    /// LSP 协议要求必须先 didOpen 才能 didChange，此回退逻辑保证调用安全
    /// </remarks>
    public async Task ChangeDocumentAsync(string filePath, IEnumerable<TextDocumentContentChangeEvent> changes, CancellationToken cancellationToken = default)
    {
        // 对齐 TS changeFile: 文件未打开时自动回退到 openFile
        if (!_openDocuments.TryGetValue(filePath, out var documentInfo))
        {
            _logger?.LogDebug("File not open, falling back to didOpen for: {FilePath}", filePath);
            // 从 changes 中提取完整内容（全文替换场景）
            var fullContent = changes.FirstOrDefault()?.Text;
            if (fullContent is not null)
            {
                var languageId = GetLanguageIdFromPath(filePath);
                await OpenDocumentAsync(filePath, languageId, fullContent, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        documentInfo.Version++;
        documentInfo.LastModifiedAt = _clock.GetUtcNow();

        foreach (var change in changes)
        {
            if (change.Range == null)
            {
                documentInfo.Content = change.Text;
            }
            else
            {
                documentInfo.Content = ApplyChange(documentInfo.Content, change);
            }
        }

        try
        {
            await _lspManager.ChangeFileAsync(filePath, documentInfo.Content, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Changed document: {FilePath} (version {Version})", filePath, documentInfo.Version);
            OnDocumentChanged(filePath, DocumentChangeKind.Changed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to change document for: {FilePath}", filePath);
        }
    }

    /// <inheritdoc />
    public async Task SaveDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // 对齐 TS saveFile: 文件未打开时静默返回（不自动回退到 openFile）
        if (!_openDocuments.ContainsKey(filePath))
        {
            _logger?.LogDebug("Cannot save document that is not open, skipping: {FilePath}", filePath);
            return;
        }

        try
        {
            await _lspManager.SaveFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Saved document: {FilePath}", filePath);
            OnDocumentChanged(filePath, DocumentChangeKind.Saved);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save document for: {FilePath}", filePath);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, OpenDocumentInfo> GetOpenDocuments()
    {
        return _openDocuments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <inheritdoc />
    public bool IsDocumentOpen(string filePath)
    {
        return _openDocuments.ContainsKey(filePath);
    }

    /// <summary>
    /// 根据文件路径推断语言 ID
    /// 对齐 TS openFile: server.config.extensionToLanguage[ext] || 'plaintext'
    /// </summary>
    private static string GetLanguageIdFromPath(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".ts" => "typescript",
            ".tsx" => "typescriptreact",
            ".js" => "javascript",
            ".jsx" => "javascriptreact",
            ".py" => "python",
            ".rs" => "rust",
            ".go" => "go",
            ".java" => "java",
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            ".xml" => "xml",
            ".html" => "html",
            ".css" => "css",
            ".md" => "markdown",
            _ => "plaintext"
        };
    }

    /// <summary>
    /// 触发文档变更事件
    /// </summary>
    private void OnDocumentChanged(string filePath, DocumentChangeKind changeKind)
    {
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs
        {
            FilePath = filePath,
            ChangeKind = changeKind
        });
    }

    /// <summary>
    /// 记录文件同步指标
    /// </summary>
    private void RecordFileSyncMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("lsp.filesync.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "LSP file sync operation count");

    private static string ApplyChange(string content, TextDocumentContentChangeEvent change)
    {
        if (change.Range == null)
        {
            return change.Text;
        }

        var lines = content.Split('\n');
        var startLine = change.Range.Start.Line;
        var startChar = change.Range.Start.Character;
        var endLine = change.Range.End.Line;
        var endChar = change.Range.End.Character;

        if (startLine < 0 || startLine >= lines.Length || endLine < 0 || endLine >= lines.Length)
        {
            return content;
        }

        var before = string.Join('\n', lines.Take(startLine)) + (startLine > 0 ? "\n" : "") + lines[startLine][..Math.Min(startChar, lines[startLine].Length)];
        var after = lines[endLine][Math.Min(endChar, lines[endLine].Length)..] + (endLine < lines.Length - 1 ? "\n" : "") + string.Join('\n', lines.Skip(endLine + 1));

        return before + change.Text + after;
    }
}
