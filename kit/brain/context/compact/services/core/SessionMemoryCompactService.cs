
namespace Core.Context.Compact;

/// <summary>
/// 会话记忆压缩服务实现 — 对齐 TS sessionMemory.ts
/// 基于持久化的会话记忆文件生成压缩摘要，避免丢失长期上下文
/// </summary>
[Register(typeof(ISessionMemoryCompactService), ServiceLifetime.Singleton)]
public sealed partial class SessionMemoryCompactService : ServiceEntity, ISessionMemoryCompactService
{
    private static readonly string SessionMemorySubdir = AppDataConstants.AppDataFolder;
    private const string SessionMemoryFileName = "session-memory.md";

    private readonly SessionMemoryCompactConfig _config;
    private readonly IMicrocompactService _microcompactService;
    private readonly IFileSystem? _fileSystem;
    private string? _cachedMemoryContent;
    private string? _memoryFilePath;
    private int _tokensAtLastExtraction;

    /// <summary>
    /// 初始化 <see cref="SessionMemoryCompactService"/> 实例
    /// </summary>
    /// <param name="microcompactService">微压缩服务，用于 token 估算</param>
    /// <param name="config">可选配置选项，null 时使用默认配置</param>
    /// <param name="fileSystem">可选文件系统抽象，为 null 时仅使用内存缓存</param>
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
            ?? throw new InvalidOperationException("[BRN001] IFileSystem 未注入，无法确定会话记忆文件路径");

        return Path.Combine(cwd, SessionMemorySubdir, SessionMemoryFileName);
    }

    /// <summary>
    /// 尝试基于会话记忆执行压缩 — 对齐 TS trySessionMemoryCompact
    /// 仅当会话记忆非空且压缩后 token 数低于阈值时返回结果
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="autoCompactThreshold">自动压缩阈值，0 表示不检查</param>
    /// <param name="transcriptPath">可选的转录文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果；不满足条件时返回 null</returns>
    public async Task<CompactResult?> TrySessionMemoryCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        int autoCompactThreshold = 0,
        string? transcriptPath = null,
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
            transcriptPath: transcriptPath,
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

    /// <summary>
    /// 检查会话记忆是否可用（存在且非空）
    /// </summary>
    /// <returns>可用返回 true，否则 false</returns>
    public async Task<bool> IsSessionMemoryAvailableAsync()
    {
        var content = await GetSessionMemoryContentAsync().ConfigureAwait(false);
        return !string.IsNullOrEmpty(content) && !IsSessionMemoryEmpty(content);
    }

    /// <summary>
    /// 获取会话记忆内容 — 优先返回缓存，其次从文件读取
    /// </summary>
    /// <returns>会话记忆内容；不存在时返回 null</returns>
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

    /// <summary>
    /// 更新会话记忆内容 — 同步更新缓存和持久化文件
    /// </summary>
    /// <param name="content">新的会话记忆内容</param>
    /// <param name="cancellationToken">取消令牌</param>
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
