namespace JoinCode.Abstractions.LLM.Chat;

public interface IToolResultFileService
{
    PersistedToolResult PersistToolResult(string sessionId, string toolUseId, string content);
    Task<PersistedToolResult> PersistToolResultAsync(string sessionId, string toolUseId, string content, CancellationToken cancellationToken = default);
    string? ReadToolResult(string sessionId, string toolUseId);
}
