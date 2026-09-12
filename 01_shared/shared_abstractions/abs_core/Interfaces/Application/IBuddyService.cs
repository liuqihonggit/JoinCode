namespace JoinCode.Abstractions.Interfaces;

public enum BuddyRarity { Common, Uncommon, Rare, Epic, Legendary }

public sealed class BuddyInfo
{
    public required string Name { get; init; }
    public required string Species { get; init; }
    public required BuddyRarity Rarity { get; init; }
    public required string Eye { get; init; }
    public required string Hat { get; init; }
    public required bool Shiny { get; init; }
    public required string AsciiArt { get; init; }
}

public interface IBuddyService
{
    BuddyInfo GetBuddy(string userId);
    string GetBuddyPrompt(string userId);
}
