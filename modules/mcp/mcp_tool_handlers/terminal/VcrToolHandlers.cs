

namespace McpToolDispatch;

/// <summary>
/// VCR 工具处理器 — 提供 HTTP API 录制/回放功能（类似 Fiddler 抓包），供测试回放用
/// </summary>
[McpToolDispatch(ToolCategory.Vcr, Optional = true)]
public sealed partial class VcrToolHandlers {
    private readonly IVcrService _vcrService;
    private readonly ILogger<VcrToolHandlers>? _logger;

    /// <summary>
    /// 初始化 VCR 工具处理器
    /// </summary>
    /// <param name="vcrService">VCR 服务实例</param>
    /// <param name="logger">日志记录器（可选）</param>
    public VcrToolHandlers(IVcrService vcrService, ILogger<VcrToolHandlers>? logger = null) {
        _vcrService = vcrService ?? throw new ArgumentNullException(nameof(vcrService));
        _logger = logger;
    }

    /// <summary>
    /// 录制 HTTP API 交互到 cassette — 启动后拦截所有 HTTP 请求+响应保存到 JSON 文件，供测试回放用
    /// </summary>
    /// <param name="cassette_name">Cassette 名称</param>
    /// <param name="cassette_directory">Cassette 保存目录（绝对路径或相对路径），不传则用默认 cassettes/ 目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.VcrRecord, "录制 HTTP API 交互到 cassette（类似 Fiddler 抓包）。启动后拦截所有 HTTP 请求+响应保存到 JSON 文件，供测试回放用。需在交互式模式(jcc chat)单进程内使用", "vcr")]
    public async Task<ToolResult> VcrRecordAsync(
        [McpToolParameter("Cassette name")] string cassette_name,
        [McpToolParameter("Cassette 保存目录（绝对路径或相对路径），不传则用默认 cassettes/ 目录")] string? cassette_directory = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(cassette_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CassetteNameCannotBeEmpty)).Build();
        }

        try {
            _vcrService.SetMode(VcrMode.Record);

            await _vcrService.LoadCassetteAsync(cassette_name, cassette_directory, cancellationToken).ConfigureAwait(false);

            var cassettePath = _vcrService.GetCassettePath(cassette_name, cassette_directory);

            _logger?.LogInformation("VCR recording started: {CassetteName} -> {CassettePath}", cassette_name, cassettePath);

            var response = new StringBuilder(256);
            response.AppendLine(L.T(StringKey.VcrRecordStarted, cassette_name));
            response.AppendLine($"  path: {cassettePath}");

            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.VcrRecordStartFailedLog), cassette_name);
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.VcrRecordStartFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 回放 VCR cassette 中录制的 HTTP API 响应 — 启动后匹配请求返回录制的响应，不发真实 HTTP 请求
    /// </summary>
    /// <param name="cassette_name">Cassette 名称</param>
    /// <param name="cassette_directory">Cassette 所在目录（绝对路径或相对路径），不传则用默认 cassettes/ 目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.VcrPlayback, "回放 VCR cassette 中录制的 HTTP API 响应。启动后匹配请求返回录制的响应，不发真实 HTTP 请求。供测试/离线开发用", "vcr")]
    public async Task<ToolResult> VcrPlaybackAsync(
        [McpToolParameter("Cassette name")] string cassette_name,
        [McpToolParameter("Cassette 所在目录（绝对路径或相对路径），不传则用默认 cassettes/ 目录")] string? cassette_directory = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(cassette_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CassetteNameCannotBeEmpty)).Build();
        }

        try {
            _vcrService.SetMode(VcrMode.Playback);

            var cassette = await _vcrService.LoadCassetteAsync(cassette_name, cassette_directory, cancellationToken).ConfigureAwait(false);

            var cassettePath = _vcrService.GetCassettePath(cassette_name, cassette_directory);

            _logger?.LogInformation("VCR playback started: {CassetteName} <- {CassettePath}", cassette_name, cassettePath);

            var response = new StringBuilder(256);
            response.AppendLine(L.T(StringKey.VcrPlaybackStarted, cassette_name));
            response.AppendLine(L.T(StringKey.VcrPlaybackLabelCassetteName, cassette.Name));
            response.AppendLine($"  path: {cassettePath}");

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.VcrPlaybackStartFailedLog), cassette_name);
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.VcrPlaybackStartFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 查询 VCR HTTP API 录制/回放服务的当前状态（模式: Off/Record/Playback + cassette 目录）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含 VCR 服务状态的工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.VcrStatus, "查询 VCR HTTP API 录制/回放服务的当前状态（模式: Off/Record/Playback + cassette 目录）", "vcr")]
    public Task<ToolResult> VcrStatusAsync(
        CancellationToken cancellationToken = default) {
        var response = new StringBuilder(128);
        response.AppendLine(L.T(StringKey.VcrServiceStatus));
        response.AppendLine(L.T(StringKey.VcrLabelCurrentMode, _vcrService.CurrentMode));
        response.AppendLine($"  cassettes_dir: {Path.GetFullPath(_vcrService.CassettesDirectory)}");

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }
}