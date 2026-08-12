namespace JoinCode.ChatCommands;

/// <summary>
/// 聊天命令上下文
/// </summary>
public sealed class ChatCommandContext
{
    /// <summary>
    /// 命令参数
    /// </summary>
    public required string Arguments { get; init; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// 会话开始时间
    /// </summary>
    public DateTime SessionStartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// 清屏回调（可选，由REPL循环注入）
    /// </summary>
    public Action? ClearScreen { get; init; }

    /// <summary>
    /// 用户确认回调（可选，用于 /commit 等需要确认的命令）
    /// </summary>
    public Func<string, bool>? Confirm { get; init; }

    /// <summary>
    /// 用户输入回调（可选，用于 /commit 等需要输入的命令）
    /// </summary>
    public Func<string, string?>? Prompt { get; init; }

    /// <summary>
    /// 密码输入回调（可选，用于 /login 等需要安全输入的命令）
    /// </summary>
    public Func<string, string?>? ReadPassword { get; init; }

    /// <summary>
    /// DI 服务容器 — composition 层通过 GetCommandServices() 扩展方法获取强类型 CommandServices
    /// </summary>
    public required IServiceProvider Services { get; init; }
}

/// <summary>
/// 聊天命令执行结果
/// </summary>
public sealed class ChatCommandResult
{
    /// <summary>
    /// 是否继续聊天循环
    /// </summary>
    public bool ShouldContinue { get; init; } = true;

    /// <summary>
    /// 是否成功处理命令
    /// </summary>
    public bool IsHandled { get; init; } = true;

    /// <summary>
    /// 创建继续执行的结果
    /// </summary>
    public static ChatCommandResult Continue() => new() { ShouldContinue = true, IsHandled = true };

    /// <summary>
    /// 创建退出聊天的结果
    /// </summary>
    public static ChatCommandResult Exit() => new() { ShouldContinue = false, IsHandled = true };

    /// <summary>
    /// 创建未处理的结果（命令不存在）
    /// </summary>
    public static ChatCommandResult NotHandled() => new() { ShouldContinue = true, IsHandled = false };
}

/// <summary>
/// 聊天命令接口
/// </summary>
public interface IChatCommand
{
    string Name { get; }

    string Description { get; }

    string Usage { get; }

    string[] Aliases { get; }

    string ArgumentHint { get; }

    bool IsHidden { get; }

    /// <summary>
    /// 命令是否当前可用 — 对齐 TS CommandBase.isEnabled()
    /// 返回 false 时命令不可见且不可执行，用于动态门控（如 entitlement 检查）
    /// 默认 true（始终可用）
    /// </summary>
    bool IsEnabled => true;

    Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context);
}
