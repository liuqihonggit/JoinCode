
namespace Core.Skills;

/// <summary>
/// 表达式方法注册表 — 替代 ExpressionEvaluator.ExecuteMethod 中的 switch 分派
/// 通过 FrozenDictionary 查找方法实现，O(1) 复杂度
/// </summary>
public static class ExpressionMethodRegistry
{
    private static readonly FrozenDictionary<string, IExpressionMethod> Methods = CreateRegistry();

    private static FrozenDictionary<string, IExpressionMethod> CreateRegistry()
    {
        var methods = new IExpressionMethod[]
        {
            new ExpressionMethods.ToUpperMethod(),
            new ExpressionMethods.ToLowerMethod(),
            new ExpressionMethods.TrimMethod(),
            new ExpressionMethods.TrimStartMethod(),
            new ExpressionMethods.TrimEndMethod(),
            new ExpressionMethods.SubstringMethod(),
            new ExpressionMethods.ReplaceMethod(),
            new ExpressionMethods.ContainsMethod(),
            new ExpressionMethods.StartsWithMethod(),
            new ExpressionMethods.EndsWithMethod(),
            new ExpressionMethods.IndexOfMethod(),
            new ExpressionMethods.LengthMethod(),
            new ExpressionMethods.SplitMethod(),
            new ExpressionMethods.FormatMethod(),
            new ExpressionMethods.AbsMethod(),
            new ExpressionMethods.RoundMethod(),
            new ExpressionMethods.FloorMethod(),
            new ExpressionMethods.CeilingMethod(),
            new ExpressionMethods.MaxMethod(),
            new ExpressionMethods.MinMethod(),
        };

        var builder = new Dictionary<string, IExpressionMethod>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in methods)
        {
            foreach (var name in method.Names)
            {
                builder[name] = method;
            }
        }

        return builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 查找方法实现 — 找不到返回 null
    /// </summary>
    public static IExpressionMethod? TryGetMethod(string methodName)
    {
        return Methods.GetValueOrDefault(methodName);
    }
}
