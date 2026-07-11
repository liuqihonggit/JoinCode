namespace McpClient.Auth;

/// <summary>
/// Unicode 清理工具 — 对齐 TS partiallySanitizeUnicode + recursivelySanitizeUnicode
/// 防御 Unicode 隐藏字符攻击（ASCII Smuggling / Hidden Prompt Injection）
/// 参考: HackerOne 报告 #3086545
/// </summary>
public static partial class McpUnicodeSanitizer
{
    /// <summary>
    /// 最大迭代轮次 — 防止恶意构造的深层嵌套 Unicode 字符串导致无限循环
    /// </summary>
    private const int MaxIterations = 10;

    /// <summary>
    /// 部分清理 Unicode 字符串 — 对齐 TS partiallySanitizeUnicode
    /// 迭代清理，最多 MaxIterations 轮，直到无变化
    /// </summary>
    public static string PartiallySanitize(string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);

        var current = input;
        for (var i = 0; i < MaxIterations; i++)
        {
            var sanitized = SanitizeRound(current);
            if (string.Equals(sanitized, current, StringComparison.Ordinal))
            {
                return sanitized;
            }

            current = sanitized;
        }

        // 超过最大迭代轮次，返回当前结果（对齐 TS 抛出错误，但 C# 侧选择保守返回）
        return current;
    }

    /// <summary>
    /// 递归清理字符串数组 — 对齐 TS recursivelySanitizeUnicode (Array 分支)
    /// </summary>
    public static string[] SanitizeStringArray(string[] values)
    {
        var result = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = PartiallySanitize(values[i]);
        }
        return result;
    }

    /// <summary>
    /// 递归清理字符串字典 — 对齐 TS recursivelySanitizeUnicode (Object 分支)
    /// </summary>
    public static Dictionary<string, string> SanitizeStringDictionary(Dictionary<string, string> dict)
    {
        var result = new Dictionary<string, string>(dict.Count);
        foreach (var (key, value) in dict)
        {
            result[PartiallySanitize(key)] = PartiallySanitize(value);
        }
        return result;
    }

    /// <summary>
    /// 清理 JSON 字符串中的 Unicode — 在反序列化前对原始 JSON 字符串进行清理
    /// 这是 AOT 兼容的替代方案，避免使用 JsonElement 递归
    /// </summary>
    public static string SanitizeJsonString(string jsonString)
    {
        ArgumentException.ThrowIfNullOrEmpty(jsonString);
        return PartiallySanitize(jsonString);
    }

    /// <summary>
    /// 单轮清理
    /// </summary>
    private static string SanitizeRound(string input)
    {
        // 1. NFKC 规范化 — 处理组合字符序列
        var result = input.Normalize(NormalizationForm.FormKC);

        // 2. 移除危险 Unicode 属性类
        //    \p{Cf}: 格式字符 (Format characters) — 零宽字符等
        //    \p{Co}: 私用区字符 (Private Use Area)
        //    \p{Cn}: 未分配字符 (Unassigned)
        result = DangerousUnicodeRegex().Replace(result, string.Empty);

        // 3. 显式字符范围移除（兜底，防止某些环境不支持 Unicode 属性类）
        result = ExplicitDangerousRangeRegex().Replace(result, string.Empty);

        return result;
    }

    /// <summary>
    /// 危险 Unicode 属性类正则 — \p{Cf}\p{Co}\p{Cn}
    /// </summary>
    [GeneratedRegex(@"[\p{Cf}\p{Co}\p{Cn}]", RegexOptions.Compiled)]
    private static partial Regex DangerousUnicodeRegex();

    /// <summary>
    /// 显式危险字符范围正则 — 兜底
    /// \u200B-\u200F: 零宽空格、LTR/RTL 标记
    /// \u202A-\u202E: 方向格式字符
    /// \u2066-\u2069: 方向隔离字符
    /// \uFEFF: BOM
    /// \uE000-\uF8FF: BMP 私用区
    /// </summary>
    [GeneratedRegex(@"[\u200B-\u200F\u202A-\u202E\u2066-\u2069\uFEFF\uE000-\uF8FF]", RegexOptions.Compiled)]
    private static partial Regex ExplicitDangerousRangeRegex();
}
