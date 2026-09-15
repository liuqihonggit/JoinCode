namespace McpToolDispatch;

/// <summary>
/// 窗口震动工具处理器 — 震动进程窗口/闪烁任务栏/获取进程信息，供子代理向用户展示存在感。
/// 通过 <see cref="IWindowShakeCoordinator"/> 1 秒去抖，防止多子代理高频震动。
/// </summary>
[McpToolDispatch(ToolCategory.Notification, Optional = true)]
public partial class ShakeWindowToolHandlers
{
    private readonly IWindowShakeCoordinator _coordinator;
    private readonly IWindowShakeService? _shakeService;
    private readonly ILogger<ShakeWindowToolHandlers>? _logger;

    /// <summary>
    /// 初始化窗口震动工具处理器
    /// </summary>
    /// <param name="coordinator">震动去抖协调器</param>
    /// <param name="shakeService">窗口震动服务（可选，未注册时仅返回文字）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ShakeWindowToolHandlers(
        IWindowShakeCoordinator coordinator,
        IWindowShakeService? shakeService = null,
        ILogger<ShakeWindowToolHandlers>? logger = null)
    {
        _coordinator = coordinator;
        _shakeService = shakeService;
        _logger = logger;
    }

    /// <summary>
    /// 震动当前进程窗口以提醒用户 — 1 秒去抖，配置关闭时仅返回文字。
    /// </summary>
    /// <param name="reason">震动原因（可选，供日志记录）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，包含电脑名和进程 PID</returns>
    [McpTool("shake_window", "震动当前进程窗口以提醒用户", "notification")]
    public async Task<ToolResult> ShakeWindowAsync(
        [McpToolParameter("震动原因(可选)", Required = false)] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var (machine, pid) = GetProcessInfo();

        if (!_coordinator.IsShakeEnabled)
            return ToolResultBuilder.Success()
                .WithText($"窗口震动已关闭（电脑:{machine}, PID:{pid}）")
                .Build();

        if (!_coordinator.TryAcquireShakeSlot())
            return ToolResultBuilder.Success()
                .WithText($"1秒内已震动过，跳过（电脑:{machine}, PID:{pid}）")
                .Build();

        try
        {
            if (_shakeService is not null)
                await _shakeService.ShakeWindowAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("窗口震动已执行: reason={Reason}, machine={Machine}, pid={Pid}", reason, machine, pid);
            return ToolResultBuilder.Success()
                .WithText($"已震动窗口（电脑:{machine}, PID:{pid}）")
                .Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "窗口震动失败");
            return ToolResultBuilder.Error()
                .WithText($"窗口震动失败: {ex.Message}（电脑:{machine}, PID:{pid}）")
                .Build();
        }
    }

    /// <summary>
    /// 闪烁任务栏图标以提醒用户 — 1 秒去抖，配置关闭时仅返回文字。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，包含电脑名和进程 PID</returns>
    [McpTool("flash_taskbar", "闪烁任务栏图标以提醒用户", "notification")]
    public async Task<ToolResult> FlashTaskbarAsync(CancellationToken cancellationToken = default)
    {
        var (machine, pid) = GetProcessInfo();

        if (!_coordinator.IsShakeEnabled)
            return ToolResultBuilder.Success()
                .WithText($"任务栏闪烁已关闭（电脑:{machine}, PID:{pid}）")
                .Build();

        if (!_coordinator.TryAcquireShakeSlot())
            return ToolResultBuilder.Success()
                .WithText($"1秒内已闪烁过，跳过（电脑:{machine}, PID:{pid}）")
                .Build();

        try
        {
            if (_shakeService is not null)
                await _shakeService.FlashTaskbarAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("任务栏闪烁已执行: machine={Machine}, pid={Pid}", machine, pid);
            return ToolResultBuilder.Success()
                .WithText($"已闪烁任务栏（电脑:{machine}, PID:{pid}）")
                .Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "任务栏闪烁失败");
            return ToolResultBuilder.Error()
                .WithText($"任务栏闪烁失败: {ex.Message}（电脑:{machine}, PID:{pid}）")
                .Build();
        }
    }

    /// <summary>
    /// 获取当前进程信息 — 电脑名和进程 PID，供 AI 回答用户"你在什么电脑的什么进程"。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，包含电脑名和进程 PID</returns>
    [McpTool("get_process_info", "获取当前进程信息(电脑名+PID)", "notification")]
    public Task<ToolResult> GetProcessInfoAsync(CancellationToken cancellationToken = default)
    {
        var (machine, pid) = GetProcessInfo();
        return Task.FromResult(ToolResultBuilder.Success()
            .WithText($"电脑名:{machine}, 进程PID:{pid}")
            .Build());
    }

    private static (string Machine, int Pid) GetProcessInfo()
        => (Environment.MachineName, Environment.ProcessId);
}
