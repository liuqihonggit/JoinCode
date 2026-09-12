
namespace Core.Scheduling;

/// <summary>
/// WorkflowStateStore Actor 命令 — Channel 中的消息类型
/// </summary>
internal interface IWorkflowStateCommand;

internal sealed record SaveSnapshotCmd(string WorkflowId, WorkflowSnapshot Snapshot, CancellationToken Ct, TaskCompletionSource Tcs) : IWorkflowStateCommand;
internal sealed record LoadSnapshotCmd(string WorkflowId, CancellationToken Ct, TaskCompletionSource<WorkflowSnapshot?> Tcs) : IWorkflowStateCommand;

/// <summary>
/// Workflow 状态存储 Actor — 序列化文件 I/O，消除 AsyncLock 锁内长 await。
/// </summary>
internal sealed class WorkflowStateActor : ActorBase<IWorkflowStateCommand, Unit>
{
    private readonly WorkflowStateStore _owner;
    private readonly ILogger<WorkflowStateStore>? _logger;

    public WorkflowStateActor(WorkflowStateStore owner, ILogger<WorkflowStateStore>? logger)
        : base()
    {
        _owner = owner;
        _logger = logger;
    }

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task SaveSnapshotAsync(string workflowId, WorkflowSnapshot snapshot, CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new SaveSnapshotCmd(workflowId, snapshot, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<WorkflowSnapshot?> LoadSnapshotAsync(string workflowId, CancellationToken ct)
    {
        var tcs = CreateTcs<WorkflowSnapshot?>();
        await SendAsync(new LoadSnapshotCmd(workflowId, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(IWorkflowStateCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case SaveSnapshotCmd cmd:
            {
                try
                {
                    await _owner.SaveSnapshotCoreAsync(cmd.WorkflowId, cmd.Snapshot, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case LoadSnapshotCmd cmd:
            {
                try
                {
                    var result = await _owner.LoadSnapshotCoreAsync(cmd.WorkflowId, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[WorkflowStateStore] Actor Consumer 异常");
    }
}
