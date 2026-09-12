namespace JoinCode.ChatCommands;

/// <summary>
/// 前缀命令执行上下文
/// </summary>
public sealed class PrefixCommandContext
{
    /// <summary>DI 服务容器（可选，当前 ! / !! 处理器直接用 Process.Start 不依赖 DI）</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>取消令牌</summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>工作目录（可选，默认当前目录）</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// 前缀命令执行结果
/// </summary>
/// <param name="Handled">是否已处理</param>
/// <param name="Output">输出文本（回显或注入 AI）</param>
/// <param name="ShouldInjectToAi">是否将输出注入 AI 上下文（! = true, !! = false）</param>
public sealed record PrefixCommandResult(bool Handled, string Output, bool ShouldInjectToAi)
{
    /// <summary>未处理的空结果</summary>
    public static PrefixCommandResult NotHandled => new(false, string.Empty, false);
}

/// <summary>
/// 前缀命令处理器接口 — 处理 ! / !! 前缀命令。
/// 对齐 IChatCommand 模式，但前缀命令后面跟任意 shell 命令而非固定命令名。
/// </summary>
public interface IPrefixCommandHandler
{
    /// <summary>前缀符号（"!" 或 "!!"）</summary>
    string Prefix { get; }

    /// <summary>是否触发 AI 对话</summary>
    bool TriggersAi { get; }

    /// <summary>执行前缀命令</summary>
    /// <param name="command">前缀后的命令内容（已去除前缀）</param>
    /// <param name="context">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<PrefixCommandResult> ExecuteAsync(string command, PrefixCommandContext context, CancellationToken cancellationToken);
}
