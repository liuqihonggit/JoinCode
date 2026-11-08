namespace McpToolDispatch;

/// <summary>
/// 窗口震动工具处理器 — 震动进程窗口/闪烁任务栏/获取进程信息，供子代理向用户展示存在感。
/// 通过 <see cref="IWindowShakeCoordinator"/> 1 秒去抖，防止多子代理高频震动。
/// </summary>
[McpToolDispatch(ToolCategory.Notification, Optional = true)]
public partial class ShakeWindowToolHandlers {
    private readonly IWindowShakeCoordinator _coordinator;
    private readonly IWindowShakeService? _shakeService;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly ILogger<ShakeWindowToolHandlers>? _logger;

    /// <summary>
    /// 初始化窗口震动工具处理器
    /// </summary>
    /// <param name="coordinator">震动去抖协调器</param>
    /// <param name="shakeService">窗口震动服务（可选，未注册时仅返回文字）</param>
    /// <param name="mailboxService">队友邮箱服务（可选，用于跨进程广播震动消息）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ShakeWindowToolHandlers(
        IWindowShakeCoordinator coordinator,
        IWindowShakeService? shakeService = null,
        ITeammateMailboxService? mailboxService = null,
        ILogger<ShakeWindowToolHandlers>? logger = null) {
        _coordinator = coordinator;
        _shakeService = shakeService;
        _mailboxService = mailboxService;
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
        CancellationToken cancellationToken = default) {
        var (machine, pid) = GetProcessInfo();

        if (!_coordinator.IsShakeEnabled)
            return ToolResultBuilder.Success()
                .WithText($"窗口震动已关闭（电脑:{machine}, PID:{pid}）")
                .Build();

        if (!_coordinator.TryAcquireShakeSlot())
            return ToolResultBuilder.Success()
                .WithText($"1秒内已震动过，跳过（电脑:{machine}, PID:{pid}）")
                .Build();

        try {
            if (_shakeService is not null) {
                var result = await _shakeService.ShakeWindowAsync(cancellationToken).ConfigureAwait(false);
                if (result is null)
                    return ToolResultBuilder.Success()
                        .WithText($"无法震动窗口 — 沙箱中找不到终端窗口（电脑:{machine}, PID:{pid}）")
                        .Build();

                await BroadcastShakeMessageAsync(machine, pid, reason, cancellationToken).ConfigureAwait(false);
                _logger?.LogDebug("窗口震动已执行: reason={Reason}, target={Target}", reason, result);
                return ToolResultBuilder.Success()
                    .WithText($"已震动窗口 — {result}（电脑:{machine}, PID:{pid}）")
                    .Build();
            }

            await BroadcastShakeMessageAsync(machine, pid, reason, cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success()
                .WithText($"已震动窗口（电脑:{machine}, PID:{pid}）")
                .Build();
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
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
    public async Task<ToolResult> FlashTaskbarAsync(CancellationToken cancellationToken = default) {
        var (machine, pid) = GetProcessInfo();

        if (!_coordinator.IsShakeEnabled)
            return ToolResultBuilder.Success()
                .WithText($"任务栏闪烁已关闭（电脑:{machine}, PID:{pid}）")
                .Build();

        if (!_coordinator.TryAcquireShakeSlot())
            return ToolResultBuilder.Success()
                .WithText($"1秒内已闪烁过，跳过（电脑:{machine}, PID:{pid}）")
                .Build();

        try {
            if (_shakeService is not null)
                await _shakeService.FlashTaskbarAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("任务栏闪烁已执行: machine={Machine}, pid={Pid}", machine, pid);
            return ToolResultBuilder.Success()
                .WithText($"已闪烁任务栏（电脑:{machine}, PID:{pid}）")
                .Build();
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
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
    public Task<ToolResult> GetProcessInfoAsync(CancellationToken cancellationToken = default) {
        var (machine, pid) = GetProcessInfo();
        var windowInfo = _shakeService?.GetWindowInfo() ?? "(窗口服务未注册)";
        return Task.FromResult(ToolResultBuilder.Success()
            .WithText($"电脑名:{machine}, 进程PID:{pid}\n{windowInfo}")
            .Build());
    }

    private static (string Machine, int Pid) GetProcessInfo()
        => (Environment.MachineName, Environment.ProcessId);

    /// <summary>
    /// 通过邮箱广播 shake 消息到其他进程 — 跨进程震动通知，ADR 0109。
    /// 消息类型 <c>"shake"</c>，内容为 <c>"machine:pid"</c>，接收端可识别并执行本地震动。
    /// </summary>
    private async Task BroadcastShakeMessageAsync(string machine, int pid, string? reason, CancellationToken cancellationToken) {
        if (_mailboxService is null)
            return;
        try {
            await _mailboxService.SendAsync(new MailboxSendRequest {
                FromAgentId = $"bot-{pid}",
                ToAgentId = "all-agents",
                MessageType = "shake",
                Content = $"{machine}:{pid}:{reason ?? ""}",
                SessionId = "shake-broadcast"
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "广播震动消息失败");
        }
    }
}