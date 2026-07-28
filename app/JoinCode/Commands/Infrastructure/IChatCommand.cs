
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
    /// 服务容器 — 聚合所有服务引用
    /// </summary>
    public required CommandServices Services { get; init; }
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

public abstract class ChatCommandBase : IChatCommand
{
    private readonly ChatCommandAttribute? _attr;

    protected ChatCommandBase()
    {
        _attr = GetType().GetCustomAttributes(typeof(ChatCommandAttribute), false).Cast<ChatCommandAttribute>().FirstOrDefault();
    }

    public virtual string Name => _attr?.Name ?? string.Empty;
    public virtual string Description => _attr?.Description ?? string.Empty;
    public virtual string Usage => _attr?.Usage ?? string.Empty;
    public virtual string[] Aliases => _attr?.Aliases ?? [];
    public virtual string ArgumentHint => _attr?.ArgumentHint ?? string.Empty;
    public virtual bool IsHidden => _attr?.IsHidden ?? false;

    /// <summary>
    /// 命令是否当前可用 — 对齐 TS CommandBase.isEnabled()
    /// 默认从特性读取，子类可 override 实现动态门控
    /// </summary>
    public virtual bool IsEnabled => _attr?.IsEnabled ?? true;

    public abstract Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context);

    /// <summary>
    /// 从 ServiceProvider 获取服务，未注册时输出错误并返回 null
    /// </summary>
    internal static T? GetService<T>(ChatCommandContext context) where T : class
    {
        var service = context.Services.ServiceProvider?.GetService<T>();
        if (service is null && !TerminalHelper.IsInputRedirected)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{typeof(T).Name} 服务未初始化{AnsiStyleConstants.Reset}");
        }
        return service;
    }

    /// <summary>
    /// 从 ServiceProvider 获取服务（非泛型版本），未注册时输出错误并返回 null
    /// </summary>
    internal static T? GetService<T>(ChatCommandContext context, Type serviceType) where T : class
    {
        var service = context.Services.ServiceProvider?.GetService(serviceType) as T;
        if (service is null && !TerminalHelper.IsInputRedirected)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{serviceType.Name} 服务未初始化{AnsiStyleConstants.Reset}");
        }
        return service;
    }

    /// <summary>
    /// 统一错误处理
    /// </summary>
    internal static void HandleError(string operation, Exception ex)
    {
        TerminalHelper.WriteLine($"{TerminalColors.Error}{operation}失败: {ex.Message}{AnsiStyleConstants.Reset}");
    }

    /// <summary>
    /// 获取标准化参数（Trim）
    /// </summary>
    internal static string GetNormalizedArgs(ChatCommandContext context)
        => context.Arguments.Trim();

    /// <summary>
    /// 获取拆分后的参数数组
    /// </summary>
    internal static string[] GetSplitArgs(ChatCommandContext context)
        => context.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// 聊天命令特性 - 用于自动注册，Category 声明命令分类（特性解耦，无需中央映射表）
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ChatCommandAttribute : Attribute
{
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Usage { get; init; } = string.Empty;

    public string[] Aliases { get; init; } = [];

    public string ArgumentHint { get; init; } = string.Empty;

    public bool IsHidden { get; init; }

    /// <summary>
    /// 命令是否当前可用 — 对齐 TS CommandBase.isEnabled()
    /// 特性声明为静态值，动态门控需在命令类中 override IsEnabled 属性
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// 命令分类 — 每个命令自己声明，源码生成器自动提取，无需中央映射表
    /// </summary>
    public ChatCommandCategory Category { get; init; } = ChatCommandCategory.Other;
}
