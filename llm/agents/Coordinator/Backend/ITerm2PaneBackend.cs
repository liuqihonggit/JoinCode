namespace Core.Agents.Coordinator;

/// <summary>
/// iTerm2 终端面板后端 — 通过 it2 CLI 在 iTerm2 中创建并管理队友面板
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPaneBackend), ServiceLifetime.Singleton)]
public sealed partial class ITerm2PaneBackend : ServiceEntity, JoinCode.Abstractions.Interfaces.IPaneBackend
{
    private readonly ILogger<ITerm2PaneBackend>? _logger;
    private readonly IProcessService _processService;
    private readonly Dictionary<string, string> _paneSessions = new(StringComparer.Ordinal);
    private int _paneCounter;

    /// <summary>后端类型标识，固定为 iTerm2</summary>
    public JoinCode.Abstractions.Interfaces.BackendType BackendType => JoinCode.Abstractions.Interfaces.BackendType.ITerm2;

    /// <summary>当前环境是否可用 iTerm2 后端（需运行于 iTerm.app 且 it2 CLI 可调用）</summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// 构造 iTerm2 面板后端实例
    /// </summary>
    /// <param name="processService">进程执行服务，用于调用 it2 CLI</param>
    /// <param name="logger">可选日志记录器</param>
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

    /// <summary>
    /// 为队友创建一个 iTerm2 面板并启动指定命令
    /// </summary>
    /// <param name="teammateId">队友标识</param>
    /// <param name="command">面板启动时执行的命令</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>面板创建结果，包含面板 ID 与后端类型</returns>
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
                ArgumentList = new[] { "split-pane", "--horizontal", "--percent", "70", command },
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

    /// <summary>
    /// 向指定 iTerm2 面板发送命令文本
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="command">要发送的命令文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SendCommandToPaneAsync(string paneId, string command, CancellationToken cancellationToken = default)
    {
        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                ArgumentList = new[] { "send-text", "--no-newline", command, "--pane", paneId },
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

    /// <summary>
    /// 设置 iTerm2 面板边框颜色（iTerm2 不直接支持，仅记录日志）
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="colorHex">十六进制颜色值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetPaneBorderColorAsync(string paneId, string colorHex, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("iTerm2 pane border color not directly supported for pane {PaneId}", paneId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置 iTerm2 面板标题
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="title">面板标题文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task SetPaneTitleAsync(string paneId, string title, CancellationToken cancellationToken = default)
    {
        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                ArgumentList = new[] { "set-title", title, "--pane", paneId },
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

    /// <summary>
    /// 关闭指定 iTerm2 面板
    /// </summary>
    /// <param name="paneId">目标面板 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task KillPaneAsync(string paneId, CancellationToken cancellationToken = default)
    {
        try
        {
            _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "it2",
                ArgumentList = new[] { "close-pane", "--pane", paneId },
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

    /// <summary>
    /// 重新平衡 iTerm2 面板布局（iTerm2 不直接支持，仅记录日志）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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
                ArgumentList = new[] { "--version" },
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
