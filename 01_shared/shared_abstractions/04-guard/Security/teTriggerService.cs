namespace JoinCode.Abstractions.Interfaces;

public enum TriggerAction { List, Get, Create, Update, Run }

public sealed class TriggerResult
{
    public required int Status { get; init; }
    public required string Json { get; init; }
}

public interface IRemoteTriggerService
{
    Task<TriggerResult> ExecuteAsync(TriggerAction action, string? triggerId = null, string? body = null, CancellationToken ct = default);
}
