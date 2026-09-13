
namespace Services.Api.Vcr;

/// <summary>
/// VCR（录像/回放）服务接口，负责管理 cassette 与交互记录
/// </summary>
public interface IVcrService
{
    /// <summary>
    /// 获取 cassette 文件完整路径
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖，为 null 时使用默认目录</param>
    /// <returns>cassette 文件绝对路径</returns>
    string GetCassettePath(string name, string? directory = null);

    /// <summary>
    /// 异步加载 cassette，若不存在则创建空 cassette
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>加载到的 cassette 实例</returns>
    Task<VcrCassette> LoadCassetteAsync(string name, string? directory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存 cassette 到磁盘
    /// </summary>
    /// <param name="cassette">cassette 实例</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步保存操作的任务</returns>
    Task SaveCassetteAsync(VcrCassette cassette, string? directory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 录制一次 HTTP 交互（仅在录制模式下生效）
    /// </summary>
    /// <param name="cassetteName">cassette 名称</param>
    /// <param name="request">请求记录</param>
    /// <param name="response">响应记录</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步录制操作的任务</returns>
    Task RecordInteractionAsync(string cassetteName, VcrRequest request, VcrResponse response, string? directory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在回放模式下查找匹配的录制交互
    /// </summary>
    /// <param name="cassetteName">cassette 名称</param>
    /// <param name="request">待匹配的请求</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配到的响应；未匹配时返回 null 或抛出异常（取决于严格模式）</returns>
    Task<VcrResponse?> FindMatchingInteractionAsync(string cassetteName, VcrRequest request, string? directory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前 VCR 模式
    /// </summary>
    VcrMode CurrentMode { get; }

    /// <summary>
    /// cassette 默认存放目录
    /// </summary>
    string CassettesDirectory { get; }

    /// <summary>
    /// 切换 VCR 运行模式
    /// </summary>
    /// <param name="mode">目标模式</param>
    void SetMode(VcrMode mode);
}
