namespace Core.Prompts;

/// <summary>
/// 系统提示词构建器 - 负责组合所有提示词部分
/// </summary>
[Register]
public sealed partial class SystemPromptBuilder {
    private readonly List<SystemPromptSection> _sections = [];
    private readonly Dictionary<string, string?> _cache = [];
    private readonly Dictionary<string, string?> _dynamicCache = [];
    [Inject] private readonly ILogger<SystemPromptBuilder>? _logger;

    public SystemPromptBuilder(ILogger<SystemPromptBuilder>? logger = null, ISystemPromptProvider? provider = null) {
        _logger = logger;
        if (provider is not null)
        {
            AddFromProvider(provider);
        }
    }

    /// <summary>
    /// 添加提示词部分
    /// </summary>
    public SystemPromptBuilder AddSection(SystemPromptSection section) {
        ArgumentNullException.ThrowIfNull(section);
        _sections.Add(section);
        return this;
    }

    /// <summary>
    /// 批量添加提示词部分
    /// </summary>
    public SystemPromptBuilder AddSections(IEnumerable<SystemPromptSection> sections) {
        ArgumentNullException.ThrowIfNull(sections);
        _sections.AddRange(sections);
        return this;
    }

    /// <summary>
    /// 从提供者添加提示词部分
    /// </summary>
    public SystemPromptBuilder AddFromProvider(ISystemPromptProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        _sections.AddRange(provider.GetSections());
        return this;
    }

    /// <summary>
    /// 构建最终系统提示词
    /// </summary>
    public string Build() {
        var (staticPrefix, dynamicSuffix) = BuildPartitioned();
        if (string.IsNullOrWhiteSpace(dynamicSuffix)) return staticPrefix;
        if (string.IsNullOrWhiteSpace(staticPrefix)) return dynamicSuffix;
        return $"{staticPrefix}\n\n{dynamicSuffix}";
    }

    /// <summary>
    /// 分区构建系统提示词 - 返回 (静态前缀, 动态后缀)
    /// 静态前缀在会话期间保持不变，用于前缀缓存命中
    /// 动态后缀每轮可能变化，不影响静态前缀的缓存
    /// </summary>
    public (string StaticPrefix, string DynamicSuffix) BuildPartitioned() {
        var staticParts = new List<string>();
        var dynamicParts = new List<string>();

        foreach (var section in _sections) {
            var content = ComputeSection(section);
            if (string.IsNullOrWhiteSpace(content)) continue;

            if (section.CacheBreak) {
                dynamicParts.Add(content);
            } else {
                staticParts.Add(content);
            }
        }

        return (
            string.Join("\n\n", staticParts),
            string.Join("\n\n", dynamicParts)
        );
    }

    /// <summary>
    /// 异步分区构建系统提示词 — 避免同步阻塞异步 I/O（如文件读取、网络调用）
    /// </summary>
    public async Task<(string StaticPrefix, string DynamicSuffix)> BuildPartitionedAsync() {
        var staticParts = new List<string>();
        var dynamicParts = new List<string>();

        foreach (var section in _sections) {
            var content = await ComputeSectionAsync(section).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) continue;

            if (section.CacheBreak) {
                dynamicParts.Add(content);
            } else {
                staticParts.Add(content);
            }
        }

        return (
            string.Join("\n\n", staticParts),
            string.Join("\n\n", dynamicParts)
        );
    }

    /// <summary>
    /// 异步构建系统提示词
    /// </summary>
    public async Task<string> BuildAsync(CancellationToken cancellationToken = default) {
        var processedSections = _sections
            .Select(ComputeSection)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToList();

        return await Task.FromResult(string.Join("\n\n", processedSections)).ConfigureAwait(false);
    }

    private string? ComputeSection(SystemPromptSection section) {
        if (section.CacheBreak) {
            var content = section.Compute();
            if (_dynamicCache.TryGetValue(section.Name, out var prev) && content == prev) {
                _logger?.LogDebug("[SystemPrompt] 动态部分 '{SectionName}' 内容未变，复用缓存", section.Name);
                return prev;
            }

            _dynamicCache[section.Name] = content;
            _logger?.LogDebug("[SystemPrompt] 动态部分 '{SectionName}' 已计算", section.Name);
            return content;
        }

        if (_cache.TryGetValue(section.Name, out var cached)) {
            return cached;
        }

        var computed = section.Compute();
        _cache[section.Name] = computed;
        _logger?.LogDebug("[SystemPrompt] 缓存部分 '{SectionName}' 已计算并缓存", section.Name);
        return computed;
    }

    /// <summary>
    /// 异步计算单个 section — 优先使用 ComputeValueTaskAsync 避免同步阻塞
    /// </summary>
    private async ValueTask<string?> ComputeSectionAsync(SystemPromptSection section) {
        if (section.CacheBreak) {
            var content = await section.ComputeValueTaskAsync().ConfigureAwait(false);
            if (_dynamicCache.TryGetValue(section.Name, out var prev) && content == prev) {
                _logger?.LogDebug("[SystemPrompt] 动态部分 '{SectionName}' 内容未变，复用缓存", section.Name);
                return prev;
            }

            _dynamicCache[section.Name] = content;
            _logger?.LogDebug("[SystemPrompt] 动态部分 '{SectionName}' 已计算", section.Name);
            return content;
        }

        if (_cache.TryGetValue(section.Name, out var cached)) {
            return cached;
        }

        var computed = await section.ComputeValueTaskAsync().ConfigureAwait(false);
        _cache[section.Name] = computed;
        _logger?.LogDebug("[SystemPrompt] 缓存部分 '{SectionName}' 已计算并缓存", section.Name);
        return computed;
    }

    /// <summary>
    /// 清除缓存（在 /clear 或 /compact 时调用）
    /// </summary>
    public void ClearCache() {
        _cache.Clear();
        _dynamicCache.Clear();
        _logger?.LogDebug("[SystemPrompt] 缓存已清除");
    }

    /// <summary>
    /// 获取缓存的部分内容
    /// </summary>
    public IReadOnlyDictionary<string, string?> GetCachedSections() {
        return _cache.ToFrozenDictionary();
    }
}
