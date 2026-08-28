namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Claude 兼容性常量 — 集中管理所有保留的 CLAUDE 字样引用
/// <para>这些字符串是向后兼容契约：CLAUDE.md 配置文件名、claude-code-hint 插件协议标签、
/// claude_code_args JSON 字段、CLAUDE_PLUGIN_ROOT 插件环境变量等。</para>
/// <para>禁止在代码中直接硬编码这些字符串，统一引用此常量类。</para>
/// </summary>
public static class ClaudeCompatConstants
{
    #region 配置文件名（向后兼容读取 CLAUDE.md）

    /// <summary>项目规则文件名 — 兼容读取 CLAUDE.md</summary>
    public const string ProjectRulesFileName = "CLAUDE.md";

    /// <summary>项目规则文件名（小写变体）</summary>
    public const string ProjectRulesFileNameLower = "claude.md";

    /// <summary>项目规则本地覆盖文件名</summary>
    public const string ProjectRulesLocalFileName = "CLAUDE.local.md";

    /// <summary>项目规则本地覆盖文件名（小写变体）</summary>
    public const string ProjectRulesLocalFileNameLower = "claude.local.md";

    /// <summary>Claude 配置目录名（兼容读取 .claude/）</summary>
    public const string ConfigDirectory = ".claude";

    #endregion

    #region: 上下文过滤键

    /// <summary>UserContext 中 CLAUDE.md 内容的过滤键</summary>
    public const string ContextKeyProjectRules = "claudeMd";

    #endregion

    #region JSON 契约字段

    /// <summary>Bridge WorkSecret 中的 CLI 参数字段名 — 与服务端的跨进程契约</summary>
    public const string JsonClaudeCodeArgs = "claude_code_args";

    #endregion

    #region XML 协议标签

    /// <summary>Shell 插件提示标签 — 插件协议契约</summary>
    public const string XmlClaudeCodeHint = "claude-code-hint";

    #endregion

    #region 枚举值（向后兼容）

    /// <summary>ExecutorVariant.JoinCodeGuide 的枚举字符串值 — 向后兼容</summary>
    public const string EnumClaudeCodeGuide = "claudeCodeGuide";

    /// <summary>DisplayId 中 claudeCodeGuide agent 的标识</summary>
    public const string AgentClaudeCodeGuide = "claudeCodeGuideAgent";

    #endregion

    #region GitHub 集成

    /// <summary>GitHub Actions OAuth Token 密钥名</summary>
    public const string GitHubSecretOAuthToken = "CLAUDE_CODE_OAUTH_TOKEN";

    /// <summary>GitHub Actions base action 引用</summary>
    public const string GitHubActionBaseAction = "anthropics/claude-code-base-action@v1";

    /// <summary>GitHub App 安装 URL</summary>
    public const string GitHubAppUrl = "https://github.com/apps/claude";

    /// <summary>GitHub Actions 文档 URL</summary>
    public const string GitHubDocsUrl = "https://docs.anthropic.com/en/docs/claude-code/github-actions";

    #endregion

    #region 插件协议环境变量

    /// <summary>插件根目录环境变量名</summary>
    public const string EnvPluginRoot = "CLAUDE_PLUGIN_ROOT";

    /// <summary>插件数据目录环境变量名</summary>
    public const string EnvPluginData = "CLAUDE_PLUGIN_DATA";

    #endregion

    #region 模板变量

    /// <summary>技能目录模板变量</summary>
    public const string TemplateSkillDir = "${CLAUDE_SKILL_DIR}";

    /// <summary>会话 ID 模板变量</summary>
    public const string TemplateSessionId = "${CLAUDE_SESSION_ID}";

    #endregion

    #region Chrome 自动化技能

    /// <summary>Chrome 自动化技能名</summary>
    public const string SkillClaudeInChrome = "claude-in-chrome";

    /// <summary>Chrome 自动化 MCP 工具前缀</summary>
    public const string McpToolPrefixClaudeInChrome = "mcp__claude-in-chrome__";

    #endregion

    #region 外部文件路径

    /// <summary>Windows 下 managed-settings.json 路径（外部程序路径）</summary>
    public const string ManagedSettingsPath = @"C:\Program Files\ClaudeCode\managed-settings.json";

    #endregion
}
