namespace JoinCode.Abstractions.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);

    Task NotifyTaskCompletedAsync(string taskId, string description, bool success, CancellationToken cancellationToken = default);

    Task NotifyAgentMessageAsync(string agentId, string agentName, string message, CancellationToken cancellationToken = default);

    bool IsAvailable { get; }
}
