namespace Core.CostTracking;

/// <summary>
/// 会话成本持久化接口 — 负责保存、恢复、列举和删除会话成本数据
/// </summary>
public interface ISessionCostPersistence {
    /// <summary>
    /// 异步保存当前会话的成本数据到持久化存储
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task SaveCurrentSessionCostsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// 异步恢复指定会话的成本状态
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>恢复的成本统计信息；若不存在或读取失败则返回 null</returns>
    Task<CostStatistics?> RestoreCostStateForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// 异步获取所有已保存成本数据的会话标识列表
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已保存会话标识的只读列表</returns>
    Task<IReadOnlyList<string>> GetSavedSessionIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步删除指定会话的成本数据
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task DeleteSessionCostsAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// 会话成本数据传输对象 — 持久化到 JSON 文件的结构
/// </summary>
public sealed partial class SessionCostData {
    /// <summary>
    /// 会话标识
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 会话成本统计信息
    /// </summary>
    public required CostStatistics Statistics { get; init; }

    /// <summary>
    /// 保存时间戳 (UTC)
    /// </summary>
    public required DateTime SavedAt { get; init; }
}

/// <summary>
/// 会话成本持久化服务 — 将会话成本数据以 JSON 文件形式存储到本地成本目录
/// </summary>
[Register(typeof(ISessionCostPersistence), ServiceLifetime.Singleton)]
public sealed partial class SessionCostPersistence : ServiceEntity, ISessionCostPersistence {
    private readonly IFileOperationService _fileOperationService;
    private readonly string _storageDirectory;
    private readonly ILogger<SessionCostPersistence>? _logger;
    private readonly IClockService _clock;
    private readonly CostTracker _costTracker;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造会话成本持久化服务实例
    /// </summary>
    /// <param name="costTracker">成本跟踪器</param>
    /// <param name="fileOperationService">文件操作服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    public SessionCostPersistence(
        CostTracker costTracker,
        IFileOperationService fileOperationService,
        ILogger<SessionCostPersistence>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null) {
        _costTracker = costTracker ?? throw new ArgumentNullException(nameof(costTracker));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _storageDirectory = AppDataConstants.Paths.JccDirectory;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 异步保存当前会话的成本数据到持久化存储
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SaveCurrentSessionCostsAsync(string sessionId, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try {
            var stats = _costTracker.GetSessionStatistics(sessionId);
            var data = new SessionCostData {
                SessionId = sessionId,
                Statistics = stats,
                SavedAt = _clock.GetUtcNow()
            };

            var costsDir = AppDataConstants.Paths.CostsDirectory;
            if (!_fileOperationService.DirectoryExists(costsDir)) {
                _fileOperationService.CreateDirectory(costsDir);
            }

            var filePath = Path.Combine(costsDir, $"{sessionId}.json");
            var json = JsonSerializer.Serialize(data, CostTrackingJsonContext.Default.SessionCostData);

            var result = await _fileOperationService.WriteFileAsync(filePath, json, ct).ConfigureAwait(false);

            if (result.Success) {
                _logger?.LogInformation("[SessionCostPersistence] 已保存会话 {SessionId} 的成本数据", sessionId);
                RecordCostPersistenceMetrics("save", true);
            } else {
                _logger?.LogError("[SessionCostPersistence] 保存会话 {SessionId} 成本数据失败: {Error}", sessionId, result.ErrorMessage);
                RecordCostPersistenceMetrics("save", false);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[SessionCostPersistence] 保存会话 {SessionId} 成本数据异常", sessionId);
        }
    }

    /// <summary>
    /// 异步恢复指定会话的成本状态
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>恢复的成本统计信息；若不存在或读取失败则返回 null</returns>
    public async Task<CostStatistics?> RestoreCostStateForSessionAsync(string sessionId, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try {
            var filePath = Path.Combine(AppDataConstants.Paths.CostsDirectory, $"{sessionId}.json");

            if (!_fileOperationService.FileExists(filePath)) {
                _logger?.LogDebug("[SessionCostPersistence] 会话 {SessionId} 无保存的成本数据", sessionId);
                return null;
            }

            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: ct).ConfigureAwait(false);

            if (!result.Success) {
                _logger?.LogWarning("[SessionCostPersistence] 读取会话 {SessionId} 成本数据失败: {Error}", sessionId, result.ErrorMessage);
                return null;
            }

            var data = RelaxedJsonSerializer.Deserialize(result.Content, CostTrackingJsonContext.Default.SessionCostData);

            if (data is null) {
                _logger?.LogWarning("[SessionCostPersistence] 反序列化会话 {SessionId} 成本数据失败", sessionId);
                return null;
            }

            _logger?.LogInformation("[SessionCostPersistence] 已恢复会话 {SessionId} 的成本数据 (保存于 {SavedAt})", sessionId, data.SavedAt);
            RecordCostPersistenceMetrics("restore", true);
            return data.Statistics;
        } catch (Exception ex) {
            _logger?.LogError(ex, "[SessionCostPersistence] 恢复会话 {SessionId} 成本数据异常", sessionId);
            return null;
        }
    }

    /// <summary>
    /// 异步获取所有已保存成本数据的会话标识列表
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已保存会话标识的只读列表</returns>
    public async Task<IReadOnlyList<string>> GetSavedSessionIdsAsync(CancellationToken ct = default) {
        try {
            var costsDir = AppDataConstants.Paths.CostsDirectory;

            if (!_fileOperationService.DirectoryExists(costsDir)) {
                return Array.Empty<string>();
            }

            var listResult = await _fileOperationService.ListDirectoryAsync(costsDir, cancellationToken: ct).ConfigureAwait(false);

            var sessionIds = new List<string>();
            foreach (var file in listResult.Files) {
                if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                    var id = file.Name[..^5];
                    sessionIds.Add(id);
                }
            }

            return sessionIds;
        } catch (Exception ex) {
            _logger?.LogError(ex, "[SessionCostPersistence] 获取已保存会话列表异常");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 异步删除指定会话的成本数据
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task DeleteSessionCostsAsync(string sessionId, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try {
            var filePath = Path.Combine(AppDataConstants.Paths.CostsDirectory, $"{sessionId}.json");

            if (_fileOperationService.FileExists(filePath)) {
                await _fileOperationService.DeleteFileAsync(filePath, ct).ConfigureAwait(false);
                _logger?.LogInformation("[SessionCostPersistence] 已删除会话 {SessionId} 的成本数据", sessionId);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[SessionCostPersistence] 删除会话 {SessionId} 成本数据异常", sessionId);
        }
    }

    private void RecordCostPersistenceMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "cost.persistence.count", operation, isSuccess, "Cost persistence count");
}