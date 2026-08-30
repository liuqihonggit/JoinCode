namespace Core.Context;

/// <summary>
/// 初始化操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class InitializeHandler : ServiceEntity, IChatAdminOperationHandler
{

    public InitializeHandler(IChatInitializer initializer)
    {
        _initializer = initializer;
    }
    private readonly IChatInitializer _initializer;

    public ChatAdminOperation Operation => ChatAdminOperation.Initialize;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await _initializer.InitializeAsync(context.ToolUseContext ?? throw new InvalidOperationException("ToolUseContext is required.")).ConfigureAwait(false);
    }
}
