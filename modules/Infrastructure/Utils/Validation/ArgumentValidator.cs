
namespace Core.Utils;

/// <summary>
/// 参数验证辅助类 - 统一参数验证逻辑，减少重复代码
/// </summary>
public static class ArgumentValidator
{
    /// <summary>
    /// 验证参数不为 null
    /// </summary>
    /// <typeparam name="T">参数类型</typeparam>
    /// <param name="value">参数值</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的参数值</returns>
    /// <exception cref="ArgumentNullException">当参数为 null 时抛出</exception>
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
        return value;
    }

    /// <summary>
    /// 验证字符串参数不为 null 或空
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的字符串值</returns>
    /// <exception cref="ArgumentException">当字符串为 null 或空时抛出</exception>
    public static string NotNullOrEmpty([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"参数 '{paramName}' 不能为 null 或空字符串。", paramName);
        }
        return value;
    }

    /// <summary>
    /// 验证字符串参数不为 null 或空白字符
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的字符串值</returns>
    /// <exception cref="ArgumentException">当字符串为 null 或空白时抛出</exception>
    public static string NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"参数 '{paramName}' 不能为 null 或空白字符串。", paramName);
        }
        return value;
    }

    /// <summary>
    /// 验证集合参数不为 null 且不为空
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    /// <param name="value">集合值</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的集合值</returns>
    /// <exception cref="ArgumentException">当集合为 null 或空时抛出</exception>
    public static IEnumerable<T> NotNullOrEmpty<T>([NotNull] IEnumerable<T>? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null || !value.Any())
        {
            throw new ArgumentException($"参数 '{paramName}' 不能为 null 或空集合。", paramName);
        }
        return value;
    }

    /// <summary>
    /// 验证数值参数大于零
    /// </summary>
    /// <param name="value">数值</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的数值</returns>
    /// <exception cref="ArgumentOutOfRangeException">当数值小于等于零时抛出</exception>
    public static int GreaterThanZero(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"参数 '{paramName}' 必须大于零。");
        }
        return value;
    }

    /// <summary>
    /// 验证数值参数大于等于零
    /// </summary>
    /// <param name="value">数值</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的数值</returns>
    /// <exception cref="ArgumentOutOfRangeException">当数值小于零时抛出</exception>
    public static int GreaterThanOrEqualToZero(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"参数 '{paramName}' 必须大于等于零。");
        }
        return value;
    }

    /// <summary>
    /// 验证数值参数在指定范围内
    /// </summary>
    /// <param name="value">数值</param>
    /// <param name="min">最小值（包含）</param>
    /// <param name="max">最大值（包含）</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的数值</returns>
    /// <exception cref="ArgumentOutOfRangeException">当数值不在范围内时抛出</exception>
    public static int InRange(int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"参数 '{paramName}' 必须在 {min} 和 {max} 之间。");
        }
        return value;
    }

    /// <summary>
    /// 验证文件路径不为 null 且格式有效
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="paramName">参数名（自动获取）</param>
    /// <returns>验证后的文件路径</returns>
    /// <exception cref="ArgumentException">当路径无效时抛出</exception>
    public static string ValidPath([NotNull] string? path, [CallerArgumentExpression(nameof(path))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"参数 '{paramName}' 不能为 null 或空白字符串。", paramName);
        }

        // 检查路径中是否包含无效字符
        var invalidChars = Path.GetInvalidPathChars();
        if (path.IndexOfAny(invalidChars) >= 0)
        {
            throw new ArgumentException($"参数 '{paramName}' 包含无效的路径字符。", paramName);
        }

        return path;
    }

    /// <summary>
    /// 验证条件为真
    /// </summary>
    /// <param name="condition">条件表达式</param>
    /// <param name="message">错误消息</param>
    /// <exception cref="ArgumentException">当条件为假时抛出</exception>
    public static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }

    /// <summary>
    /// 验证条件为真，使用延迟加载的错误消息
    /// </summary>
    /// <param name="condition">条件表达式</param>
    /// <param name="messageFactory">错误消息工厂</param>
    /// <exception cref="ArgumentException">当条件为假时抛出</exception>
    public static void IsTrue(bool condition, Func<string> messageFactory)
    {
        if (!condition)
        {
            throw new ArgumentException(messageFactory());
        }
    }
}
