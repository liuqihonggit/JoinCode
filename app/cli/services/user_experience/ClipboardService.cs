namespace IO.Services;

/// <summary>
/// 剪贴板服务 — 跨平台读写系统剪贴板（Windows/MacOS/Linux），通过外部进程调用原生剪贴板命令
/// </summary>
[Register(typeof(IClipboardService), ServiceLifetime.Singleton)]
public sealed partial class ClipboardService : ServiceEntity, IClipboardService
{
    private readonly ILogger<ClipboardService>? _logger;
    private readonly IProcessService _processService;

    /// <summary>
    /// 构造函数 — 注入进程服务与可选日志
    /// </summary>
    /// <param name="processService">进程服务，用于启动剪贴板原生命令</param>
    /// <param name="logger">日志记录器，可选</param>
    public ClipboardService(IProcessService processService, ILogger<ClipboardService>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    /// <summary>
    /// 设置剪贴板文本 — 根据当前平台调用对应原生命令
    /// </summary>
    /// <param name="text">要写入剪贴板的文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (OperatingSystem.IsWindows())
        {
            await SetWindowsClipboardAsync(text, cancellationToken).ConfigureAwait(false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await SetMacOSClipboardAsync(text, cancellationToken).ConfigureAwait(false);
        }
        else if (OperatingSystem.IsLinux())
        {
            await SetLinuxClipboardAsync(text, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new PlatformNotSupportedException("[APP007] 剪贴板操作不支持当前平台");
        }
    }

    /// <summary>
    /// 读取剪贴板文本 — 根据当前平台调用对应原生命令
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>剪贴板文本，若为空或不支持平台则返回 null</returns>
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await GetWindowsClipboardAsync(cancellationToken).ConfigureAwait(false);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await GetMacOSClipboardAsync(cancellationToken).ConfigureAwait(false);
        }

        if (OperatingSystem.IsLinux())
        {
            return await GetLinuxClipboardAsync(cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task SetWindowsClipboardAsync(string text, CancellationToken cancellationToken)
    {
        var options = new InteractiveProcessOptions
        {
            FileName = "clip"
        };

        var interactiveProcess = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);
        await interactiveProcess.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        interactiveProcess.StandardInput.Close();
        await interactiveProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await interactiveProcess.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<string?> GetWindowsClipboardAsync(CancellationToken cancellationToken)
    {
        var options = new ProcessOptions
        {
            FileName = "powershell",
            ArgumentList = new[] { "-NoProfile", "-Command", "Get-Clipboard" }
        };

        var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(result.StandardOutput) ? null : result.StandardOutput;
    }

    private async Task SetMacOSClipboardAsync(string text, CancellationToken cancellationToken)
    {
        var options = new InteractiveProcessOptions
        {
            FileName = "pbcopy"
        };

        var interactiveProcess = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);
        await interactiveProcess.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        interactiveProcess.StandardInput.Close();
        await interactiveProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await interactiveProcess.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<string?> GetMacOSClipboardAsync(CancellationToken cancellationToken)
    {
        var options = new ProcessOptions
        {
            FileName = "pbpaste"
        };

        var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(result.StandardOutput) ? null : result.StandardOutput;
    }

    private async Task SetLinuxClipboardAsync(string text, CancellationToken cancellationToken)
    {
        var options = new InteractiveProcessOptions
        {
            FileName = "xclip",
            ArgumentList = new[] { "-selection", "clipboard" }
        };

        var interactiveProcess = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);
        await interactiveProcess.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        interactiveProcess.StandardInput.Close();
        await interactiveProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await interactiveProcess.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<string?> GetLinuxClipboardAsync(CancellationToken cancellationToken)
    {
        var options = new ProcessOptions
        {
            FileName = "xclip",
            ArgumentList = new[] { "-selection", "clipboard", "-o" }
        };

        var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(result.StandardOutput) ? null : result.StandardOutput;
    }
}
