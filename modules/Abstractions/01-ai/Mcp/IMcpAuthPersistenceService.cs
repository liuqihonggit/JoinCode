namespace JoinCode.Abstractions.Interfaces;

public interface IMcpAuthPersistenceService
{
    Task SaveAsync(string authName, string authType, string serializedData, CancellationToken ct = default);
    Task<AuthConfigEntry?> LoadAsync(string authName, CancellationToken ct = default);
    Task<IReadOnlyList<AuthConfigEntry>> ListAsync(CancellationToken ct = default);
    Task RemoveAsync(string authName, CancellationToken ct = default);
}

public sealed class AuthConfigEntry
{
    public required string Name { get; init; }
    public required string AuthType { get; init; }
    public required string Data { get; init; }
    public required DateTime SavedAt { get; init; }
}
