namespace JoinCode.Abstractions.LLM;

public interface IChatClient {
    /// <summary>获取聊天补全服务。</summary>
    IQueryService GetChatCompletionService();
    /// <summary>获取插件集合。</summary>
    IToolCollection Plugins { get; }
}