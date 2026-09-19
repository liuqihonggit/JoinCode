
namespace Core.Skills.ExpressionMethods;

/// <summary>
/// 绝对值方法 — 计算目标数值的绝对值
/// </summary>
public sealed class AbsMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "abs" };

    /// <summary>
    /// 执行绝对值计算操作
    /// </summary>
    /// <param name="target">目标数值的字符串表示</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>目标数值的绝对值；无法解析为数值时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) {
            return Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

/// <summary>
/// 四舍五入方法 — 将目标数值按指定小数位数进行四舍五入
/// </summary>
public sealed class RoundMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "round" };

    /// <summary>
    /// 执行四舍五入操作
    /// </summary>
    /// <param name="target">目标数值的字符串表示</param>
    /// <param name="args">参数列表：第一个参数为保留的小数位数（可选，默认为 0）</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>四舍五入后的数值；无法解析为数值时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) {
            var decimals = args.Count >= 1 && int.TryParse(elementToString(args[0]), out var d) ? d : 0;
            return Math.Round(value, decimals).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

/// <summary>
/// 向下取整方法 — 计算不大于目标数值的最大整数
/// </summary>
public sealed class FloorMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "floor" };

    /// <summary>
    /// 执行向下取整操作
    /// </summary>
    /// <param name="target">目标数值的字符串表示</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>向下取整后的数值；无法解析为数值时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) {
            return Math.Floor(value).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

/// <summary>
/// 向上取整方法 — 计算不小于目标数值的最小整数
/// </summary>
public sealed class CeilingMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写，支持别名）
    /// </summary>
    public string[] Names => new[] { "ceiling", "ceil" };

    /// <summary>
    /// 执行向上取整操作
    /// </summary>
    /// <param name="target">目标数值的字符串表示</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>向上取整后的数值；无法解析为数值时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) {
            return Math.Ceiling(value).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

/// <summary>
/// 最大值方法 — 比较目标数值与参数数值，返回较大者
/// </summary>
public sealed class MaxMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "max" };

    /// <summary>
    /// 执行求最大值操作
    /// </summary>
    /// <param name="target">目标数值的字符串表示</param>
    /// <param name="args">参数列表：第一个参数为参与比较的另一个数值</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>两个数值中的较大者；参数不足或无法解析时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (args.Count >= 1 &&
            double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value1) &&
            double.TryParse(elementToString(args[0]), NumberStyles.Any, CultureInfo.InvariantCulture, out var value2)) {
            return Math.Max(value1, value2).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}

/// <summary>
/// 最小值方法 — 比较目标数值与参数数值，返回较小者
/// </summary>
public sealed class MinMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "min" };

    /// <summary>
    /// 执行求最小值操作
    /// </summary>
    /// <param name="target">目标数值的字符串表示</param>
    /// <param name="args">参数列表：第一个参数为参与比较的另一个数值</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>两个数值中的较小者；参数不足或无法解析时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (args.Count >= 1 &&
            double.TryParse(target, NumberStyles.Any, CultureInfo.InvariantCulture, out var value1) &&
            double.TryParse(elementToString(args[0]), NumberStyles.Any, CultureInfo.InvariantCulture, out var value2)) {
            return Math.Min(value1, value2).ToString(CultureInfo.InvariantCulture);
        }
        return target;
    }
}