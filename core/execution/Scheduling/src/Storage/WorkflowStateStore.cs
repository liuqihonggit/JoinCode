
namespace Core.Scheduling;

/// <summary>
/// Workflow 状态存储实现 — 每 workflow 独立文件 workflow_{id}.state.json，原子写 + 损坏隔离
/// </summary>
[Register(typeof(IWorkflowStateStore), ServiceLifetime.Singleton)]
public sealed partial class WorkflowStateStore : ServiceEntity, IWorkflowStateStore
{
    private readonly IFileOperationService _fileOperationService;
    private readonly string _persistenceDirectory;
    private readonly ILogger<WorkflowStateStore>? _logger;
    private readonly AsyncLock _lock = new();

    public WorkflowStateStore(
        IFileOperationService fileOperationService,
        string? persistenceDirectory = null,
        ILogger<WorkflowStateStore>? logger = null)
    {
        _fileOperationService = fileOperationService;
        _persistenceDirectory = persistenceDirectory ?? Path.Combine(Environment.CurrentDirectory, AppDataConstants.AppDataFolder, "workflow-states");
        _logger = logger;
    }

    public async Task SaveSnapshotAsync(string workflowId, WorkflowSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);
        ArgumentNullException.ThrowIfNull(snapshot);

        using (await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (!_fileOperationService.DirectoryExists(_persistenceDirectory))
            {
                _fileOperationService.CreateDirectory(_persistenceDirectory);
            }

            var filePath = GetSnapshotFilePath(workflowId);
            var json = RelaxedJsonSerializer.Serialize(snapshot, SchedulingTasksJsonContext.Default);

            var tempPath = filePath + ".tmp";
            try
            {
                await _fileOperationService.WriteFileAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
                await _fileOperationService.MoveFileAsync(tempPath, filePath, overwrite: true, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (_fileOperationService.FileExists(tempPath))
                {
                    try { await _fileOperationService.DeleteFileAsync(tempPath, cancellationToken).ConfigureAwait(false); }
                    catch (Exception cleanupEx) { _logger?.LogWarning(cleanupEx, "清理 workflow 快照临时文件失败: {TempPath}", tempPath); }
                }
                throw;
            }

            _logger?.LogDebug("已保存 workflow 快照: {WorkflowId}, 步骤数: {StepCount}", workflowId, snapshot.StepStates.Count);
        }
    }

    public async Task<WorkflowSnapshot?> LoadSnapshotAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);

        using (await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            var filePath = GetSnapshotFilePath(workflowId);
            if (!_fileOperationService.FileExists(filePath))
            {
                return null;
            }

            var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                _logger?.LogWarning("读取 workflow 快照失败: {FilePath}, {Error}", filePath, readResult.ErrorMessage ?? "读取失败");
                return null;
            }

            try
            {
                return RelaxedJsonSerializer.Deserialize(readResult.Content, SchedulingTasksJsonContext.Default.WorkflowSnapshot);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "workflow 快照损坏: {FilePath}, {Error}", filePath, ex.Message);
                await QuarantineCorruptFileAsync(filePath, cancellationToken).ConfigureAwait(false);
                return null;
            }
        }
    }

    private string GetSnapshotFilePath(string workflowId) => Path.Combine(_persistenceDirectory, $"workflow_{workflowId}.state.json");

    /// <summary>
    /// 隔离损坏的快照文件 — 移动到带时间戳的 .corrupt 后缀，避免反复报错且保留证据
    /// </summary>
    private async Task QuarantineCorruptFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var corruptPath = $"{filePath}.{DateTime.Now:yyyyMMddHHmmss}.corrupt";
            await _fileOperationService.MoveFileAsync(filePath, corruptPath, overwrite: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _logger?.LogWarning("已隔离损坏的 workflow 快照: {CorruptPath}", corruptPath);
        }
        catch (Exception moveEx)
        {
            _logger?.LogWarning(moveEx, "隔离损坏 workflow 快照失败: {FilePath}", filePath);
        }
    }
}
