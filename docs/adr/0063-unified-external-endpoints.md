# 0063. 统一对外暴露地址

- 状态：proposed
- 日期：2026-09-05
- 决策者：AI + 用户确认

## 背景

项目对外暴露的 URL/Endpoint 散落在 10+ 文件中硬编码，无统一管理：

| 类别 | 硬编码位置 | 问题 |
|------|-----------|------|
| 供应商端点 | `SettingsLoader.cs` 骨架 + 3 个 `ProviderDefinition.DefaultBaseUrl` | 同一端点两处定义，易不一致 |
| GitHub API | `UpgradeService.cs` + `ReleaseNotesService.cs` | owner/name 默认 `"jcc"/"JoinCode"` 重复两处 |
| Bridge 地址 | `BridgeApiClient.cs` + `BridgeMain.Helpers.cs` | `http://localhost:3456` 和 `https://claude.ai` 分散 |
| Azure OAuth | `AzureProviderDefinition.cs` | 端点写死，无法配置 |
| MCP 注册表 | `McpOfficialRegistry.cs` | `const string`，无法覆盖 |
| 域名黑名单 API | `DomainBlocklistChecker.cs` | URL 写死 |
| Chrome 集成 | `ChromeIntegrationService.cs` | URL 写死 |

部分地址可通过环境变量覆盖（`JCC_ENDPOINT`、`JCC_API_BASE_URL`），但基础设施地址（GitHub API、MCP 注册表、OAuth）完全游离在配置体系之外。

## 决策

采用**混合方案**：业务地址走 settings.json 配置驱动（已实现，保持不变），基础设施地址集中到 `JccEndpoints` 静态常量类 + 可被环境变量覆盖。

### 1. 新增 `JccEndpoints` 静态常量类

**位置**：`foundation/Abstractions/00-core/Core/Utils/Constants/JccEndpoints.cs`

集中所有基础设施地址的默认值，按用途分组：

```csharp
public static class JccEndpoints
{
    // GitHub API（更新/Release Notes）
    public const string GitHubApiBase = "https://api.github.com";
    public const string DefaultRepoOwner = "jcc";
    public const string DefaultRepoName = "JoinCode";

    // MCP 官方注册表
    public const string McpOfficialRegistry = "https://registry.modelcontextprotocol.io";

    // Azure OAuth
    public const string AzureOAuthAuthorizeBase = "https://login.microsoftonline.com/common/oauth2/v2.0";
    public const string AzureOAuthScope = "https://cognitiveservices.azure.com/.default";

    // Bridge
    public const string DefaultBridgeLocal = "http://localhost:3456";
    public const string DefaultBridgeRemote = "https://claude.ai";

    // 更新服务器（见 ADR 0064）
    public const string DefaultUpdateManifestUrl = "https://update.jcc.dev/manifest.json";

    // 其他
    public const string ChromeIntegrationUrl = "https://jcc.dev/chrome";
    public const string DomainBlocklistApiBase = "https://api.anthropic.com/api/web/domain_info";
}
```

### 2. 新增环境变量到 `JccEnvVar` 枚举

通过 `[EnumValue]` + 源码生成器管理，覆盖 `JccEndpoints` 默认值：

| 环境变量 | 覆盖目标 | 说明 |
|----------|---------|------|
| `JCC_GITHUB_API_BASE` | `JccEndpoints.GitHubApiBase` | GitHub API 基址（企业版/代理） |
| `JCC_REPO_OWNER` | `JccEndpoints.DefaultRepoOwner` | 仓库 owner |
| `JCC_REPO_NAME` | `JccEndpoints.DefaultRepoName` | 仓库名 |
| `JCC_MCP_REGISTRY_URL` | `JccEndpoints.McpOfficialRegistry` | MCP 注册表地址 |
| `JCC_UPDATE_MANIFEST_URL` | `JccEndpoints.DefaultUpdateManifestUrl` | 更新清单地址 |
| `JCC_UPDATE_SOURCE_TYPE` | 更新源类型 | `static`/`api`/`github-mirror`/`local`（见 ADR 0064） |

### 3. 新增 `JccEndpointsResolver` 解析器

**位置**：`foundation/Abstractions/00-core/Core/Utils/Constants/JccEndpointsResolver.cs`

提供 `ResolveXxx()` 方法：先查环境变量，回退到 `JccEndpoints` 常量。AOT 友好（无反射）。

```csharp
public static class JccEndpointsResolver
{
    public static string GitHubApiBase =>
        Environment.GetEnvironmentVariable(JccEnvVarExtensions.ToValue(JccEnvVar.JccGithubApiBase))
        ?? JccEndpoints.GitHubApiBase;
    // ... 其他 Resolve 方法
}
```

### 4. 改造消费方

| 文件 | 改造内容 |
|------|---------|
| `UpgradeService.cs` | `https://api.github.com` → `JccEndpointsResolver.GitHubApiBase`；owner/name 从环境变量读 |
| `ReleaseNotesService.cs` | 同上 |
| `McpOfficialRegistry.cs` | `DefaultRegistryUrl` → `JccEndpointsResolver.McpOfficialRegistry` |
| `AzureProviderDefinition.cs` | OAuth 端点 → `JccEndpoints.AzureOAuth*` |
| `BridgeApiClient.cs` | `http://localhost:3456` → `JccEndpoints.DefaultBridgeLocal` |
| `BridgeMain.Helpers.cs` | `https://claude.ai` → `JccEndpoints.DefaultBridgeRemote` |
| `DomainBlocklistChecker.cs` | URL → `JccEndpoints.DomainBlocklistApiBase` |
| `ChromeIntegrationService.cs` | URL → `JccEndpoints.ChromeIntegrationUrl` |

## 替代方案

- **全部配置驱动**：所有地址放 settings.json。基础设施地址在配置加载前就可能被需要（鸡生蛋问题），且增加配置复杂度。未采用。
- **全部代码常量不可覆盖**：最简单但不灵活，企业版/内网/代理场景无法适配。未采用。
- **复用现有 `WorkflowConstants`**：该类偏 workflow 语义，地址常量语义独立，单独建类更清晰。未采用。

## 后果

- 正面：基础设施地址单一数据源（`JccEndpoints`），环境变量统一覆盖入口（`JccEndpointsResolver`），企业版/内网/代理场景可通过环境变量适配，零配置文件改动。
- 负面：新增一个常量类 + 解析器，消费方需改为调用 `JccEndpointsResolver` 而非直接用字符串。改动面约 8 个文件。
- 中性：业务地址（供应商端点）仍走 settings.json，两套机制并存（配置驱动 vs 常量+环境变量），但语义清晰分离。

## 反向引用

- AGENTS.md「六项架构规则」规则5（传接口不传属性）— `JccEndpointsResolver` 作为统一入口，消费方传 resolver 而非拆开的字符串
- AGENTS.md「枚举 + [EnumValue] 使用规范」— 新增环境变量走 `JccEnvVar` 枚举 + 源码生成器
