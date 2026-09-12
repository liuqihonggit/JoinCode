namespace IO.ProcessService;

/// <summary>
/// 命令参数危险字符校验器 — 第一道防线，黑名单拦截 shell 元字符注入
/// <para>
/// 黑名单策略：拒绝包含 shell 元字符（&amp;|;`$()&lt;&gt; 换行）的参数，
/// 除非调用方显式设置 <c>SkipArgumentValidation=true</c>（如 bash -c "cmd &amp;&amp; cmd" 场景）。
/// </para>
/// <para>
/// 不拦截的字符：空格、-、/、\、:、.、=、_ 等 — 这些是路径和参数的合法字符。
/// </para>
/// </summary>
public static class CommandArgumentValidator
{
    /// <summary>
    /// 危险字符黑名单 — shell 元字符，可能导致命令注入
    /// </summary>
    public static readonly FrozenSet<char> DangerousChars = FrozenSet.Create(
        '&', '|', ';', '`', '$', '(', ')', '<', '>', '\n', '\r'
    );

    /// <summary>
    /// 校验单个参数字符串是否包含危险字符
    /// </summary>
    /// <param name="argument">待校验的参数字符串</param>
    /// <exception cref="ArgumentException">参数包含危险字符时抛出</exception>
    public static void ValidateString(string argument)
    {
        if (string.IsNullOrEmpty(argument)) return;

        foreach (var ch in argument)
        {
            if (DangerousChars.Contains(ch))
            {
                throw new ArgumentException(
                    $"参数包含危险字符 '{ch}' (U+{(int)ch:X4})，可能导致命令注入。参数值: \"{argument}\"。" +
                    "如需允许该字符，请设置 SkipArgumentValidation=true。",
                    nameof(argument));
            }
        }
    }

    /// <summary>
    /// 校验参数列表中每个参数是否包含危险字符
    /// </summary>
    /// <param name="arguments">待校验的参数列表，null 或空则跳过</param>
    /// <exception cref="ArgumentException">任一参数包含危险字符时抛出</exception>
    public static void ValidateList(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0) return;

        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (string.IsNullOrEmpty(arg)) continue;

            foreach (var ch in arg)
            {
                if (DangerousChars.Contains(ch))
                {
                    throw new ArgumentException(
                        $"参数列表第 {i} 项包含危险字符 '{ch}' (U+{(int)ch:X4})，可能导致命令注入。参数值: \"{arg}\"。" +
                        "如需允许该字符，请设置 SkipArgumentValidation=true。",
                        nameof(arguments));
                }
            }
        }
    }
}
