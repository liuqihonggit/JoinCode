namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 搜索范围验证器 — 检测 CLI 搜索命令是否存在范围过大风险
/// 如 rg --no-ignore、搜索 C:\ 或 / 等系统目录、find / 等
/// </summary>
public interface ISearchScopeValidator
{
    /// <summary>
    /// 验证命令的搜索范围是否安全
    /// </summary>
    /// <param name="command">解析后的 Shell 命令</param>
    /// <param name="workingDirectory">当前工作目录</param>
    /// <returns>验证结果：安全返回 null，不安全返回风险详情</returns>
    SearchScopeValidationResult? Validate(ShellCommand command, string workingDirectory);
}

/// <summary>
/// 搜索范围热重载接口 — settings.json 变更时更新危险标志和过大路径配置
/// </summary>
public interface ISearchScopeReloadable
{
    /// <summary>
    /// 热重载搜索范围配置 — 双变量切换模式
    /// </summary>
    void ReloadSearchScope(SearchScopeConfig config);
}

/// <summary>
/// 搜索范围运行时配置 — 从 SearchScopeSettings 转换而来
/// </summary>
public sealed class SearchScopeConfig
{
    public bool Enabled { get; init; } = true;
    public Dictionary<string, FrozenSet<string>> ExtraDangerousFlags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public FrozenSet<string> ExtraExcessivePathPrefixes { get; init; } = FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 搜索范围验证结果
/// </summary>
public sealed record SearchScopeValidationResult(
    CommandRisk Risk,
    string Details,
    string? Suggestion = null);
