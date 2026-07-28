using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class DesktopHandoffService : IDesktopHandoffService
{
    private readonly IProcessService _processService;
    [Inject] private readonly ILogger<DesktopHandoffService>? _logger;

    public DesktopHandoffService(IProcessService processService, ILogger<DesktopHandoffService>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    public bool IsDesktopAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return false;
            try
            {
                return _processService.FindExecutableAsync("jcc-desktop").GetAwaiter().GetResult() != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? DesktopConnectionInfo
    {
        get
        {
            if (!IsDesktopAvailable) return null;
            return "jcc-desktop 应用已检测到，可通过 Bridge 连接";
        }
    }

    public Task<bool> HandoffToDesktopAsync(string sessionId, CancellationToken ct = default)
    {
        if (!IsDesktopAvailable)
        {
            _logger?.LogWarning("桌面应用未检测到，无法转移会话");
            return Task.FromResult(false);
        }

        _logger?.LogInformation("正在将会话 {SessionId} 转移到桌面应用", sessionId);
        return Task.FromResult(true);
    }
}
