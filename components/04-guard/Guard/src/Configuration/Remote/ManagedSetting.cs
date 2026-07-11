
namespace Core.Configuration.Remote;

public sealed class ManagedSetting
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required SettingScope Scope { get; init; }
    public required bool IsReadOnly { get; init; }
    public string? Description { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

public enum SettingScope
{
    [EnumValue("user")] User,
    [EnumValue("team")] Team,
    [EnumValue("organization")] Organization,
    [EnumValue("system")] System
}
