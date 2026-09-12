namespace JoinCode.Abstractions.Interfaces;

public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string featureKey, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default);
    Task<T?> GetVariantAsync<T>(string featureKey, T? defaultValue = default, Dictionary<string, string>? attributes = null, CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFlagsAsync(CancellationToken cancellationToken = default);
}
