
namespace Core.Skills;

/// <summary>
/// 表达式方法策略接口 — 替代 ExpressionEvaluator.ExecuteMethod 中的 switch 分派
/// 每个方法实现独立封装自己的逻辑，通过 ExpressionMethodRegistry 注册
/// </summary>
public interface IExpressionMethod
{
    /// <summary>
    /// 方法名列表（小写，支持别名）— 如 ["toupper", "touppercase"]
    /// </summary>
    string[] Names { get; }

    /// <summary>
    /// 执行方法
    /// </summary>
    /// <param name="target">目标值（已转为字符串）</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">辅助方法：将 JsonElement 转为字符串</param>
    /// <returns>执行结果</returns>
    string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString);
}
