namespace JoinCode.Abstractions.State;

/// <summary>
/// 聊天消息文档
/// </summary>
public sealed class ApiMessageDocument : ChatMessage {
    /// <summary>获取或设置元数据字典。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}
