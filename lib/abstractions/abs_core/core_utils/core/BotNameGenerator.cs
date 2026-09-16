namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 子代理 bot 前缀随机中文名生成器 — 生成 <c>bot{中文名}</c> 格式的显示名，进程内去重。
/// 用于子代理 spawn 时 <see cref="JoinCode.Abstractions.Models.Agent.SubAgentOptions.DisplayName"/> 为空的场景。
/// </summary>
public static class BotNameGenerator
{
    /// <summary>
    /// 中文小名池 — 36 个常见中文小名，覆盖不同风格。
    /// </summary>
    private static readonly string[] s_chineseNames =
    [
        "小明", "小红", "小虎", "小薇", "阿杰", "阿宝",
        "小雪", "小雷", "小风", "小云", "小龙", "小凤",
        "阿伟", "阿芳", "小刚", "小娟", "小强", "小燕",
        "阿明", "阿华", "小亮", "小玲", "小波", "小静",
        "阿军", "阿丽", "小涛", "小敏", "小鹏", "小霞",
        "阿超", "阿秀", "小宇", "小婷", "小辉", "小悦"
    ];

    /// <summary>
    /// 进程内已用名称集合 — 去重保证同进程不重名。
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> s_usedNames = new();

    /// <summary>
    /// 随机数生成器 — 线程安全包装。
    /// </summary>
    private static readonly Random s_random = new();

    /// <summary>
    /// 生成一个 <c>bot{中文名}</c> 格式的随机显示名，进程内去重。
    /// </summary>
    /// <returns>格式为 <c>bot{中文名}</c> 的显示名，如 <c>bot小明</c>、<c>bot阿虎</c>。</returns>
    public static string Generate()
    {
        for (var i = 0; i < s_chineseNames.Length; i++)
        {
            var name = s_chineseNames[s_random.Next(s_chineseNames.Length)];
            var botName = $"bot{name}";
            if (s_usedNames.TryAdd(botName, 0))
                return botName;
        }

        var fallback = $"bot{s_random.Next(1000, 9999)}";
        s_usedNames.TryAdd(fallback, 0);
        return fallback;
    }

    /// <summary>
    /// 释放已使用的名称，允许后续 <see cref="Generate"/> 复用。
    /// 在子代理生命周期结束时调用。
    /// </summary>
    /// <param name="name">要释放的 bot 名称。</param>
    public static void Release(string name) => s_usedNames.TryRemove(name, out _);

    /// <summary>
    /// 清除所有已记录的名称 — 仅用于测试重置。
    /// </summary>
    public static void Clear() => s_usedNames.Clear();
}
