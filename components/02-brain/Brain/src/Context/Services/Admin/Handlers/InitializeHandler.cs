using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 初始化操作处理器
/// </summary>
[Register]
public sealed partial class InitializeHandler : IChatAdminOperationHandler
{
    [Inject] private readonly IChatInitializer _initializer;

    public ChatAdminOperation Operation => ChatAdminOperation.Initialize;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await _initializer.InitializeAsync(context.ToolUseContext!).ConfigureAwait(false);
    }
}
