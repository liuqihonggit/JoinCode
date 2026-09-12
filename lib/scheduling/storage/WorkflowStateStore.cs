
namespace Core.Scheduling;

/// <summary>
/// Workflow 状态存储实现 — 每 workflow 独立文件 workflow_{id}.state.json，原子写 + 损坏隔离
/// Actor 化：组合 WorkflowStateActor 序列化文件 I/O，消除 AsyncLock。
/// </summary>
[Register(typeof(IWorkflowStateStore), ServiceLifetime.Singleton)]
public sealed partial class WorkflowStateStore : ServiceEntity, IWorkflowStateStore
{
    private readonly IFileOperationService _fileOperationService;
    private readonly string _persistenceDirectory;
    private readonly ILogger<WorkflowStateStore>? _logger;
    private readonly WorkflowStateActor _actor;

    public WorkflowStateStore(
        IFileOperationService fileOperationService,
        string? persistenceDirectory = null,
        ILogger<WorkflowStateStore>? logger = null)
    {
        _fileOperationService = fileOperationService;
        _persistenceDirectory = persistenceDirectory ?? AppDataConstants.Paths.WorkflowStatesDirectory;
        _logger = logger;
        _actor = new WorkflowStateActor(this, _logger);
    }

    public async Task SaveSnapshotAsync(string workflowId, WorkflowSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);
        ArgumentNullException.ThrowIfNull(snapshot);
        await _actor.SaveSnapshotAsync(workflowId, snapshot, cancellationToken).ConfigureAwait(false);
    }

    internal async Task SaveSnapshotCoreAsync(string workflowId, WorkflowSnapshot snapshot, CancellationToken cancellationToken)
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

    public async Task<WorkflowSnapshot?> LoadSnapshotAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);
        return await _actor.LoadSnapshotAsync(workflowId, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<WorkflowSnapshot?> LoadSnapshotCoreAsync(string workflowId, CancellationToken cancellationToken)
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

    protected override void OnDispose()
    {
        _ = _actor.DisposeAsync();
    }
}
