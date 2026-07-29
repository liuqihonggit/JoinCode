namespace JoinCode.Abstractions.Models.Features;

public sealed class FeatureFlag
{
    public required string Key { get; init; }
    public bool Enabled { get; init; }
    public double RolloutPercentage { get; init; }
    public Dictionary<string, string>? TargetingRules { get; init; }
    public object? DefaultValue { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
