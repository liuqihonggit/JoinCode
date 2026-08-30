namespace JoinCode.Hands.Desktop;

/// <summary>
/// 宏录制 JSON 序列化上下文 — AOT 兼容的源码生成器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Macro))]
[JsonSerializable(typeof(List<DesktopOperation>))]
internal sealed partial class MacroJsonContext : JsonSerializerContext;

/// <summary>
/// 宏录制器 — 录制/回放/保存/加载桌面操作序列（PRD S-02/S-03）
/// API 级录制：在 IDesktopInputService 执行操作时自动记录，无需全局钩子
/// </summary>
[Register(typeof(IMacroRecorder), ServiceLifetime.Singleton)]
public sealed partial class MacroRecorder : ServiceEntity, IMacroRecorder
{
    private readonly IDesktopInputService _input;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<MacroRecorder>? _logger;

    private readonly object _lock = new();
    private bool _isRecording;
    private string _macroName = string.Empty;
    private readonly List<DesktopOperation> _recordedOperations = new();

    public MacroRecorder(IDesktopInputService input, IFileSystem fileSystem, ILogger<MacroRecorder>? logger = null)
    {
        _input = input;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>是否正在录制</summary>
    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _isRecording;
            }
        }
    }

    /// <summary>开始录制（清空之前的录制内容）</summary>
    public void StartRecording(string macroName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(macroName);
        lock (_lock)
        {
            _isRecording = true;
            _macroName = macroName;
            _recordedOperations.Clear();
        }
        _logger?.LogInformation("开始录制宏: {Name}", macroName);
    }

    /// <summary>停止录制并返回宏</summary>
    public Macro StopRecording()
    {
        lock (_lock)
        {
            _isRecording = false;
            var macro = new Macro(_macroName, _recordedOperations.ToArray(), DateTimeOffset.UtcNow);
            _logger?.LogInformation("停止录制宏: {Name}, 共 {Count} 步", macro.Name, macro.Operations.Count);
            return macro;
        }
    }

    /// <summary>记录一个操作（仅在录制状态下有效）</summary>
    public void RecordOperation(DesktopOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_lock)
        {
            if (_isRecording)
                _recordedOperations.Add(operation);
        }
    }

    /// <summary>回放宏 — 按顺序执行操作序列</summary>
    public async Task<MacroPlaybackResult> PlayAsync(Macro macro, int speedMultiplier = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var delayMs = speedMultiplier <= 0 ? 0 : 500 / speedMultiplier;
        var succeeded = 0;
        var failed = 0;

        _logger?.LogInformation("开始回放宏: {Name}, 共 {Count} 步, 加速 {Speed}x", macro.Name, macro.Operations.Count, speedMultiplier);

        foreach (var op in macro.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteOperationAsync(op, cancellationToken).ConfigureAwait(false);
            if (result) succeeded++; else failed++;

            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        _logger?.LogInformation("回放完成: {Succeeded} 成功, {Failed} 失败, 耗时 {Elapsed}ms", succeeded, failed, stopwatch.ElapsedMilliseconds);

        return new MacroPlaybackResult(macro.Operations.Count, succeeded, failed, stopwatch.Elapsed);
    }

    /// <summary>保存宏到文件（JSON）</summary>
    public void SaveMacro(Macro macro, string filePath)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = RelaxedJsonSerializer.Serialize(macro, MacroJsonContext.Default);
        _fileSystem.WriteAllText(filePath, json);
        _logger?.LogInformation("保存宏到: {Path}", filePath);
    }

    /// <summary>从文件加载宏</summary>
    public Macro LoadMacro(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var json = _fileSystem.ReadAllText(filePath);
        var macro = JsonSerializer.Deserialize(json, MacroJsonContext.Default.Macro)
            ?? throw new InvalidOperationException("宏文件解析失败");
        _logger?.LogInformation("加载宏: {Name}, 共 {Count} 步", macro.Name, macro.Operations.Count);
        return macro;
    }

    internal async Task<bool> ExecuteOperationAsync(DesktopOperation op, CancellationToken ct)
    {
        try
        {
            DesktopOperation result;
            switch (op.Kind)
            {
                case DesktopOperationKind.Click:
                    result = await _input.ClickAsync(op.X, op.Y, op.MouseAction ?? MouseAction.Click, ct).ConfigureAwait(false);
                    return result.Succeeded;

                case DesktopOperationKind.KeyPress:
                    result = await _input.KeyPressAsync(op.X, op.Modifiers ?? KeyModifier.None, ct).ConfigureAwait(false);
                    return result.Succeeded;

                case DesktopOperationKind.TypeText:
                    result = await _input.TypeTextAsync(op.Text ?? string.Empty, ct).ConfigureAwait(false);
                    return result.Succeeded;

                case DesktopOperationKind.Move:
                    result = await _input.MoveToAsync(op.X, op.Y, ct).ConfigureAwait(false);
                    return result.Succeeded;

                default:
                    _logger?.LogDebug("跳过不支持回放的操作类型: {Kind}", op.Kind);
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "回放操作失败: {Kind}", op.Kind);
            return false;
        }
    }
}
