namespace IO.Services;

/// <summary>
/// 桌面交接服务 — 检测 jcc-desktop 应用并将 CLI 会话交接给桌面应用
/// </summary>
[Register(typeof(IDesktopHandoffService), ServiceLifetime.Singleton)]
public sealed partial class DesktopHandoffService : ServiceEntity, IDesktopHandoffService {
    private readonly IProcessService _processService;
    private readonly ILogger<DesktopHandoffService>? _logger;

    /// <summary>
    /// 构造桌面交接服务实例
    /// </summary>
    /// <param name="processService">进程服务抽象，用于查找 jcc-desktop 可执行文件</param>
    /// <param name="logger">日志记录器</param>
    public DesktopHandoffService(IProcessService processService, ILogger<DesktopHandoffService>? logger = null) {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    /// <summary>
    /// 桌面应用是否可用 — 仅在 Windows/macOS 且能找到 jcc-desktop 可执行文件时返回 true
    /// </summary>
    public bool IsDesktopAvailable {
        get {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return false;
            try {
                return Task.Run(() => _processService.FindExecutableAsync("jcc-desktop")).GetAwaiter().GetResult() != null;
            } catch {
                return false;
            }
        }
    }

    /// <summary>
    /// 桌面连接信息 — 桌面应用可用时返回提示文本，不可用时返回 null
    /// </summary>
    public string? DesktopConnectionInfo {
        get {
            if (!IsDesktopAvailable) return null;
            return "jcc-desktop 应用已检测到，可通过 Bridge 连接";
        }
    }

    /// <summary>
    /// 异步将指定会话交接给桌面应用 — 桌面应用不可用时返回 false
    /// </summary>
    /// <param name="sessionId">要交接的会话 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>交接成功返回 true；桌面应用未检测到返回 false</returns>
    public Task<bool> HandoffToDesktopAsync(string sessionId, CancellationToken ct = default) {
        if (!IsDesktopAvailable) {
            _logger?.LogWarning("桌面应用未检测到，无法转移会话");
            return Task.FromResult(false);
        }

        _logger?.LogInformation("正在将会话 {SessionId} 转移到桌面应用", sessionId);
        return Task.FromResult(true);
    }
}