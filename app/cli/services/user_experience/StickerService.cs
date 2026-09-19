namespace IO.Services;

/// <summary>
/// 贴纸服务 — 提供贴纸页面 URL 查询与浏览器打开能力
/// </summary>
[Register(typeof(IStickerService), ServiceLifetime.Singleton)]
public sealed partial class StickerService : ServiceEntity, IStickerService {
    private const string StickerPageUrl = "https://jcc.dev/stickers";
    private readonly IProcessService _processService;
    private readonly ILogger<StickerService>? _logger;

    /// <summary>初始化 <see cref="StickerService"/> 实例</summary>
    /// <param name="processService">进程服务，用于打开浏览器</param>
    /// <param name="logger">可选的日志记录器，为 null 时不记录日志</param>
    public StickerService(IProcessService processService, ILogger<StickerService>? logger = null) {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    /// <summary>获取贴纸页面 URL</summary>
    /// <returns>贴纸页面 URL 常量</returns>
    public string GetStickerPageUrl() => StickerPageUrl;

    /// <summary>异步在默认浏览器中打开贴纸页面；非交互环境直接返回 false</summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>打开成功返回 true；非交互环境或打开失败返回 false</returns>
    public async Task<bool> OpenStickerPageAsync(CancellationToken ct = default) {
        if (TestEnvironmentDetector.IsNonInteractive) {
            _logger?.LogInformation("非交互环境,跳过打开贴纸页面");
            return false;
        }

        try {
            return await _processService.OpenAsync(StickerPageUrl, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "打开浏览器失败");
            return false;
        }
    }
}