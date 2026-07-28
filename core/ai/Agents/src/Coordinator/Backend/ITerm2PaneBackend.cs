namespace Core.Agents.Coordinator;

[Register]
public sealed partial class ITerm2PaneBackend : JoinCode.Abstractions.Interfaces.IPaneBackend
{
    [Inject] private readonly ILogger<ITerm2PaneBackend>? _logger;
    private readonly IProcessService _processService;
    private readonly Dictionary<string, string> _paneSessions = new(StringComparer.Ordinal);
    private int _paneCounter;

    public JoinCode.Abstractions.Interfaces.BackendType BackendType => JoinCode.Abstractions.Interfaces.BackendType.ITerm2;

    public bool IsAvailable { get; }

    public ITerm2PaneBackend(IProcessService processService, ILogger<ITerm2PaneBackend>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        IsAvailable = CheckITerm2Available();
        if (!IsAvailable)
        {
            _logger?.LogDebug("iTerm2 not available (not running in iTerm2 or it2 CLI not found)");
        }
    }

    public Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string command, CancellationToken cancellationToken = default)
    {
        var paneId = $"iterm2-{Interlocked.Increment(ref _paneCounter)}";
        _paneSessions[teammateId] = paneId;

        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                Arguments = $"split-pane --horizontal --percent 70 {command}",
                TimeoutMs = 10000,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }, cancellationToken).GetAwaiter().GetResult();

            _logger?.LogInformation("Created iTerm2 pane for teammate {TeammateId}: {PaneId}", teammateId, paneId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create iTerm2 pane for teammate {TeammateId}", teammateId);
        }

        return Task.FromResult(new JoinCode.Abstractions.Interfaces.CreatePaneResult
        {
            PaneId = paneId,
            BackendType = JoinCode.Abstractions.Interfaces.BackendType.ITerm2
        });
    }

    public Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
    {
        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                Arguments = $"send-text --no-newline \"{command}\" --pane {paneId}",
                TimeoutMs = 5000,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send command to iTerm2 pane {PaneId}", paneId);
        }

        return Task.CompletedTask;
    }

    public Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("iTerm2 pane border color not directly supported for pane {PaneId}", paneId);
        return Task.CompletedTask;
    }

    public Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
    {
        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                Arguments = $"set-title \"{title}\" --pane {paneId}",
                TimeoutMs = 5000,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to set iTerm2 pane title for {PaneId}", paneId);
        }

        return Task.CompletedTask;
    }

    public Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
    {
        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                Arguments = $"close-pane --pane {paneId}",
                TimeoutMs = 5000,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to kill iTerm2 pane {PaneId}", paneId);
        }

        return Task.CompletedTask;
    }

    public Task RebalancePanesAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("iTerm2 pane rebalancing not directly supported");
        return Task.CompletedTask;
    }

    private bool CheckITerm2Available()
    {
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (!string.Equals(termProgram, "iTerm.app", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var result = _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                Arguments = "--version",
                TimeoutMs = 5000
            }).GetAwaiter().GetResult();
            return result.Success;
        }
        catch
        {
            return false;
        }
    }
}
