namespace Core.Agents.Coordinator;

[Register]
public sealed partial class InProcessPaneBackend : JoinCode.Abstractions.Interfaces.IPaneBackend
{
    public JoinCode.Abstractions.Interfaces.BackendType BackendType => JoinCode.Abstractions.Interfaces.BackendType.InProcess;

    public Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(string teammateId, string command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new JoinCode.Abstractions.Interfaces.CreatePaneResult
        {
            PaneId = teammateId,
            BackendType = JoinCode.Abstractions.Interfaces.BackendType.InProcess
        });
    }

    public Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RebalancePanesAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
