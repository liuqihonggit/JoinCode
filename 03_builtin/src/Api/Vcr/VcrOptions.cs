
namespace Services.Api.Vcr;

public sealed class VcrOptions
{
    public VcrMode Mode { get; init; } = VcrMode.None;
    public string CassettesDirectory { get; init; } = "cassettes";
    public bool RecordHeaders { get; init; } = true;
    public bool RecordContent { get; init; } = true;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool StrictPlayback { get; init; } = false;
    public int MaxCassetteSizeBytes { get; init; } = 10 * 1024 * 1024;
}
