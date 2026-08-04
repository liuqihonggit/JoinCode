namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 能力缓存 — 应用启动时一次性检测并冻结
/// 消费方通过 ShellType 获取已缓存的 ShellCapability，无需 DI 注入
/// </summary>
public static class ShellCapabilityCache
{
    private static FrozenDictionary<ShellType, ShellCapability>? _capabilities;

    /// <summary>
    /// 是否已初始化
    /// </summary>
    public static bool IsInitialized => _capabilities is not null;

    /// <summary>
    /// 初始化缓存 — 应用启动时调用一次
    /// </summary>
    public static void Initialize(IReadOnlyDictionary<ShellType, ShellCapability> capabilities)
    {
        if (_capabilities is not null)
            throw new InvalidOperationException("ShellCapabilityCache already initialized");
        _capabilities = capabilities.ToFrozenDictionary();
    }

    /// <summary>
    /// 获取指定 ShellType 的能力描述
    /// </summary>
    public static ShellCapability Get(ShellType type)
    {
        if (_capabilities is null)
            throw new InvalidOperationException("ShellCapabilityCache not initialized. Call Initialize() first.");
        return _capabilities[type];
    }

    /// <summary>
    /// 尝试获取指定 ShellType 的能力描述
    /// </summary>
    public static bool TryGet(ShellType type, [NotNullWhen(true)] out ShellCapability? capability)
    {
        if (_capabilities is null)
        {
            capability = null;
            return false;
        }
        return _capabilities.TryGetValue(type, out capability);
    }

    /// <summary>
    /// 获取所有已注册的 ShellType
    /// </summary>
    public static IReadOnlyCollection<ShellType> RegisteredTypes
        => _capabilities?.Keys ?? [];

    /// <summary>
    /// 获取所有 ShellInfo 快照 — 用于提示词注入
    /// </summary>
    public static IReadOnlyDictionary<ShellType, ShellInfo> GetAllShellInfos()
    {
        if (_capabilities is null)
            return FrozenDictionary<ShellType, ShellInfo>.Empty;
        return _capabilities.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToShellInfo());
    }

    /// <summary>
    /// 重置缓存 — 仅用于测试
    /// </summary>
    internal static void Reset()
    {
        _capabilities = null;
    }
}
