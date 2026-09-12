namespace Core.Memdir;

/// <summary>
/// MemoryManagement Actor 命令 — Channel 中的消息类型
/// </summary>
internal interface IMemoryMgmtCommand;

internal sealed record ScanMemoriesCmd(string Query, string? Category, int Limit, CancellationToken Ct, TaskCompletionSource<MemoryScanResult> Tcs) : IMemoryMgmtCommand;
internal sealed record GetMemoryAgeInfoCmd(CancellationToken Ct, TaskCompletionSource<List<MemoryAgeInfo>> Tcs) : IMemoryMgmtCommand;
internal sealed record AddTeamMemoryPathCmd(string TeamId, string Path, bool IsShared, List<string>? AllowedAgents, CancellationToken Ct, TaskCompletionSource Tcs) : IMemoryMgmtCommand;
internal sealed record GetTeamMemoryPathsCmd(string? TeamId, CancellationToken Ct, TaskCompletionSource<List<TeamMemoryPath>> Tcs) : IMemoryMgmtCommand;
internal sealed record RemoveTeamMemoryPathCmd(string TeamId, string Path, CancellationToken Ct, TaskCompletionSource<bool> Tcs) : IMemoryMgmtCommand;
internal sealed record ScanTeamMemoriesCmd(string TeamId, string Query, int Limit, CancellationToken Ct, TaskCompletionSource<MemoryScanResult> Tcs) : IMemoryMgmtCommand;

/// <summary>
/// 内存管理 Actor — 序列化 6 个锁方法，消除 AsyncLock 锁内长 await。
/// Consumer 调用 MemoryManagementService 的 core 方法，_teamMemoryPaths 和文件 I/O 由 Consumer 独占。
/// </summary>
internal sealed class MemoryMgmtActor : ActorBase<IMemoryMgmtCommand, Unit>
{
    private readonly MemoryManagementService _owner;
    private readonly ILogger<MemoryManagementService>? _logger;

    public MemoryMgmtActor(MemoryManagementService owner, ILogger<MemoryManagementService>? logger)
        : base()
    {
        _owner = owner;
        _logger = logger;
    }

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<MemoryScanResult> ScanMemoriesAsync(string query, string? category, int limit, CancellationToken ct)
    {
        var tcs = CreateTcs<MemoryScanResult>();
        await SendAsync(new ScanMemoriesCmd(query, category, limit, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<List<MemoryAgeInfo>> GetMemoryAgeInfoAsync(CancellationToken ct)
    {
        var tcs = CreateTcs<List<MemoryAgeInfo>>();
        await SendAsync(new GetMemoryAgeInfoCmd(ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task AddTeamMemoryPathAsync(string teamId, string path, bool isShared, List<string>? allowedAgents, CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new AddTeamMemoryPathCmd(teamId, path, isShared, allowedAgents, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<List<TeamMemoryPath>> GetTeamMemoryPathsAsync(string? teamId, CancellationToken ct)
    {
        var tcs = CreateTcs<List<TeamMemoryPath>>();
        await SendAsync(new GetTeamMemoryPathsCmd(teamId, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<bool> RemoveTeamMemoryPathAsync(string teamId, string path, CancellationToken ct)
    {
        var tcs = CreateTcs<bool>();
        await SendAsync(new RemoveTeamMemoryPathCmd(teamId, path, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<MemoryScanResult> ScanTeamMemoriesAsync(string teamId, string query, int limit, CancellationToken ct)
    {
        var tcs = CreateTcs<MemoryScanResult>();
        await SendAsync(new ScanTeamMemoriesCmd(teamId, query, limit, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(IMemoryMgmtCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case ScanMemoriesCmd cmd:
            {
                try
                {
                    var result = await _owner.ScanMemoriesCoreAsync(cmd.Query, cmd.Category, cmd.Limit, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case GetMemoryAgeInfoCmd cmd:
            {
                try
                {
                    var result = await _owner.GetMemoryAgeInfoCoreAsync(cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case AddTeamMemoryPathCmd cmd:
            {
                try
                {
                    await _owner.AddTeamMemoryPathCoreAsync(cmd.TeamId, cmd.Path, cmd.IsShared, cmd.AllowedAgents, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case GetTeamMemoryPathsCmd cmd:
            {
                try
                {
                    var result = await _owner.GetTeamMemoryPathsCoreAsync(cmd.TeamId, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case RemoveTeamMemoryPathCmd cmd:
            {
                try
                {
                    var result = await _owner.RemoveTeamMemoryPathCoreAsync(cmd.TeamId, cmd.Path, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case ScanTeamMemoriesCmd cmd:
            {
                try
                {
                    var result = await _owner.ScanTeamMemoriesCoreAsync(cmd.TeamId, cmd.Query, cmd.Limit, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult(result);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[MemoryMgmt] Actor Consumer 异常");
    }
}
