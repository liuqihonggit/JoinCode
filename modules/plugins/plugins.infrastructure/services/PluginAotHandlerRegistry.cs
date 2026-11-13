namespace JoinCode.Plugins.Infrastructure.Services;

/// <summary>
/// AOT handler 注册表实现 — 从源码生成器产出的注册代码构建 handler 路由表
/// <para>源码生成器生成 AotHandlerRegistrations.Register(builder) 方法</para>
/// <para>builder 收集所有 (nodeId, method, handler) 注册项后构造 FrozenDictionary 路由表</para>
/// <para>handler 委托接受 IServiceProvider 解析 DI 依赖的 Handler 实例</para>
/// </summary>
public sealed class PluginAotHandlerRegistry : IAotHandlerRegistry {
    private readonly FrozenDictionary<string, FrozenDictionary<string, Func<IServiceProvider, object?[], object?>>> _registrations;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 构造 AOT handler 注册表
    /// </summary>
    /// <param name="serviceProvider">DI 容器 — handler 委托从中解析 Handler 实例</param>
    /// <param name="registrations">源码生成器产出的注册项（nodeId → method → handler 委托）</param>
    public PluginAotHandlerRegistry(
        IServiceProvider serviceProvider,
        FrozenDictionary<string, FrozenDictionary<string, Func<IServiceProvider, object?[], object?>>> registrations) {
        _serviceProvider = serviceProvider;
        _registrations = registrations;
    }

    /// <inheritdoc/>
    public FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId) {
        if (!_registrations.TryGetValue(nodeId, out var inner)) {
            return null;
        }

        var result = new Dictionary<string, Func<object?[], object?>>(StringComparer.Ordinal);
        foreach (var kvp in inner) {
            var handler = kvp.Value;
            var sp = _serviceProvider;
            result[kvp.Key] = args => handler(sp, args);
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
