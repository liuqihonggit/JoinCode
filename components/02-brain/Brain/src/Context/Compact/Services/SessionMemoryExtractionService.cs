
namespace Core.Context.Compact;

/// <summary>
/// 会话记忆提取服务实现 — 对齐 TS sessionMemory.ts::extractSessionMemory
/// 核心消费点：SessionMemoryPromptTemplate.BuildSessionMemoryUpdatePrompt()
/// </summary>
[Register]
public sealed partial class SessionMemoryExtractionService : ISessionMemoryExtractionService
{
    private readonly ISessionMemoryCompactService _compactService;
    private readonly IFileSystem _fileSystem;
    private readonly SessionMemoryCompactConfig _config;
    private int _tokensAtLastExtraction;

    public SessionMemoryExtractionService(
        ISessionMemoryCompactService compactService,
        IFileSystem fileSystem,
        IOptions<SessionMemoryCompactConfig>? config = null)
    {
        _compactService = compactService ?? throw new ArgumentNullException(nameof(compactService));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _config = config?.Value ?? SessionMemoryCompactConfig.Default;
    }

    /// <inheritdoc />
    public async Task<string> InitializeSessionMemoryFileAsync(CancellationToken cancellationToken = default)
    {
        var path = GetMemoryFilePath();
        var dir = Path.GetDirectoryName(path)!;

        if (!_fileSystem.DirectoryExists(dir))
        {
            _fileSystem.CreateDirectory(dir);
        }

        if (!_fileSystem.FileExists(path))
        {
            var template = SessionMemoryPromptTemplate.DefaultSessionMemoryTemplate;
            await _fileSystem.WriteAllTextAsync(path, template, cancellationToken).ConfigureAwait(false);
            await _compactService.UpdateSessionMemoryAsync(template, cancellationToken).ConfigureAwait(false);
            return template;
        }

        var content = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        await _compactService.UpdateSessionMemoryAsync(content, cancellationToken).ConfigureAwait(false);
        return content;
    }

    /// <inheritdoc />
    public async Task<string> BuildExtractionPromptAsync(CancellationToken cancellationToken = default)
    {
        var path = GetMemoryFilePath();
        var currentNotes = _fileSystem.FileExists(path)
            ? await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : SessionMemoryPromptTemplate.DefaultSessionMemoryTemplate;

        return SessionMemoryPromptTemplate.BuildSessionMemoryUpdatePrompt(currentNotes, path);
    }

    /// <inheritdoc />
    public bool ShouldExtract(int currentTokenCount, int toolCallsSinceLastUpdate)
    {
        if (_tokensAtLastExtraction == 0)
        {
            return currentTokenCount >= _config.MinMessageTokensToInit;
        }

        var tokensSinceLastExtraction = currentTokenCount - _tokensAtLastExtraction;
        return tokensSinceLastExtraction >= _config.MinTokensBetweenUpdate
            || toolCallsSinceLastUpdate >= _config.ToolCallsBetweenUpdates;
    }

    /// <inheritdoc />
    public string GetMemoryFilePath()
    {
        var cwd = _fileSystem.GetCurrentDirectory();
        return Path.Combine(cwd, ".jcc", "session-memory.md");
    }

    /// <inheritdoc />
    public void RecordExtractionCompleted(int tokenCountAtExtraction)
    {
        _tokensAtLastExtraction = tokenCountAtExtraction;
    }
}
