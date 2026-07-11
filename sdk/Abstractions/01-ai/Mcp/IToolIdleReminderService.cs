namespace JoinCode.Abstractions.Interfaces;

public interface IToolIdleReminderService
{
    void RecordAssistantTurn(string? toolNameUsed = null);

    Task<IReadOnlyList<ToolIdleReminderResult>> CheckAndGenerateRemindersAsync(CancellationToken ct = default);

    void Reset();
}

public sealed record ToolIdleReminderResult(string ToolName, string Message);
