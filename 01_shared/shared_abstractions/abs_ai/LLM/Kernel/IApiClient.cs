namespace JoinCode.Abstractions.LLM;

public interface IChatClient
{
    IQueryService GetChatCompletionService();
    IToolCollection Plugins { get; }
}
