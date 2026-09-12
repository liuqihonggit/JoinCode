namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 对外暴露地址常量 — 集中管理所有基础设施 URL/Endpoint 的默认值
/// 业务地址（供应商端点）走 settings.json 配置驱动，基础设施地址走此常量类 + 环境变量覆盖（见 <see cref="JccEndpointsResolver"/>）
/// 禁止在代码中硬编码基础设施 URL 字符串，统一引用此常量或 <see cref="JccEndpointsResolver"/>
/// > ADR: 0063
/// </summary>
public static class JccEndpoints
{
    // ── GitHub API（更新/Release Notes）──

    /// <summary>
    /// GitHub API 基址（可通过 JCC_GITHUB_API_BASE 环境变量覆盖，用于企业版/代理）
    /// </summary>
    public const string GitHubApiBase = "https://api.github.com";

    /// <summary>
    /// GitLab API 基址（可通过 JCC_GITLAB_API_BASE 环境变量覆盖，用于自建 GitLab/代理）
    /// </summary>
    public const string GitLabApiBase = "https://gitlab.com/api/v4";

    /// <summary>
    /// Gitea API 基址（可通过 JCC_GITEA_API_BASE 环境变量覆盖，用于自建 Gitea/代理）
    /// </summary>
    public const string GiteaApiBase = "https://gitea.com/api/v1";

    /// <summary>
    /// 默认仓库 owner（可通过 JCC_REPO_OWNER 环境变量覆盖）
    /// </summary>
    public const string DefaultRepoOwner = "jcc";

    /// <summary>
    /// 默认仓库名（可通过 JCC_REPO_NAME 环境变量覆盖）
    /// </summary>
    public const string DefaultRepoName = "JoinCode";

    // ── MCP 官方注册表 ──

    /// <summary>
    /// MCP 官方注册表地址（可通过 JCC_MCP_REGISTRY_URL 环境变量覆盖）
    /// </summary>
    public const string McpOfficialRegistry = "https://registry.modelcontextprotocol.io";

    // ── Azure OAuth ──

    /// <summary>
    /// Azure OAuth 授权端点基址（不含 /authorize 后缀）
    /// </summary>
    public const string AzureOAuthBase = "https://login.microsoftonline.com/common/oauth2/v2.0";

    /// <summary>
    /// Azure OAuth 默认 scope
    /// </summary>
    public const string AzureOAuthScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>
    /// Azure OAuth 本地回调地址
    /// </summary>
    public const string AzureOAuthRedirectUri = "http://localhost:5000/oauth/callback";

    // ── Bridge ──

    /// <summary>
    /// Bridge 本地默认地址（可通过 JCC_API_BASE_URL 环境变量覆盖）
    /// </summary>
    public const string DefaultBridgeLocal = "http://localhost:3456";

    /// <summary>
    /// Bridge 远程默认地址
    /// </summary>
    public const string DefaultBridgeRemote = "https://claude.ai";

    // ── 更新服务器（见 ADR 0064）──

    /// <summary>
    /// 默认更新清单地址（可通过 JCC_UPDATE_MANIFEST_URL 环境变量覆盖）
    /// </summary>
    public const string DefaultUpdateManifestUrl = "https://update.jcc.dev/manifest.json";

    /// <summary>
    /// 默认更新通道
    /// </summary>
    public const string DefaultUpdateChannel = "stable";

    // ── 其他基础设施 URL ──

    /// <summary>
    /// Chrome 集成引导页地址
    /// </summary>
    public const string ChromeIntegrationUrl = "https://jcc.dev/chrome";

    /// <summary>
    /// 域名黑名单检查 API 基址（不含 ?domain= 查询参数）
    /// </summary>
    public const string DomainBlocklistApiBase = "https://api.anthropic.com/api/web/domain_info";

    /// <summary>
    /// GitHub App 安装地址（Claude 兼容）
    /// </summary>
    public const string GitHubAppInstallUrl = "https://github.com/apps/claude";

    // ── 开发工具下载提示 URL（非 API 调用，仅用户提示）──

    /// <summary>
    /// PowerShell 下载页（缺失时提示用户）
    /// </summary>
    public const string PowerShellDownloadUrl = "https://github.com/PowerShell/PowerShell";

    /// <summary>
    /// Python 下载页（缺失时提示用户）
    /// </summary>
    public const string PythonDownloadUrl = "https://www.python.org/downloads/";
}
