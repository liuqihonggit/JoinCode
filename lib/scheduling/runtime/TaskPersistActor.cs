namespace Core.Scheduling.Runtime;

/// <summary>
/// TaskRuntime Actor 命令 — Channel 中的消息类型
/// </summary>
internal interface ITaskPersistCommand;

internal sealed record PersistCmd(CancellationToken Ct, TaskCompletionSource Tcs) : ITaskPersistCommand;
internal sealed record RecoverCmd(string? GoalId, CancellationToken Ct, TaskCompletionSource<IReadOnlyList<RuntimeTask>> Tcs) : ITaskPersistCommand;

/// <summary>
/// TaskRuntime 持久化 Actor — 序列化 Persist/Recover 文件 I/O，消除 AsyncLock 锁内长 await。
/// </summary>
internal sealed class TaskPersistActor : ActorBase<ITaskPersistCommand, Unit>
{
    private readonly TaskRuntime _owner;
    private readonly ILogger<TaskRuntime>? _logger;

    public TaskPersistActor(TaskRuntime owner, ILogger<TaskRuntime>? logger)
        : base()
    {
        _owner = owner;
        _logger = logger;
    }

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task PersistAsync(CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new PersistCmd(ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RuntimeTask>> RecoverTasksAsync(string? goalId, CancellationToken ct)
    {
        var tcs = CreateTcs<IReadOnlyList<RuntimeTask>>();
        await SendAsync(new RecoverCmd(goalId, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(ITaskPersistCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case PersistCmd cmd:
            {
                try
                {
                    await _owner.PersistCoreAsync(cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case RecoverCmd cmd:
            {
                try
                {
                    var result = await _owner.RecoverTasksCoreAsync(cmd.GoalId, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[TaskRuntime] Persist Actor Consumer 异常");
    }
}
