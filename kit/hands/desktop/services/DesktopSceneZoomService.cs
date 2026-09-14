namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面场景缩放服务实现 — 四叉树象限缩小 + 状态更新 + 清晰度判断
/// </summary>
[Register(typeof(IDesktopSceneZoomService), ServiceLifetime.Singleton)]
public sealed class DesktopSceneZoomService : ServiceEntity, IDesktopSceneZoomService
{
    private const int ClearThreshold = 64;

    private readonly IQuadtreeRenderer _renderer;
    private readonly IDesktopSceneStateStore _stateStore;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// DI 构造函数
    /// </summary>
    public DesktopSceneZoomService(
        IQuadtreeRenderer renderer,
        IDesktopSceneStateStore stateStore,
        IFileSystem fileSystem)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>
    /// 选象限缩小，返回子图 + 更新后的格子编码/层数 + 清晰度判断
    /// </summary>
    public async Task<DesktopSceneZoom> ZoomAsync(string sceneId, int quadrant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(sceneId);
        if (quadrant is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(quadrant), "象限编号必须 1-4: 1=左上 2=右上 3=左下 4=右下");

        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        if (!env.IsInteractive)
            throw new InvalidOperationException(env.Diagnostic);

        var state = await _stateStore.LoadAsync(sceneId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"场景 {sceneId} 状态不存在，请先调 desktop_look 建立场景");

        if (state.LastScreenshotPath is null || !_fileSystem.FileExists(state.LastScreenshotPath))
            throw new InvalidOperationException($"场景 {sceneId} 截图文件不存在: {state.LastScreenshotPath}");

        var screenshotBase64 = await _fileSystem.ReadAllTextAsync(state.LastScreenshotPath, cancellationToken).ConfigureAwait(false);
        var (width, height) = PngHeaderParser.ParseDimensions(screenshotBase64);

        var mappedQuadrant = MapQuadrant(quadrant);
        var newCellCode = state.CurrentCellCode + "." + mappedQuadrant;
        var newDepth = state.CurrentDepth + 1;

        var zoomResult = await _renderer.ZoomAsync(
            screenshotBase64, newCellCode, width, height, state.CurrentDepth, newDepth, cancellationToken).ConfigureAwait(false);

        var subWidth = zoomResult.Grid.ImageWidth;
        var subHeight = zoomResult.Grid.ImageHeight;
        var isClearEnough = subWidth <= ClearThreshold && subHeight <= ClearThreshold;

        var newScreenshotPath = await SaveSubImageAsync(sceneId, newDepth, zoomResult.SubImageBase64, cancellationToken).ConfigureAwait(false);

        var zoomHistory = state.ZoomHistory.ToList();
        zoomHistory.Add(new ZoomHistoryEntry(newDepth, newCellCode, quadrant, DateTimeOffset.UtcNow));
        var newState = state with
        {
            CurrentDepth = newDepth,
            CurrentCellCode = newCellCode,
            ZoomHistory = zoomHistory,
            LastScreenshotPath = newScreenshotPath,
            LastAction = "zoom"
        };
        await _stateStore.SaveAsync(newState, cancellationToken).ConfigureAwait(false);

        return new DesktopSceneZoom(zoomResult.SubImageBase64, newCellCode, newDepth, subWidth, subHeight, isClearEnough);
    }

    private static int MapQuadrant(int quadrant) => quadrant switch
    {
        1 => 2,
        2 => 3,
        3 => 0,
        4 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(quadrant))
    };

    private async Task<string> SaveSubImageAsync(string sceneId, int depth, string base64, CancellationToken ct)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "scenarios");
        if (!_fileSystem.DirectoryExists(dir))
            _fileSystem.CreateDirectory(dir);
        var path = _fileSystem.CombinePath(dir, $"{sceneId}_zoom_{depth}.b64");
        await _fileSystem.WriteAllTextAsync(path, base64, ct).ConfigureAwait(false);
        return path;
    }
}
