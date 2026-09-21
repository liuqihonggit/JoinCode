namespace JoinCode.Abstractions.Interfaces;

public enum BuddyRarity { Common, Uncommon, Rare, Epic, Legendary }

public sealed class BuddyInfo {
    /// <summary>获取名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取物种。</summary>
    public required string Species { get; init; }
    /// <summary>获取稀有度。</summary>
    public required BuddyRarity Rarity { get; init; }
    /// <summary>获取眼睛样式。</summary>
    public required string Eye { get; init; }
    /// <summary>获取帽子样式。</summary>
    public required string Hat { get; init; }
    /// <summary>获取是否为闪光形态。</summary>
    public required bool Shiny { get; init; }
    /// <summary>获取 ASCII 艺术图。</summary>
    public required string AsciiArt { get; init; }
}

public interface IBuddyService {
    /// <summary>获取指定用户的伙伴信息。</summary>
    /// <param name="userId">用户标识。</param>
    BuddyInfo GetBuddy(string userId);
    /// <summary>获取指定用户的伙伴提示词。</summary>
    /// <param name="userId">用户标识。</param>
    string GetBuddyPrompt(string userId);
}