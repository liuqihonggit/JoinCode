
namespace Core.Context;

/// <summary>
/// 上下文层级管理器实现
/// 管理多层上下文结构（Detailed -> Summary -> Index）
/// 线程安全，使用 SemaphoreSlim 优化异步并发性能
/// </summary>
[Register(typeof(IContextHierarchy), JoinCode.Abstractions.Attributes.ServiceLifetime.Scoped)]
public sealed partial class ContextHierarchy : IContextHierarchy, IDisposable
{
    private readonly List<IContextLayer> _layers = new();
    private readonly Dictionary<ContextLayerType, IContextLayer> _layerDict = new();
    private readonly SemaphoreSlim _lock;
    [Inject] private readonly ILogger<ContextHierarchy>? _logger;
    private readonly ContextHierarchyOptions _options;

    /// <inheritdoc />
    public int TokenThreshold { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public ContextHierarchy(

        IOptions<ContextHierarchyOptions>? options = null,
        ILogger<ContextHierarchy>? logger = null)
    {

        _lock = new SemaphoreSlim(1, 1);
        _options = options?.Value ?? new ContextHierarchyOptions();
        _logger = logger;
        TokenThreshold = _options.TokenThreshold;
    }

    /// <summary>
    /// 创建带默认配置的 ContextHierarchy
    /// </summary>
    public static ContextHierarchy Create(
        ContextHierarchyOptions? options = null,
        ILogger<ContextHierarchy>? logger = null)
    {
        return new ContextHierarchy(
            Microsoft.Extensions.Options.Options.Create(options ?? new ContextHierarchyOptions()),
            logger);
    }

    /// <inheritdoc />
    public async Task AddLayerAsync(IContextLayer layer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(layer);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 移除同类型的现有层级
            if (_layerDict.Remove(layer.LayerType, out var existingLayer))
            {
                _layers.Remove(existingLayer);
            }

            // 按层级类型排序插入（保持列表有序）
            InsertSorted(layer);
            _layerDict[layer.LayerType] = layer;

            _logger?.LogDebug(
                "[ContextHierarchy] 添加层级 {LayerType}, Token数: {TokenCount}",
                layer.LayerType,
                layer.TokenCount);

            // 检查是否需要自动压缩
            if (_options.AutoCompressionEnabled)
            {
                await CheckAndTriggerAutoCompressionAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveLayerAsync(ContextLayerType layerType, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_layerDict.Remove(layerType, out var layer))
            {
                _logger?.LogWarning(
                    "[ContextHierarchy] 尝试移除不存在的层级: {LayerType}",
                    layerType);
                return false;
            }

            _layers.Remove(layer);
            _logger?.LogDebug(
                "[ContextHierarchy] 移除层级 {LayerType}",
                layerType);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IContextLayer?> GetLayerAsync(ContextLayerType layerType, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _layerDict.TryGetValue(layerType, out var layer);
            return layer;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IContextLayer>> GetLayersAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _layers;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IContextLayer?> GetCurrentLayerAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _layers.Count > 0 ? _layers[_layers.Count - 1] : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IContextLayer> PromoteToLayerAsync(
        ContextLayerType targetLayer,
        Func<string, ContextLayerType, string> compressionFunc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(compressionFunc);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _layers.Count > 0 ? _layers[_layers.Count - 1] : null;
            if (current == null)
            {
                throw new InvalidOperationException("没有可用的当前层级进行提升");
            }

            if (current.LayerType >= targetLayer)
            {
                throw new InvalidOperationException(
                    $"无法提升到相同或更低层级: 当前 {current.LayerType}, 目标 {targetLayer}");
            }

            // 执行压缩
            var compressedContent = compressionFunc(current.Content, targetLayer);

            var promotedLayer = new ContextLayer(
                targetLayer,
                compressedContent,
                $"Promoted_{targetLayer}_{Guid.NewGuid():N}");

            // 移除原始层级并添加压缩后的层级
            _layers.Remove(current);
            _layerDict.Remove(current.LayerType);

            InsertSorted(promotedLayer);
            _layerDict[targetLayer] = promotedLayer;

            _logger?.LogInformation(
                "[ContextHierarchy] 层级提升: {SourceLayer} -> {TargetLayer}, " +
                "Token: {SourceTokens} -> {TargetTokens}",
                current.LayerType,
                targetLayer,
                current.TokenCount,
                promotedLayer.TokenCount);

            return promotedLayer;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DemoteToLayerAsync(ContextLayerType sourceLayer, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_layerDict.TryGetValue(sourceLayer, out var layer))
            {
                _logger?.LogWarning(
                    "[ContextHierarchy] 尝试恢复不存在的层级: {LayerType}",
                    sourceLayer);
                return false;
            }

            // 尝试解压
            if (!layer.IsCompressed)
            {
                _logger?.LogWarning(
                    "[ContextHierarchy] 层级 {LayerType} 未压缩，无需恢复",
                    sourceLayer);
                return false;
            }

            layer.Decompress();

            _logger?.LogInformation(
                "[ContextHierarchy] 层级恢复: {SourceLayer} -> Detailed",
                sourceLayer);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> GetEffectiveContextAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_layers.Count == 0)
            {
                return string.Empty;
            }

            // 列表已按层级类型排序（低到高），所以从后往前遍历（高到低）
            var sb = new StringBuilder();
            bool first = true;

            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var layer = _layers[i];
                if (string.IsNullOrWhiteSpace(layer.Content))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append("\n\n");
                }
                first = false;

                sb.Append('[').Append(layer.LayerType).Append("] ").Append(layer.Content);
            }

            return sb.ToString();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> GetTotalTokenCountAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var total = 0;
            for (int i = 0; i < _layers.Count; i++)
            {
                total += _layers[i].TokenCount;
            }
            return total;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 按层级类型排序插入（保持列表有序）
    /// </summary>
    private void InsertSorted(IContextLayer layer)
    {
        // 二分查找插入位置
        int left = 0;
        int right = _layers.Count;

        while (left < right)
        {
            int mid = left + (right - left) / 2;
            if (_layers[mid].LayerType < layer.LayerType)
            {
                left = mid + 1;
            }
            else
                       {
                right = mid;
            }
        }

        _layers.Insert(left, layer);
    }

    /// <summary>
    /// 异步检查并触发自动压缩
    /// 注意：此方法应在已持有锁的情况下调用，不重复获取锁
    /// </summary>
    private async Task CheckAndTriggerAutoCompressionAsync(CancellationToken ct = default)
    {
        // 计算总token数（不获取锁，因为调用方已持有）
        var totalTokens = 0;
        for (int i = 0; i < _layers.Count; i++)
        {
            totalTokens += _layers[i].TokenCount;
        }

        if (totalTokens <= TokenThreshold)
        {
            return;
        }

        _logger?.LogInformation(
            "[ContextHierarchy] Token 总数 ({TotalTokens}) 超过阈值 ({Threshold})，触发自动压缩",
            totalTokens,
            TokenThreshold);

        // 尝试压缩最详细的层级
        if (_layerDict.TryGetValue(ContextLayerType.Detailed, out var detailedLayer))
        {
            try
            {
                detailedLayer.Compress();
                _logger?.LogInformation(
                    "[ContextHierarchy] 自动压缩完成: {LayerType} -> {TokenCount} tokens",
                    detailedLayer.LayerType,
                    detailedLayer.TokenCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "[ContextHierarchy] 自动压缩失败: {LayerType}",
                    detailedLayer.LayerType);
            }
        }
    }

    public void Dispose() => _lock.Dispose();
}
