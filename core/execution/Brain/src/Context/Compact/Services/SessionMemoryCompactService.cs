
namespace Core.Context.Compact;

[Register]
public sealed partial class SessionMemoryCompactService : ISessionMemoryCompactService
{
    private static readonly string SessionMemorySubdir = AppDataConstants.AppDataFolder;
    private const string SessionMemoryFileName = "session-memory.md";

    private readonly SessionMemoryCompactConfig _config;
    private readonly IMicrocompactService _microcompactService;
    private readonly IFileSystem? _fileSystem;
    private string? _cachedMemoryContent;
    private string? _memoryFilePath;
    private int _tokensAtLastExtraction;

    public SessionMemoryCompactService(
        IMicrocompactService microcompactService,
        IOptions<SessionMemoryCompactConfig>? config = null,
        IFileSystem? fileSystem = null)
    {
        _microcompactService = microcompactService ?? throw new ArgumentNullException(nameof(microcompactService));
        _config = config?.Value ?? SessionMemoryCompactConfig.Default;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// 设置会话记忆文件路径 — 由会话初始化时调用
    /// </summary>
    public void SetMemoryFilePath(string workingDirectory)
    {
        _memoryFilePath = Path.Combine(workingDirectory, SessionMemorySubdir, SessionMemoryFileName);
    }

    /// <summary>
    /// 获取会话记忆文件路径
    /// </summary>
    private string GetMemoryFilePath()
    {
        if (_memoryFilePath is not null) return _memoryFilePath;

        var cwd = _fileSystem?.GetCurrentDirectory()
            ?? throw new InvalidOperationException("IFileSystem 未注入，无法确定会话记忆文件路径");

        return Path.Combine(cwd, SessionMemorySubdir, SessionMemoryFileName);
    }

    public async Task<CompactResult?> TrySessionMemoryCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        int autoCompactThreshold = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var memoryContent = await GetSessionMemoryContentAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(memoryContent))
        {
            return null;
        }

        if (IsSessionMemoryEmpty(memoryContent))
        {
            return null;
        }

        var lastSummarizedIndex = FindLastSummarizedIndex(messages);
        var startIndex = CalculateMessagesToKeepIndex(messages, lastSummarizedIndex);
        var messagesToKeep = messages.Skip(startIndex).ToList();

        var preCompactTokens = _microcompactService.EstimateMessageTokens(messages);
        var truncatedContent = TruncateSessionMemoryForCompact(memoryContent);

        var summaryContent = CompactPromptTemplate.GetCompactUserSummaryMessage(
            truncatedContent,
            suppressFollowUpQuestions: true,
            transcriptPath: null,
            recentMessagesPreserved: true);

        var postCompactTokens = _microcompactService.EstimateMessageTokens(
            [new ApiMessage(MessageRole.User, summaryContent)]);

        if (autoCompactThreshold > 0 && postCompactTokens >= autoCompactThreshold)
        {
            return null;
        }

        _tokensAtLastExtraction = _microcompactService.EstimateMessageTokens(messages);

        return new CompactResult
        {
            Compacted = true,
            Level = CompactLevel.SessionMemoryCompact,
            Trigger = CompactTrigger.Auto,
            Summary = summaryContent,
            PreCompactTokenCount = preCompactTokens,
            PostCompactTokenCount = postCompactTokens,
            MessagesRemoved = messages.Count - messagesToKeep.Count,
            MessagesPreserved = messagesToKeep.Count
        };
    }

    public async Task<bool> IsSessionMemoryAvailableAsync()
    {
        var content = await GetSessionMemoryContentAsync().ConfigureAwait(false);
        return !string.IsNullOrEmpty(content) && !IsSessionMemoryEmpty(content);
    }

    public async Task<string?> GetSessionMemoryContentAsync()
    {
        if (_cachedMemoryContent is not null)
        {
            return _cachedMemoryContent;
        }

        if (_fileSystem is not null)
        {
            var path = GetMemoryFilePath();
            if (_fileSystem.FileExists(path))
            {
                _cachedMemoryContent = await _fileSystem.ReadAllTextAsync(path).ConfigureAwait(false);
                return _cachedMemoryContent;
            }
        }

        return _cachedMemoryContent;
    }

    public async Task UpdateSessionMemoryAsync(string content, CancellationToken cancellationToken = default)
    {
        _cachedMemoryContent = content;

        if (_fileSystem is not null)
        {
            var path = GetMemoryFilePath();
            var dir = Path.GetDirectoryName(path)!;
            if (!_fileSystem.DirectoryExists(dir))
            {
                _fileSystem.CreateDirectory(dir);
            }
            await _fileSystem.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void SetMemoryContent(string content)
    {
        _cachedMemoryContent = content;
    }

    private static bool IsSessionMemoryEmpty(string content)
    {
        var template = SessionMemoryPromptTemplate.DefaultSessionMemoryTemplate;
        var trimmedContent = content.Trim();
        var trimmedTemplate = template.Trim();
        return trimmedContent.Length <= trimmedTemplate.Length;
    }

    private static int FindLastSummarizedIndex(IReadOnlyList<ApiMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Metadata is not null
                && msg.Metadata.TryGetValue("is_compact_boundary", out var val)
                && val.ValueKind == JsonValueKind.True)
            {
                return i;
            }
        }

        return -1;
    }

    private int CalculateMessagesToKeepIndex(IReadOnlyList<ApiMessage> messages, int lastSummarizedIndex)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        var startIndex = lastSummarizedIndex >= 0 ? lastSummarizedIndex + 1 : messages.Count;

        var totalTokens = 0;
        var textBlockMessageCount = 0;

        for (var i = startIndex; i < messages.Count; i++)
        {
            totalTokens += _microcompactService.EstimateMessageTokens([messages[i]]);
            if (messages[i].Role is MessageRole.User or MessageRole.Assistant && !string.IsNullOrEmpty(messages[i].Content))
            {
                textBlockMessageCount++;
            }
        }

        if (totalTokens >= _config.MaxTokens)
        {
            return startIndex;
        }

        if (totalTokens >= _config.MinTokens && textBlockMessageCount >= _config.MinTextBlockMessages)
        {
            return startIndex;
        }

        var floor = lastSummarizedIndex >= 0 ? lastSummarizedIndex + 1 : 0;
        for (var i = startIndex - 1; i >= floor; i--)
        {
            totalTokens += _microcompactService.EstimateMessageTokens([messages[i]]);
            if (messages[i].Role is MessageRole.User or MessageRole.Assistant && !string.IsNullOrEmpty(messages[i].Content))
            {
                textBlockMessageCount++;
            }

            startIndex = i;

            if (totalTokens >= _config.MaxTokens)
            {
                break;
            }

            if (totalTokens >= _config.MinTokens && textBlockMessageCount >= _config.MinTextBlockMessages)
            {
                break;
            }
        }

        return startIndex;
    }

    private string TruncateSessionMemoryForCompact(string content)
    {
        var (truncated, _) = SessionMemoryPromptTemplate.TruncateSessionMemoryForCompact(content);
        return truncated;
    }
}
