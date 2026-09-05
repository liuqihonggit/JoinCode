namespace Tools.Handlers;

/// <summary>
/// 宏录制工具处理器 — 录制/回放/保存/加载操作序列（PRD S-02/S-03）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class MacroToolHandlers
{
    private readonly IMacroRecorder _recorder;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<MacroToolHandlers>? _logger;

    public MacroToolHandlers(IMacroRecorder recorder, IFileSystem fileSystem, ILogger<MacroToolHandlers>? logger = null)
    {
        _recorder = recorder;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>开始录制宏（S-02）</summary>
    [McpTool("start_recording", "开始录制桌面操作序列为宏", "desktop")]
    public Task<ToolResult> StartRecordingAsync(
        [McpToolParameter("宏名称", Required = true)] string macroName,
        CancellationToken ct = default)
    {
        _recorder.StartRecording(macroName);
        return Task.FromResult(ToolResultBuilder.Success().WithText($"开始录制宏「{macroName}」,后续桌面操作将被记录").Build());
    }

    /// <summary>停止录制并保存宏（S-02）</summary>
    [McpTool("stop_recording", "停止录制并保存宏到文件", "desktop")]
    public Task<ToolResult> StopRecordingAsync(
        [McpToolParameter("保存路径（.json）,不传则不保存", Required = false)] string? savePath = null,
        CancellationToken ct = default)
    {
        if (!_recorder.IsRecording)
            return Task.FromResult(ToolResultBuilder.Error().WithText("当前未在录制状态").Build());

        var macro = _recorder.StopRecording();
        var sb = new StringBuilder(128);
        sb.AppendLine($"已停止录制宏「{macro.Name}」,共 {macro.Operations.Count} 步操作");

        if (!string.IsNullOrEmpty(savePath))
        {
            try
            {
                _recorder.SaveMacro(macro, savePath);
                sb.AppendLine($"已保存到: {savePath}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"保存失败: {ex.Message}");
            }
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    /// <summary>回放宏（S-03）</summary>
    [McpTool("play_macro", "从文件加载宏并回放,支持加速执行", "desktop")]
    public async Task<ToolResult> PlayMacroAsync(
        [McpToolParameter("宏文件路径（.json）", Required = true)] string filePath,
        [McpToolParameter("加速倍数（1=原速,2=2倍速,0=最大速度）", Required = false)] int speed = 1,
        CancellationToken ct = default)
    {
        Macro macro;
        try
        {
            macro = _recorder.LoadMacro(filePath);
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"加载宏失败: {ex.Message}").Build();
        }

        var result = await _recorder.PlayAsync(macro, speed, ct).ConfigureAwait(false);

        var sb = new StringBuilder(128);
        sb.AppendLine($"回放宏「{macro.Name}」完成:");
        sb.AppendLine($"  总步骤: {result.TotalSteps}");
        sb.AppendLine($"  成功: {result.SucceededSteps}");
        sb.AppendLine($"  失败: {result.FailedSteps}");
        sb.AppendLine($"  耗时: {result.Elapsed.TotalMilliseconds:F0}ms");
        sb.AppendLine($"  加速: {speed}x");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>列出目录中的宏文件</summary>
    [McpTool("list_macros", "列出指定目录中的宏文件(.json)", "desktop")]
    public Task<ToolResult> ListMacrosAsync(
        [McpToolParameter("目录路径", Required = false)] string? directory = null,
        CancellationToken ct = default)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? Path.Combine(Path.GetTempPath(), "jcc-macros") : directory;
        if (!_fileSystem.DirectoryExists(dir))
            _fileSystem.CreateDirectory(dir);

        var files = _fileSystem.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return Task.FromResult(ToolResultBuilder.Success().WithText($"目录「{dir}」中无宏文件").Build());

        var sb = new StringBuilder(128);
        sb.AppendLine($"目录「{dir}」中共 {files.Length} 个宏文件:");
        foreach (var f in files)
        {
            var size = _fileSystem.GetFileLength(f);
            var time = _fileSystem.GetLastWriteTime(f);
            var name = Path.GetFileName(f);
            sb.AppendLine($"  {name} ({size} bytes, {time:yyyy-MM-dd HH:mm})");
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(sb.ToString()).Build());
    }
}
