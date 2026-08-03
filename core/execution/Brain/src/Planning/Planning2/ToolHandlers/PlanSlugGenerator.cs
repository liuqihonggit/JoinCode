
namespace Core.Planning;

/// <summary>
/// Plan 文件名 slug 生成器 — 对齐 TS generateWordSlug() + getPlanSlug()
/// 格式: {adjective}-{verb}-{noun}，如 gleaming-brewing-phoenix
/// 同一 session 内缓存 slug，保证进出 plan mode 覆盖同一文件
/// </summary>
internal static class PlanSlugGenerator
{
    private const int MaxSlugRetries = 10;

    private static readonly string[] Adjectives =
    [
        "amber", "azure", "blazing", "calm", "cosmic", "crimson", "crystal", "dazzling",
        "deep", "drifting", "ember", "ethereal", "fierce", "flickering", "flowing", "frosty",
        "gentle", "gleaming", "golden", "graceful", "harmonic", "hidden", "hollow", "icy",
        "indigo", "infinite", "ivory", "jade", "keen", "kindled", "luminous", "lunar",
        "magnetic", "marble", "misty", "morning", "mystic", "noble", "obsidian", "olive",
        "opal", "pale", "phantom", "polished", "primal", "quiet", "radiant", "rapid",
        "restless", "rosy", "ruby", "sacred", "sapphire", "shadowy", "shimmering", "silent",
        "silver", "slate", "smooth", "solid", "sparkling", "stellar", "stormy", "swift",
        "tender", "thunderous", "tranquil", "twilight", "ultimate", "vast", "velvet", "vivid",
        "warm", "whispering", "wild", "woven", "zenith"
    ];

    private static readonly string[] Verbs =
    [
        "baking", "balancing", "blazing", "blooming", "brewing", "building", "burning", "carving",
        "casting", "charting", "chasing", "crafting", "creating", "cruising", "dancing", "designing",
        "discovering", "dreaming", "driving", "echoing", "emerging", "evolving", "exploring", "fading",
        "flying", "forging", "forming", "gathering", "gliding", "growing", "harvesting", "hatching",
        "igniting", "imagining", "kindling", "launching", "learning", "mapping", "molding", "navigating",
        "painting", "pioneering", "planning", "playing", "pondering", "racing", "reading", "rendering",
        "rising", "roaming", "sailing", "scanning", "sculpting", "shaping", "shifting", "shining",
        "singing", "sketching", "soaring", "sparking", "spinning", "streaming", "striking", "surfing",
        "swimming", "syncing", "thinking", "threading", "tuning", "unfolding", "vaulting", "wandering",
        "weaving", "whispering", "wiring", "writing"
    ];

    private static readonly string[] Nouns =
    [
        "aurora", "badger", "beacon", "bluebird", "butterfly", "canyon", "cascade", "cedar",
        "chameleon", "citadel", "comet", "coral", "crane", "crystal", "dolphin", "dragon",
        "eagle", "elm", "falcon", "firefly", "flame", "fox", "galaxy", "gazelle",
        "glacier", "goshawk", "harbor", "hawk", "heron", "horizon", "hummingbird", "jaguar",
        "kingfisher", "labyrinth", "lamprey", "lighthouse", "lion", "lotus", "lynx", "magnet",
        "mammoth", "mantis", "meadow", "mercury", "monarch", "moonstone", "mosaic", "nebula",
        "nexus", "obsidian", "orca", "osprey", "otter", "owl", "panther", "paradox",
        "phoenix", "pillar", "pinnacle", "prism", "pyramid", "quartz", "raven", "reef",
        "ridge", "sapphire", "satellite", "scarab", "sequoia", "shadow", "spire", "starling",
        "storm", "swallow", "tempest", "thunder", "tiger", "titan", "tornado", "vortex",
        "vulture", "walrus", "wolverine", "zebra"
    ];

    private static readonly ConcurrentDictionary<string, string> SlugCache = new();

    /// <summary>
    /// 对齐 TS getPlanSlug(): 获取或生成 session 级别的 slug 缓存
    /// </summary>
    public static string GetOrCreateSlug(string sessionId, IFileSystem fs)
    {
        return SlugCache.GetOrAdd(sessionId, static (id, fileSystem) =>
        {
            var slug = GenerateWordSlug();

            try
            {
                var plansDir = GetPlansDirectory();
                fileSystem.CreateDirectory(plansDir);

                for (var i = 0; i < MaxSlugRetries; i++)
                {
                    var filePath = Path.Combine(plansDir, $"{slug}.md");
                    if (!fileSystem.FileExists(filePath))
                    {
                        return slug;
                    }
                    slug = GenerateWordSlug();
                }
            }
            catch (Exception ex)
            {
                // 目录创建失败时直接返回 slug，不检查文件冲突
                System.Diagnostics.Trace.WriteLine($"Plan slug directory check failed: {ex.Message}");
            }

            return slug;
        }, fs);
    }

    /// <summary>
    /// 对齐 TS clearPlanSlug(): 清除指定 session 的 slug 缓存
    /// </summary>
    public static void ClearSlug(string sessionId)
    {
        SlugCache.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// 对齐 TS clearAllPlanSlugs(): 清除所有 slug 缓存
    /// </summary>
    public static void ClearAllSlugs()
    {
        SlugCache.Clear();
    }

    /// <summary>
    /// 对齐 TS generateWordSlug(): 生成 {adjective}-{verb}-{noun} 格式的随机 slug
    /// </summary>
    internal static string GenerateWordSlug()
    {
        var adjective = Adjectives[Random.Shared.Next(Adjectives.Length)];
        var verb = Verbs[Random.Shared.Next(Verbs.Length)];
        var noun = Nouns[Random.Shared.Next(Nouns.Length)];
        return $"{adjective}-{verb}-{noun}";
    }

    /// <summary>
    /// 获取 plans 目录路径
    /// </summary>
    internal static string GetPlansDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            AppDataConstants.PlansFolderName);
    }
}
