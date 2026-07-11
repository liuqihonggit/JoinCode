namespace Core.CostTracking;

public interface ISessionCostPersistence
{
    Task SaveCurrentSessionCostsAsync(string sessionId, CancellationToken ct = default);
    Task<CostStatistics?> RestoreCostStateForSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSavedSessionIdsAsync(CancellationToken ct = default);
    Task DeleteSessionCostsAsync(string sessionId, CancellationToken ct = default);
}

public sealed partial class SessionCostData
{
    public required string SessionId { get; init; }
    public required CostStatistics Statistics { get; init; }
    public required DateTime SavedAt { get; init; }
}

[Register]
public sealed partial class SessionCostPersistence : ISessionCostPersistence
{
    private readonly IFileOperationService _fileOperationService;
    private readonly string _storageDirectory;
    [Inject] private readonly ILogger<SessionCostPersistence>? _logger;
    private readonly IClockService _clock;
    private readonly CostTracker _costTracker;
    private readonly ITelemetryService? _telemetryService;

    public SessionCostPersistence(
        CostTracker costTracker,
        IFileOperationService fileOperationService,
        ILogger<SessionCostPersistence>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _costTracker = costTracker ?? throw new ArgumentNullException(nameof(costTracker));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _storageDirectory = WorkflowConstants.Paths.JccDirectory;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task SaveCurrentSessionCostsAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try
        {
            var stats = _costTracker.GetSessionStatistics(sessionId);
            var data = new SessionCostData
            {
                SessionId = sessionId,
                Statistics = stats,
                SavedAt = _clock.GetUtcNow()
            };

            var costsDir = Path.Combine(_storageDirectory, "costs");
            if (!_fileOperationService.DirectoryExists(costsDir))
            {
                _fileOperationService.CreateDirectory(costsDir);
            }

            var filePath = Path.Combine(costsDir, $"{sessionId}.json");
            var json = JsonSerializer.Serialize(data, CostTrackingJsonContext.Default.SessionCostData);

            var result = await _fileOperationService.WriteFileAsync(filePath, json, ct).ConfigureAwait(false);

            if (result.Success)
            {
                _logger?.LogInformation("[SessionCostPersistence] 已保存会话 {SessionId} 的成本数据", sessionId);
                RecordCostPersistenceMetrics("save", true);
            }
            else
            {
                _logger?.LogError("[SessionCostPersistence] 保存会话 {SessionId} 成本数据失败: {Error}", sessionId, result.ErrorMessage);
                RecordCostPersistenceMetrics("save", false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SessionCostPersistence] 保存会话 {SessionId} 成本数据异常", sessionId);
        }
    }

    public async Task<CostStatistics?> RestoreCostStateForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try
        {
            var filePath = Path.Combine(_storageDirectory, "costs", $"{sessionId}.json");

            if (!_fileOperationService.FileExists(filePath))
            {
                _logger?.LogDebug("[SessionCostPersistence] 会话 {SessionId} 无保存的成本数据", sessionId);
                return null;
            }

            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: ct).ConfigureAwait(false);

            if (!result.Success)
            {
                _logger?.LogWarning("[SessionCostPersistence] 读取会话 {SessionId} 成本数据失败: {Error}", sessionId, result.ErrorMessage);
                return null;
            }

            var data = JsonSerializer.Deserialize(result.Content, CostTrackingJsonContext.Default.SessionCostData);

            if (data is null)
            {
                _logger?.LogWarning("[SessionCostPersistence] 反序列化会话 {SessionId} 成本数据失败", sessionId);
                return null;
            }

            _logger?.LogInformation("[SessionCostPersistence] 已恢复会话 {SessionId} 的成本数据 (保存于 {SavedAt})", sessionId, data.SavedAt);
            RecordCostPersistenceMetrics("restore", true);
            return data.Statistics;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SessionCostPersistence] 恢复会话 {SessionId} 成本数据异常", sessionId);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetSavedSessionIdsAsync(CancellationToken ct = default)
    {
        try
        {
            var costsDir = Path.Combine(_storageDirectory, "costs");

            if (!_fileOperationService.DirectoryExists(costsDir))
            {
                return Array.Empty<string>();
            }

            var listResult = await _fileOperationService.ListDirectoryAsync(costsDir, cancellationToken: ct).ConfigureAwait(false);

            var sessionIds = new List<string>();
            foreach (var file in listResult.Files)
            {
                if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var id = file.Name[..^5];
                    sessionIds.Add(id);
                }
            }

            return sessionIds;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SessionCostPersistence] 获取已保存会话列表异常");
            return Array.Empty<string>();
        }
    }

    public async Task DeleteSessionCostsAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try
        {
            var filePath = Path.Combine(_storageDirectory, "costs", $"{sessionId}.json");

            if (_fileOperationService.FileExists(filePath))
            {
                await _fileOperationService.DeleteFileAsync(filePath, ct).ConfigureAwait(false);
                _logger?.LogInformation("[SessionCostPersistence] 已删除会话 {SessionId} 的成本数据", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SessionCostPersistence] 删除会话 {SessionId} 成本数据异常", sessionId);
        }
    }

    private void RecordCostPersistenceMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("cost.persistence.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Cost persistence count");
}
