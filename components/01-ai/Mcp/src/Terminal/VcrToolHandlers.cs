

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Vcr, Optional = true)]
public sealed partial class VcrToolHandlers
{
    private readonly IVcrService _vcrService;
    [Inject] private readonly ILogger<VcrToolHandlers>? _logger;

    public VcrToolHandlers(IVcrService vcrService, ILogger<VcrToolHandlers>? logger = null)
    {
        _vcrService = vcrService ?? throw new ArgumentNullException(nameof(vcrService));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.VcrRecord, "Start VCR recording, log interactions to specified cassette", "vcr")]
    public async Task<ToolResult> VcrRecordAsync(
        [McpToolParameter("Cassette name")] string cassette_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cassette_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.CassetteNameCannotBeEmpty)).Build();
        }

        try
        {
            _vcrService.SetMode(VcrMode.Record);

            await _vcrService.LoadCassetteAsync(cassette_name, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("VCR recording started: {CassetteName}", cassette_name);

            return McpResultBuilder.Success()
                .WithText(L.T(StringKey.VcrRecordStarted, cassette_name))
                .Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VcrRecordStartFailedLog), cassette_name);
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VcrRecordStartFailed, ex.Message))
                .Build();
        }
    }

    [McpTool(SystemToolNameConstants.VcrPlayback, "Playback interactions from VCR cassette", "vcr")]
    public async Task<ToolResult> VcrPlaybackAsync(
        [McpToolParameter("Cassette name")] string cassette_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cassette_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.CassetteNameCannotBeEmpty)).Build();
        }

        try
        {
            _vcrService.SetMode(VcrMode.Playback);

            var cassette = await _vcrService.LoadCassetteAsync(cassette_name, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("VCR playback started: {CassetteName}", cassette_name);

            var response = new StringBuilder(256);
            response.AppendLine(L.T(StringKey.VcrPlaybackStarted, cassette_name));
            response.AppendLine(L.T(StringKey.VcrPlaybackLabelCassetteName, cassette.Name));

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VcrPlaybackStartFailedLog), cassette_name);
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.VcrPlaybackStartFailed, ex.Message))
                .Build();
        }
    }

    [McpTool(SystemToolNameConstants.VcrStatus, "Get VCR service status", "vcr")]
    public Task<ToolResult> VcrStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder(128);
        response.AppendLine(L.T(StringKey.VcrServiceStatus));
        response.AppendLine(L.T(StringKey.VcrLabelCurrentMode, _vcrService.CurrentMode));

        return Task.FromResult(McpResultBuilder.Success().WithText(response.ToString()).Build());
    }
}
