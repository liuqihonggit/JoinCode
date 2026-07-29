using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class BuddyService : IBuddyService
{
    private static readonly string[] Species = ["duck", "goose", "blob", "cat", "dragon", "octopus", "owl", "penguin", "turtle", "snail", "ghost", "axolotl", "capybara", "cactus", "robot", "rabbit", "mushroom", "chonk"];
    private static readonly string[] Eyes = ["·", "✦", "×", "◉", "@", "°"];
    private static readonly string[] Hats = ["", "^", "~", "=", "*", "#", "+", "@"];
    private static readonly string[] Names = ["Quackers", "Goosey", "Blobby", "Whiskers", "Draco", "Inky", "Hoot", "Waddle", "Shelly", "Slimey", "Boo", "Axie", "Cappy", "Spike", "Beep", "Bouncy", "Shroomy", "Chunk"];
    private static readonly string Salt = "jcc-buddy-salt-2026";

    private readonly ConcurrentDictionary<string, BuddyInfo> _cache = new();

    public BuddyInfo GetBuddy(string userId)
    {
        return _cache.GetOrAdd(userId, GenerateBuddy);
    }

    public string GetBuddyPrompt(string userId)
    {
        var buddy = GetBuddy(userId);
        return $"[系统: 用户有一个伙伴精灵 {buddy.Name}（{buddy.Species}，{buddy.Rarity}稀有度）。当用户直接对伙伴说话时，让路给伙伴回应。伙伴不是AI助手本身。]";
    }

    private static BuddyInfo GenerateBuddy(string userId)
    {
        var seed = HashUserId(userId);
        var rng = new Mulberry32(seed);

        var rarityRoll = rng.NextDouble();
        var rarity = rarityRoll switch
        {
            < 0.01 => BuddyRarity.Legendary,
            < 0.05 => BuddyRarity.Epic,
            < 0.15 => BuddyRarity.Rare,
            < 0.40 => BuddyRarity.Uncommon,
            _ => BuddyRarity.Common
        };

        var speciesIndex = rng.Next(Species.Length);
        var eyeIndex = rng.Next(Eyes.Length);
        var hatIndex = rarity == BuddyRarity.Common ? 0 : rng.Next(Hats.Length);
        var shiny = rng.NextDouble() < 0.01;

        var species = Species[speciesIndex];
        var name = Names[speciesIndex];

        var asciiArt = GenerateAsciiArt(species, Eyes[eyeIndex], Hats[hatIndex], shiny);

        return new BuddyInfo
        {
            Name = name,
            Species = species,
            Rarity = rarity,
            Eye = Eyes[eyeIndex],
            Hat = Hats[hatIndex],
            Shiny = shiny,
            AsciiArt = asciiArt
        };
    }

    private static int HashUserId(string userId)
    {
        unchecked
        {
            int hash = 5381;
            var combined = userId + Salt;
            foreach (var c in combined)
                hash = ((hash << 5) + hash) + c;
            return Math.Abs(hash);
        }
    }

    private static string GenerateAsciiArt(string species, string eye, string hat, bool shiny)
    {
        var prefix = shiny ? "* " : "";
        var hatLine = string.IsNullOrEmpty(hat) ? "   " : $" {hat} ";
        return $"{prefix}{hatLine}\n   {eye}_{eye}\n   >^<";
    }

    private sealed class Mulberry32(int seed)
    {
        private int _state = seed;

        public int Next()
        {
            _state += 0x6D2B79F5;
            var t = _state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return Math.Abs(t ^ (t >> 14));
        }

        public double NextDouble() => (Next() & 0x7FFFFFFF) / (double)0x7FFFFFFF;

        public int Next(int maxValue) => Next() % maxValue;
    }
}
