
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
    (string SystemPrompt, MessageList MessageList) LoadState();
    /// <summary>异步加载状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<(string SystemPrompt, MessageList MessageList)> LoadStateAsync(CancellationToken cancellationToken = default);
    /// <summary>清除状态。</summary>
    bool ClearState();
    /// <summary>异步清除状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> ClearStateAsync(CancellationToken cancellationToken = default);
}