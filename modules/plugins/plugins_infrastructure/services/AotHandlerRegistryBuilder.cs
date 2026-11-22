namespace JoinCode.Plugins.Infrastructure.Services;

/// <summary>
/// AOT handler 注册表构建器实现 — 收集源码生成器产出的 handler 注册项
/// <para>源码生成器生成 AotHandlerRegistrations.Register(builder) 方法</para>
/// <para>builder 收集所有 (nodeId, method, handler) 注册项后构造路由表</para>
/// </summary>
public sealed class AotHandlerRegistryBuilder : IAotHandlerRegistryBuilder {
    private readonly Dictionary<string, Dictionary<string, Func<IServiceProvider, object?[], object?>>> _registrations = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void AddHandler(string nodeId, string method, Func<IServiceProvider, object?[], object?> handler) {
        if (!_registrations.TryGetValue(nodeId, out var inner)) {
            inner = new Dictionary<string, Func<IServiceProvider, object?[], object?>>(StringComparer.Ordinal);
            _registrations[nodeId] = inner;
        }

        inner[method] = handler;
    }

    /// <summary>
    /// 构建不可变路由表
    /// </summary>
    public FrozenDictionary<string, FrozenDictionary<string, Func<IServiceProvider, object?[], object?>>> Build() {
        var outer = new Dictionary<string, FrozenDictionary<string, Func<IServiceProvider, object?[], object?>>>(StringComparer.Ordinal);
        foreach (var kvp in _registrations) {
            outer[kvp.Key] = kvp.Value.ToFrozenDictionary(StringComparer.Ordinal);
        }

        return outer.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
