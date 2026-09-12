namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具处理组根基类 — 定义超时策略。子类（组长）sealed override TimeoutPolicy。
/// 源码生成器读取继承链，将 TimeoutPolicy 注入到生成的 IToolHandler wrapper。
/// 新增工具时继承正确的组即可，无需修改中间件或配置。
/// </summary>
public abstract class ToolHandlerGroupBase
{
    /// <summary>超时策略 — 由子类 sealed override</summary>
    public abstract ToolTimeoutPolicy TimeoutPolicy { get; }
}

/// <summary>
/// 一次性命令组 — 2分钟绝对超时 + kill + 可续期。
/// 适用于 Shell/PowerShell/搜索/Hook 等一次性命令执行工具。
/// </summary>
public abstract class OneShotCommandGroup : ToolHandlerGroupBase
{
    /// <inheritdoc />
    public sealed override ToolTimeoutPolicy TimeoutPolicy => ToolTimeoutPolicy.AbsoluteTwoMinutes;
}

/// <summary>
/// 长期运行组 — 无绝对超时。
/// 适用于 REPL 等长期交互式进程工具。
/// </summary>
public abstract class LongRunningGroup : ToolHandlerGroupBase
{
    /// <inheritdoc />
    public sealed override ToolTimeoutPolicy TimeoutPolicy => ToolTimeoutPolicy.None;
}
