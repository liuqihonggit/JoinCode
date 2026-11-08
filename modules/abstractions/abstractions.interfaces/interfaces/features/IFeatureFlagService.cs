namespace JoinCode.Abstractions.Interfaces;

public interface IFeatureFlagService {
    /// <summary>异步判断特性开关是否启用。</summary>
    Task<bool> IsEnabledAsync(string featureKey, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default);
    /// <summary>异步获取特性变体值。</summary>
    Task<T?> GetVariantAsync<T>(string featureKey, T? defaultValue = default, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default);
    /// <summary>异步刷新特性开关缓存。</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
    /// <summary>异步获取所有特性开关。</summary>
    Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFlagsAsync(CancellationToken cancellationToken = default);
}