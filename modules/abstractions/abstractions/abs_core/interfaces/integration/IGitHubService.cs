namespace JoinCode.Abstractions.Interfaces;

public sealed class PRSubscription {
    /// <summary>获取 PR 引用。</summary>
    public required string PrRef { get; init; }
    /// <summary>获取订阅事件。</summary>
    public required string Events { get; init; }
    /// <summary>获取订阅时间。</summary>
    public required DateTime SubscribedAt { get; init; }
}

public interface IGitHubService {
    /// <summary>列出所有 PR 订阅。</summary>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<PRSubscription>> ListSubscriptionsAsync(CancellationToken ct = default);
    /// <summary>订阅 PR 事件。</summary>
    /// <param name="prRef">PR 引用。</param>
    /// <param name="events">订阅事件类型。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PRSubscription> SubscribeAsync(string prRef, string events = GitHubLogFilterEnumConstants.All, CancellationToken ct = default);
    /// <summary>取消订阅 PR 事件。</summary>
    /// <param name="prRef">PR 引用。</param>
    /// <param name="ct">取消令牌。</param>
    Task UnsubscribeAsync(string prRef, CancellationToken ct = default);
}