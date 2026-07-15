namespace Core.Agents.Coordinator;

[Register]
public sealed partial class TmuxPaneBackend : JoinCode.Abstractions.Interfaces.IPaneBackend
{
    private static readonly string[] TmuxColorMap =
    [
        "red", "blue", "green", "yellow", "magenta", "colour208", "colour205", "cyan",
        "colour131", "colour75", "colour114", "colour221"
    ];

    [Inject] private readonly ILogger<TmuxPaneBackend>? _logger;
    private readonly IProcessService _processService;
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private readonly HashSet<string> _managedPanes = new(StringComparer.Ordinal);
    private readonly string? _swarmSocket;
    private readonly bool _insideTmux;
    private string? _windowTarget;
    private string? _leaderPaneId;

    public JoinCode.Abstractions.Interfaces.BackendType BackendType => JoinCode.Abstractions.Interfaces.BackendType.Tmux;

    public bool IsAvailable { get; }

    public TmuxPaneBackend(IProcessService processService, ILogger<TmuxPaneBackend>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        _insideTmux = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUX"));
        _swarmSocket = $"claude-swarm-{Environment.ProcessId}";

        IsAvailable = CheckTmuxAvailable();
        if (!IsAvailable)
        {
            _logger?.LogDebug("tmux not available on this system");
        }
    }

    public async Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string command, CancellationToken cancellationToken = default)
    {
        await _creationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_insideTmux)
                return await CreatePaneInsideTmuxAsync(teammateId, command, cancellationToken).ConfigureAwait(false);
            else
                return await CreatePaneExternalSessionAsync(teammateId, command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _creationLock.Release();
        }
    }

    public async Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
    {
        var args = GetTmuxArgs("send-keys", "-t", paneId, command, "Enter");
        var result = await RunTmuxAsync(args, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to send command to pane {paneId}: {result.Error}");
    }

    public async Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
    {
        var tmuxColor = HexToTmuxColor(colorHex);

        await RunTmuxAsync(GetTmuxArgs("select-pane", "-t", paneId, "-P", $"fg={tmuxColor}"), cancellationToken).ConfigureAwait(false);
        await RunTmuxAsync(GetTmuxArgs("set-option", "-p", "-t", paneId, "pane-border-style", $"fg={tmuxColor}"), cancellationToken).ConfigureAwait(false);
        await RunTmuxAsync(GetTmuxArgs("set-option", "-p", "-t", paneId, "pane-active-border-style", $"fg={tmuxColor}"), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
    {
        await RunTmuxAsync(GetTmuxArgs("select-pane", "-t", paneId, "-T", title), cancellationToken).ConfigureAwait(false);
    }

    public async Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
    {
        var result = await RunTmuxAsync(GetTmuxArgs("kill-pane", "-t", paneId), cancellationToken).ConfigureAwait(false);
        _managedPanes.Remove(paneId);

        if (result.ExitCode != 0)
            _logger?.LogWarning("Failed to kill pane {PaneId}: {Error}", paneId, result.Error);
    }

    public async Task RebalancePanesAsync(CancellationToken cancellationToken = default)
    {
        if (_insideTmux && _leaderPaneId is not null && _windowTarget is not null)
        {
            await RunTmuxAsync(GetTmuxArgs("select-layout", "-t", _windowTarget, "main-vertical"), cancellationToken).ConfigureAwait(false);
            await RunTmuxAsync(GetTmuxArgs("resize-pane", "-t", _leaderPaneId, "-x", "30%"), cancellationToken).ConfigureAwait(false);
        }
        else if (_windowTarget is not null)
        {
            await RunTmuxAsync(GetSwarmTmuxArgs("select-layout", "-t", _windowTarget, "tiled"), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreatePaneInsideTmuxAsync(
        string teammateId, string command, CancellationToken cancellationToken)
    {
        if (_leaderPaneId is null)
        {
            _leaderPaneId = Environment.GetEnvironmentVariable("TMUX_PANE");
            _windowTarget = _leaderPaneId;
        }

        var leaderPaneId = _leaderPaneId ?? throw new InvalidOperationException("Leader pane ID not set.");

        if (_managedPanes.Count == 0)
        {
            var result = await RunTmuxAsync(["split-window", "-t", leaderPaneId, "-h", "-l", "70%", "-P", "-F", "#{pane_id}"], cancellationToken).ConfigureAwait(false);
            var paneId = result.Output.Trim();

            _managedPanes.Add(paneId);
            _windowTarget = paneId;

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            await SendCommandToPaneAsync(paneId, command, cancellationToken).ConfigureAwait(false);

            return new JoinCode.Abstractions.Interfaces.CreatePaneResult { PaneId = paneId, BackendType = JoinCode.Abstractions.Interfaces.BackendType.Tmux };
        }

        var splitVertically = _managedPanes.Count % 2 == 1;
        var targetIndex = (_managedPanes.Count - 1) / 2;
        var targetPane = _managedPanes.ElementAt(targetIndex);
        var splitFlag = splitVertically ? "-v" : "-h";

        var splitResult = await RunTmuxAsync(["split-window", "-t", targetPane, splitFlag, "-P", "-F", "#{pane_id}"], cancellationToken).ConfigureAwait(false);
        var newPaneId = splitResult.Output.Trim();

        _managedPanes.Add(newPaneId);

        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        await SendCommandToPaneAsync(newPaneId, command, cancellationToken).ConfigureAwait(false);
        await RebalancePanesAsync(cancellationToken).ConfigureAwait(false);

        return new JoinCode.Abstractions.Interfaces.CreatePaneResult { PaneId = newPaneId, BackendType = JoinCode.Abstractions.Interfaces.BackendType.Tmux };
    }

    private async Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreatePaneExternalSessionAsync(
        string teammateId, string command, CancellationToken cancellationToken)
    {
        if (_managedPanes.Count == 0)
        {
            var result = await RunTmuxAsync(GetSwarmTmuxArgs("new-session", "-d", "-s", "claude-swarm", "-n", "swarm-view", "-P", "-F", "#{pane_id}"), cancellationToken).ConfigureAwait(false);
            var paneId = result.Output.Trim();

            _managedPanes.Add(paneId);
            _windowTarget = "claude-swarm:swarm-view";

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            await SendCommandToPaneAsync(paneId, command, cancellationToken).ConfigureAwait(false);

            return new JoinCode.Abstractions.Interfaces.CreatePaneResult { PaneId = paneId, BackendType = JoinCode.Abstractions.Interfaces.BackendType.Tmux };
        }

        var splitVertically = _managedPanes.Count % 2 == 1;
        var splitFlag = splitVertically ? "-v" : "-h";
        var targetIndex = (_managedPanes.Count - 1) / 2;
        var targetPane = _managedPanes.ElementAt(targetIndex);

        var splitResult = await RunTmuxAsync(GetSwarmTmuxArgs("split-window", "-t", targetPane, splitFlag, "-P", "-F", "#{pane_id}"), cancellationToken).ConfigureAwait(false);
        var newPaneId = splitResult.Output.Trim();

        _managedPanes.Add(newPaneId);

        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        await RunTmuxAsync(GetSwarmTmuxArgs("send-keys", "-t", newPaneId, command, "Enter"), cancellationToken).ConfigureAwait(false);
        await RebalancePanesAsync(cancellationToken).ConfigureAwait(false);

        return new JoinCode.Abstractions.Interfaces.CreatePaneResult { PaneId = newPaneId, BackendType = JoinCode.Abstractions.Interfaces.BackendType.Tmux };
    }

    private string[] GetTmuxArgs(params string[] args) => args;

    private string[] GetSwarmTmuxArgs(params string[] args)
    {
        var result = new string[2 + args.Length];
        result[0] = "-L";
        result[1] = _swarmSocket ?? throw new InvalidOperationException("Swarm socket not set.");
        Array.Copy(args, 0, result, 2, args.Length);
        return result;
    }

    private static string HexToTmuxColor(string hex)
    {
        if (hex.StartsWith('#') && hex.Length == 7)
            return $"colour{HexToAnsi256(hex)}";
        return hex;
    }

    private static int HexToAnsi256(string hex)
    {
        var r = Convert.ToInt32(hex[1..3], 16);
        var g = Convert.ToInt32(hex[3..5], 16);
        var b = Convert.ToInt32(hex[5..7], 16);
        return 16 + (36 * (r / 51)) + (6 * (g / 51)) + (b / 51);
    }

    private bool CheckTmuxAvailable()
    {
        try
        {
            var result = _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "tmux",
                Arguments = "-V",
                TimeoutMs = 5000
            }).GetAwaiter().GetResult();
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunTmuxAsync(string[] args, CancellationToken cancellationToken)
    {
        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = "tmux",
            Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
        }, cancellationToken).ConfigureAwait(false);

        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }
}
