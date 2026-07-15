
using JoinCode.Abstractions.Attributes;

namespace McpToolRegistry;

/// <summary>
/// 工具缓存键生成策略
/// </summary>
public static class ToolCacheKeys
{
    /// <summary>
    /// 生成单个工具的缓存键
    /// </summary>
    public static string ForTool(string toolName) => $"toolinfo:{toolName}";

    /// <summary>
    /// 所有工具的缓存键
    /// </summary>
    public static readonly string AllTools = "toolinfo:all";

    /// <summary>
    /// 生成客户端工具前缀的缓存键模式
    /// </summary>
    public static string ForClientPrefix(string clientId) => $"{clientId}.";
}

/// <summary>
/// 工具缓存管理器 - 负责工具信息的缓存策略和失效机制
/// </summary>
[Register]
public sealed partial class ToolCacheManager
{
    private readonly IMemoryCache _cache;
    private readonly WorkflowConfig _config;

    public ToolCacheManager(IMemoryCache cache, WorkflowConfig config)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 获取工具信息（带缓存）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="factory">缓存未命中时的工厂方法</param>
    /// <returns>工具信息，如果工厂返回null则返回null</returns>
    public ToolInfo? GetToolInfo(string toolName, Func<ToolInfo?> factory)
    {
        var cacheKey = ToolCacheKeys.ForTool(toolName);

        if (_cache.TryGetValue(cacheKey, out ToolInfo? cachedTool) && cachedTool != null)
        {
            return cachedTool;
        }

        var toolInfo = factory();
        if (toolInfo == null)
        {
            return null;
        }

        var expiration = TimeSpan.FromMinutes(_config.ToolExecution.ToolCacheExpirationMinutes);
        _cache.Set(cacheKey, toolInfo, expiration);

        return toolInfo;
    }

    /// <summary>
    /// 获取所有工具信息（带缓存）
    /// </summary>
    /// <param name="factory">缓存未命中时的工厂方法</param>
    /// <returns>工具信息列表</returns>
    public IReadOnlyList<ToolInfo> GetAllToolInfos(Func<IEnumerable<ToolInfo>> factory)
    {
        if (_cache.TryGetValue(ToolCacheKeys.AllTools, out List<ToolInfo>? cachedTools) && cachedTools != null)
        {
            return cachedTools;
        }

        var tools = factory().ToList();
        var expiration = TimeSpan.FromMinutes(_config.ToolExecution.ToolCacheExpirationMinutes);
        _cache.Set(ToolCacheKeys.AllTools, tools, expiration);

        return tools;
    }

    /// <summary>
    /// 使指定工具的缓存失效
    /// </summary>
    /// <param name="toolName">工具名称</param>
    public void InvalidateToolCache(string toolName)
    {
        _cache.Remove(ToolCacheKeys.ForTool(toolName));
        _cache.Remove(ToolCacheKeys.AllTools);
    }

    /// <summary>
    /// 使所有工具缓存失效
    /// </summary>
    public void InvalidateAllCache()
    {
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Clear();
        }
        else
        {
            _cache.Remove(ToolCacheKeys.AllTools);
        }
    }

    /// <summary>
    /// 使指定客户端的所有工具缓存失效
    /// </summary>
    /// <param name="clientId">客户端ID</param>
    /// <param name="toolNameProvider">提供所有工具名称的委托</param>
    public void InvalidateClientTools(string clientId, Func<IEnumerable<string>> toolNameProvider)
    {
        var prefix = ToolCacheKeys.ForClientPrefix(clientId);
        var clientToolNames = toolNameProvider()
            .Where(name => name.StartsWith(prefix))
            .ToList();

        foreach (var toolName in clientToolNames)
        {
            InvalidateToolCache(toolName);
        }
    }

    /// <summary>
    /// 获取缓存过期时间
    /// </summary>
    public TimeSpan CacheExpiration => TimeSpan.FromMinutes(_config.ToolExecution.ToolCacheExpirationMinutes);
}
