# ~/.jcc/ 路径统一整理计划

## 背景

`~/.jcc/` 目录下有 16 个子目录 + 8 个顶层文件,但大量路径硬编码在各个服务类中(`".jcc", "xxx"`),
未通过 `AppDataPaths` 统一管理。存在混放、重复定义、作用域不明确等问题。

**目标**: 所有 `.jcc` 子目录路径收归 `AppDataPaths.cs` 单一入口,按用户级/项目级/exe级三分区管理,
日后改路径只需改一处。

## 三层作用域定义

| 层级 | 路径 | 用途 | 示例 |
|------|------|------|------|
| 用户级 | `~/.jcc/` | 跨项目共享、用户全局状态 | settings.json, cron-tasks/, sessions/ |
| 项目级 | `{cwd}/.jcc/` | 项目隔离、随项目走 | workflow-states/, gh_cache/ |
| exe级 | `AppContext.BaseDirectory` | 跟 exe 走,exe 升级时同步 | runtime/ (native DLL) |

## 当前状态 — 顶层文件(8个)

| 文件名 | 代码引用 | 作用域 | 状态 |
|--------|---------|--------|------|
| settings.json | `ConfigLoader.cs:136` + `SettingsLoader.cs:212` | 用户级+项目级 | ✅ 两级都有 |
| auth.json | `AppDataPaths.cs:108` | 用户级 | ✅ |
| global.json | `AppDataPaths.cs:115` | 用户级 | ✅ |
| gui-preferences.json | `GuiPreferencesStore.cs:16` | 用户级 | ✅ |
| keyword-sections.json | `DynamicKeywordConfigService.cs:20` | 用户级 | ✅ |
| lsp-servers.json | `LspConfigLoader.cs:188` | 用户级 | ✅ |
| onboarding_complete.json | `OnboardingStatePersistence.cs:20` | 用户级 | ✅ |
| trusted_folders.json | `TrustFolderManager.cs:18` | 用户级 | ✅ |

## 当前状态 — 用户级目录(~/.jcc/)

| 目录名 | 代码引用 | 用途 | 状态 |
|--------|---------|------|------|
| cron-tasks/ | `AppDataPaths.cs:135` | 定时任务 | ✅ 已统一 |
| memdir/ | `AppDataPaths.cs:138` | 记忆存储 | ✅ 已统一 |
| sessions/ | `WorkflowConstants.cs:190` + `ISessionScanner.cs:50` | 会话历史 | 🔴 两级混用 |
| file-history/ | `FileHistoryService.cs:24` (硬编码) | 文件编辑历史 | ❌ 硬编码 |
| paste-cache/ | `PasteStore.cs:19` (硬编码) | 大文本粘贴缓存 | ❌ 硬编码 |
| shell-snapshots/ | `BashSystemActuator.cs:17` (硬编码) | Shell 快照 | ❌ 硬编码 |
| plans/ | `AppDataPaths.cs:64` (名称) + `PathPermissionChecker.cs:476` (硬编码路径) | 计划文件 | ⚠️ 半统一 |
| tasks/ | `TaskDirectoryOptions.cs:134` (硬编码) | 任务存储 | ❌ 硬编码 |
| teams/ | `AppDataPaths.cs:53` (名称) + `TeamManager.cs:20` | 团队配置 | ✅ 已统一 |
| tokens/ | `AppDataPaths.cs:120` | OAuth Token | ✅ 已统一 |
| mcp/ | `AppDataPaths.cs:66` (名称) + `McpAuthToolHandlers.cs:14` (硬编码路径) | MCP 认证 | ⚠️ 半统一 |
| tool-templates/ | `ToolTemplateService.cs:22` (硬编码) | 工具模板 | ❌ 硬编码 |
| agents/ | `AgentToolHandlers.cs:131` (硬编码) | Agent 状态 | ❌ 硬编码 |
| costs/ | `SessionCostPersistence.cs:57` (硬编码) | 成本跟踪 | ❌ 硬编码 |
| source/ | `SourceCodeEngine.cs:290` (硬编码) | 源代码克隆 | ❌ 硬编码 |
| runtime/ | `NativeDllBootstrapper.cs:15` (硬编码) | Native DLL | 🔴 应改 exe级 |

## 当前状态 — 项目级目录({cwd}/.jcc/)

| 目录名 | 代码引用 | 用途 | 状态 |
|--------|---------|------|------|
| workflow-states/ | `AppDataPaths.cs:152` | 工作流状态 | ✅ 已统一 |
| runtime-tasks/ | `AppDataPaths.cs:155` | 运行时任务 | ✅ 已统一 |
| goal-state/ | `AppDataPaths.cs:158` | 目标状态 | ✅ 已统一 |
| .env/ | `AppDataPaths.cs:161` | 环境变量 | ✅ 已统一 |
| UpdateContent/ | `AppDataPaths.cs:164` | 更新内容 | ✅ 已统一 |
| worktrees/ | `AppDataPaths.cs:55` (名称) | Worktree | ✅ 已统一 |
| dumps/ | `AppDataPaths.cs:144` | 转储 | 🔴 定义在用户级,应改项目级 |
| gh_cache/ | `GitHubToolHandlers.cs:49` (硬编码) | GitHub API 缓存 | 🔴 代码项目级,实际在用户级 |
| reflexion/ | `FileBasedReflexionMemory.cs:16` (硬编码) | 反思记忆 | ❌ 硬编码 |
| diag/ | `FileToolHandlers.cs:1301` (硬编码) | 诊断 | ❌ 硬编码 |
| memory/ | `MemoryManagementService.cs:318` (硬编码) | 团队记忆 | ❌ 硬编码 |
| todo/ | `TodoService.cs:24` (硬编码) | TODO | ❌ 硬编码 |
| mode/ | `BriefModeService.cs:26` (硬编码) | 模式 | ❌ 硬编码 |
| permission/ | `AgentPermissionMode.cs:16` (硬编码) | 权限规则 | ❌ 硬编码 |
| structured-output/ | `StructuredOutputToolHandler.cs:24` (硬编码) | 结构化输出 | ❌ 硬编码 |
| code-index/ | `CodeIndexer.cs:24` (硬编码) | 代码索引 | ❌ 硬编码 |

## 问题清单

| # | 级别 | 问题 | 修复方案 |
|---|------|------|---------|
| 1 | 🔴 | sessions 两级混用(`WorkflowConstants` 用户级 + `ISessionScanner` 项目级) | 统一到用户级 `~/.jcc/sessions/` |
| 2 | 🔴 | gh_cache 代码项目级但实际在用户级 | 统一到项目级 `{cwd}/.jcc/gh_cache/` |
| 3 | 🔴 | runtime/ 34个DLL ~80MB 放用户级 | 改 exe级 `AppContext.BaseDirectory/runtime/` |
| 4 | 🔴 | dumps/ 定义在用户级,应项目级 | 改项目级 `{cwd}/.jcc/dumps/` |
| 5 | ❌ | 15个目录硬编码 `".jcc","xxx"` | 收归 `AppDataPaths.cs` |
| 6 | ⚠️ | `WorkflowConstants.Paths` 与 `AppDataPaths` 路径重复定义 | 统一到 `AppDataPaths` |

## 执行计划

### Step 1: 扩展 AppDataPaths.cs

添加所有缺失的目录路径属性,按三分区组织:

```
// === 用户级路径 (~/.jcc/) ===
// 已有: CronTasksDirectory, MemdirDirectory, CostTrackingFilePath, DumpsDirectory(移走)
// 新增: FileHistoryDirectory, PasteCacheDirectory, ShellSnapshotsDirectory,
//       ToolTemplatesDirectory, AgentsDirectory, CostsDirectory, SourceDirectory,
//       KeywordSectionsFilePath, LspServersFilePath, GuiPreferencesFilePath,
//       OnboardingCompleteFilePath

// === 项目级路径 ({cwd}/.jcc/) ===
// 已有: WorkflowStatesDirectory, RuntimeTasksDirectory, GoalStateDirectory,
//       DotEnvDirectory, UpdateContentDirectory
// 新增: DumpsDirectory(从用户级移来), GhCacheDirectory, ReflexionDirectory,
//       DiagDirectory, MemoryDirectory, TodoDirectory, ModeDirectory,
//       PermissionDirectory, StructuredOutputDirectory, CodeIndexDirectory

// === exe级路径 (AppContext.BaseDirectory) ===
// 新增: RuntimeDirectory (native DLL)
```

### Step 2: 替换硬编码

逐个文件替换 `".jcc", "xxx"` → `AppDataConstants.XxxDirectory`

### Step 3: 修正混放

- sessions: `ISessionScanner.cs:50` 改用 `WorkflowConstants.Paths.SessionsDirectory`
- gh_cache: 确定项目级,代码已正确,实际文件位置后续清理
- runtime: `NativeDllBootstrapper.cs` 改用 `AppContext.BaseDirectory`
- dumps: `AppDataPaths.cs:144` 从 `JccDirectory` 改到 `ProjectJccDirectory`

### Step 4: 编译+测试+提交

每步编译验证,全部完成后提交。

<!-- 🤖 Auto Decision: 2026-09-11 -->
<!-- 决策: 路径全部收归 AppDataPaths.cs 单一入口类 -->
<!-- 原因: 用户要求"日后好改",单一入口改一处即生效,支持环境变量覆盖 -->
<!-- 替代方案: 扩展 WorkflowConstants.Paths(已有部分路径,但与 AppDataPaths 重复) -->
<!-- 验证: 编译通过(Abstractions+Infrastructure+Core+Services+App 全部0错误0警告),单元测试通过 ✅ -->
<!-- 提交: a659194e8 — 23文件改动,249增65删 -->
<!-- 遗留: runtime/ 作用域改exe级暂缓(涉及DLL加载风险); NativeDllBootstrapper 用 XdgPathResolver 不直接硬编码.jcc -->
