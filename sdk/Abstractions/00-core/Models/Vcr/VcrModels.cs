
namespace JoinCode.Abstractions.Models.Vcr;

public enum VcrMode
{
    [EnumValue("none")] None = 0,
    [EnumValue("record")] Record = 1,
    [EnumValue("playback")] Playback = 2
}

public sealed class VcrCassette
{
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    public int InteractionCount { get; init; }
}
