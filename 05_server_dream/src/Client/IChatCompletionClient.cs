
namespace JoinCode.Dream.Services;

/// <summary>
/// 聊天完成客户端接口 - 封装 IQueryService 以便测试
/// </summary>
public interface IChatCompletionClient
{
    /// <summary>
    /// 获取聊天完成结果
    /// </summary>
    /// <param name="chatHistory">聊天历史</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>完成结果内容</returns>
    Task<string> GetCompletionAsync(MessageList chatHistory, CancellationToken cancellationToken = default);
}
