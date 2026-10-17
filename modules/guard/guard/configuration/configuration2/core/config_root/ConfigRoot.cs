// JCC11005 抑制: IConfigRoot.Get<T> 返回 T? 接口契约，全层未命中返回 default 是正常情况非异常
#pragma warning disable JCC11005
namespace JoinCode.Guard.Configuration.ConfigRoot;

/// <summary>
/// 统一配置入口实现 — 三层合并 Local &gt; Pro &gt; User，IMemoryCache 懒加载（TASK054）。
/// <para>线程安全：Get 返回不可变快照，IMemoryCache 内部线程安全，配置变更原子替换不影已持有快照。</para>
/// <para>缓存策略：L1 字段值缓存，key = "cfg:{path}"，SlidingExpiration 30 分钟，Priority.Low（内存压力优先释放）。</para>
/// </summary>
public sealed class UnifiedConfigRoot : IConfigRoot {
    private const string CacheKeyPrefix = "cfg:";
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);
    private readonly IConfigLayer _user;
    private readonly IConfigLayer _pro;
    private readonly IConfigLayer _local;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// 构造统一配置入口。
    /// </summary>
    /// <param name="user">用户级配置层（~/.jcc/，跨项目共享，基础）</param>
    /// <param name="pro">项目级配置层（{projectRoot}/.jcc/，项目隔离，覆盖 User）</param>
    /// <param name="local">本地配置层（settings.local.json，gitignore，覆盖 Pro）</param>
    /// <param name="cache">内存缓存（懒加载 + 内存压力自动释放；DI 容器管理生命周期，本类不 Dispose）</param>
    public UnifiedConfigRoot(IConfigLayer user, IConfigLayer pro, IConfigLayer local, IMemoryCache cache) {
        _user = user;
        _pro = pro;
        _local = local;
        _cache = cache;
    }

    /// <inheritdoc />
    public IConfigLayer User => _user;

    /// <inheritdoc />
    public IConfigLayer Pro => _pro;

    /// <inheritdoc />
    public IConfigLayer Local => _local;

    /// <inheritdoc />
    public T? Get<T>(string path) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var cacheKey = CacheKeyPrefix + path;
        if (_cache.TryGetValue(cacheKey, out T? cached)) {
            return cached;
        }

        var value = ResolveFromLayers<T>(path);
        var entryOptions = new MemoryCacheEntryOptions {
            SlidingExpiration = CacheSlidingExpiration,
            Priority = CacheItemPriority.Low,
        };
        _cache.Set(cacheKey, value, entryOptions);
        return value;
    }

    /// <inheritdoc />
    public T Get<T>(string path, T defaultValue) {
        var value = Get<T>(path);
        return value is null ? defaultValue : value;
    }

    /// <inheritdoc />
    public IConfigSection GetSection(string path) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new ConfigSection(this, path);
    }

    /// <inheritdoc />
    public void Invalidate(string path) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        _cache.Remove(CacheKeyPrefix + path);
    }

    private T? ResolveFromLayers<T>(string path) {
        var local = _local.Get<T>(path);
        if (local is not null) {
            return local;
        }
        var pro = _pro.Get<T>(path);
        if (pro is not null) {
            return pro;
        }
        return _user.Get<T>(path);
    }
}

/// <summary>
/// 配置子节视图实现 — 从指定路径前缀下继续按相对路径获取（TASK054）。
/// <para>线程安全：委托到 ConfigRoot，无共享可变状态。</para>
/// </summary>
internal sealed class ConfigSection : IConfigSection {
    private readonly UnifiedConfigRoot _root;
    private readonly string _prefix;

    /// <summary>
    /// 构造配置子节视图。
    /// </summary>
    /// <param name="root">所属 UnifiedConfigRoot 实例</param>
    /// <param name="prefix">路径前缀（点分，如 "current.ui"）</param>
    public ConfigSection(UnifiedConfigRoot root, string prefix) {
        _root = root;
        _prefix = prefix;
    }

    /// <inheritdoc />
    public T? Get<T>(string relativePath) {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        return _root.Get<T>(_prefix + "." + relativePath);
    }

    /// <inheritdoc />
    public IConfigSection GetSection(string relativePath) {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        return new ConfigSection(_root, _prefix + "." + relativePath);
    }
}
