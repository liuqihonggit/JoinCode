namespace JoinCode.Abstractions.Interfaces;

public interface IAgentTaskContext
{
    string TaskId { get; }

    int AgentIndex { get; }

    int TotalAgents { get; }

    string WorkScope { get; }

    string TaskName { get; }

    string Description { get; }

    DateTime CreatedAt { get; }

    int Priority { get; }

    string? ParentTaskId { get; }

    Dictionary<string, JsonElement> GetMetadata();

    Task<IReadOnlyList<StructuredTaskEntry>> GetStructuredTasksAsync(CancellationToken cancellationToken = default);

    T? GetMetadataValue<T>(string key, T? defaultValue = default);

    void SetMetadataValue<T>(string key, T value);

    Task AddStructuredTaskAsync(StructuredTaskEntry task, CancellationToken cancellationToken = default);

    Task UpdateStructuredTaskAsync(int order, string? result = null, string? status = null, CancellationToken cancellationToken = default);

    Task ExcludePossibilityAsync(int taskOrder, int possibilityIndex, string reason, CancellationToken cancellationToken = default);
}
