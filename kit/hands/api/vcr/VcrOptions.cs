
namespace Services.Api.Vcr;

/// <summary>
/// VCR（录像/回放）配置选项
/// </summary>
public sealed class VcrOptions {
    /// <summary>
    /// VCR 运行模式
    /// </summary>
    public VcrMode Mode { get; init; } = VcrMode.None;

    /// <summary>
    /// cassette 文件存放目录
    /// </summary>
    public string CassettesDirectory { get; init; } = "cassettes";

    /// <summary>
    /// 是否录制请求/响应头
    /// </summary>
    public bool RecordHeaders { get; init; } = true;

    /// <summary>
    /// 是否录制请求/响应体
    /// </summary>
    public bool RecordContent { get; init; } = true;

    /// <summary>
    /// 请求超时时间
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用严格回放模式（未匹配到录制交互时抛出异常）
    /// </summary>
    public bool StrictPlayback { get; init; } = false;

    /// <summary>
    /// cassette 文件最大字节数
    /// </summary>
    public int MaxCassetteSizeBytes { get; init; } = 10 * 1024 * 1024;
}