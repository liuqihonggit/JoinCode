
namespace Core.Tests.Services;

/// <summary>
/// 内存状态服务 - 用于高速测试，不使用真实数据库
/// </summary>
public sealed class InMemoryStateService : IStateService, IDisposable
{
    private readonly ConcurrentDictionary<string, SessionState> _storage = new();
    private const string StateId = "current";

    public void SaveState(string systemPrompt, MessageList chatHistory)
    {
        var chatHistoryList = chatHistory
            .Select(m => new ApiMessageState
            {
                // 使用 ToValue() 保存小写角色名（对齐 [EnumValue] 定义），
                // 避免 ToString() 返回 PascalCase 导致 FromValue() 反查失败
                Role = m.Role.ToValue(),
                Content = m.Content ?? string.Empty,
                Timestamp = DateTime.UtcNow,
                // 保存 Metadata（JsonElement → string），避免工具调用信息丢失导致前缀缓存破坏
                Metadata = SerializeMetadata(m.Metadata)
            })
            .ToImmutableList();

        var sessionState = new SessionState
        {
            SystemPrompt = systemPrompt,
            MessageList = chatHistoryList,
            LastActivityAt = DateTime.UtcNow
        };

        _storage[StateId] = sessionState;
    }

    /// <summary>
    /// 将 ApiMessage.Metadata 序列化为 string 字典（对齐 StateService.SerializeMetadata）
    /// </summary>
    private static ImmutableDictionary<string, string> SerializeMetadata(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var kvp in metadata)
        {
            builder[kvp.Key] = kvp.Value.GetRawText();
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// 将 string 字典反序列化为 ApiMessage.Metadata（对齐 StateService.DeserializeMetadata）
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement>? DeserializeMetadata(IReadOnlyDictionary<string, string>? stored)
    {
        if (stored is null || stored.Count == 0) return null;

        var dict = new Dictionary<string, JsonElement>(stored.Count);
        foreach (var kvp in stored)
        {
            dict[kvp.Key] = JsonDocument.Parse(kvp.Value).RootElement.Clone();
        }
        return dict;
    }

    public Task SaveStateAsync(string systemPrompt, MessageList chatHistory, CancellationToken cancellationToken = default)
    {
        SaveState(systemPrompt, chatHistory);
        return Task.CompletedTask;
    }

    public (string SystemPrompt, MessageList MessageList) LoadState()
    {
        if (_storage.TryGetValue(StateId, out var sessionState))
        {
            var chatHistory = new MessageList();
            // 去重: 基于非空 Content 去重，相同内容保留 Role 优先级更高的版本（Tool > Assistant > User > System）
            // 原因: 早期版本可能把 Tool 消息保存为 User 角色，导致同一内容出现两次（user + tool）
            // 注意: 空 content 不去重（assistant 工具调用消息 content 为 null/空，但有不同 metadata）
            var seenContent = new HashSet<string>(StringComparer.Ordinal);
            var rolePriority = new Dictionary<JoinCode.Abstractions.LLM.Chat.MessageRole, int>
            {
                [JoinCode.Abstractions.LLM.Chat.MessageRole.System] = 0,
                [JoinCode.Abstractions.LLM.Chat.MessageRole.User] = 1,
                [JoinCode.Abstractions.LLM.Chat.MessageRole.Assistant] = 2,
                [JoinCode.Abstractions.LLM.Chat.MessageRole.Tool] = 3,
            };

            foreach (var message in sessionState.MessageList)
            {
                // FromValue 返回可空，未知角色回退为 User（对齐原 default 分支行为）
                var role = JoinCode.Abstractions.LLM.Chat.MessageRoleExtensions.FromValue(message.Role) ?? JoinCode.Abstractions.LLM.Chat.MessageRole.User;
                // 恢复 Metadata（string → JsonElement），避免工具调用信息丢失导致前缀缓存破坏
                var metadata = DeserializeMetadata(message.Metadata);

                var content = message.Content ?? string.Empty;
                var isToolMessage = role == JoinCode.Abstractions.LLM.Chat.MessageRole.Tool;
                // 只对非空 content 去重（空 content 的 assistant 工具调用消息有不同 metadata，不应合并）
                if (!string.IsNullOrEmpty(content) && seenContent.Contains(content))
                {
                    // 已存在相同内容：检查是否需要替换为更高优先级的角色
                    var replaced = false;
                    for (var i = 0; i < chatHistory.Count; i++)
                    {
                        if ((chatHistory[i].Content ?? string.Empty) == content)
                        {
                            var existingPriority = rolePriority.GetValueOrDefault(chatHistory[i].Role, 0);
                            var newPriority = rolePriority.GetValueOrDefault(role, 0);
                            if (newPriority > existingPriority)
                            {
                                // 替换为更高优先级的角色（如 Tool 替换 User — 修复 legacy 数据）
                                chatHistory[i] = new ApiMessage(role, content, metadata);
                                replaced = true;
                            }
                            break;
                        }
                    }
                    // Tool 消息只有在未替换低优先级消息时才作为独立消息添加
                    // （同内容不同 tool_call_id 的 Tool 结果必须保留，否则会导致孤立 tool_call → API 400 + 永久数据丢失）
                    // 非 Tool 消息或已替换的消息跳过添加（去重）
                    if (!isToolMessage || replaced)
                        continue;
                    // Tool 消息未替换（如 Tool+Tool 同内容不同 tool_call_id）→ 作为独立消息保留
                }

                if (!string.IsNullOrEmpty(content) && !seenContent.Contains(content))
                    seenContent.Add(content);
                // 直接构造 ApiMessage 保留 Metadata，不使用 AddSystemMessage 等便捷方法（它们不传 Metadata）
                chatHistory.Add(new ApiMessage(role, content, metadata));
            }
            return (sessionState.SystemPrompt, chatHistory);
        }

        return (string.Empty, new MessageList());
    }

    public Task<(string SystemPrompt, MessageList MessageList)> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(LoadState());
    }

    public bool ClearState()
    {
        return _storage.TryRemove(StateId, out _);
    }

    public Task<bool> ClearStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ClearState());
    }

    public void Dispose()
    {
        _storage.Clear();
    }
}
