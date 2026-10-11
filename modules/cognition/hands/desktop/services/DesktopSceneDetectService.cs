namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面场景 UI 元素检测服务实现 — 多模态识别当前区域的 UI 元素
/// </summary>
[Register(typeof(IDesktopSceneDetectService), ServiceLifetime.Singleton)]
public sealed class DesktopSceneDetectService : ServiceEntity, IDesktopSceneDetectService {
    private readonly IUiElementDetector _detector;
    private readonly IDesktopSceneStateStore _stateStore;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// DI 构造函数
    /// </summary>
    public DesktopSceneDetectService(
        IUiElementDetector detector,
        IDesktopSceneStateStore stateStore,
        IFileSystem fileSystem) {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>
    /// 识别当前缩放区域的 UI 元素（按钮/输入框/标签等）
    /// </summary>
    public async Task<DesktopSceneDetection> DetectAsync(string sceneId, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(sceneId);

        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        if (!env.IsInteractive)
            throw new InvalidOperationException(env.Diagnostic);

        var state = await _stateStore.LoadAsync(sceneId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"场景 {sceneId} 状态不存在，请先调 desktop_look 建立场景");

        if (state.LastScreenshotPath is null || !_fileSystem.FileExists(state.LastScreenshotPath))
            throw new InvalidOperationException($"场景 {sceneId} 截图文件不存在: {state.LastScreenshotPath}");

        var screenshotBase64 = await _fileSystem.ReadAllTextAsync(state.LastScreenshotPath, cancellationToken).ConfigureAwait(false);
        var detectionResult = await _detector.DetectAsync(screenshotBase64, cancellationToken).ConfigureAwait(false);

        var elements = detectionResult.Elements
            .Select(e => new DetectedUiElement(MapElementType(e.Type), e.Text ?? e.Description ?? "", state.CurrentCellCode))
            .ToList();

        return new DesktopSceneDetection(sceneId, elements);
    }

    private static string MapElementType(UiElementType type) => type switch {
        UiElementType.Button => "button",
        UiElementType.TextBox => "input",
        UiElementType.Menu or UiElementType.MenuItem => "menu",
        UiElementType.CheckBox => "checkbox",
        UiElementType.RadioButton => "radio",
        UiElementType.Link => "link",
        UiElementType.Icon => "icon",
        UiElementType.Text or UiElementType.TitleBar => "label",
        UiElementType.Image => "image",
        UiElementType.ComboBox => "select",
        UiElementType.ListItem => "listitem",
        _ => "unknown"
    };
}