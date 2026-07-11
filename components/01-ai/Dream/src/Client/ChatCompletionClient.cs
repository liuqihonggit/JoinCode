
namespace JoinCode.Dream.Services;

/// <summary>
/// 聊天完成客户端实现 - 包装 IQueryService
/// </summary>
[Register]
public sealed partial class ChatCompletionClient : IChatCompletionClient
{
    private readonly IChatClient _kernel;

    public ChatCompletionClient(IChatClient kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    /// <inheritdoc />
    public async Task<string> GetCompletionAsync(MessageList chatHistory, CancellationToken cancellationToken = default)
    {
        var chatCompletion = _kernel.GetChatCompletionService();
        var results = await chatCompletion.GetApiMessageContentsAsync(
            chatHistory,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return results.FirstOrDefault()?.Content ?? string.Empty;
    }
}
