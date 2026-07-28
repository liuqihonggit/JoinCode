
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 剪贴板服务接口 - 跨平台剪贴板读写
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// 设置剪贴板文本内容
    /// </summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取剪贴板文本内容
    /// </summary>
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}
