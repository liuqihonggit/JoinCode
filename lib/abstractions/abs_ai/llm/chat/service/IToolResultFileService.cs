namespace JoinCode.Abstractions.LLM.Chat;

public interface IToolResultFileService {
    /// <summary>持久化工具调用结果到文件。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="toolUseId">工具调用标识。</param>
    /// <param name="content">结果内容。</param>
    PersistedToolResult PersistToolResult(string sessionId, string toolUseId, string content);
    /// <summary>异步持久化工具调用结果到文件。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="toolUseId">工具调用标识。</param>
    /// <param name="content">结果内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PersistedToolResult> PersistToolResultAsync(string sessionId, string toolUseId, string content, CancellationToken cancellationToken = default);
    /// <summary>读取工具调用结果文件内容。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="toolUseId">工具调用标识。</param>
    string? ReadToolResult(string sessionId, string toolUseId);
}