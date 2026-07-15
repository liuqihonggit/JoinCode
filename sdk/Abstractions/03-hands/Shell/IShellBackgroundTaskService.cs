namespace JoinCode.Abstractions.Interfaces;

public interface IShellBackgroundTaskService
{
    Task<ShellBackgroundTaskInfo> CreateTaskAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<ShellBackgroundTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    Task<List<ShellBackgroundTaskInfo>> ListTasksAsync(CancellationToken cancellationToken = default);

    Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default);

    Task<ShellBackgroundTaskInfo> WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default);

    Task<string> GetTaskOutputAsync(string taskId, CancellationToken cancellationToken = default);

    Task<List<ShellBackgroundTaskInfo>> ListTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default);

    Task<int> CancelTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制杀死所有运行中的后台任务 — 对齐 TS 用户强行消灭全部后台进程
    /// 使用 tree-kill 杀死进程树，回收内存
    /// </summary>
    Task<int> KillAllRunningAsync(CancellationToken cancellationToken = default);
}
