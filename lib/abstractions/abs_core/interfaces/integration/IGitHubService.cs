namespace JoinCode.Abstractions.Interfaces;

public sealed class PRSubscription
{
    public required string PrRef { get; init; }
    public required string Events { get; init; }
    public required DateTime SubscribedAt { get; init; }
}

public interface IGitHubService
{
    Task<IReadOnlyList<PRSubscription>> ListSubscriptionsAsync(CancellationToken ct = default);
    Task<PRSubscription> SubscribeAsync(string prRef, string events = "all", CancellationToken ct = default);
    Task UnsubscribeAsync(string prRef, CancellationToken ct = default);
}
