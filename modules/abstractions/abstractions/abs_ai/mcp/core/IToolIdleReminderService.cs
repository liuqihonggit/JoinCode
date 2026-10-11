namespace JoinCode.Abstractions.Interfaces;

/// <summary>工具空闲提醒服务接口。</summary>
public interface IToolIdleReminderService {
    /// <summary>记录助手回合（含使用的工具名）。</summary>
    void RecordAssistantTurn(string? toolNameUsed = null);

    /// <summary>异步检查并生成空闲提醒。</summary>
    Task<IReadOnlyList<ToolIdleReminderResult>> CheckAndGenerateRemindersAsync(CancellationToken ct = default);

    /// <summary>重置状态。</summary>
    void Reset();
}

/// <summary>工具空闲提醒结果。</summary>
public sealed record ToolIdleReminderResult(string ToolName, string Message);
