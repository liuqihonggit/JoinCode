
using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class ClipboardService : IClipboardService
{
    [Inject] private readonly ILogger<ClipboardService>? _logger;
    [Inject] private readonly IProcessService _processService;

    public ClipboardService(IProcessService processService, ILogger<ClipboardService>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

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
            throw new PlatformNotSupportedException("剪贴板操作不支持当前平台");
        }
    }

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
            Arguments = "-NoProfile -Command \"Get-Clipboard\""
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
            Arguments = "-selection clipboard"
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
            Arguments = "-selection clipboard -o"
        };

        var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(result.StandardOutput) ? null : result.StandardOutput;
    }
}
