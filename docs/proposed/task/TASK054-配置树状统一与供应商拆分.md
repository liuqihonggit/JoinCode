# TASK054 — 配置树状统一与供应商拆分

> 触发：PR #418（Web 搜索多供应商）合并后，补充 settings.json 的 API KEY 配置；用户要求全部配置统一成树状，每个供应商拆文件夹，models.json 从 settings.json 内联拆出就近放置。
> 形态：**主配置在子配置文件夹外层，像一颗树一样生长** — 物理树状拆分简化理解，逻辑加载合并保持消费方无感知。

## 1. 现状调查

### 1.1 运行时用户配置文件（实际被代码读取）

| 文件 | 位置 | 用途 | 加载器 |
|------|------|------|--------|
| `settings.json` | `~/.jcc/settings.json`（用户级）+ `.jcc/settings.json`（项目级，覆盖） | 主配置：vendor/current/autoFetchModels/update | `ConfigLoader`/`SettingsLoader` |
| `global.json` | `~/.jcc/global.json` | 全局配置（跨项目） | `ConfigurationService` |

> 路径可通过 `JCC_SETTINGS_FILE_NAME` / `JCC_GLOBAL_CONFIG_FILE_NAME` 环境变量覆盖（`AppDataPaths.cs`）。

### 1.2 settings.json 顶层节点（`SettingsJson.cs`）

| 节点 | 类型 | 用途 |
|------|------|------|
| `vendor` | `Dictionary<string, ProfileSettings>` | LLM 供应商预设（7 个：sensenova/openai/anthropic/deepseek/deepseek-anthropic/agnes/zhipu），每个含 protocol/endpoint/apiKeyEnvVar/**models（内联）**/modelsEndpoint |
| `current` | `CurrentSettings` | 当前激活配置（profile 指针 + **~45 字段平铺**） |
| `autoFetchModels` | `bool` | 启动时自动拉取远程模型列表 |
| `update` | `UpdateSourceConfig` | 自动更新配置 |

### 1.3 非用户配置的 json（不纳入统一，仅记录用途）

| 文件 | 用途 | 处理 |
|------|------|------|
| `global.json`（项目根） | .NET SDK 版本 | 不动 |
| `nuget.config` | NuGet 源 | 不动 |
| `Directory.Build.props/targets` | MSBuild 共享属性 | 不动 |
| `modules/apps/gui/theming/Appearance.json` | GUI 主题（**内嵌资源** `GetManifestResourceStream`） | 不动 |
| `build/smoke-test.json` | 冒烟测试用例 | 不动 |
| `stryker-config.json` × 16 | 变异测试配置（每测试项目一个） | 不动 |
| `xunit.runner.json` × 3 | xUnit 运行器配置 | 不动 |
| `mockserver*.json` × 12 | Mock 服务器测试夹具 | 不动 |
| `ctor_scan_report.json` / `doc_migration_plan.json` | 工具产物 | 不动 |

### 1.4 search providers 现状（PR #418 刚合并）

| 供应商 | API KEY 来源 | 优先级 | 代码位置 |
|--------|-------------|--------|----------|
| Tavily | 环境变量 `TAVILY_API_KEY`（硬编码） | 100 | `providers_search/providers/TavilySearchProvider.cs` |
| Serper | 环境变量 `SERPER_API_KEY`（硬编码） | 200 | `.../SerperSearchProvider.cs` |
| Brave | 环境变量 `BRAVE_API_KEY`（硬编码） | 300 | `.../BraveSearchProvider.cs` |
| SearXNG | 环境变量 `SEARXNG_URL`（硬编码） | 400 | `.../SearXngSearchProvider.cs` |
| Anthropic | `ProviderConfig.Definition.SupportsWebSearch` | 500 | `.../AnthropicSearchProvider.cs` + `AnthropicWebSearchCore.cs` |
| SubAgent | 无（兜底，永真） | 999 | `.../SubAgentSearchProvider.cs` |

**问题**：6 个 provider 的 .cs 平铺在 `providers/` 同层；API KEY 硬编码环境变量名，未接入 settings.json 配置体系；无 models.json（模型数据内联在 settings.json vendor.models，由 `ModelConfigLoader` 从 `SettingsJson.Vendor` 流入）。

### 1.5 CurrentSettings 字段审计（`SettingsJson.cs:86-442`，~45 字段）

| 组 | 字段 | 数量 |
|----|------|------|
| profile 指针 | `profile` | 1 |
| UI/偏好 | `theme`/`editorMode`/`language`/`showTurnDuration`/`showThinkingSummaries`/`statusLine`/`outputStyle`/`windowShakeEnabled`/`chatRoomEnabled`/`debuglog` | 10 |
| 推理 | `effortLevel`/`fastMode`/`alwaysThinkingEnabled`/`autoCompactEnabled`/`availableModels`/`modelOverrides`/`modelHistory` | 7 |
| 行为/Git | `defaultShell`/`respectGitignore`/`cleanupPeriodDays`/`includeCoAuthoredBy`/`includeGitInstructions`/`autoMemoryEnabled`/`autoDreamEnabled` | 7 |
| 安全/权限 | `permissions`/`hooks`/`sandbox`/`disableAllHooks` | 4 |
| MCP/插件 | `mcpServers`/`enableAllProjectMcpServers`/`enabledMcpjsonServers`/`disabledMcpjsonServers`/`enabledPlugins` | 5 |
| 子代理 | `subAgentConcurrency`/`subAgentLiveness`/`actor` | 3 |
| 工具 | `toolScore`/`blacklistedTools`/`toolPenalties`/`customHyperedges`/`searchScope` | 5 |
| 运行模式 | `isUnattendedMode`/`isAntiCharLossConfirm`/`apiKeyHelper`/`env` | 4 |
| Worktree | `worktree`/`activeWorktreeSession` | 2 |

## 2. 问题

1. **CurrentSettings ~45 字段平铺** — 严重臃肿，难以理解哪个字段属于哪类职责
2. **配置散落、用途不明** — settings.json 内联 vendor.models，用户看不到每个供应商有哪些模型；散落 json 文件无索引
3. **search providers 未接入配置体系** — API KEY 硬编码环境变量名，无法通过 settings.json 配置/覆盖
4. **供应商代码平铺** — 6 个 provider .cs 同层，无法就近放置供应商专属配置/资源
5. **MCP/模型/工具字段散落** — `enableAllProjectMcpServers`/`enabledMcpjsonServers`/`disabledMcpjsonServers` 三开关散落；`availableModels`/`modelOverrides`/`modelHistory` 三模型字段散落；`toolScore`/`blacklistedTools`/`toolPenalties`/`customHyperedges` 四工具字段散落
6. **无统一配置树** — LLM vendor 和 search provider 配置组织方式不一致

## 3. 方案：配置树状生长

### 3.1 设计原则

1. **主配置在子配置文件夹外层** — `settings.json` 是树根，只放指针 + 全局开关；子配置在子文件夹内
2. **像树一样生长** — 每个分支（current/providers_llm/search_providers）是子文件夹，每个叶是单一职责的 json 文件
3. **物理拆分 + 逻辑合并** — 物理上拆成多个小文件（简化理解），加载器合并成现有 DTO（消费方无感知，不破坏 `ConfigKey.Theme` 等平铺访问）
4. **约定大于配置** — 路径按约定（`current/ui.json`、`providers_llm/openai/config.json`），settings.json 不必显式列指针
5. **apiKeyEnvVar 模式** — 存环境变量名不存明文 key（安全）

### 3.2 完整配置树形态（`~/.jcc/`）

```
~/.jcc/                              ← 根（主配置在外层）
  settings.json                      ← 主入口：极简，只有 profile 指针 + 全局开关
  current/                           ← 运行时偏好分支（CurrentSettings 拆成 10 个子文件）
    ui.json                          ← UI/偏好 10 字段：theme/editorMode/language/...
    inference.json                   ← 推理 7 字段：effortLevel/fastMode/availableModels/...
    behavior.json                    ← 行为/Git 7 字段：defaultShell/respectGitignore/...
    security.json                    ← 安全/权限 4 字段：permissions/hooks/sandbox/disableAllHooks
    mcp.json                         ← MCP 5 字段：mcpServers/enableAllProjectMcpServers/...（归并三开关）
    plugins.json                     ← 插件：enabledPlugins
    subagent.json                    ← 子代理 3 字段：subAgentConcurrency/subAgentLiveness/actor
    tools.json                       ← 工具 5 字段：toolScore/blacklistedTools/toolPenalties/customHyperedges/searchScope（归并四散落）
    worktree.json                    ← Worktree 2 字段：worktree/activeWorktreeSession
    runtime.json                     ← 运行模式 4 字段：isUnattendedMode/isAntiCharLossConfirm/apiKeyHelper/env
  providers_llm/                           ← LLM 供应商分支（7 个）
    sensenova/
      config.json                    ← protocol/endpoint/apiKeyEnvVar/model/modelsEndpoint
      models.json                    ← [{ id, displayName, contextWindow, aliases, capabilities }]
    openai/
      config.json
      models.json
    anthropic/
      config.json
      models.json
    deepseek/
      config.json
      models.json
    deepseek-anthropic/
      config.json
      models.json
    agnes/
      config.json
      models.json
    zhipu/
      config.json
      models.json
  providers_search/                  ← 搜索供应商分支（6 个）
    tavily/
      config.json                    ← apiKeyEnvVar/priority/enabled
      models.json                    ← []（占位；search provider 不消费模型，anthropic 例外）
    serper/
      config.json
      models.json
    brave/
      config.json
      models.json
    searxng/
      config.json                    ← endpointEnvVar/priority/enabled
      models.json
    anthropic/
      config.json                    ← priority/enabled（复用 LLM anthropic vendor 的 key）
      models.json                    ← [{ id: "claude-sonnet-4-6", ... }]（fast model）
    subagent/
      config.json                    ← priority/enabled
      models.json                    ← []
```

### 3.3 settings.json 主入口（极简）

```json
{
  "current": { "profile": "sensenova" },
  "autoFetchModels": true,
  "update": { ... }
}
```

> `vendor` 和 `searchProviders` 节点**不再内联**在 settings.json — 按约定路径从 `providers_llm/` 和 `providers_search/` 子文件夹加载。settings.json 只保留 `current.profile` 指针 + 全局开关。

### 3.4 CurrentSettings 物理拆分 + 逻辑合并

**物理**：`current/` 下 10 个子文件，每个单一职责（ui.json/inference.json/...）。

**逻辑合并**：`ConfigLoader` 加载 `current/*.json` 后合并成现有 `CurrentSettings` DTO，消费方无感知：
- `ConfigKey.Theme` 仍读 `current.theme` — 加载器把 `current/ui.json` 的 `theme` 合并到 `CurrentSettings.Theme`
- `[SettingsMerge]` 源码生成器不变 — 它操作的是合并后的 DTO，不关心物理来源
- `SettingsMapper`/`SettingsChangeApplier` 不变 — 它们读写 DTO，加载器负责物理↔DTO 映射

**归并**（散落字段合并到子文件）：
- `mcp.json` 收纳 `mcpServers` + `enableAllProjectMcpServers` + `enabledMcpjsonServers` + `disabledMcpjsonServers` + `enabledPlugins`（原 5 字段散落）
- `tools.json` 收纳 `toolScore` + `blacklistedTools` + `toolPenalties` + `customHyperedges` + `searchScope`（原 5 字段散落）
- `inference.json` 收纳 `availableModels` + `modelOverrides` + `modelHistory`（原 3 字段散落）

### 3.5 供应商配置树

**LLM vendor**（`providers_llm/{name}/`）：
- `config.json` — `protocol`/`endpoint`/`apiKeyEnvVar`/`model`/`modelsEndpoint`（原 `ProfileSettings` 除 `models` 外的字段）
- `models.json` — 模型列表（原 `ProfileSettings.Models` 内联，拆出独立文件）

**search provider**（`providers_search/{name}/`）：
- `config.json` — `apiKeyEnvVar`/`priority`/`enabled`/`endpointEnvVar?`（SearXNG 用 endpointEnvVar）
- `models.json` — 大多数 `[]`；`anthropic/` 列 fast model

### 3.6 代码侧供应商文件夹拆分

```
modules/cognition/hands/web/providers_search/providers/
  tavily/
    TavilySearchProvider.cs
    models.json                    ← 默认/示例配置（随包发布，用户可覆盖）
  serper/
    SerperSearchProvider.cs
    models.json
  brave/
    BraveSearchProvider.cs
    models.json
  searxng/
    SearXngSearchProvider.cs
    models.json
  anthropic/
    AnthropicSearchProvider.cs
    AnthropicWebSearchCore.cs      ← Anthropic 共享逻辑就近放
    models.json
  subagent/
    SubAgentSearchProvider.cs
    models.json
```

> 代码文件夹内的 `models.json` 作为**默认配置模板**随包发布；运行时用户配置（`~/.jcc/providers_search/{name}/models.json`）覆盖默认。`Hands.csproj` 是 SDK-style 自动 glob，移动 .cs 不需改 csproj。

### 3.7 加载策略（逻辑合并，消费方无感知）

```
ConfigLoader.LoadAsync()
  ├─ 读 settings.json → { current.profile, autoFetchModels, update }
  ├─ 读 current/*.json（10 个）→ 合并成 CurrentSettings DTO
  ├─ 读 providers_llm/*/config.json + models.json → 合并成 Dictionary<string, ProfileSettings>
  └─ 读 providers_search/*/config.json + models.json → 合并成 SearchProviderConfig
```

- 物理文件不存在时回退默认值（约定大于配置，渐进式迁移）
- `SettingsChangeApplier` 监听子文件变更，触发对应 DTO 重建 + 热重载
- `[SettingsMerge]` 生成器操作合并后 DTO，不感知物理拆分

### 3.8 API KEY 接入方式

- `config.json` 存 `apiKeyEnvVar`（环境变量名，不存明文 key，安全）
- search provider 构造注入 `ISearchProviderConfig`，从配置读 `apiKeyEnvVar`，再 `Environment.GetEnvironmentVariable(name)`
- 配置缺失时回退硬编码默认环境变量名（约定大于配置，测试不破坏）

### 3.9 统一 config 入口（`IConfigRoot` + IMemoryCache 懒加载）

**目标**：全局统一一个 config 入口，通过字段路径获取，缓存未命中自动从物理文件提取，内存压力时自动释放（复用 `Microsoft.Extensions.Caching.Memory`）。

**接口**（放 `abstractions/abs_core/configuration/`）：

```csharp
/// <summary>统一配置入口 — 按字段路径获取，IMemoryCache 懒加载，内存压力自动释放</summary>
public interface IConfigRoot {
    /// <summary>按路径获取配置值 — "current.ui.theme" → "dark"。未命中自动从物理文件提取并缓存</summary>
    T? Get<T>(string path);
    /// <summary>按路径获取，未命中返回 defaultValue</summary>
    T Get<T>(string path, T defaultValue);
    /// <summary>获取子节 — "current.ui" → IConfigSection（可继续 Get）</summary>
    IConfigSection GetSection(string path);
    /// <summary>使指定路径缓存失效（配置文件变更时调用）</summary>
    void Invalidate(string path);
}
```

**实现**（`ConfigRoot` 注入 `IMemoryCache` + `IConfigFileLoader`）：

```
Get<T>("current.ui.theme"):
  1. 查 IMemoryCache["cfg:current.ui.theme"] → 命中返回
  2. 未命中 → IConfigFileLoader.LoadAsync("current/ui.json") → JsonDocument
     → 导航到 "theme" 字段 → 反序列化成 T
  3. IMemoryCache.Set("cfg:current.ui.theme", value, options):
     - SetSize(1)
     - SetSlidingExpiration(TimeSpan.FromMinutes(30))   // 30 分钟无访问则过期
     - SetPriority(CacheItemPriority.Low)                // 内存压力时优先释放
  4. 返回
```

**内存压力自动释放**（复用现有 `MemoryCacheService` 模式）：

```csharp
_cache = new MemoryCache(new MemoryCacheOptions {
    SizeLimit = 4096,                    // 最多缓存 4096 个配置字段
    CompactionPercentage = 0.25,         // 压缩时移除 25%
    ExpirationScanFrequency = TimeSpan.FromMinutes(5),
    // CompactionOnMemoryPressure 默认 true — GC 内存压力时自动触发压缩
});
```

> `Microsoft.Extensions.Caching.Memory` 已 AOT 兼容（项目 TreatWarningsAsErrors + PublishAot 验证），`MemoryCacheService`/`ToolCacheManager`/`WebFetchCache` 已在用。

**路径约定**（点分路径 → 物理文件 + 字段）：

| 路径 | 物理文件 | 字段 |
|------|----------|------|
| `current.ui.theme` | `current/ui.json` | `theme` |
| `current.inference.fastMode` | `current/inference.json` | `fastMode` |
| `vendors.openai.endpoint` | `providers_llm/openai/config.json` | `endpoint` |
| `vendors.openai.models[0].id` | `providers_llm/openai/models.json` | `[0].id` |
| `searchProviders.tavily.apiKeyEnvVar` | `providers_search/tavily/config.json` | `apiKeyEnvVar` |

**与现有 `SettingsJson` DTO 的关系**：

- `IConfigRoot` 是新的**统一入口**（路径访问 + IMemoryCache 懒加载）
- `SettingsJson` DTO **保留**（强类型，`ConfigLoader` 合并后供 `[SettingsMerge]` 生成器 / `SettingsMapper` / `SettingsChangeApplier` 用）
- **桥接**：`ConfigRoot` 底层既可从物理文件懒加载（按需），也可从已合并的 `SettingsJson` DTO 取值（启动时全量加载场景）
- **渐进式**：消费方可选 `settings.Current.Theme`（强类型）或 `config.Get<string>("current.ui.theme")`（缓存路径），不强制迁移

**两级缓存**（避免重复 parse 同一文件）：

- L1 字段值缓存：`"cfg:current.ui.theme"` → `"dark"`（细粒度，命中零开销）
- L2 文件解析缓存：`"file:current/ui.json"` → `JsonDocument`（避免每次字段提取都 parse 整个文件）
- L1 未命中 → 查 L2 → L2 未命中 → 读文件 parse → 存 L2 → 提取字段 → 存 L1

### 3.10 线程安全：不可变快照 + 直接 `=`

**要求**：`Get<T>(path)` 返回**不可变快照**，调用方直接 `=` 赋值即线程安全，无需锁。

**实现**：
1. **DTO 不可变** — 所有配置 DTO 用 `record` 或 `init`-only 属性（现有 `SettingsJson`/`ProfileSettings`/`CurrentSettings` 已是 `init`，天然不可变）
2. **缓存存不可变快照** — `IMemoryCache.Set(key, snapshot)` 存的是不可变对象引用
3. **`Get` 返回引用直接 `=`** — `var theme = config.Get<string>("current.ui.theme");` — `theme` 是不可变快照引用，多线程读安全
4. **配置变更原子替换** — 文件变更触发重建新快照 → `IMemoryCache.Set` 原子替换旧条目（`IMemoryCache` 内部线程安全）；已持有旧快照的线程不受影响（继续用旧快照），新 `Get` 拿到新快照

```
线程 A: var theme = config.Get<string>("current.ui.theme");  // 拿到 snapshot_v1 = "dark"
线程 B: // 文件变更 → ConfigRoot 重建 snapshot_v2 = "light" → IMemoryCache 原子替换
线程 A: // 继续用 theme="dark"（snapshot_v1 不可变，不受影响）✅
线程 C: var theme2 = config.Get<string>("current.ui.theme"); // 拿到 snapshot_v2 = "light"
```

> **禁止**：返回可变 live reference（如 `SettingsJson` 的 mutable 字段）— 配置热重载时其他线程可能看到半更新状态。所有 DTO 必须 `init`-only 或 `record`。

### 3.11 `config.user` / `config.pro` 两层 + 项目隔离

**两层模式**：

| 属性 | 位置 | 作用域 | 覆盖关系 |
|------|------|--------|----------|
| `config.User` | `~/.jcc/`（`AppDataConstants.Paths.JccDirectory`） | 用户级，跨项目共享 | 基础 |
| `config.Pro` | `{projectRoot}/.jcc/` | 项目级，**项目隔离** | 覆盖 User |
| `config.Local` | `{projectRoot}/.jcc/settings.local.json` | 本地个人（gitignore） | 覆盖 Pro |

**接口**：

```csharp
public interface IConfigRoot {
    IConfigLayer User { get; }      // 用户级（单例，~/.jcc/）
    IConfigLayer Pro { get; }       // 项目级（按当前项目隔离，{projectRoot}/.jcc/）
    IConfigLayer Local { get; }     // 本地（{projectRoot}/.jcc/settings.local.json，gitignore）

    /// <summary>合并获取：Local > Pro > User，第一个非 null 返回</summary>
    T? Get<T>(string path);
}

public interface IConfigLayer {
    T? Get<T>(string path);         // 仅本层
    bool Exists { get; }            // 该层配置是否存在
}
```

**项目隔离设计**（解决"pro 需要项目隔离"）：

1. **`IProjectLocator`** — 定位当前项目根：
   - 从 CWD 向上搜索 `.git/` 目录（git 根）或 `.jcc/` 目录
   - 找到 → 返回项目根路径；找不到 → `Pro`/`Local` 不存在（只有 `User`）

2. **`ProjectConfig` 按项目根路径隔离**：
   - `ProjectConfigRegistry`：`FrozenDictionary<string, ProjectConfig>`（key = 项目根绝对路径）
   - 不同项目目录 → 不同 `ProjectConfig` 实例 → 独立 `IMemoryCache` → 互不干扰
   - 切换项目（CWD 变更）→ `IProjectLocator` 重新定位 → 从 registry 取或新建对应 `ProjectConfig`

3. **合并获取**：
   ```
   Get<T>("current.ui.theme"):
     1. local = Local.Get<T>("current.ui.theme")    // {projectRoot}/.jcc/settings.local.json
     2. if local is not null return local
     3. pro = Pro.Get<T>("current.ui.theme")         // {projectRoot}/.jcc/current/ui.json
     4. if pro is not null return pro
     5. return User.Get<T>("current.ui.theme")       // ~/.jcc/current/ui.json
   ```

4. **项目隔离保证**：
   - 项目 A（`D:\projA\`）的 `Pro` 读 `D:\projA\.jcc\`
   - 项目 B（`D:\projB\`）的 `Pro` 读 `D:\projB\.jcc\`
   - 两个 `ProjectConfig` 实例独立缓存，A 的配置变更不触发 B 的缓存失效
   - `User` 层单例共享（`~/.jcc/`），所有项目共用

> 现有 `SettingsLoader.GetUserSettingsPath()` / `GetProjectSettingsPath(projectDir)` / `GetLocalSettingsPath(projectDir)` 已实现路径解析，本方案复用并明确成 `IConfigLayer` 三层 + `IProjectLocator` 项目定位。

## 4. 执行步骤（渐进式，每步编译+提交）

> **施工顺序原则**：先建统一访问层（IConfigRoot 基础设施），再接入 search providers，物理拆分（current/*.json、providers_llm/{name}/models.json）延后 — 当前 settings.json 内联配置能用，物理拆分是简化理解的优化，不阻塞功能。

| 步骤 | 内容 | 涉及文件 |
|------|------|----------|
| **P1** | **统一访问层（基础设施）**：`IConfigRoot` + `IConfigLayer`(User/Pro/Local) + `IProjectLocator` + `ConfigRoot` 实现（读**现有** settings.json，IMemoryCache 两级缓存 + 不可变快照 + 内存压力释放）+ DI 注册 | `abstractions/abs_core/configuration/` + `guard/configuration/` |
| **P2** | `SettingsJson.cs` 新增 `searchProviders` 节点 DTO + `SearchProviderProfileSettings` + Merge | `SettingsJson.cs` |
| **P3** | `SettingsLoader.cs` `BuildDefaultSettingsJson` 补充 `searchProviders` 骨架 | `SettingsLoader.cs` |
| **P4** | search providers 代码拆子文件夹 + `ISearchProviderConfig`（通过 `IConfigRoot` 读）+ 6 个 provider 构造改造 | `providers_search/` |
| **P5** | search providers 测试 + 编译 + 提交 | `hands.api.tests/web/` |
| **P6**（可延后） | LLM vendor models 物理拆分到 `providers_llm/{name}/config.json + models.json` + `ConfigFileLoader` 适配 | `SettingsJson.cs`/`ConfigLoader.cs` |
| **P7**（可延后） | CurrentSettings 物理拆分到 `current/*.json` 10 子文件 + `ConfigFileLoader` 适配 | `SettingsJson.cs`/`ConfigLoader.cs` |
| **P8** | `InitCommand`/`ResetConfigCommand` 生成默认配置树骨架 | `composition/slash/g-uard/config/` |

> **P1 先行**：`ConfigRoot` 底层先读现有单文件 `settings.json`（不依赖物理拆分），`IConfigFileLoader` 抽象隔离物理来源 — 后续 P6/P7 物理拆分时只需适配 `IConfigFileLoader`，`IConfigRoot` 和消费方不变。
>
> **P6/P7 可延后**：当前 `settings.json` 内联配置能用，物理拆分是"简化理解"的优化，不阻塞功能。先落地 P1-P5 让 search providers 接入统一入口，再按需做 P6-P8。
>
> 渐进式：每步编译通过后提交，任何时刻可中断恢复。

## 5. 验收标准

| 验收项 | 方法 |
|--------|------|
| settings.json 主入口极简（只有 profile 指针 + 全局开关） | 检查 `BuildDefaultSettingsJson` 输出 |
| `current/` 下 10 个子文件，各单一职责 | 目录结构 |
| `providers_llm/` 下 7 个供应商文件夹，各含 config.json + models.json | 目录结构 |
| `providers_search/` 下 7 个供应商文件夹，各含 config.json + models.json | 目录结构 |
| search provider 从 config.json 读 apiKeyEnvVar（配置驱动） | 单元测试 |
| 消费方无感知（`ConfigKey.Theme` 等平铺访问不变） | 现有测试全绿 |
| 物理拆分 + 逻辑合并（加载器合并子文件成 DTO） | `ConfigLoader` 单元测试 |
| `IConfigRoot` 统一入口：`Get<T>(path)` 按路径获取 + IMemoryCache 懒加载 | `ConfigRoot` 单元测试：命中/未命中/失效 |
| 内存压力自动释放（`CompactionOnMemoryPressure` + `CacheItemPriority.Low`） | 缓存压缩测试 |
| 编译通过（0 警告） | `dotnet build` |

## 6. 决策记录

- **modes.json = models.json**（用户确认笔误）— 模型列表配置
- **代码+配置都拆分**（用户确认）— 每供应商文件夹含 .cs + models.json
- **新增 searchProviders 节点**（用户确认）— 独立于 vendor，语义清晰（search ≠ LLM）
- **apiKeyEnvVar 模式**（不存明文 key）— 安全，复用现有 vendor 模式
- **树状生长形态**（用户确认）— 主配置外层 + 子配置文件夹，物理拆分 + 逻辑合并
- **CurrentSettings 拆 10 个子文件**（审计建议）— 解决 45 字段臃肿，按职责分组
- **散落字段归并**（审计建议）— MCP 三开关/模型三字段/工具四字段归并到子文件
- **S1-S7 全部统一**（用户确认）— search providers + LLM vendor + CurrentSettings 全纳入
- **统一 IConfigRoot 入口**（用户追加）— 全局一个 config 入口，按字段路径获取，IMemoryCache 懒加载 + 内存压力自动释放
- **两级缓存**（L1 字段值 + L2 文件解析）— 避免重复 parse，命中零开销
- **复用 Microsoft.Extensions.Caching.Memory**（用户指定）— 已 AOT 兼容，`MemoryCacheService` 模式复用
- **不可变快照 + 直接 =**（用户要求）— `Get<T>` 返回不可变 DTO 快照，调用方直接 `=` 线程安全，配置变更原子替换不影已持有快照
- **config.user / config.pro 两层 + 项目隔离**（用户要求）— `IConfigLayer` 三层(User/Pro/Local)，`IProjectLocator` 按 `.git/`/`.jcc/` 定位项目根，`ProjectConfig` 按项目根路径隔离独立缓存
- **统一供应商登记**（用户追加）— 一个父类 `IProviderDescriptor` 查询供应商列表/类型/是否支持 OpenAI 格式，动态检索（DI 收集），LLM + Search 全纳入

## 7. 统一供应商登记（用户追加）

### 7.1 需求

用户要求：
1. **供应商列表** — 能列出所有已注册供应商（LLM + Search）
2. **供应商类型** — LLM / Search 等分类标签
3. **供应商是否支持 OpenAI 格式** — 能力查询
4. **一个父类就可以查询** — 统一父接口 `IProviderDescriptor`
5. **动态检索出来** — 运行时从 DI 收集，不硬编码

### 7.2 现状（两套独立机制）

| 维度 | LLM 供应商 | Search 供应商 |
|------|-----------|--------------|
| 抽象 | `IProviderDefinition` | `ISearchProvider` |
| 身份 | `VendorKind` 枚举 + `string ProviderName` | `string Name`（无枚举） |
| 注册 | 运行时 settings.json vendor 节点构建 | 编译时 `[Register]` + 源码生成器 |
| 容器 | `IProviderDefinitionRegistry`（FrozenDictionary） | DI `IEnumerable<ISearchProvider>` |
| 聚合 | 入 `IRegistryHub.Providers` | 未入 RegistryHub |
| 调度 | 单选（按名 TryGet） | 多选（Priority 遍历） |

### 7.3 设计：统一供应商描述符 + 目录

#### 7.3.1 `ProviderCategory` 枚举（供应商类型）

```csharp
public enum ProviderCategory {
    [EnumValue("llm")]       Llm = 0,
    [EnumValue("search")]    Search = 1,
    [EnumValue("embedding")] Embedding = 2,
}
```

#### 7.3.2 `IProviderDescriptor` 父接口（统一查询入口）

```csharp
/// <summary>供应商描述符 — 统一父接口，一个类查询供应商所有属性</summary>
public interface IProviderDescriptor {
    string Name { get; }                    // "openai"、"tavily" 等
    ProviderCategory Category { get; }      // Llm / Search / Embedding
    bool SupportsOpenAiFormat { get; }      // 是否支持 OpenAI 格式
    bool IsAvailable { get; }               // 是否可用（API KEY 已配置等）
    string? DisplayName { get; }            // 显示名（可选）
}
```

#### 7.3.3 `IProviderCatalog` 目录（动态检索）

```csharp
/// <summary>供应商目录 — 动态检索所有已注册供应商</summary>
public interface IProviderCatalog {
    /// <summary>获取所有供应商描述符（LLM + Search + ...）</summary>
    IReadOnlyList<IProviderDescriptor> GetAll();
    /// <summary>按类别筛选</summary>
    IReadOnlyList<IProviderDescriptor> GetByCategory(ProviderCategory category);
    /// <summary>按名称查找</summary>
    bool TryGet(string name, [NotNullWhen(true)] out IProviderDescriptor? descriptor);
}
```

#### 7.3.4 适配器（桥接现有两套机制 → IProviderDescriptor）

**LLM 适配器**：`LlmProviderDescriptor : IProviderDescriptor`
- 包装 `IProviderDefinition`，`Category = Llm`，`SupportsOpenAiFormat = protocol == "openai-compatible"`

**Search 适配器**：`SearchProviderDescriptor : IProviderDescriptor`
- 包装 `ISearchProvider`，`Category = Search`，`SupportsOpenAiFormat = false`

#### 7.3.5 `ProviderCatalog` 实现（DI 动态收集）

```csharp
[Register(typeof(IProviderCatalog), ServiceLifetime.Singleton)]
public sealed class ProviderCatalog : IProviderCatalog {
    private readonly FrozenDictionary<string, IProviderDescriptor> _byName;
    private readonly IReadOnlyList<IProviderDescriptor> _all;

    public ProviderCatalog(
        IProviderDefinitionRegistry llmRegistry,
        IEnumerable<ISearchProvider> searchProviders
    ) {
        var llmDescriptors = llmRegistry.GetRegisteredProviders()
            .Select(name => new LlmProviderDescriptor(llmRegistry.TryGet(name)!));
        var searchDescriptors = searchProviders.Select(p => new SearchProviderDescriptor(p));
        _all = llmDescriptors.Concat(searchDescriptors).ToFrozenList();
        _byName = _all.ToFrozenDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IProviderDescriptor> GetAll() => _all;
    public IReadOnlyList<IProviderDescriptor> GetByCategory(ProviderCategory c)
        => _all.Where(d => d.Category == c).ToFrozenList();
    public bool TryGet(string name, [NotNullWhen(true)] out IProviderDescriptor? d)
        => _byName.TryGetValue(name, out d);
}
```

### 7.4 消费示例

```csharp
// 列出所有供应商
foreach (var p in catalog.GetAll())
    Console.WriteLine($"{p.Name} [{p.Category}] OpenAI={p.SupportsOpenAiFormat} Available={p.IsAvailable}");

// 只看 LLM 供应商
var llms = catalog.GetByCategory(ProviderCategory.Llm);

// 查某个供应商是否支持 OpenAI 格式
if (catalog.TryGet("openai", out var desc) && desc.SupportsOpenAiFormat) { ... }
```

### 7.5 执行步骤

| 步骤 | 内容 |
|------|------|
| P4a | `ProviderCategory` 枚举 + `IProviderDescriptor` 接口 + `IProviderCatalog` 接口（abstractions 层） |
| P4b | `LlmProviderDescriptor` 适配器 + `SearchProviderDescriptor` 适配器 + `ProviderCatalog` 实现 |
| P4c | DI 注册 + 单元测试 |
| P4d | search providers 代码拆子文件夹 + provider 构造注入 IConfigRoot |

<!-- 🤖 Auto Decision: 2026-10-18 -->
<!-- 决策: 配置树状生长 — settings.json 作极简树根，current/providers_llm/search_providers 三分支子文件夹，物理拆分+逻辑合并 -->
<!-- 原因: 用户要求"主配置在子配置文件夹外层，像一颗树一样生长"，简化理解 + 消费方无感知 -->
<!-- 替代方案: 全部内联 settings.json（单文件膨胀，违背"拆分"诉求）/ 只拆供应商不拆 current（current 45 字段仍臃肿）-->
<!-- 验证: P1-P8 全部完成，24 单元测试 + 9 InitCommand 测试全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 P8 -->
<!-- 决策: BuildDefaultConfigTreeAsync 从 BuildDefaultSettingsJson DTO 拆分到树状文件，极简 MinimalSettingsJson DTO 避免空字典序列化 -->
<!-- 原因: SettingsJson.Vendor/SearchProviders 是非 nullable 字典默认 []，序列化成 {}，极简 DTO 只含 current.profile+autoFetchModels+update -->
<!-- 替代方案: 改 SettingsJson 字段为 nullable（影响面大）/ 用 JsonNode 动态构造（AOT 不兼容）-->
<!-- 验证: 6 P8 单元测试 + 9 InitCommand 测试全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-18 ADR 0140 -->
<!-- 决策: 新增 DeepSeek 搜索供应商（priority=90），providers_search/ 从 6 个扩展到 7 个 -->
<!-- 原因: DeepSeek 官方推出 Search & Fetch API，集成到工程中（ADR 0140） -->
<!-- 关联: docs/implemented/adr/component_strategy/0140-iwebfetchprovider-deepseek-search-fetch-integration.md -->
<!-- 验证: 90 web + 68 配置测试全绿 ✅ -->
