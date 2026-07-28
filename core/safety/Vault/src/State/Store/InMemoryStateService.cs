namespace State;

/// <summary>
/// 内存状态服务 — 纯内存，0磁盘IO，调试/E2E测试用
/// 通过 JCC_STATE_MODE=InMemory 环境变量激活
/// </summary>
public sealed class InMemoryStateService : IStateService, IDisposable
{
    private readonly ConcurrentDictionary<string, SessionState> _storage = new();
    private readonly IClockService _clock;
    private const string StateId = "current";

    public InMemoryStateService(IClockService clock)
    {
        _clock = clock;
    }

    public void SaveState(string systemPrompt, MessageList chatHistory)
    {
        var chatHistoryList = chatHistory
            .Select(m => new ApiMessageState
            {
                Role = m.Role.ToValue(),
                Content = m.Content ?? string.Empty,
                Timestamp = _clock.GetUtcNow(),
                Metadata = SerializeMetadata(m.Metadata)
            })
            .ToImmutableList();

        _storage[StateId] = new SessionState
        {
            SystemPrompt = systemPrompt,
            MessageList = chatHistoryList,
            LastActivityAt = _clock.GetUtcNow()
        };
    }

    public Task SaveStateAsync(string systemPrompt, MessageList chatHistory, CancellationToken cancellationToken = default)
    {
        SaveState(systemPrompt, chatHistory);
        return Task.CompletedTask;
    }

    public (string SystemPrompt, MessageList MessageList) LoadState()
    {
        if (!_storage.TryGetValue(StateId, out var sessionState))
            return (string.Empty, new MessageList());

        var chatHistory = new MessageList();
        var seenContent = new HashSet<string>(StringComparer.Ordinal);
        var rolePriority = new Dictionary<MessageRole, int>
        {
            [MessageRole.System] = 0,
            [MessageRole.User] = 1,
            [MessageRole.Assistant] = 2,
            [MessageRole.Tool] = 3,
        };

        foreach (var message in sessionState.MessageList)
        {
            var role = MessageRoleExtensions.FromValue(message.Role) ?? MessageRole.User;
            var metadata = DeserializeMetadata(message.Metadata);
            var content = message.Content ?? string.Empty;
            var isToolMessage = role == MessageRole.Tool;

            if (!string.IsNullOrEmpty(content) && seenContent.Contains(content))
            {
                var replaced = false;
                for (var i = 0; i < chatHistory.Count; i++)
                {
                    if ((chatHistory[i].Content ?? string.Empty) == content)
                    {
                        var existingPriority = rolePriority.GetValueOrDefault(chatHistory[i].Role, 0);
                        var newPriority = rolePriority.GetValueOrDefault(role, 0);
                        if (newPriority > existingPriority)
                        {
                            chatHistory[i] = new ApiMessage(role, content, metadata);
                            replaced = true;
                        }
                        break;
                    }
                }
                if (!isToolMessage || replaced)
                    continue;
            }

            if (!string.IsNullOrEmpty(content) && !seenContent.Contains(content))
                seenContent.Add(content);
            chatHistory.Add(new ApiMessage(role, content, metadata));
        }

        return (sessionState.SystemPrompt, chatHistory);
    }

    public Task<(string SystemPrompt, MessageList MessageList)> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(LoadState());
    }

    public bool ClearState() => _storage.TryRemove(StateId, out _);

    public Task<bool> ClearStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ClearState());

    public void Dispose() => _storage.Clear();

    private static ImmutableDictionary<string, string> SerializeMetadata(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var kvp in metadata)
            builder[kvp.Key] = kvp.Value.GetRawText();
        return builder.ToImmutable();
    }

    private static IReadOnlyDictionary<string, JsonElement>? DeserializeMetadata(IReadOnlyDictionary<string, string>? stored)
    {
        if (stored is null || stored.Count == 0) return null;

        var dict = new Dictionary<string, JsonElement>(stored.Count);
        foreach (var kvp in stored)
            dict[kvp.Key] = JsonDocument.Parse(kvp.Value).RootElement.Clone();
        return dict;
    }
}
