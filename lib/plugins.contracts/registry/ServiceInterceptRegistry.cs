namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 服务配置覆写注册表 — 对齐 DSH ctx.intercept(name, config)
/// <para>允许插件覆写服务配置而不改服务实现，流入 Service.ResolveConfig 合并</para>
/// <para>多插件可叠加覆写，按注册顺序依次应用</para>
/// <para>线程安全：ConcurrentDictionary + ImmutableList</para>
/// </summary>
public sealed class ServiceInterceptRegistry {
    private ImmutableDictionary<string, ImmutableList<Action<object>>> _interceptors = ImmutableDictionary<string, ImmutableList<Action<object>>>.Empty;

    /// <summary>
    /// 注册服务配置覆写 — 返回 disposer
    /// <para>多个插件可对同一服务注册多个覆写，按注册顺序叠加</para>
    /// </summary>
    public IDisposable Intercept(string serviceName, Action<object> configOverride) {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(configOverride);

        ImmutableInterlocked.Update(ref _interceptors,
            d => d.SetItem(serviceName, d.GetValueOrDefault(serviceName, ImmutableList<Action<object>>.Empty).Add(configOverride)));

        return new InterceptDisposer(() =>
            ImmutableInterlocked.Update(ref _interceptors,
                d => {
                    var next = d.GetValueOrDefault(serviceName, ImmutableList<Action<object>>.Empty).Remove(configOverride);
                    return next.IsEmpty ? d.Remove(serviceName) : d.SetItem(serviceName, next);
                }));
    }

    /// <summary>
    /// 合并配置：base → intercept 覆写依次应用
    /// <para>对齐 DSH Service.ResolveConfig 合并祖先 intercept 配置</para>
    /// </summary>
    public T ResolveConfig<T>(string serviceName, T baseConfig) where T : class {
        ArgumentNullException.ThrowIfNull(baseConfig);
        if (Volatile.Read(ref _interceptors).TryGetValue(serviceName, out var overrides)) {
            foreach (var o in overrides) {
                o(baseConfig);
            }
        }
        return baseConfig;
    }

    /// <summary>是否有覆写</summary>
    public bool HasIntercept(string serviceName) {
        return Volatile.Read(ref _interceptors).TryGetValue(serviceName, out var list) && list.Count > 0;
    }

    /// <summary>覆写数量</summary>
    public int InterceptCount(string serviceName) {
        return Volatile.Read(ref _interceptors).TryGetValue(serviceName, out var list) ? list.Count : 0;
    }

    /// <summary>清除某服务的所有覆写</summary>
    public void Clear(string serviceName) => ImmutableInterlocked.Update(ref _interceptors, d => d.Remove(serviceName));

    private sealed class InterceptDisposer(Action unsubscribe) : IDisposable {
        private int _disposed;

        /// <summary>释放资源。</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            unsubscribe();
        }
    }
}