namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代码沙箱服务接口 - 在隔离环境中执行代码
/// </summary>
public interface ICodeSandboxService
{
    /// <summary>
    /// 在沙箱中执行 C# 代码
    /// </summary>
    /// <param name="code">要执行的代码</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<string> ExecuteAsync(string code, int timeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行 C# 表达式
    /// </summary>
    /// <param name="expression">表达式</param>
    /// <param name="variables">变量定义（JSON格式）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<string> EvaluateExpressionAsync(string expression, string? variables, CancellationToken cancellationToken = default);
}
