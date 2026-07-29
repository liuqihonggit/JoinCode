using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class StickerService : IStickerService
{
    private const string StickerPageUrl = "https://jcc.dev/stickers";
    private readonly IProcessService _processService;
    [Inject] private readonly ILogger<StickerService>? _logger;

    public StickerService(IProcessService processService, ILogger<StickerService>? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    public string GetStickerPageUrl() => StickerPageUrl;

    public async Task<bool> OpenStickerPageAsync(CancellationToken ct = default)
    {
        if (TestEnvironmentDetector.IsNonInteractive)
        {
            _logger?.LogInformation("非交互环境,跳过打开贴纸页面");
            return false;
        }

        try
        {
            return await _processService.OpenAsync(StickerPageUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "打开浏览器失败");
            return false;
        }
    }
}
