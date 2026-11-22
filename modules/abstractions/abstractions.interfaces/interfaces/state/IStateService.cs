
namespace JoinCode.Abstractions.Interfaces;

public interface IStateService {
    /// <summary>保存状态。</summary>
    /// <param name="systemPrompt">系统提示词。</param>
    /// <param name="chatHistory">聊天历史。</param>
    void SaveState(string systemPrompt, MessageList chatHistory);
    /// <summary>异步保存状态。</summary>
    /// <param name="systemPrompt">系统提示词。</param>
    /// <param name="chatHistory">聊天历史。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveStateAsync(string systemPrompt, MessageList chatHistory, CancellationToken cancellationToken = default);
    /// <summary>加载状态。</summary>
    LoadedChatState LoadState();
    /// <summary>异步加载状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<LoadedChatState> LoadStateAsync(CancellationToken cancellationToken = default);
    /// <summary>清除状态。</summary>
    bool ClearState();
    /// <summary>异步清除状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> ClearStateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 加载出的会话状态 — 系统提示词与还原后的聊天历史。
/// </summary>
/// <param name="SystemPrompt">持久化的系统提示词；无持久化状态时为空串。</param>
/// <param name="MessageList">还原后的聊天历史；无持久化状态时为空列表。</param>
public sealed record LoadedChatState(string SystemPrompt, MessageList MessageList);