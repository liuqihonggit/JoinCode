namespace Core.Bridge;

/// <summary>
/// Bridge 工作 API 客户端封装 — ACK/StopWork + 重试 + 错误吞咽
/// 从 BridgeMain 提取,降低大类方法数和 API 调用逻辑复杂度
/// </summary>
internal sealed class BridgeWorkApiClient {
    private readonly BridgeApiClient _apiClient;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造 Bridge 工作 API 客户端。
    /// </summary>
    /// <param name="apiClient">Bridge API 客户端。</param>
    /// <param name="logger">日志记录器，可选。</param>
    public BridgeWorkApiClient(BridgeApiClient apiClient, ILogger? logger) {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>ACK 工作 — 吞咽异常仅记录警告</summary>
    public async Task AckWorkAsync(string? environmentId, string workId, string sessionToken, CancellationToken ct) {
        try {
            await _apiClient.AcknowledgeWorkAsync(
                environmentId ?? throw new InvalidOperationException("EnvironmentId not set"), workId, sessionToken, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "BridgeMain: ACK failed for work {WorkId}", workId);
        }
    }

    /// <summary>带重试的停止工作 — 最多重试 3 次,指数退避</summary>
    public async Task StopWorkWithRetryAsync(string? environmentId, string workId, CancellationToken ct, int baseDelayMs = 1000) {
        if (environmentId is null) return;

        for (var attempt = 0; attempt < 3; attempt++) {
            try {
                await _apiClient.StopWorkAsync(environmentId, workId, ct).ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                if (attempt < 2) {
                    var delayMs = baseDelayMs * (1 << attempt);
                    _logger?.LogDebug(ex,
                        "BridgeMain: stopWork attempt {Attempt} failed for {WorkId}, retrying in {Delay}ms",
                        attempt + 1, workId, delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                } else {
                    _logger?.LogDebug(ex, "BridgeMain: stopWork failed for {WorkId} after 3 attempts (non-fatal)", workId);
                }
            }
        }
    }

    /// <summary>单次停止工作 + 吞咽异常</summary>
    public async Task SafeStopWorkAsync(string? environmentId, string workId, CancellationToken ct) {
        if (environmentId is null) return;
        try {
            await _apiClient.StopWorkAsync(environmentId, workId, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "BridgeMain: stopWork failed for {WorkId} (non-fatal)", workId);
        }
    }
}