namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面场景截图编排服务实现 — 截图 + 四叉树网格构建 + 网格叠加渲染 + 状态持久化
/// </summary>
[Register(typeof(IDesktopSceneCaptureService), ServiceLifetime.Singleton)]
public sealed class DesktopSceneCaptureService : ServiceEntity, IDesktopSceneCaptureService
{
    private readonly IScreenCaptureService _screenCapture;
    private readonly IQuadtreeAnnotator _annotator;
    private readonly IQuadtreeRenderer _renderer;
    private readonly IDesktopSceneStateStore _stateStore;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// DI 构造函数
    /// </summary>
    public DesktopSceneCaptureService(
        IScreenCaptureService screenCapture,
        IQuadtreeAnnotator annotator,
        IQuadtreeRenderer renderer,
        IDesktopSceneStateStore stateStore,
        IFileSystem fileSystem)
    {
        _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
        _annotator = annotator ?? throw new ArgumentNullException(nameof(annotator));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>
    /// 全屏截图并构建四叉树网格叠加渲染图，同时持久化场景状态
    /// </summary>
    public async Task<DesktopSceneCapture> CaptureWithGridAsync(string sceneId, int depth = 2, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(sceneId);
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        if (!env.IsInteractive)
            throw new InvalidOperationException(env.Diagnostic);

        var screenshotBase64 = await _screenCapture.CaptureFullScreenAsync(cancellationToken).ConfigureAwait(false);
        var (width, height) = PngHeaderParser.ParseDimensions(screenshotBase64);

        var grid = _annotator.BuildGrid(width, height, depth);
        var paints = grid.Cells.ToFrozenDictionary(c => c.Code, _ => 0.3);
        var paintedGrid = _annotator.PaintCells(grid, paints);
        var renderResult = await _renderer.RenderAsync(screenshotBase64, paintedGrid, cancellationToken).ConfigureAwait(false);

        var screenshotPath = await SaveScreenshotAsync(sceneId, screenshotBase64, cancellationToken).ConfigureAwait(false);
        var state = new DesktopSceneState(
            sceneId, 0, "L0", [], screenshotPath, "look", DateTimeOffset.UtcNow);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);

        return new DesktopSceneCapture(screenshotBase64, renderResult.RenderedBase64, width, height, depth);
    }

    private async Task<string> SaveScreenshotAsync(string sceneId, string base64, CancellationToken ct)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "scenarios");
        if (!_fileSystem.DirectoryExists(dir))
            _fileSystem.CreateDirectory(dir);
        var path = _fileSystem.CombinePath(dir, sceneId + "_screenshot.b64");
        await _fileSystem.WriteAllTextAsync(path, base64, ct).ConfigureAwait(false);
        return path;
    }
}

/// <summary>
/// PNG 头解析器 — 从 base64 PNG 提取宽高（无需图像库依赖）
/// </summary>
internal static class PngHeaderParser
{
    /// <summary>
    /// 从 base64 PNG 解析图片尺寸（PNG IHDR chunk 偏移 16-23 字节）
    /// </summary>
    public static (int Width, int Height) ParseDimensions(string base64Png)
    {
        var bytes = Convert.FromBase64String(base64Png);
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50)
            throw new ArgumentException("无效 PNG 数据", nameof(base64Png));
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }
}
