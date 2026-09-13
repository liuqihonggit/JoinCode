
namespace Services.Notification;

/// <summary>
/// 通知服务 — 在 Windows 上弹出系统通知,其他平台输出日志
/// </summary>
[Register(typeof(INotificationService), ServiceLifetime.Singleton)]
public partial class NotificationService : ServiceEntity, INotificationService
{
    private readonly ILogger<NotificationService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly bool _isWindows;
    private readonly bool _isTestEnvironment;

    /// <summary>
    /// 通知发送事件 — 在通知发送后触发
    /// </summary>
    public event EventHandler<NotificationSentEventArgs>? NotificationSent;

    /// <summary>
    /// 创建通知服务实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    /// <param name="telemetryService">可选的遥测服务</param>
    public NotificationService(ILogger<NotificationService>? logger = null, ITelemetryService? telemetryService = null)
    {
        _logger = logger;
        _telemetryService = telemetryService;
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _isTestEnvironment = IsTestEnvironment();
    }

    /// <inheritdoc />
    public bool IsAvailable => _isWindows;

    private static bool IsTestEnvironment()
    {
        return TestEnvironmentDetector.IsTestEnvironment;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            return;

        _logger?.LogInformation(L.T(StringKey.VaultLogSendNotification), title, message);

        _telemetryService?.RecordCount("notification.count", new Dictionary<string, string> { ["operation"] = "send" }, "count", "Notification count");
        NotificationSent?.Invoke(this, new NotificationSentEventArgs(title, message, nameof(NotificationType.Info)));

        if (_isTestEnvironment)
            return;

        if (_isWindows)
        {
            await SendWindowsNotificationAsync(title, message, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger?.LogInformation("{Title}: {Message}", title, message);
        }
    }

    /// <inheritdoc />
    public async Task NotifyTaskCompletedAsync(string taskId, string description, bool success, CancellationToken cancellationToken = default)
    {
        var title = success ? L.T(StringKey.VaultTaskCompleted) : L.T(StringKey.VaultTaskFailed);
        var msg = $"[{taskId}] {description}";
        await NotifyAsync(title, msg, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task NotifyAgentMessageAsync(string agentId, string agentName, string message, CancellationToken cancellationToken = default)
    {
        var title = L.T(StringKey.VaultAgentMessage, agentName);
        var displayMessage = $"[{agentId}] {message}";
        await NotifyAsync(title, displayMessage, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendWindowsNotificationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var escapedTitle = title.Replace("'", "''").Replace("\"", "`\"");
            var escapedMessage = message.Replace("'", "''").Replace("\"", "`\"");
            var script = $"Add-Type -AssemblyName System.Windows.Forms; $n = New-Object System.Windows.Forms.NotifyIcon; $n.Icon = [System.Drawing.SystemIcons]::Information; $n.BalloonTipTitle = '{escapedTitle}'; $n.BalloonTipText = '{escapedMessage}'; $n.Visible = $true; $n.ShowBalloonTip(3000); Start-Sleep -Milliseconds 3000; $n.Dispose()";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask.ConfigureAwait(false);
                _logger?.LogWarning(L.T(StringKey.VaultLogWindowsNotificationFailed), stderr);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogWindowsNotificationException));
        }
    }
}


/// <summary>
/// 通知发送事件参数
/// </summary>
public sealed partial class NotificationSentEventArgs : EventArgs
{
    /// <summary>通知标题</summary>
    public string Title { get; }
    /// <summary>通知正文</summary>
    public string Message { get; }
    /// <summary>通知级别(如 Info、Warning、Error)</summary>
    public string Level { get; }

    /// <summary>
    /// 创建通知发送事件参数
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知正文</param>
    /// <param name="level">通知级别</param>
    public NotificationSentEventArgs(string title, string message, string level)
    {
        Title = title;
        Message = message;
        Level = level;
    }
}

/// <summary>
/// 控制台通知服务（备用实现）
/// </summary>
public partial class ConsoleNotificationService : INotificationService
{
    private readonly ILogger<ConsoleNotificationService>? _logger;

    /// <summary>
    /// 创建控制台通知服务实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public ConsoleNotificationService(ILogger<ConsoleNotificationService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[{Timestamp}] {Title}: {Message}", DateTime.Now.ToString("HH:mm:ss"), title, message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyTaskCompletedAsync(string taskId, string description, bool success, CancellationToken cancellationToken = default)
    {
        var title = success ? L.T(StringKey.VaultTaskCompleted) : L.T(StringKey.VaultTaskFailed);
        var message = $"[{taskId}] {description}";
        return NotifyAsync(title, message, cancellationToken);
    }

    /// <inheritdoc />
    public Task NotifyAgentMessageAsync(string agentId, string agentName, string message, CancellationToken cancellationToken = default)
    {
        var title = L.T(StringKey.VaultAgentMessage, agentName);
        var displayMessage = $"[{agentId}] {message}";
        return NotifyAsync(title, displayMessage, cancellationToken);
    }
}
