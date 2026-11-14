namespace JoinCode.Plugins.Infrastructure.Services;

/// <summary>
/// 插件文件监控服务 — 监控 ~/.jcc/plugins/ 约定文件夹中的 plugin.yaml 文件
/// <para>检测到新 plugin.yaml 时，解析 YAML 配置，调用 DynamicPluginRegistry0.DefinePlugin + RunPlugin</para>
/// <para>检测到 plugin.yaml 删除时，调用 DynamicPluginRegistry.StopPlugin + UndefinePlugin</para>
/// <para>外部插件通过 YAML 配置 + 文件监控加载，handler 由 IAotHandlerRegistry（源码生成器）提供</para>
/// </summary>
public sealed class PluginFileWatcher : IAsyncDisposable {
    private readonly DynamicPluginRegistry _registry;
    private readonly IFileSystem _fs;
    private readonly ILogger<PluginFileWatcher>? _logger;
    private IFileSystemWatcher? _watcher;
    private volatile bool _isWatching;
    private readonly string _pluginsDir;

    /// <summary>
    /// 构造插件文件监控服务
    /// </summary>
    /// <param name="registry">动态插件注册表</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志器</param>
    public PluginFileWatcher(
        DynamicPluginRegistry registry,
        IFileSystem fs,
        ILogger<PluginFileWatcher>? logger = null) {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _pluginsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "plugins");
    }

    /// <summary>是否正在监控</summary>
    public bool IsWatching => _isWatching;

    /// <summary>插件目录路径</summary>
    public string PluginsDirectory => _pluginsDir;

    /// <summary>
    /// 启动监控 — 创建约定文件夹（如不存在），启动 FileSystemWatcher
    /// </summary>
    public void StartWatching() {
        if (_isWatching) {
            _logger?.LogWarning("[PluginFileWatcher] 已在监控中，忽略重复启动请求");
            return;
        }

        if (!_fs.DirectoryExists(_pluginsDir)) {
            _fs.CreateDirectory(_pluginsDir);
            _logger?.LogInformation("[PluginFileWatcher] 创建插件约定文件夹: {Dir}", _pluginsDir);
        }

        _watcher = _fs.Watch(_pluginsDir);
        _watcher.IncludeSubdirectories = true;
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        _watcher.Filter = "plugin.yaml";

        _watcher.DebouncedCreated += OnPluginYamlCreated;
        _watcher.DebouncedChanged += OnPluginYamlChanged;
        _watcher.DebouncedDeleted += OnPluginYamlDeleted;
        _watcher.EnableRaisingEvents = true;

        ScanExistingPlugins();

        _isWatching = true;
        _logger?.LogInformation("[PluginFile7FileWatcher] 开始监控插件目录: {Dir}", _pluginsDir);
    }

    /// <summary>
    /// 停止监控 — 释放 FileSystemWatcher
    /// </summary>
    public async Task StopWatchingAsync() {
        if (!_isWatching) {
            return;
        }

        if (_watcher is not null) {
            _watcher.DebouncedCreated -= OnPluginYamlCreated;
            _watcher.DebouncedChanged -= OnPluginYamlChanged;
            _watcher.DebouncedDeleted -= OnPluginYamlDeleted;
            _watcher.EnableRaisingEvents = false;
            await _watcher.DisposeAsync().ConfigureAwait(false);
            _watcher = null;
        }

        _isWatching = false;
        _logger?.LogInformation("[PluginFileWatcher] 已停止监控");
    }

    /// <summary>
    /// 扫描已有插件 — 启动时加载约定文件夹中已存在的 plugin.yaml
    /// </summary>
    internal void ScanExistingPlugins() {
        if (!_fs.DirectoryExists(_pluginsDir)) {
            return;
        }

        var yamlFiles = _fs.GetFiles(_pluginsDir, "plugin.yaml", SearchOption.AllDirectories);
        foreach (var yamlPath in yamlFiles) {
            LoadPluginFromYaml(yamlPath);
        }

        _logger?.LogInformation("[PluginFileWatcher] 扫描到 {Count} 个已有插件", yamlFiles.Length);
    }

    /// <summary>
    /// 从 plugin.yaml 加载插件 — 解析 YAML，注册到 DynamicPluginRegistry
    /// handler 由 IAotHandlerRegistry（源码生成器编译期生成）提供，不需要反射加载 DLL
    /// </summary>
    internal void LoadPluginFromYaml(string yamlPath) {
        try {
            var yamlContent = _fs.ReadAllText(yamlPath).GetAwaiter().GetResult();
            var config = ParseYaml(yamlContent);

            if (string.IsNullOrWhiteSpace(config.Name) || string.IsNullOrWhiteSpace(config.NodeId)) {
                _logger?.LogWarning("[PluginFileWatcher] plugin.yaml 缺少必填字段: {Path}", yamlPath);
                return;
            }

            var pluginDir = Path.GetDirectoryName(yamlPath) ?? string.Empty;
            var version = Version.TryParse(config.Version, out var v) ? v : new Version(1, 0, 0);

            var capabilities = config.Capabilities.Count > 0
                ? config.Capabilities.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
                : FrozenSet<string>.Empty;

            var existing = _registry.InspectPlugin(config.Name);
            if (existing is not null) {
                _registry.StopPlugin(config.Name);
                _registry.UndefinePlugin(config.Name);
            }

            _registry.DefinePlugin(
                config.Name,
                pluginDir,
                version,
                config.EntryType,
                config.Methods.Count > 0 ? config.Methods[0].EntryMethod : "ExecuteAsync",
                capabilities);

            _registry.RunPlugin(config.Name);

            _logger?.LogInformation("[PluginFileWatcher] 加载插件: {Name} (node={NodeId})", config.Name, config.NodeId);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[PluginFileWatcher] 加载 plugin.yaml 失败: {Path}", yamlPath);
        }
    }

    /// <summary>
    /// 卸载插件 — 从 DynamicPluginRegistry 移除
    /// </summary>
    internal void UnloadPlugin(string pluginName) {
        _registry.StopPlugin(pluginName);
        _registry.UndefinePlugin(pluginName);
        _logger?.LogInformation("[PluginFileWatcher] 卸载插件: {Name}", pluginName);
    }

    private void OnPluginYamlCreated(object? sender, FileChangedEventArgs e) {
        _logger?.LogInformation("[PluginFileWatcher] 检测到新 plugin.yaml: {Path}", e.FullPath);
        LoadPluginFromYaml(e.FullPath);
    }

    private void OnPluginYamlChanged(object? sender, FileChangedEventArgs e) {
        _logger?.LogInformation("[PluginFileWatcher] 检测到 plugin.yaml 变更: {Path}", e.FullPath);
        LoadPluginFromYaml(e.FullPath);
    }

    private void OnPluginYamlDeleted(object? sender, FileChangedEventArgs e) {
        var pluginName = Path.GetFileName(Path.GetDirectoryName(e.FullPath)) ?? string.Empty;
        _logger?.LogInformation("[PluginFileWatcher] 检测到 plugin.yaml 删除: {Path}", e.FullPath);
        UnloadPlugin(pluginName);
    }

    private static PluginYamlConfig ParseYaml(string yamlContent) {
        var yaml = new YamlStream();
        using var reader = new StringReader(yamlContent);
        yaml.Load(reader);

        if (yaml.Documents.Count == 0) {
            return new PluginYamlConfig();
        }

        if (yaml.Documents[0].RootNode is not YamlMappingNode root) {
            return new PluginYamlConfig();
        }

        var config = new PluginYamlConfig();
        var children = root.Children;

        if (children.TryGetValue(new YamlScalarNode("name"), out var nameNode)) {
            config.Name = nameNode.ToString();
        }
        if (children.TryGetValue(new YamlScalarNode("version"), out var versionNode)) {
            config.Version = versionNode.ToString();
        }
        if (children.TryGetValue(new YamlScalarNode("node-id"), out var nodeIdNode)) {
            config.NodeId = nodeIdNode.ToString();
        }
        if (children.TryGetValue(new YamlScalarNode("assembly-path"), out var assemblyPathNode)) {
            config.AssemblyPath = assemblyPathNode.ToString();
        }
        if (children.TryGetValue(new YamlScalarNode("entry-type"), out var entryTypeNode)) {
            config.EntryType = entryTypeNode.ToString();
        }
        if (children.TryGetValue(new YamlScalarNode("capabilities"), out var capabilitiesNode)
            && capabilitiesNode is YamlSequenceNode capSeq) {
            config.Capabilities = capSeq.Select(n => n.ToString()).ToList();
        }
        if (children.TryGetValue(new YamlScalarNode("methods"), out var methodsNode)
            && methodsNode is YamlSequenceNode methodSeq) {
            foreach (var methodNode in methodSeq.OfType<YamlMappingNode>()) {
                var methodConfig = new PluginMethodConfig();
                if (methodNode.Children.TryGetValue(new YamlScalarNode("name"), out var mNameNode)) {
                    methodConfig.Name = mNameNode.ToString();
                }
                if (methodNode.Children.TryGetValue(new YamlScalarNode("entry-method"), out var mEntryNode)) {
                    methodConfig.EntryMethod = mEntryNode.ToString();
                }
                config.Methods.Add(methodConfig);
            }
        }

        return config;
    }

    /// <summary>
    /// 异步释放 — 停止监控
    /// </summary>
    public async ValueTask DisposeAsync() {
        await StopWatchingAsync().ConfigureAwait(false);
    }
}
