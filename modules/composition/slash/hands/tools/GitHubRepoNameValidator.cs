namespace JoinCode.ChatCommands;

/// <summary>
/// GitHub 仓库名白名单校验器 — B3 命令注入防御统一数据源
/// <para>
/// 仓库名格式 owner/repo,仅允许字母数字点下划线短横线 + 单斜杠分隔。
/// 拒绝一切 shell 元字符(&amp;|;`$()&lt;&gt; 空格 换行)与多斜杠。
/// </para>
/// <para>
/// 统一数据源: InstallGitHubAppCommand 的 GetCurrentRepoAsync 与 ChooseRepoAsync 必须委托此校验,
/// 禁止在消费方重复正则(AGENTS.md 第 11 条: 硬编码变委托)。
/// </para>
/// </summary>
internal static partial class GitHubRepoNameValidator {
    /// <summary>
    /// 仓库名白名单正则 — owner/repo,每段仅允许字母数字点下划线短横线
    /// <para>GeneratedRegex 保证 AOT 兼容,避免反射 emit</para>
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex RepoNamePattern { get; }

    /// <summary>
    /// 尝试校验并规范化仓库名 — 校验通过返回 true 并输出规范化结果(移除 .git 后缀)
    /// </summary>
    /// <param name="input">待校验的仓库名(可能是 owner/repo 或 owner/repo.git)</param>
    /// <param name="validated">校验通过时输出规范化仓库名,失败时输出 null</param>
    /// <param name="error">校验失败时输出 Rust 风格四要素报错(为什么拒绝 + 触发参数 + 守卫层名 + 正确写法),通过时输出 null</param>
    /// <returns>校验通过返回 true,失败返回 false</returns>
    public static bool TryValidate(string input, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? validated, out string? error) {
        if (string.IsNullOrWhiteSpace(input)) {
            validated = null;
            error = BuildError(input, "仓库名为空或空白");
            return false;
        }

        var normalized = input;
        const string dotGitSuffix = ".git";
        if (normalized.EndsWith(dotGitSuffix, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[..^dotGitSuffix.Length];
        }

        if (!RepoNamePattern.IsMatch(normalized)) {
            validated = null;
            error = BuildError(input, "仓库名格式非法(只允许 owner/repo,每段仅含字母数字点下划线短横线,单斜杠分隔)");
            return false;
        }

        validated = normalized;
        error = null;
        return true;
    }

    /// <summary>
    /// 构建 Rust 风格四要素报错 — 为什么拒绝 + 触发参数 + 守卫层名 + 正确写法
    /// </summary>
    private static string BuildError(string input, string reason) =>
        $"拒绝仓库名: {reason}\n" +
        $"  触发参数: \"{input}\"\n" +
        $"  拦截守卫: GitHubRepoNameValidator(B3 命令注入防御)\n" +
        $"  正确写法: owner/repo(如 JoinCode/JoinCode),仅含字母数字 . _ - 与单 /";
}
