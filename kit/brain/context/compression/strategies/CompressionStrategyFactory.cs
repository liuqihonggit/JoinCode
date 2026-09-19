
namespace Core.Context.Compression;

/// <summary>
/// 压缩策略工厂实现
/// </summary>
[Register(typeof(ICompressionStrategyFactory), ServiceLifetime.Singleton)]
public partial class CompressionStrategyFactory : ServiceEntity, ICompressionStrategyFactory {
    private readonly Dictionary<string, ICompressionStrategy> _strategies;
    private readonly Dictionary<ContentType, List<ICompressionStrategy>> _strategiesByType;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造压缩策略工厂，并注册默认策略
    /// </summary>
    /// <param name="telemetryService">可选的遥测服务</param>
    public CompressionStrategyFactory(ITelemetryService? telemetryService = null) {
        _telemetryService = telemetryService;
        _strategies = new Dictionary<string, ICompressionStrategy>(StringComparer.OrdinalIgnoreCase);
        _strategiesByType = new Dictionary<ContentType, List<ICompressionStrategy>>();

        foreach (var type in Enum.GetValues<ContentType>()) {
            _strategiesByType[type] = new List<ICompressionStrategy>();
        }

        RegisterDefaultStrategies();
    }

    /// <summary>
    /// 获取适合指定内容的策略
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>压缩策略，如果没有找到则返回 null</returns>
    public ICompressionStrategy? GetStrategy(string content, ContentType contentType) {
        if (!_strategiesByType.TryGetValue(contentType, out var strategies)) {
            return null;
        }

        var compatibleStrategies = strategies
            .Where(s => s.CanHandle(content, contentType))
            .OrderByDescending(s => s.Priority)
            .ToList();

        if (compatibleStrategies.Count == 0) {
            return null;
        }

        if (compatibleStrategies.Count == 1) {
            RecordStrategySelectionMetrics(compatibleStrategies[0].Name, contentType.ToString());
            return compatibleStrategies[0];
        }

        var selected = SelectBestStrategy(compatibleStrategies, content, contentType);
        if (selected != null) {
            RecordStrategySelectionMetrics(selected.Name, contentType.ToString());
        }
        return selected;
    }

    /// <summary>
    /// 获取指定类型的所有策略，按优先级降序排列
    /// </summary>
    /// <param name="contentType">内容类型</param>
    /// <returns>策略列表</returns>
    public IEnumerable<ICompressionStrategy> GetStrategiesForType(ContentType contentType) {
        return _strategiesByType.TryGetValue(contentType, out var strategies)
            ? strategies.OrderByDescending(s => s.Priority).ToList()
            : Enumerable.Empty<ICompressionStrategy>();
    }

    /// <summary>
    /// 检查指定类型是否存在已注册的策略
    /// </summary>
    /// <param name="contentType">内容类型</param>
    /// <returns>是否存在策略</returns>
    public bool HasStrategyFor(ContentType contentType) {
        return _strategiesByType.TryGetValue(contentType, out var strategies) &&
               strategies.Count > 0;
    }

    /// <summary>
    /// 注册压缩策略
    /// </summary>
    /// <param name="strategy">策略实例</param>
    public void RegisterStrategy(ICompressionStrategy strategy) {
        ArgumentNullException.ThrowIfNull(strategy);

        if (_strategies.ContainsKey(strategy.Name)) {
            throw new InvalidOperationException(
                $"Strategy with name '{strategy.Name}' is already registered.");
        }

        _strategies[strategy.Name] = strategy;

        foreach (var contentType in strategy.SupportedContentTypes) {
            if (_strategiesByType.TryGetValue(contentType, out var list)) {
                list.Add(strategy);
            }
        }
    }

    /// <summary>
    /// 按名称注销策略
    /// </summary>
    /// <param name="strategyName">策略名称</param>
    /// <returns>是否成功注销</returns>
    public bool UnregisterStrategy(string strategyName) {
        if (!_strategies.TryGetValue(strategyName, out var strategy)) {
            return false;
        }

        _strategies.Remove(strategyName);

        foreach (var contentType in strategy.SupportedContentTypes) {
            if (_strategiesByType.TryGetValue(contentType, out var list)) {
                list.Remove(strategy);
            }
        }

        return true;
    }

    /// <summary>
    /// 获取所有已注册的策略，按名称升序排列
    /// </summary>
    /// <returns>所有策略</returns>
    public IEnumerable<ICompressionStrategy> GetAllStrategies() {
        return _strategies.Values.OrderBy(s => s.Name);
    }

    private void RegisterDefaultStrategies() {
        RegisterStrategy(new CodeContentCompressor());
        RegisterStrategy(new DialogueCompressor());
        RegisterStrategy(new ReferenceIndexCompressor());
    }

    private void RecordStrategySelectionMetrics(string strategyName, string contentType)
        => _telemetryService?.RecordCount("compression.strategy.selection.count", new() { ["strategy"] = strategyName, ["content_type"] = contentType }, "count", "Compression strategy selection count");

    private static ICompressionStrategy SelectBestStrategy(
        List<ICompressionStrategy> strategies,
        string content,
        ContentType contentType) {
        if (strategies.Count == 1) {
            return strategies[0];
        }

        var options = new CompressionOptions();
        ICompressionStrategy? bestStrategy = null;
        var bestScore = double.MinValue;

        foreach (var strategy in strategies) {
            try {
                var estimatedRatio = strategy.EstimateCompressionRatio(content, options);
                var score = strategy.Priority * 10 + (1 - estimatedRatio) * 100;

                if (score > bestScore) {
                    bestScore = score;
                    bestStrategy = strategy;
                }
            } catch {
                continue;
            }
        }

        return bestStrategy ?? strategies[0];
    }
}

/// <summary>
/// 压缩策略工厂扩展方法
/// </summary>
public static class CompressionStrategyFactoryExtensions {
    /// <summary>
    /// 批量注册策略
    /// </summary>
    public static void RegisterStrategies(
        this ICompressionStrategyFactory factory,
        IEnumerable<ICompressionStrategy> strategies) {
        foreach (var strategy in strategies) {
            factory.RegisterStrategy(strategy);
        }
    }

    /// <summary>
    /// 获取指定类型的最佳策略
    /// </summary>
    public static ICompressionStrategy? GetBestStrategyForType(
        this ICompressionStrategyFactory factory,
        ContentType contentType) {
        return factory.GetStrategiesForType(contentType).FirstOrDefault();
    }

    /// <summary>
    /// 检查是否存在指定名称的策略
    /// </summary>
    public static bool HasStrategy(
        this ICompressionStrategyFactory factory,
        string strategyName) {
        return factory.GetAllStrategies().Any(s =>
            s.Name.Equals(strategyName, StringComparison.OrdinalIgnoreCase));
    }
}