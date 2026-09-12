namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 地址解析器 — 优先读环境变量，回退到 <see cref="JccEndpoints"/> 常量默认值
/// 所有基础设施地址的消费方应通过此解析器获取地址，而非直接引用 <see cref="JccEndpoints"/>
/// AOT 友好（无反射），环境变量名通过 <see cref="JccEnvVar"/> 枚举 + 源码生成器管理
/// > ADR: 0063
/// </summary>
public static class JccEndpointsResolver
{
    /// <summary>
    /// GitHub API 基址（JCC_GITHUB_API_BASE 覆盖）
    /// </summary>
    public static string GitHubApiBase =>
        Environment.GetEnvironmentVariable(JccEnvVar.GithubApiBase.ToValue())
        ?? JccEndpoints.GitHubApiBase;

    /// <summary>
    /// GitLab API 基址（JCC_GITLAB_API_BASE 覆盖）
    /// </summary>
    public static string GitLabApiBase =>
        Environment.GetEnvironmentVariable(JccEnvVar.GitlabApiBase.ToValue())
        ?? JccEndpoints.GitLabApiBase;

    /// <summary>
    /// Gitea API 基址（JCC_GITEA_API_BASE 覆盖）
    /// </summary>
    public static string GiteaApiBase =>
        Environment.GetEnvironmentVariable(JccEnvVar.GiteaApiBase.ToValue())
        ?? JccEndpoints.GiteaApiBase;

    /// <summary>
    /// 仓库 owner（JCC_REPO_OWNER 覆盖）
    /// </summary>
    public static string RepoOwner =>
        Environment.GetEnvironmentVariable(JccEnvVar.RepoOwner.ToValue())
        ?? JccEndpoints.DefaultRepoOwner;

    /// <summary>
    /// 仓库名（JCC_REPO_NAME 覆盖）
    /// </summary>
    public static string RepoName =>
        Environment.GetEnvironmentVariable(JccEnvVar.RepoName.ToValue())
        ?? JccEndpoints.DefaultRepoName;

    /// <summary>
    /// MCP 官方注册表地址（JCC_MCP_REGISTRY_URL 覆盖）
    /// </summary>
    public static string McpOfficialRegistry =>
        Environment.GetEnvironmentVariable(JccEnvVar.McpRegistryUrl.ToValue())
        ?? JccEndpoints.McpOfficialRegistry;

    /// <summary>
    /// 更新清单地址（JCC_UPDATE_MANIFEST_URL 覆盖）
    /// </summary>
    public static string UpdateManifestUrl =>
        Environment.GetEnvironmentVariable(JccEnvVar.UpdateManifestUrl.ToValue())
        ?? JccEndpoints.DefaultUpdateManifestUrl;

    /// <summary>
    /// 更新通道（JCC_UPDATE_CHANNEL 覆盖）
    /// </summary>
    public static string UpdateChannel =>
        Environment.GetEnvironmentVariable(JccEnvVar.UpdateChannel.ToValue())
        ?? JccEndpoints.DefaultUpdateChannel;

    /// <summary>
    /// Azure OAuth 授权端点完整 URL
    /// </summary>
    public static string AzureOAuthAuthorizeUrl =>
        JccEndpoints.AzureOAuthBase + "/authorize";

    /// <summary>
    /// Azure OAuth 令牌端点完整 URL
    /// </summary>
    public static string AzureOAuthTokenUrl =>
        JccEndpoints.AzureOAuthBase + "/token";
}
