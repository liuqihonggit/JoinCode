
namespace Core.Hooks.Configuration;

/// <summary>
/// 钩子配置管理器接口
/// </summary>
public interface IHookConfigurationManager
{
    /// <summary>
    /// 加载所有钩子配置
    /// </summary>
    Task<HookConfigurationGroup> LoadAllHooksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取特定事件的钩子
    /// </summary>
    Task<IReadOnlyList<SourcedHookConfig>> GetHooksForEventAsync(
        HookEvent hookEvent,
        string? matcher = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取排序后的匹配器
    /// </summary>
    Task<IReadOnlyList<string>> GetSortedMatchersAsync(
        HookEvent hookEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加钩子配置
    /// </summary>
    Task AddHookAsync(
        HookSource source,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除钩子配置
    /// </summary>
    Task RemoveHookAsync(
        HookSource source,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除缓存
    /// </summary>
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子配置管理器实现
/// </summary>
public sealed partial class HookConfigurationManager : IHookConfigurationManager, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<HookConfigurationManager>? _logger;
    private readonly ConcurrentDictionary<HookSource, IHookConfigurationProvider> _providers;
    private readonly ConcurrentDictionary<string, HookConfigurationGroup> _cache;

    private const string CacheKey = "all_hooks";

    public HookConfigurationManager(
        IFileSystem fs,
        ILogger<HookConfigurationManager>? logger = null)
    {
        _lock = new SemaphoreSlim(1, 1);
        _fs = fs;
        _logger = logger;
        _providers = new ConcurrentDictionary<HookSource, IHookConfigurationProvider>();
        _cache = new ConcurrentDictionary<string, HookConfigurationGroup>();
    }

    /// <summary>
    /// 注册配置提供者
    /// </summary>
    public void RegisterProvider(HookSource source, IHookConfigurationProvider provider)
    {
        _providers[source] = provider;
        _logger?.LogDebug("Registered hook configuration provider for source: {Source}", source);
    }

    /// <inheritdoc />
    public async Task<HookConfigurationGroup> LoadAllHooksAsync(CancellationToken cancellationToken = default)
    {
        // 检查缓存
        if (_cache.TryGetValue(CacheKey, out var cached))
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 双重检查
            if (_cache.TryGetValue(CacheKey, out cached))
            {
                return cached;
            }

            var group = new HookConfigurationGroup();

            // 按优先级顺序加载各来源
            var sources = Enum.GetValues<HookSource>()
                .OrderBy(s => s.GetPriority());

            foreach (var source in sources)
            {
                if (_providers.TryGetValue(source, out var provider))
                {
                    try
                    {
                        var hooks = await provider.LoadHooksAsync(cancellationToken).ConfigureAwait(false);
                        foreach (var hook in hooks)
                        {
                            group.Add(hook);
                        }

                        _logger?.LogDebug(
                            "Loaded {Count} hooks from {Source}",
                            hooks.Count,
                            source);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(
                            ex,
                            "Failed to load hooks from {Source}",
                            source);
                    }
                }
            }

            _cache[CacheKey] = group;
            return group;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourcedHookConfig>> GetHooksForEventAsync(
        HookEvent hookEvent,
        string? matcher = null,
        CancellationToken cancellationToken = default)
    {
        var group = await LoadAllHooksAsync(cancellationToken).ConfigureAwait(false);
        return group.GetHooks(hookEvent, matcher);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSortedMatchersAsync(
        HookEvent hookEvent,
        CancellationToken cancellationToken = default)
    {
        var group = await LoadAllHooksAsync(cancellationToken).ConfigureAwait(false);
        return group.GetSortedMatchers(hookEvent);
    }

    /// <inheritdoc />
    public async Task AddHookAsync(
        HookSource source,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(source, out var provider))
        {
            throw new InvalidOperationException($"No provider registered for source: {source}");
        }

        if (!source.IsEditable())
        {
            throw new InvalidOperationException($"Source {source} is not editable");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await provider.AddHookAsync(hookEvent, matcher, hook, cancellationToken).ConfigureAwait(false);
            _cache.Clear();

            _logger?.LogInformation(
                "Added hook to {Source} for event {Event}: {HookDisplay}",
                source,
                hookEvent,
                hook.GetDisplayText());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveHookAsync(
        HookSource source,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(source, out var provider))
        {
            throw new InvalidOperationException($"No provider registered for source: {source}");
        }

        if (!source.IsEditable())
        {
            throw new InvalidOperationException($"Source {source} is not editable");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await provider.RemoveHookAsync(hookEvent, matcher, hook, cancellationToken).ConfigureAwait(false);
            _cache.Clear();

            _logger?.LogInformation(
                "Removed hook from {Source} for event {Event}: {HookDisplay}",
                source,
                hookEvent,
                hook.GetDisplayText());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        _logger?.LogDebug("Hook configuration cache invalidated");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}

/// <summary>
/// 钩子配置提供者接口
/// </summary>
public interface IHookConfigurationProvider
{
    /// <summary>
    /// 加载钩子配置
    /// </summary>
    Task<List<SourcedHookConfig>> LoadHooksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加钩子
    /// </summary>
    Task AddHookAsync(
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除钩子
    /// </summary>
    Task RemoveHookAsync(
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON 文件配置提供者
/// </summary>
public partial class JsonFileHookConfigurationProvider : IHookConfigurationProvider
{
    private readonly string _filePath;
    private readonly HookSource _source;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    public JsonFileHookConfigurationProvider(
        string filePath,
        HookSource source,
        IFileSystem fs,
        ILogger? logger = null)
    {
        _filePath = filePath;
        _source = source;
        _fs = fs;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<List<SourcedHookConfig>> LoadHooksAsync(CancellationToken cancellationToken = default)
    {
        var hooks = new List<SourcedHookConfig>();

        if (!_fs.FileExists(_filePath))
        {
            return Task.FromResult(hooks);
        }

        try
        {
            var json = _fs.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize(json, HooksJsonContext.Default.HookSettingsFile);

            if (settings?.Hooks == null)
            {
                return Task.FromResult(hooks);
            }

            foreach (var eventEntry in settings.Hooks)
            {
                var hookEvent = HookEventExtensions.FromValue(eventEntry.Key);
                if (hookEvent is null)
                {
                    _logger?.LogWarning("Unknown hook event: {Event}", eventEntry.Key);
                    continue;
                }

                foreach (var matcher in eventEntry.Value)
                {
                    foreach (var command in matcher.Hooks)
                    {
                        hooks.Add(new SourcedHookConfig
                        {
                            Event = hookEvent.Value,
                            Matcher = matcher.Matcher,
                            Command = command,
                            Source = _source
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load hooks from {FilePath}", _filePath);
        }

        return Task.FromResult(hooks);
    }

    /// <inheritdoc />
    public async Task AddHookAsync(
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync().ConfigureAwait(false);
        var eventKey = hookEvent.ToEventName();
        if (!settings.Hooks.TryGetValue(eventKey, out var matchers))
        {
            matchers = new List<HookMatcher>();
            settings.Hooks[eventKey] = matchers;
        }

        var existingMatcher = matchers.FirstOrDefault(m => m.Matcher == matcher);
        if (existingMatcher == null)
        {
            existingMatcher = new HookMatcher
            {
                Matcher = matcher,
                Hooks = new List<HookCommand>()
            };
            matchers.Add(existingMatcher);
        }

        existingMatcher.Hooks.Add(hook);

        await SaveSettingsAsync(settings).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveHookAsync(
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync().ConfigureAwait(false);

        var eventKey = hookEvent.ToEventName();
        if (!settings.Hooks.TryGetValue(eventKey, out var matchers))
        {
            return;
        }

        var existingMatcher = matchers.FirstOrDefault(m => m.Matcher == matcher);
        if (existingMatcher == null)
        {
            return;
        }

        existingMatcher.Hooks.RemoveAll(h => h.IsEqualTo(hook));

        if (existingMatcher.Hooks.Count == 0)
        {
            matchers.Remove(existingMatcher);
        }

        if (matchers.Count == 0)
        {
            settings.Hooks.Remove(eventKey);
        }

        await SaveSettingsAsync(settings).ConfigureAwait(false);
    }

    private async Task<HookSettingsFile> LoadSettingsAsync()
    {
        if (!_fs.FileExists(_filePath))
        {
            return new HookSettingsFile { Hooks = new Dictionary<string, List<HookMatcher>>() };
        }

        return await _fs.ReadAndDeserializeAsync(_filePath, HooksJsonContext.Default.HookSettingsFile).ConfigureAwait(false)
            ?? new HookSettingsFile { Hooks = new Dictionary<string, List<HookMatcher>>() };
    }

    private async Task SaveSettingsAsync(HookSettingsFile settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        DirectoryHelper.EnsureDirectoryExists(_fs, directory);

        var json = JsonSerializer.Serialize(settings, HooksJsonContext.Default.HookSettingsFile);
        await _fs.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
    }
}

/// <summary>
/// 钩子设置文件结构
/// </summary>
public partial class HookSettingsFile
{
    public Dictionary<string, List<HookMatcher>> Hooks { get; set; } = new();
}
