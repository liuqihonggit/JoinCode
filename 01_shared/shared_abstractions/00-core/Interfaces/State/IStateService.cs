
namespace JoinCode.Abstractions.Interfaces;

public interface IStateService {
    void SaveState(string systemPrompt, MessageList chatHistory);
    Task SaveStateAsync(string systemPrompt, MessageList chatHistory, CancellationToken cancellationToken = default);
    (string SystemPrompt, MessageList MessageList) LoadState();
    Task<(string SystemPrompt, MessageList MessageList)> LoadStateAsync(CancellationToken cancellationToken = default);
    bool ClearState();
    Task<bool> ClearStateAsync(CancellationToken cancellationToken = default);
}
