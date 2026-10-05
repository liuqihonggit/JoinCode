
namespace Core.Context;

/// <summary>
/// 上下文层级管理器实现
/// 管理多层上下文结构（Detailed -> Summary -> Index）
/// Actor 模式 — 内部组合 HierarchyActor(继承 ActorBase)串行化所有操作,获得监督/背压/生命周期/错误恢复
/// </summary>
[Register(typeof(IContextHierarchy), JoinCode.Abstractions.Attributes.ServiceLifetime.Scoped)]
public sealed partial class ContextHierarchy : ServiceEntity, IContextHierarchy, IDisposable {
    private readonly List<IContextLayer> _layers = new();
    private readonly Dictionary<ContextLayerType, IContextLayer> _layerDict = new();
    private readonly HierarchyActor _actor;
    private readonly ILogger<ContextHierarchy>? _logger;
    private readonly ContextHierarchyOptions _options;
    private volatile bool _disposed;

    /// <summary>
    /// 内部 Actor — 继承 ActorBase,Consumer 线程独占 _layers/_layerDict,消除 AsyncLock
    /// </summary>
    private sealed class HierarchyActor(ContextHierarchy owner, ILogger? logger)
        : ActorBase<HierarchyActor.Command, Unit>(logger: logger) {

        /// <summary>命令 — 携带操作委托和回复通道</summary>
        public sealed class Command(Action<ContextHierarchy, TaskCompletionSource<object?>> execute, TaskCompletionSource<object?> tcs) {
            public readonly Action<ContextHierarchy, TaskCompletionSource<object?>> Execute = execute;
            public readonly TaskCompletionSource<object?> Tcs = tcs;
        }

        protected override void Handle(Command command, CancellationToken ct) {
            try { command.Execute(owner, command.Tcs); }
            catch (Exception ex) { command.Tcs.TrySetException(ex); }
        }
    }

    /// <inheritdoc />
    public int TokenThreshold { get; set; }

    /// <summary>
    /// 构造函数 — 启动内部 Actor
    /// </summary>
    public ContextHierarchy(

        IOptions<ContextHierarchyOptions>? options = null,
        ILogger<ContextHierarchy>? logger = null) {

        _options = options?.Value ?? new ContextHierarchyOptions();
        _logger = logger;
        TokenThreshold = _options.TokenThreshold;
        _actor = new HierarchyActor(this, logger);
    }

    /// <summary>
    /// 创建带默认配置的 ContextHierarchy
    /// </summary>
    public static ContextHierarchy Create(
        ContextHierarchyOptions? options = null,
        ILogger<ContextHierarchy>? logger = null) {
        return new ContextHierarchy(
            Microsoft.Extensions.Options.Options.Create(options ?? new ContextHierarchyOptions()),
            logger);
    }

    /// <inheritdoc />
    public async Task AddLayerAsync(IContextLayer layer, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(layer);
        await AskAsync(self => {
            if (self._layerDict.Remove(layer.LayerType, out var existingLayer)) {
                self._layers.Remove(existingLayer);
            }

            self.InsertSorted(layer);
            self._layerDict[layer.LayerType] = layer;

            self._logger?.LogDebug(
                "[ContextHierarchy] 添加层级 {LayerType}, Token数: {TokenCount}",
                layer.LayerType,
                layer.TokenCount);

            if (self._options.AutoCompressionEnabled) {
                self.CheckAndTriggerAutoCompression();
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveLayerAsync(ContextLayerType layerType, CancellationToken ct = default)
        => await AskAsync(self => {
            if (!self._layerDict.Remove(layerType, out var layer)) {
                self._logger?.LogWarning(
                    "[ContextHierarchy] 尝试移除不存在的层级: {LayerType}",
                    layerType);
                return false;
            }

            self._layers.Remove(layer);
            self._logger?.LogDebug(
                "[ContextHierarchy] 移除层级 {LayerType}",
                layerType);
            return true;
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IContextLayer?> GetLayerAsync(ContextLayerType layerType, CancellationToken ct = default)
        => await AskAsync(self => {
            self._layerDict.TryGetValue(layerType, out var layer);
            return layer;
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IContextLayer>> GetLayersAsync(CancellationToken ct = default)
        => await AskAsync(self => (IReadOnlyList<IContextLayer>)self._layers, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IContextLayer?> GetCurrentLayerAsync(CancellationToken ct = default)
        => await AskAsync(self => self._layers.Count > 0 ? self._layers[^1] : null, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IContextLayer> PromoteToLayerAsync(
        ContextLayerType targetLayer,
        Func<string, ContextLayerType, string> compressionFunc,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(compressionFunc);
        return await AskAsync(self => {
            var current = self._layers.Count > 0 ? self._layers[^1] : null;
            if (current == null) {
                throw new InvalidOperationException("[BRN002] 没有可用的当前层级进行提升");
            }

            if (current.LayerType >= targetLayer) {
                throw new InvalidOperationException(
                    $"无法提升到相同或更低层级: 当前 {current.LayerType}, 目标 {targetLayer}");
            }

            var compressedContent = compressionFunc(current.Content, targetLayer);

            var promotedLayer = new ContextLayer(
                targetLayer,
                compressedContent,
                $"Promoted_{targetLayer}_{Guid.NewGuid():N}");

            self._layers.Remove(current);
            self._layerDict.Remove(current.LayerType);

            self.InsertSorted(promotedLayer);
            self._layerDict[targetLayer] = promotedLayer;

            self._logger?.LogInformation(
                "[ContextHierarchy] 层级提升: {SourceLayer} -> {TargetLayer}, " +
                "Token: {SourceTokens} -> {TargetTokens}",
                current.LayerType,
                targetLayer,
                current.TokenCount,
                promotedLayer.TokenCount);

            return promotedLayer;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DemoteToLayerAsync(ContextLayerType sourceLayer, CancellationToken ct = default)
        => await AskAsync(self => {
            if (!self._layerDict.TryGetValue(sourceLayer, out var layer)) {
                self._logger?.LogWarning(
                    "[ContextHierarchy] 尝试恢复不存在的层级: {LayerType}",
                    sourceLayer);
                return false;
            }

            if (!layer.IsCompressed) {
                self._logger?.LogWarning(
                    "[ContextHierarchy] 层级 {LayerType} 未压缩，无需恢复",
                    sourceLayer);
                return false;
            }

            layer.Decompress();

            self._logger?.LogInformation(
                "[ContextHierarchy] 层级恢复: {SourceLayer} -> Detailed",
                sourceLayer);

            return true;
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string> GetEffectiveContextAsync(CancellationToken ct = default)
        => await AskAsync(self => {
            if (self._layers.Count == 0) {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var first = true;

            for (var i = self._layers.Count - 1; i >= 0; i--) {
                var layer = self._layers[i];
                if (string.IsNullOrWhiteSpace(layer.Content)) {
                    continue;
                }

                if (!first) {
                    sb.Append("\n\n");
                }
                first = false;

                sb.Append('[').Append(layer.LayerType).Append("] ").Append(layer.Content);
            }

            return sb.ToString();
        }, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<int> GetTotalTokenCountAsync(CancellationToken ct = default)
        => await AskAsync(self => {
            var total = 0;
            for (var i = 0; i < self._layers.Count; i++) {
                total += self._layers[i].TokenCount;
            }
            return total;
        }, ct).ConfigureAwait(false);

    /// <summary>
    /// 按层级类型排序插入（保持列表有序）— 仅在 Actor Consumer 线程调用
    /// </summary>
    private void InsertSorted(IContextLayer layer) {
        var left = 0;
        var right = _layers.Count;

        while (left < right) {
            var mid = left + (right - left) / 2;
            if (_layers[mid].LayerType < layer.LayerType) {
                left = mid + 1;
            } else {
                right = mid;
            }
        }

        _layers.Insert(left, layer);
    }

    /// <summary>
    /// 检查并触发自动压缩 — 同步,仅在 Actor Consumer 线程调用(无锁)
    /// </summary>
    private void CheckAndTriggerAutoCompression() {
        var totalTokens = 0;
        for (var i = 0; i < _layers.Count; i++) {
            totalTokens += _layers[i].TokenCount;
        }

        if (totalTokens <= TokenThreshold) {
            return;
        }

        _logger?.LogInformation(
            "[ContextHierarchy] Token 总数 ({TotalTokens}) 超过阈值 ({Threshold})，触发自动压缩",
            totalTokens,
            TokenThreshold);

        if (_layerDict.TryGetValue(ContextLayerType.Detailed, out var detailedLayer)) {
            try {
                detailedLayer.Compress();
                _logger?.LogInformation(
                    "[ContextHierarchy] 自动压缩完成: {LayerType} -> {TokenCount} tokens",
                    detailedLayer.LayerType,
                    detailedLayer.TokenCount);
            } catch (Exception ex) {
                _logger?.LogError(
                    ex,
                    "[ContextHierarchy] 自动压缩失败: {LayerType}",
                    detailedLayer.LayerType);
            }
        }
    }

    /// <summary>Actor 请求-响应(无返回值):TrySend 命令 + await tcs.Task;支持取消</summary>
    private async Task AskAsync(Action<ContextHierarchy> action, CancellationToken ct) {
        if (_disposed) throw new ObjectDisposedException(nameof(ContextHierarchy));
        ct.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        if (!_actor.TrySend(new HierarchyActor.Command((self, t) => { action(self); t.SetResult(null); }, tcs)))
            throw new ObjectDisposedException(nameof(ContextHierarchy));
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Actor 请求-响应(带返回值):TrySend 命令 + await tcs.Task;支持取消</summary>
    private async Task<TResult> AskAsync<TResult>(Func<ContextHierarchy, TResult> action, CancellationToken ct) {
        if (_disposed) throw new ObjectDisposedException(nameof(ContextHierarchy));
        ct.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        if (!_actor.TrySend(new HierarchyActor.Command((self, t) => t.SetResult(action(self)), tcs)))
            throw new ObjectDisposedException(nameof(ContextHierarchy));
        return (TResult)(await tcs.Task.ConfigureAwait(false))!;
    }

    /// <summary>
    /// 同步释放 — 标记已释放,Actor 由 DisposeAsync 异步释放
    /// </summary>
    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        base.Dispose();
    }

    /// <summary>
    /// 异步释放 Actor(等 Consumer 退出 + in-flight 完成);幂等
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
