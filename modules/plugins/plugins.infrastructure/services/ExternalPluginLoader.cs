namespace JoinCode.Plugins.Infrastructure.Services;

/// <summary>
/// 外部插件加载器 — 从 YAML 配置的 assembly-path 加载 DLL，创建 handler 表
/// <para>非 AOT 模式下通过 AssemblyLoadContext 加载外部 DLL</para>
/// <para>AOT 模式下此加载器不可用 — 只有源码生成器路径可用</para>
/// <para>handler 委托签名: Func&lt;object?[], object?&gt; — 接受参数数组，返回结果</para>
/// </summary>
[RequiresUnreferencedCode("External plugin loading uses reflection (AssemblyLoadContext, GetType, GetMethod, CreateInstance) and is not AOT-compatible")]
public sealed class ExternalPluginLoader {
    private readonly IFileSystem _fs;
    private readonly ILogger<ExternalPluginLoader>? _logger;

    /// <summary>
    /// 构造外部插件加载器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志器</param>
    public ExternalPluginLoader(IFileSystem fs, ILogger<ExternalPluginLoader>? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    /// <summary>
    /// 从 YAML 配置加载 DLL 并创建 handler 表
    /// <para>委托接受 PluginPackage，返回 handler 表（method → handler 委托）</para>
    /// <para>委托内部通过 AssemblyLoadContext 加载 DLL，反射创建 handler 实例</para>
    /// </summary>
    /// <param name="yamlConfig">插件 YAML 配置</param>
    /// <param name="pluginDir">插件目录（plugin.yaml 所在目录的绝对路径）</param>
    /// <returns>handler 表（method → handler 委托），加载失败返回空表</returns>
    [RequiresUnreferencedCode("Uses AssemblyLoadContext.LoadFromAssemblyPath and reflection")]
    public FrozenDictionary<string, Func<object?[], object?>> LoadHandlers(PluginYamlConfig yamlConfig, string pluginDir) {
        var assemblyFullPath = Path.IsPathRooted(yamlConfig.AssemblyPath)
            ? yamlConfig.AssemblyPath
            : Path.Combine(pluginDir, yamlConfig.AssemblyPath);

        if (!_fs.FileExists(assemblyFullPath)) {
            _logger?.LogWarning("[ExternalPluginLoader] DLL 不存在: {Path}", assemblyFullPath);
            return FrozenDictionary<string, Func<object?[], object?>>.Empty;
        }

        try {
            var alc = new PluginAssemblyLoadContext(assemblyFullPath);
            var assembly = alc.LoadFromAssemblyPath(assemblyFullPath);
            var entryType = assembly.GetType(yamlConfig.EntryType);

            if (entryType is null) {
                _logger?.LogWarning("[ExternalPluginLoader] 入口类型不存在: {Type}", yamlConfig.EntryType);
                alc.Unload();
                return FrozenDictionary<string, Func<object?[], object?>>.Empty;
            }

            var handlers = new Dictionary<string, Func<object?[], object?>>(StringComparer.Ordinal);

            foreach (var methodConfig in yamlConfig.Methods) {
                var methodInfo = entryType.GetMethod(methodConfig.EntryMethod, BindingFlags.Public | BindingFlags.Instance);
                if (methodInfo is null) {
                    _logger?.LogWarning("[ExternalPluginLoader] 方法不存在: {Type}.{Method}", yamlConfig.EntryType, methodConfig.EntryMethod);
                    continue;
                }

                var instance = Activator.CreateInstance(entryType);
                if (instance is null) {
                    _logger?.LogWarning("[ExternalPluginLoader] 无法创建实例: {Type}", yamlConfig.EntryType);
                    continue;
                }

                handlers[methodConfig.Name] = args => InvokeMethod(methodInfo, instance, args);
            }

            _logger?.LogInformation("[ExternalPluginLoader] 加载插件 {Name}: {Count} 个 handler", yamlConfig.Name, handlers.Count);
            return handlers.ToFrozenDictionary(StringComparer.Ordinal);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[ExternalPluginLoader] 加载插件 {Name} 失败", yamlConfig.Name);
            return FrozenDictionary<string, Func<object?[], object?>>.Empty;
        }
    }

    private static object? InvokeMethod(MethodInfo methodInfo, object instance, object?[] args) {
        var parameters = methodInfo.GetParameters();
        var invokeArgs = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length && i < args.Length; i++) {
            invokeArgs[i] = args[i];
        }

        var result = methodInfo.Invoke(instance, invokeArgs);

        if (result is Task task) {
            task.GetAwaiter().GetResult();
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);
        }

        return result;
    }

    /// <summary>
    /// 插件程序集加载上下文 — 支持卸载的 AssemblyLoadContext
    /// </summary>
    private sealed class PluginAssemblyLoadContext : AssemblyLoadContext {
        private readonly AssemblyDependencyResolver _resolver;

        /// <summary>构造可收集的 AssemblyLoadContext</summary>
        /// <param name="pluginPath">插件 DLL 路径</param>
        public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: true) {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName) {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }
}
