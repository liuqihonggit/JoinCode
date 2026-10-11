
namespace JoinCode.Abstractions.Models.Vcr;

public enum VcrMode {
    [EnumValue("none")] None = 0,
    [EnumValue("record")] Record = 1,
    [EnumValue("playback")] Playback = 2
}

public sealed class VcrCassette {
    /// <summary>获取录像带名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取更新时间。</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取交互次数。</summary>
    public int InteractionCount { get; init; }
}