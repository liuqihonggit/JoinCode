namespace IO.Services;

/// <summary>
/// 伙伴精灵服务 — 基于用户 ID 确定性生成专属伙伴（物种/稀有度/外观），结果按用户缓存
/// </summary>
[Register(typeof(IBuddyService), ServiceLifetime.Singleton)]
public sealed partial class BuddyService : ServiceEntity, IBuddyService {
    private static readonly string[] Species = new[] { "duck", "goose", "blob", "cat", "dragon", "octopus", "owl", "penguin", "turtle", "snail", "ghost", "axolotl", "capybara", "cactus", "robot", "rabbit", "mushroom", "chonk" };
    private static readonly string[] Eyes = new[] { "·", "✦", "×", "◉", "@", "°" };
    private static readonly string[] Hats = new[] { "", "^", "~", "=", "*", "#", "+", "@" };
    private static readonly string[] Names = new[] { "Quackers", "Goosey", "Blobby", "Whiskers", "Draco", "Inky", "Hoot", "Waddle", "Shelly", "Slimey", "Boo", "Axie", "Cappy", "Spike", "Beep", "Bouncy", "Shroomy", "Chunk" };
    private static readonly string Salt = "jcc-buddy-salt-2026";

    private ImmutableDictionary<string, BuddyInfo> _cache = ImmutableDictionary<string, BuddyInfo>.Empty;

    /// <summary>
    /// 获取用户的伙伴精灵信息 — 首次调用时按用户 ID 确定性生成并缓存
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <returns>伙伴精灵信息</returns>
    public BuddyInfo GetBuddy(string userId) {
        var snapshot = Volatile.Read(ref _cache);
        if (snapshot.TryGetValue(userId, out var existing))
            return existing;

        var generated = GenerateBuddy(userId);
        ImmutableInterlocked.Update(ref _cache, d => d.ContainsKey(userId) ? d : d.Add(userId, generated));
        return Volatile.Read(ref _cache)[userId];
    }

    /// <summary>
    /// 获取伙伴精灵的系统提示词 — 用于注入到对话上下文，告知模型用户拥有伙伴精灵
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <returns>伙伴精灵系统提示词文本</returns>
    public string GetBuddyPrompt(string userId) {
        var buddy = GetBuddy(userId);
        return $"[系统: 用户有一个伙伴精灵 {buddy.Name}（{buddy.Species}，{buddy.Rarity}稀有度）。当用户直接对伙伴说话时，让路给伙伴回应。伙伴不是AI助手本身。]";
    }

    private static BuddyInfo GenerateBuddy(string userId) {
        var seed = HashUserId(userId);
        var rng = new Mulberry32(seed);

        var rarityRoll = rng.NextDouble();
        var rarity = rarityRoll switch {
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

        return new BuddyInfo {
            Name = name,
            Species = species,
            Rarity = rarity,
            Eye = Eyes[eyeIndex],
            Hat = Hats[hatIndex],
            Shiny = shiny,
            AsciiArt = asciiArt
        };
    }

    private static int HashUserId(string userId) {
        unchecked {
            var hash = 5381;
            var combined = userId + Salt;
            foreach (var c in combined)
                hash = ((hash << 5) + hash) + c;
            return Math.Abs(hash);
        }
    }

    private static string GenerateAsciiArt(string species, string eye, string hat, bool shiny) {
        var prefix = shiny ? "* " : "";
        var hatLine = string.IsNullOrEmpty(hat) ? "   " : $" {hat} ";
        return $"{prefix}{hatLine}\n   {eye}_{eye}\n   >^<";
    }

    private sealed class Mulberry32(int seed) {
        private int _state = seed;

        /// <summary>生成下一个伪随机 32 位整数 — 基于 Mulberry32 算法更新内部状态</summary>
        /// <returns>非负伪随机整数</returns>
        public int Next() {
            _state += 0x6D2B79F5;
            var t = _state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return Math.Abs(t ^ (t >> 14));
        }

        /// <summary>生成一个 [0,1) 范围内的伪随机双精度浮点数</summary>
        /// <returns>[0,1) 范围内的伪随机双精度浮点数</returns>
        public double NextDouble() => (Next() & 0x7FFFFFFF) / (double)0x7FFFFFFF;

        /// <summary>生成一个 [0,maxValue) 范围内的伪随机整数</summary>
        /// <param name="maxValue"> exclusive 上界</param>
        /// <returns>[0,maxValue) 范围内的伪随机整数</returns>
        public int Next(int maxValue) => Next() % maxValue;
    }
}