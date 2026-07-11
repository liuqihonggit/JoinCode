
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 会话标签服务接口 - 管理会话标签的增删查
/// </summary>
public interface ISessionTagService
{
    /// <summary>
    /// 为会话添加标签
    /// </summary>
    bool AddTag(string sessionId, string tag);

    /// <summary>
    /// 为会话移除标签
    /// </summary>
    bool RemoveTag(string sessionId, string tag);

    /// <summary>
    /// 获取会话的所有标签
    /// </summary>
    IReadOnlyList<string> GetTags(string sessionId);

    /// <summary>
    /// 获取所有会话的标签映射
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> GetAllTags();
}
