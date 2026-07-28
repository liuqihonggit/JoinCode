namespace Core.Agents.Coordinator;

[Register(typeof(JoinCode.Abstractions.Interfaces.ITeammateLayoutManager))]
public sealed partial class TeammateLayoutManager : JoinCode.Abstractions.Interfaces.ITeammateLayoutManager
{
    private static readonly string[] AgentColors =
    [
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4",
        "#FFEAA7", "#DDA0DD", "#98D8C8", "#F7DC6F",
        "#BB8FCE", "#85C1E9", "#F8C471", "#82E0AA"
    ];

    private readonly JoinCode.Abstractions.Interfaces.IPaneBackend _backend;
    [Inject] private readonly ILogger<TeammateLayoutManager>? _logger;
    private readonly Dictionary<string, string> _teammateColors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _teammatePanes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _colorIndex;

    public JoinCode.Abstractions.Interfaces.BackendType CurrentBackendType => _backend.BackendType;

    public TeammateLayoutManager(
        JoinCode.Abstractions.Interfaces.IPaneBackend backend,
        ILogger<TeammateLayoutManager>? logger = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _logger = logger;
    }

    public async Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string agentType, string command, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _backend.CreateTeammatePaneAsync(teammateId, command, cancellationToken).ConfigureAwait(false);
            _teammatePanes[teammateId] = result.PaneId;

            var color = AssignTeammateColor(teammateId);
            await _backend.SetPaneBorderColorAsync(result.PaneId, color, cancellationToken).ConfigureAwait(false);
            await _backend.SetPaneTitleAsync(result.PaneId, $"{agentType}:{teammateId[..Math.Min(8, teammateId.Length)]}", cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Created teammate pane: TeammateId={TeammateId}, PaneId={PaneId}, Backend={Backend}",
                teammateId, result.PaneId, result.BackendType);

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string AssignTeammateColor(string teammateId)
    {
        if (_teammateColors.TryGetValue(teammateId, out var existing))
            return existing;

        var color = AgentColors[_colorIndex % AgentColors.Length];
        _colorIndex++;
        _teammateColors[teammateId] = color;
        return color;
    }

    public async Task RemoveTeammatePaneAsync(string teammateId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_teammatePanes.TryGetValue(teammateId, out var paneId))
            {
                await _backend.KillPaneAsync(paneId, cancellationToken).ConfigureAwait(false);
                _teammatePanes.Remove(teammateId);
                _teammateColors.Remove(teammateId);

                if (_teammatePanes.Count > 0)
                    await _backend.RebalancePanesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RebalanceLayoutAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _backend.RebalancePanesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
