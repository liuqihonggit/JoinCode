namespace Core.Agents.Coordinator;

/// <summary>
/// tmux 终端面板后端 — 通过 tmux CLI 创建并管理队友面板，支持在 tmux 会话内嵌套或外部独立会话两种模式
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPaneBackend), ServiceLifetime.Singleton)]
public sealed partial class TmuxPaneBackend : ServiceEntity, JoinCode.Abstractions.Interfaces.IPaneBackend
{
    private static readonly string[] TmuxColorMap =
    new[] {
        "red", "blue", "green", "yellow", "magenta", "colour208", "colour205", "cyan",
        "colour131", "colour75", "colour114", "colour221"
     };

    private readonly ILogger<TmuxPaneBackend>? _logger;
    private readonly IProcessService _processService;
    private readonly AsyncLock _creationLock = new();
    private readonly HashSet<string> _managedPanes = new(StringComparer.Ordinal);
    private readonly string? _swarmSocket;
    private readonly bool _insideTmux;
    private string? _windowTarget;
    private string? _leaderPaneId;

    /// <summary>后端类型标识，固定为 Tmux</summary>
    public JoinCode.Abstractions.Interfaces.BackendType BackendType => JoinCode.Abstractions.Interfaces.BackendType.Tmux;

    /// <summary>当前环境是否可用 tmux 后端（tmux CLI 可调用即视为可用）</summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// 构造 tmux 面板后端实例
    /// </summary>
    /// <param name="processService">进程执行服务，用于调用 tmux CLI</param>
    /// <param name="logger">可选日志记录器</param>
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

    /// <summary>
    /// 为队友创建一个 tmux 面板并启动指定命令
    /// </summary>
    /// <param name="teammateId">队友标识</param>
    /// <param name="command">面板启动时执行的命令</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>面板创建结果，包含面板 ID 与后端类型</returns>
    public async Task<JoinCode.Abstractions.Interfaces.CreatePaneResult> CreateTeammatePaneAsync(
        string teammateId, string command, CancellationToken cancellationToken = default)
    {
        using var guard = await _creationLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_creationLock.Name}' 等待超时");

        if (_insideTmux)
            return await CreatePaneInsideTmuxAsync(teammateId, command, cancellationToken).ConfigureAwait(false);
        else
            return await CreatePaneExternalSessionAsync(teammateId, command, cancellationToken).ConfigureAwait(false);

    }

    /// <summary>
    /// 向指定 tmux 面板发送命令文本并按回车
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="command">要发送的命令文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
    {
        var args = GetTmuxArgs("send-keys", "-t", paneId, command, "Enter");
        var result = await RunTmuxAsync(args, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to send command to pane {paneId}: {result.Error}");
    }

    /// <summary>
    /// 设置 tmux 面板边框颜色（含 pane-border-style 与 pane-active-border-style）
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="colorHex">十六进制颜色值，自动转换为 tmux colour256 颜色名</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
    {
        var tmuxColor = HexToTmuxColor(colorHex);

        await RunTmuxAsync(GetTmuxArgs("select-pane", "-t", paneId, "-P", $"fg={tmuxColor}"), cancellationToken).ConfigureAwait(false);
        await RunTmuxAsync(GetTmuxArgs("set-option", "-p", "-t", paneId, "pane-border-style", $"fg={tmuxColor}"), cancellationToken).ConfigureAwait(false);
        await RunTmuxAsync(GetTmuxArgs("set-option", "-p", "-t", paneId, "pane-active-border-style", $"fg={tmuxColor}"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 设置 tmux 面板标题
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="title">面板标题文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
    {
        await RunTmuxAsync(GetTmuxArgs("select-pane", "-t", paneId, "-T", title), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 关闭指定 tmux 面板并从受管集合中移除
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
    {
        var result = await RunTmuxAsync(GetTmuxArgs("kill-pane", "-t", paneId), cancellationToken).ConfigureAwait(false);
        _managedPanes.Remove(paneId);

        if (result.ExitCode != 0)
            _logger?.LogWarning("Failed to kill pane {PaneId}: {Error}", paneId, result.Error);
    }

    /// <summary>
    /// 重新平衡 tmux 面板布局；会话内模式使用 main-vertical 并固定主面板宽度，外部会话模式使用 tiled 布局
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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
                ArgumentList = new[] { "-V" },
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
            ArgumentList = args
        }, cancellationToken).ConfigureAwait(false);

        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }

    /// <summary>释放资源 — 释放 tmux 会话创建锁</summary>
    public override void Dispose()
    {
        _creationLock.Dispose();
        base.Dispose();
    }
}
