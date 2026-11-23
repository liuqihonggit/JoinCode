
namespace JoinCode.Abstractions.Interfaces;

public interface IVcrService {
    /// <summary>获取录像带文件路径。</summary>
    /// <param name="name">录像带名称。</param>
    /// <param name="directory">目录（可选）。</param>
    string GetCassettePath(string name, string? directory = null);
    /// <summary>获取录像带根目录。</summary>
    string CassettesDirectory { get; }
    /// <summary>异步加载录像带。</summary>
    /// <param name="name">录像带名称。</param>
    /// <param name="directory">目录（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<VcrCassette> LoadCassetteAsync(string name, string? directory = null, CancellationToken cancellationToken = default);
    /// <summary>获取当前 VCR 模式。</summary>
    VcrMode CurrentMode { get; }
    /// <summary>设置 VCR 模式。</summary>
    /// <param name="mode">VCR 模式。</param>
    void SetMode(VcrMode mode);
}