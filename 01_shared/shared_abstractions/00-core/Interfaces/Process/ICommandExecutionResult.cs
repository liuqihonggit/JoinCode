namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 统一命令执行结果接口 — bash/shell/git 命令执行的共同结构化信息
/// <para>
/// 三套结果类型(ProcessResult/GitCommandResult/SystemActuatorExecutionResult)均实现此接口,
/// 提供统一的 ExitCode/Success/Output/Error/ExecutionTime 字段,日后可扩展更多字段(MemoryUsage/CpuTime 等)
/// </para>
/// </summary>
public interface ICommandExecutionResult
{
    /// <summary>退出码(0=成功,非0=失败,null=未获取/中断)</summary>
    int? ExitCode { get; }

    /// <summary>是否成功(ExitCode==0)</summary>
    bool Success { get; }

    /// <summary>标准输出(stdout)</summary>
    string Output { get; }

    /// <summary>标准错误(stderr)</summary>
    string Error { get; }

    /// <summary>执行时长</summary>
    TimeSpan ExecutionTime { get; }
}
