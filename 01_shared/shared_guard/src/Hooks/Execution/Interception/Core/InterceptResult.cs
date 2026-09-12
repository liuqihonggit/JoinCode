namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令拦截结果 — 拦截器对命令的处理结果(可有状态、可异步)
/// <para>
/// 结果类型:
/// <list type="bullet">
/// <item><see cref="Handled"/> 已处理,短路返回此结果</item>
/// <item><see cref="Continue"/> 未处理,继续下一个拦截器</item>
/// </list>
/// </para>
/// </summary>
public abstract record InterceptResult
{
    /// <summary>
    /// 已处理 — 短路返回此工具结果,命令不再继续管道
    /// </summary>
    /// <param name="Result">拦截处理产生的工具结果</param>
    public sealed record Handled(ToolResult Result) : InterceptResult;

    /// <summary>
    /// 未处理 — 继续评估下一个拦截器
    /// </summary>
    public sealed record Continue : InterceptResult;
}
