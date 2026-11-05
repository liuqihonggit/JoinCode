
namespace Core.Skills.ExpressionMethods;

/// <summary>
/// 转大写方法 — 将目标字符串转换为全大写形式
/// </summary>
public sealed class ToUpperMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写，支持别名）
    /// </summary>
    public string[] Names => new[] { "toupper", "touppercase" };

    /// <summary>
    /// 执行转大写操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>转换后的全大写字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.ToUpperInvariant();
}

/// <summary>
/// 转小写方法 — 将目标字符串转换为全小写形式
/// </summary>
public sealed class ToLowerMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写，支持别名）
    /// </summary>
    public string[] Names => new[] { "tolower", "tolowercase" };

    /// <summary>
    /// 执行转小写操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>转换后的全小写字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.ToLowerInvariant();
}

/// <summary>
/// 去除首尾空白方法 — 移除目标字符串首尾的空白字符
/// </summary>
public sealed class TrimMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "trim" };

    /// <summary>
    /// 执行去除首尾空白操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>去除首尾空白后的字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.Trim();
}

/// <summary>
/// 去除首部空白方法 — 移除目标字符串首部的空白字符
/// </summary>
public sealed class TrimStartMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "trimstart" };

    /// <summary>
    /// 执行去除首部空白操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>去除首部空白后的字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.TrimStart();
}

/// <summary>
/// 去除尾部空白方法 — 移除目标字符串尾部的空白字符
/// </summary>
public sealed class TrimEndMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "trimend" };

    /// <summary>
    /// 执行去除尾部空白操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>去除尾部空白后的字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.TrimEnd();
}

/// <summary>
/// 子字符串方法 — 从目标字符串中截取指定位置的子串
/// </summary>
public sealed class SubstringMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "substring" };

    /// <summary>
    /// 执行子字符串截取操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为起始索引，第二个参数为长度（可选）</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>截取到的子字符串；参数无效时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (args.Count >= 1 && int.TryParse(elementToString(args[0]), out var startIndex)) {
            var length = args.Count >= 2 && int.TryParse(elementToString(args[1]), out var len) ? len : target.Length - startIndex;
            if (startIndex >= 0 && startIndex < target.Length) {
                length = Math.Min(length, target.Length - startIndex);
                return target.Substring(startIndex, length);
            }
        }
        return target;
    }
}

/// <summary>
/// 替换方法 — 将目标字符串中指定的子串替换为新子串
/// </summary>
public sealed class ReplaceMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "replace" };

    /// <summary>
    /// 执行字符串替换操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为要查找的子串，第二个参数为替换后的子串</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>替换后的字符串；参数不足时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (args.Count >= 2) {
            return target.Replace(elementToString(args[0]), elementToString(args[1]));
        }
        return target;
    }
}

/// <summary>
/// 包含判断方法 — 判断目标字符串是否包含指定子串
/// </summary>
public sealed class ContainsMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "contains" };

    /// <summary>
    /// 执行包含判断操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为要查找的子串</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>包含返回 "true"，否则返回 "false"</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.Contains(elementToString(args[0])).ToString() : "false";
}

/// <summary>
/// 起始判断方法 — 判断目标字符串是否以指定子串开头
/// </summary>
public sealed class StartsWithMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "startswith" };

    /// <summary>
    /// 执行起始判断操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为要匹配的前缀子串</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>以指定子串开头返回 "true"，否则返回 "false"</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.StartsWith(elementToString(args[0])).ToString() : "false";
}

/// <summary>
/// 结尾判断方法 — 判断目标字符串是否以指定子串结尾
/// </summary>
public sealed class EndsWithMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "endswith" };

    /// <summary>
    /// 执行结尾判断操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为要匹配的后缀子串</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>以指定子串结尾返回 "true"，否则返回 "false"</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.EndsWith(elementToString(args[0])).ToString() : "false";
}

/// <summary>
/// 查找索引方法 — 查找指定子串在目标字符串中首次出现的索引
/// </summary>
public sealed class IndexOfMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "indexof" };

    /// <summary>
    /// 执行查找索引操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为要查找的子串</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>子串首次出现的索引；未找到返回 "-1"；参数不足也返回 "-1"</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => args.Count >= 1 ? target.IndexOf(elementToString(args[0]), StringComparison.Ordinal).ToString() : "-1";
}

/// <summary>
/// 长度方法 — 获取目标字符串的字符长度
/// </summary>
public sealed class LengthMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "length" };

    /// <summary>
    /// 执行获取长度操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>目标字符串的长度</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString)
        => target.Length.ToString();
}

/// <summary>
/// 分割方法 — 按指定分隔符分割目标字符串并以逗号空格连接
/// </summary>
public sealed class SplitMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "split" };

    /// <summary>
    /// 执行分割操作
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="args">参数列表：第一个参数为分隔符</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>分割后以逗号空格连接的字符串；参数不足时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (args.Count >= 1) {
            var separator = elementToString(args[0]);
            var parts = target.Split(separator);
            return string.Join(", ", parts);
        }
        return target;
    }
}

/// <summary>
/// 格式化方法 — 使用目标字符串作为格式模板，按参数进行字符串格式化
/// </summary>
public sealed class FormatMethod : IExpressionMethod {
    /// <summary>
    /// 方法名列表（小写）
    /// </summary>
    public string[] Names => new[] { "format" };

    /// <summary>
    /// 执行字符串格式化操作
    /// </summary>
    /// <param name="target">作为格式模板的目标字符串</param>
    /// <param name="args">参数列表：用于填充格式占位符的参数</param>
    /// <param name="elementToString">将 JsonElement 转为字符串的辅助方法</param>
    /// <returns>格式化后的字符串；格式化失败或参数不足时返回原字符串</returns>
    public string Execute(string target, List<JsonElement> args, Func<JsonElement, string> elementToString) {
        if (args.Count >= 1) {
            try {
                return string.Format(CultureInfo.InvariantCulture, target, args.Select(elementToString).Cast<object>().ToArray());
            } catch {
                return target;
            }
        }
        return target;
    }
}