namespace JoinCode.Guard.Configuration.ConfigRoot;

/// <summary>
/// 统一配置入口实现 — 三层合并 Local &gt; Pro &gt; User，IMemoryCache 懒加载（TASK054）。
/// <para>线程安全：TryGet 返回不可变快照，IMemoryCache 内部线程安全，配置变更原子替换不影已持有快照。</para>
/// <para>缓存策略：L1 字段值缓存，key = "cfg:{path}"，SlidingExpiration 30 分钟，Priority.Low（内存压力优先释放）。</para>
/// <para>TryGet 模式：bool 成功时 out 必然非 null，强制调用方处理未命中。</para>
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
    public bool TryGet<T>(string path, [NotNullWhen(true)] out T? value) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var cacheKey = CacheKeyPrefix + path;
        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null) {
            value = cached;
            return true;
        }

        if (ResolveFromLayers<T>(path, out var resolved)) {
            var entryOptions = new MemoryCacheEntryOptions {
                SlidingExpiration = CacheSlidingExpiration,
                Priority = CacheItemPriority.Low,
                Size = 1,
            };
            _cache.Set(cacheKey, resolved, entryOptions);
            value = resolved;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public T Get<T>(string path, T defaultValue) {
        if (TryGet<T>(path, out var value)) {
            return value;
        }
        return defaultValue;
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

    private bool ResolveFromLayers<T>(string path, [NotNullWhen(true)] out T? value) {
        if (_local.TryGet<T>(path, out value)) {
            return true;
        }
        if (_pro.TryGet<T>(path, out value)) {
            return true;
        }
        if (_user.TryGet<T>(path, out value)) {
            return true;
        }
        value = default;
        return false;
    }
}

/// <summary>
/// 配置子节视图实现 — 从指定路径前缀下继续按相对路径获取（TASK054）。
/// <para>线程安全：委托到 UnifiedConfigRoot，无共享可变状态。</para>
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
    public bool TryGet<T>(string relativePath, [NotNullWhen(true)] out T? value) {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        return _root.TryGet<T>(_prefix + "." + relativePath, out value);
    }

    /// <inheritdoc />
    public IConfigSection GetSection(string relativePath) {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        return new ConfigSection(_root, _prefix + "." + relativePath);
    }
}
