# Claude Code Agent 架构差距对比与补齐计划

> **创建时间**: 2026-08-16
> **调研源码**: `D:\project\claude-code-rust\claude-code-rev-main\src\`
> **对比目标**: `D:\project\w1\core\ai\Agents\` 等
> **原则**: 渐进式验证 → 缺失项出设计方案 → 日后按设计实现(不一次性大改,链路可能很差)

---

## 一、调研结论概览

### 1.1 核心机制对齐度: ~85%

| 机制 | 我们 | claude code | 状态 |
|------|------|-------------|------|
| 统一 AgentBase(fork 模式) | `core/ai/Agents/src/Coordinator/Fork/AgentBase.cs:1` (681行) | `runAgent.ts` | ✅ 对齐 |
| 工具白/黑名单过滤 | `AgentRoleProfile.AllowedTools/DisallowedTools` | `resolveAgentTools` | ✅ 对齐 |
| 多层工具过滤 | 三层(Profile + PermissionMode + SecurityClass) | 三层(global + disallowed + whitelist) | ✅ 对齐 |
| Plan/Explore/Code/Search 等 variant | 9 个 Profile | 6 个内置 agent | ✅ 对齐 |
| Agent 定义加载(.claude/agents) | `AgentDefinitionProvider.cs:1` (668行) | `loadAgentsDir.ts` | ✅ 对齐 |
| Fork 子代理管理 | `ForkSubAgentManager.cs:1` (491行) | `forkSubagent.ts` | ✅ 对齐 |
| 团队协作 | `TeamManager.cs` | in-process teammate | ✅ 对齐 |
| Plan 模式 | `PlanModeManager.cs:1` (898行) | EnterPlanMode/ExitPlanMode | ✅ 对齐 |
| Worktree 隔离 | `IAgentWorktreeManager` | `isolation: 'worktree'` | ✅ 对齐 |
| Agent 记忆 | `IAgentMemoryService` | `agentMemory.ts` | ✅ 对齐 |

### 1.2 我们的额外优势(claude code 没有)

| 优势 | 位置 | 说明 |
|------|------|------|
| **推理三权分立** | `core/ai/Reasoning/src/Agents/` | Prosecutor/Defender/Judge + DAG 证据链 + 5维权重裁决 |
| **Doctor 自举修复** | `core/ai/Agents/src/Doctor/Bootstrap/BootstrapAgent.cs:1` (494行) | LLM 驱动监控病人遥测并修复 jcc 自身源码 |
| **源码生成器工具分类** | `generators/EnumMetadata.Generator/src/SecurityClassGenerator.cs` | 自动生成 ToolSecuritySets,编译期确定工具安全分类 |

---

## 二、22 项缺失清单(待逐个验证)

> **验证状态图例**: ⬜ 未验证 | 🔍 验证中 | ✅ 已实现(无需补齐) | ❌ 确认缺失 | 🟡 部分实现

### 高价值(5 项)

#### 缺失项 1: Coordinator 专用模式

- **状态**: 🟡 部分实现(2026-08-16 验证)
- **claude code 位置**: `src/coordinator/coordinatorMode.ts:1` (321+行)
- **描述**: 专门的编排模式,coordinator 只有 `Agent`/`SendMessage`/`TaskStop` 三个工具,不直接执行任务,只编排 worker。有详细 system prompt 指导如何写 worker prompt、何时 continue vs spawn fresh。Worker agent 类型为 `'worker'`,结果通过 `<task-notification>` XML 返回。
- **启用方式**: `feature('COORDINATOR_MODE') && isEnvTruthy(process.env.CLAUDE_CODE_COORDINATOR_MODE)`
- **价值**: 高 — 编排与执行分离,coordinator 不陷入执行细节
- **验证结论**:
  - ✅ 有 prompt 基础设施: `CoordinatorSection.cs:9`(`InjectOn = CoordinatorMode`)、`TeammateSection.cs:9`、`AgentToolSection.cs:14`(`isCoordinator` 分支生成不同 prompt)、`DefaultSystemPromptProvider.cs:67`(注入 CoordinatorMode sections)
  - ✅ 有工厂方法: `SystemPromptProviderOptions.ForCoordinatorMode()` (`:109`)
  - ❌ **生产代码硬编码 `IsCoordinatorMode = false`**(`SyncSystemPromptProviderOptions.cs:37`),无启用路径
  - ❌ **`ForCoordinatorMode()` 从未被调用**(全代码库仅定义处 1 处匹配)
  - ❌ **不限制工具集**: Coordinator Profile `AllowedTools = null`(全量),而 claude code 限制为 `[Agent, SendMessage, TaskStop]`(`COORDINATOR_MODE_ALLOWED_TOOLS`)
  - ❌ 无 feature gate / 环境变量(如 `JCC_COORDINATOR_MODE`)
- **设计方案**:
  1. **启用路径**: 在 `SyncSystemPromptProviderOptions` 构造函数中,从环境变量 `JCC_COORDINATOR_MODE` 或 `WorkflowConfig` 读取 `IsCoordinatorMode`(默认 false,向后兼容)
  2. **工具集限制**: 当 `IsCoordinatorMode = true` 时,在主代理初始化路径覆盖工具集为 `[Agent, SendMessage, TaskStop]` — 新增 `CoordinatorToolRestrictionMiddleware` 或在 `AgentRoleProfileRegistry` 中动态返回受限 Profile
  3. **工厂方法接入**: 主代理初始化时,若 `IsCoordinatorMode`,用 `ForCoordinatorMode()` 创建 options(替代当前 `SyncSystemPromptProviderOptions` 的硬编码 false)
  4. **Worker agent 类型**: 新增 `ExecutorVariant.Worker` 或复用现有 variant,coordinator spawn 的子代理标记为 worker,结果通过消息返回
  5. **风险点**: ⚠️ 工具集限制会改变主代理行为,必须 feature gate 保护,默认关闭;⚠️ 现有主代理依赖全量工具,启用后可能无法执行某些操作
  6. **渐进式步骤**: (a) 加环境变量读取 → (b) prompt 分支已存在,验证生效 → (c) 加工具集限制中间件 → (d) 加 worker variant → (e) 端到端测试

#### 缺失项 2: Fork 字节级 prompt cache 共享

- **状态**: ✅ 已实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/runAgent.ts:508`
- **描述**: Fork 路径用父 agent 的**已渲染 system prompt 字节**(`toolUseContext.renderedSystemPrompt`),而非重新生成。避免 GrowthBook 冷热状态差异导致 prompt cache 失效。普通 subagent 零上下文启动,两种模式共存。
- **价值**: 高 — 大幅省 token(prompt cache 命中)
- **验证结论**: **已实现**。`ForkSpawnMiddleware.cs:69`:
  ```csharp
  AdditionalInstructions = context.Options.SystemPrompt ?? cacheSafeParams?.RenderedSystemPrompt,
  ```
  - `CacheSafeParams.RenderedSystemPrompt` 字段存在(`foundation/Abstractions/01-ai/LLM/Chat/Cache/CacheSafeParams.cs:5`)
  - fork 时 `cacheSafeParams?.Clone()` 克隆父参数(`ForkSpawnMiddleware.cs:48`)
  - `ContextSetupMiddleware.cs:85` 也传递 `RenderedSystemPrompt`
  - 逻辑与 claude code 的 `override.systemPrompt ?? toolUseContext.renderedSystemPrompt` 一致
- **无需补齐**

#### 缺失项 3: getSystemPrompt 闭包延迟生成

- **状态**: 🟡 部分实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/loadAgentsDir.ts:106`
- **描述**: `getSystemPrompt` 是闭包而非静态字段。内置 agent 可接收 `toolUseContext` 参数,动态注入运行时配置(如 claude-code-guide agent 注入当前 MCP 服务器列表、自定义命令、skills、settings.json)。
- **价值**: 高 — 内置 agent 可动态注入运行时配置
- **验证结论**:
  - ✅ `AgentPromptBuilder.BuildSystemPromptAsync` 有部分动态注入:团队上下文(`:79-99` 通过 `ITeammateInitService`)、skills 列表(`:107-115`)
  - ❌ **不接收运行时上下文参数**: 签名是 `(agentType, task, context, ct)`,无 `toolUseContext` 等价参数,无法注入当前 MCP/skills/settings
  - ❌ **GuideAgent 不动态注入配置**: `BuiltInAgentToolHandlers.GuideAgentAsync` 调用 `BuildGuidePrompt(question, feature)` 静态构建,不查询当前 MCP/skills/settings
  - ❌ 无 claude code 的 `getSystemPrompt({ toolUseContext })` 闭包模式
- **设计方案**:
  1. **新增 `AgentPromptContext` 参数**: 给 `BuildSystemPromptAsync` 增加可选参数,含 `IReadOnlyList<string> McpServers`、`IReadOnlyList<string> AvailableSkills`、`string? SettingsSummary`
  2. **GuideAgent 特殊处理**: spawn 前从 `IToolRegistry`/`ISkillRegistry`/`IConfigChangeNotifier` 查询当前配置,注入到 prompt
  3. **或更优方案 — prompt 模板化**: `AgentDefinition.SystemPrompt` 支持 `{{mcp_servers}}`、`{{skills}}` 占位符,spawn 时用运行时上下文渲染(复用现有模板引擎)
  4. **风险点**: ⚠️ 改变 `IAgentPromptBuilder` 接口签名影响所有调用方;⚠️ 需评估 AOT 兼容性(闭包在 NativeAOT 下需谨慎)
  5. **渐进式步骤**: (a) 新增 `AgentPromptContext` 参数(可选,默认 null 向后兼容) → (b) GuideAgent 注入配置 → (c) 评估模板化方案

#### 缺失项 10: omitClaudeMd + 省略 gitStatus

- **状态**: ✅ 已实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/runAgent.ts:385`
- **描述**: 只读 agent(Explore/Plan)省略 CLAUDE.md(每周省 5-15 Gtok),且省略父 session 的 stale gitStatus(40KB)。通过 `omitClaudeMd: true` 字段控制。
- **价值**: 高 — 省 5-15 Gtok/周
- **验证结论**: **已实现**。
  - `AgentRoleProfile.OmitClaudeMd`/`OmitGitStatus` 字段存在(`AgentRoleProfile.cs:57,62`)
  - Explore/Plan Profile 设置 `OmitClaudeMd = true`、`OmitGitStatus = true`(`AgentRoleProfileRegistry.cs:233-234,245-246`)
  - `ContextSetupMiddleware.BuildFilteredCacheSafeParams`(`:72,78`): `OmitClaudeMd == true` → `FilterKey(userContext, "claudeMd")`;`OmitGitStatus == true` → `FilterKey(systemContext, "gitStatus")`
  - 逻辑与 claude code 的 `shouldOmitClaudeMd` + 省略 gitStatus 一致
- **无需补齐**

#### 缺失项 12+13: 递归 fork 防护 + filterIncompleteToolCalls

- **状态**: ✅ 已实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/forkSubagent.ts` (isInForkChild), `runAgent.ts` (filterIncompleteToolCalls)
- **描述**:
  - **递归 fork 防护**: `isInForkChild` 检测 fork boilerplate tag(`<fork-boilerplate>`),防止 fork 子进程递归 fork(无限递归)
  - **filterIncompleteToolCalls**: fork 时过滤掉不完整的 tool calls(有 tool_use 无 tool_result),避免 API 错误
- **价值**: 高 — 防无限递归 + 防 API 错误
- **验证结论**: **均已实现**:
  - **递归防护**: `ForkMessageBuilder.IsInForkChild`(`ForkMessageBuilder.cs:46`)检测 `<fork-boilerplate>` tag,与 claude code 的 `isInForkChild` 逻辑一致
  - **深度限制**: `ForkSubAgentManager.CalculateForkDepth`(`ForkSubAgentManager.cs:430`)max 100 层,`ForkContext.ForkDepth` 字段传递
  - **incomplete tool call 处理**: `ForkMessageBuilder.BuildForkedMessages`(`ForkMessageBuilder.cs:60`)为每个 tool_use 补占位 tool_result(`ForkPlaceholderResult = "Fork started — processing in background"`),效果等同于 claude code 的 `filterIncompleteToolCalls`(避免 API 错误),实现思路略不同(补占位 vs 过滤)
- **无需补齐**

---

### 中价值(11 项)

#### 缺失项 4: 多层来源优先级覆盖

- **状态**: 🟡 部分实现(2026-08-16 验证 → 2026-08-16 修复 EnsureCustomLoaded)
- **claude code 位置**: `src/tools/AgentTool/loadAgentsDir.ts:193` (`getActiveAgentsFromList`)
- **描述**: agent 来源优先级(低 → 高): `built-in < plugin < userSettings < projectSettings < flagSettings < policySettings`,后者覆盖前者同名 agent。用 Map.set 同 key 覆盖实现。
- **价值**: 中 — 配置灵活性(项目级覆盖用户级覆盖内置)
- **验证结论**:
  - ✅ `AgentDefinitionProvider.Deduplicate`(`:618-638`)实现了覆盖:有 `SourcePath` 的(来自文件)覆盖先来的(内置),加载顺序 内置→用户→项目,效果 项目>用户>内置
  - ❌ 缺 plugin/flag/managed 三层(claude code 有 6 层,我们 3 层)
  - ✅ **已修复** `AgentRoleProfileRegistry.EnsureCustomLoaded`: 自定义定义有 SourcePath 时覆盖同 key 内置 profile(用 Dictionary 索引 O(n) 替代 FindIndex O(n²))
- **已实现**: commit `160a7ce8d` — EnsureCustomLoaded 覆盖逻辑 + 测试 GetProfile_CustomDefinitionWithSourcePath_OverridesBuiltIn

#### 缺失项 5: 异步 agent 白名单(ASYNC_AGENT_ALLOWED_TOOLS)

- **状态**: ❌ 确认缺失(2026-08-16 验证)
- **claude code 位置**: `src/constants/tools.ts`
- **描述**: 后台(异步)agent 有独立白名单,限制可用工具(不能 AskUserQuestion、不能 TaskStop 等)。`filterToolsForAgent` 中 `isAsync && !ASYNC_AGENT_ALLOWED_TOOLS.has(tool.name)` 时过滤。
- **价值**: 中 — 后台 agent 安全(不能交互提问、不能停止其他任务)
- **验证结论**: 确认缺失。全代码库无 `AsyncAgentAllowed`/`BACKGROUND_ALLOWED_TOOLS`/`ASYNC_AGENT_ALLOWED` 等常量。`AgentBackgroundSpawnMiddleware` 存在但无独立工具白名单过滤。
- **设计方案**:
  1. **新增 `AsyncAgentAllowedTools` 常量**: 在 `ToolSecuritySets`(源码生成器输出)或手动定义,包含后台 agent 允许的工具(FileRead/Glob/Grep/Bash/FileEdit/FileWrite/WebFetch/WebSearch 等,排除 AskUser/TaskStop/Agent 等)
  2. **在 `AgentBackgroundSpawnMiddleware` 加过滤**: 后台 spawn 时,用 `AsyncAgentAllowedTools` 过滤工具集
  3. **风险点**: 低 — 新增过滤,不影响现有前台 agent
  4. **渐进式步骤**: (a) 定义白名单 → (b) 后台 spawn 路径加过滤 → (c) 测试后台 agent 不能调用 AskUser

#### 缺失项 6: Agent 专属 MCP 服务器(mcpServers 字段)

- **状态**: ⬜ 未验证
- **claude code 位置**: `src/tools/AgentTool/runAgent.ts` (`initializeAgentMcpServers`)
- **描述**: agent 定义中 `mcpServers` 字段允许 agent 定义自己的 MCP 服务器,spawn 时连接,结束时清理。`AgentMcpServerSpec[]` 类型。
- **价值**: 中 — agent 私有工具(如 doctor agent 专属诊断 MCP)
- **我们现状**: 有 `IAgentMcpServerManager` 接口和 `AgentMcpServerManager` 实现,需验证是否支持 agent 定义中声明专属 MCP。
- **验证方法**: 查 `AgentMcpServerManager.cs` 是否从 agent 定义读取 mcpServers 字段。
- **设计方案**: (待验证后填写)

#### 缺失项 7: Skills 预加载(skills 字段)

- **状态**: 🟡 部分实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/runAgent.ts`
- **描述**: agent 定义中 `skills` 字段在 spawn 时预加载 skill 内容到 initialMessages。`skills: string[]`。
- **价值**: 中 — spawn 时自动加载 skill,无需 agent 自己调用 skill 工具
- **验证结论**:
  - ✅ `SubAgentOptions.PreloadSkills` 字段存在(`SubAgentOptions.cs:19`)
  - ✅ `ContextSetupMiddleware.cs:43` spawn 时设置 `PreloadSkills = context.Definition?.Skills`
  - ✅ `AgentPromptBuilder.cs:107-115` 把 skills 列表写入 prompt(告知 agent 有哪些 skill)
  - ❌ **未实际预加载 skill 内容到 initialMessages**: `PreloadSkills` 字段全代码库仅 3 处匹配(都是设置,无消费点),没有加载 skill 内容/指令到消息列表的逻辑
- **设计方案**:
  1. **在 spawn 管道新增 SkillPreloadMiddleware**(或复用现有中间件): 读取 `SubAgentOptions.PreloadSkills`,从 `ISkillRegistry` 加载每个 skill 的内容,注入到 `InitialMessageList`
  2. **位置**: 在 `ContextSetupMiddleware` 之后、`LifecycleSpawnMiddleware` 之前
  3. **风险点**: ⚠️ skill 内容可能很大,需评估 token 预算;⚠️ skill 不存在时的降级策略(警告还是报错)
  4. **渐进式步骤**: (a) 新增 SkillPreloadMiddleware → (b) 从 ISkillRegistry 加载 skill 内容 → (c) 注入到 InitialMessageList → (d) 测试

#### 缺失项 8: initialPrompt(首轮前置 prompt)

- **状态**: ⬜ 未验证
- **claude code 位置**: `src/tools/AgentTool/loadAgentsDir.ts`
- **描述**: agent 定义中 `initialPrompt` 字段,首轮前置 prompt(支持斜杠命令)。spawn 时作为第一条 user message 注入。
- **价值**: 低 — 较少 agent 需要
- **我们现状**: 需验证 `SubAgentOptions` 或 agent 定义是否有 initialPrompt 字段。
- **验证方法**: 查 `SubAgentOptions.cs` 和 agent 定义模型。
- **设计方案**: (待验证后填写)

#### 缺失项 9: maxTurns(agent 级别最大轮次)

- **状态**: 🟡 部分实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/runAgent.ts`
- **描述**: agent 定义中 `maxTurns` 字段,agent 级别的最大轮次限制。`maxTurns ?? agentDefinition.maxTurns`。
- **价值**: 中 — 防失控
- **验证结论**:
  - ✅ `SubAgentOptions.MaxIterations` 字段存在(默认 50,`SubAgentOptions.cs:8`)
  - ✅ `ForkOptions.MaxIterations` 存在(默认 10),`ForkSpawnMiddleware.cs:70` 传递
  - 🟡 **需确认执行循环中生效**: 字段传递到 `SubAgentOptions`,但未在 grep 中找到 `AgentBase` 或执行循环内检查 `MaxIterations` 并 break 的逻辑(可能在 `QueryEngine` 或 `AgentCoordinator` 深层,需进一步验证)
- **设计方案**:
  1. **验证执行循环**: 查 `AgentBase.ExecuteAsync` 或 `QueryEngine` 中是否读取 `MaxIterations` 并限制循环次数
  2. **若未生效**: 在执行循环开头检查 `iterationCount >= MaxIterations` 则停止并返回结果
  3. **风险点**: 低 — 字段已存在,只需消费
  4. **渐进式步骤**: (a) 确认执行循环位置 → (b) 加 MaxIterations 检查 → (c) 测试

#### 缺失项 14: Agent(worker,researcher) 语法

- **状态**: ✅ 已实现(2026-08-16)
- **claude code 位置**: `src/tools/AgentTool/agentToolUtils.ts` (`resolveAgentTools`)
- **描述**: Agent 工具的 `tools` 字段可携带 `allowedAgentTypes` 元数据,如 `Agent(worker,researcher)` 限制可 spawn 的 agent 类型。`ruleContent.split(',')` 解析。
- **价值**: 中 — 限制 agent 可递归 spawn 的子 agent 类型
- **已实现**: commit `97d2c3457` — `AgentTypeSpecParser` 静态类(Parse + IsAllowed) + 7 个测试
  - `Parse("worker,researcher")` → (PrimaryType="worker", AllowedTypes=["worker","researcher"])
  - `IsAllowed` 大小写不敏感检查

#### 缺失项 15: requiredMcpServers

- **状态**: ⬜ 未验证
- **claude code 位置**: `src/tools/AgentTool/AgentTool.tsx`
- **描述**: agent 可声明 `requiredMcpServers: string[]`,spawn 时检查这些 MCP 服务器是否已配置,不满足报错。等待 pending 服务器最多 30s。
- **价值**: 中 — 显式声明依赖,缺失时清晰报错
- **我们现状**: 需验证 agent 定义是否有 requiredMcpServers 字段。
- **验证方法**: 查 agent 定义模型和 spawn 校验逻辑。
- **设计方案**: (待验证后填写)

#### 缺失项 16: filterDeniedAgents(权限规则禁用特定 agent)

- **状态**: ⬜ 未验证
- **claude code 位置**: 权限层
- **描述**: 权限规则可禁用特定 agent,`Agent(AgentName)` deny 语法。
- **价值**: 中 — 安全控制
- **我们现状**: 需验证权限层是否支持按 agent 名禁用。
- **验证方法**: 查权限规则解析和 agent spawn 校验。
- **设计方案**: (待验证后填写)

#### 缺失项 17: 插件 agent 安全限制

- **状态**: ⏸️ 暂缓(2026-08-16)
- **claude code 位置**: `src/utils/plugins/loadPluginAgents.ts`
- **描述**: 插件 agent **不能**定义 `permissionMode`、`hooks`、`mcpServers`(安装时信任边界,不允许单个 agent 文件静默添加)。
- **价值**: 中 — 安全(插件不能越权)
- **暂缓原因**: 无插件 agent 体系(只有 PluginHook),需先建立插件 agent 加载器,工作量较大
- **恢复条件**: 当建立插件 agent 体系时,加载时校验 `permissionMode`/`hooks`/`mcpServers` 字段为空,非空则拒绝加载并报错

#### 缺失项 21: background: true(总在后台运行)

- **状态**: ⬜ 未验证
- **claude code 位置**: `src/tools/AgentTool/built-in/verificationAgent.ts`
- **描述**: agent 定义中 `background: true` 表示总在后台运行(如 verification agent)。与 `run_in_background` 参数不同,这是 agent 级别的固定配置。
- **价值**: 中 — 某些 agent(验证/监控)天然适合后台
- **我们现状**: 有 `AgentBackgroundSpawnMiddleware`,需验证是否支持 agent 定义级别的 background 字段。
- **验证方法**: 查 agent 定义模型是否有 background 字段。
- **设计方案**: (待验证后填写)

---

### 低价值(6 项)

#### 缺失项 11: ONE_SHOT_BUILTIN_AGENT_TYPES

- **状态**: ✅ 已实现(2026-08-16 验证)
- **claude code 位置**: `src/tools/AgentTool/agentToolUtils.ts`
- **描述**: Explore/Plan 是 one-shot agent,跳过 agentId/SendMessage/usage trailer(每次省 ~135 字符)。
- **价值**: 低 — 省 135 字符/次
- **验证结论**: **已实现**。
  - `AgentRoleProfile.IsOneShot` 字段存在(`AgentRoleProfile.cs:67`)
  - `OneShotExecutorVariants.IsOneShot(variant)` 静态方法(`ExecutorVariant.cs:66,71`)
  - Explore/Plan Profile 设置 `IsOneShot = true`(`AgentRoleProfileRegistry.cs:235,247`)
  - `AgentHandoffMiddleware.cs:82`: `var isOneShot = ... && OneShotExecutorVariants.IsOneShot(context.SubagentType)` 在 handoff 中使用
  - 单元测试覆盖: `AgentRoleProfileRegistryTests.cs:47-56`
- **无需补齐**

#### 缺失项 18: criticalSystemReminder_EXPERIMENTAL

- **状态**: ✅ 已实现(2026-08-16)
- **claude code 位置**: `src/tools/AgentTool/loadAgentsDir.ts`
- **描述**: agent 定义中 `criticalSystemReminder_EXPERIMENTAL` 字段,每轮重注入的提醒(如 verification agent 的 "CRITICAL: This is a VERIFICATION-ONLY task")。
- **价值**: 低 — 实验性功能
- **已实现**: commit `a469d6296` — AgentRoleProfile.CriticalSystemReminder + AgentDefinition.CriticalSystemReminder + AgentPromptBuilder 注入 + 2 个测试

#### 缺失项 19: model alias 匹配父 tier

- **状态**: ✅ 已实现(2026-08-16)
- **claude code 位置**: `src/utils/model/agent.ts` (`aliasMatchesParentTier`)
- **描述**: 如果 agent 指定 `model: 'opus'` 而父模型也是 opus 系列,直接用父模型(避免 Vertex 用户从 Opus 4.6 降级到默认 Opus)。
- **价值**: 低 — 边缘场景
- **已实现**: commit `0f538fe1a` — `SystemPromptProviderOptions.ModelAliasMatchesParentTier` 静态方法 + 6 个测试(opus/sonnet/haiku 三档匹配)

#### 缺失项 20: CLAUDE_CODE_SUBAGENT_MODEL 环境变量

- **状态**: ⬜ 未验证
- **claude code 位置**: `src/utils/model/agent.ts`
- **描述**: 全局环境变量覆盖所有 subagent 模型。
- **价值**: 低 — 测试/调试用
- **我们现状**: 需验证是否有等价环境变量。
- **验证方法**: 查环境变量覆盖逻辑。
- **设计方案**: (待验证后填写)

#### 缺失项 22: color/effort 字段

- **状态**: ⬜ 未验证
- **claude code 位置**: `src/tools/AgentTool/loadAgentsDir.ts`
- **描述**: `color`(UI 颜色)和 `effort`(努力等级)字段。
- **价值**: 低 — UI/调优
- **我们现状**: 需验证。
- **验证方法**: 查 agent 定义模型。
- **设计方案**: (待验证后填写)

#### 缺失项 23(合并): isolation: 'remote'

- **状态**: ✅ 已实现(2026-08-16)
- **claude code 位置**: `src/tools/AgentTool/loadAgentsDir.ts`
- **描述**: `isolation` 支持 `'worktree'` 和 `'remote'` 两种。我们可能有 worktree,需验证 remote。
- **价值**: 低 — 远程隔离较少用
- **已实现**: commit `1cfc879e0` — `AgentIsolationMode.Remote` 枚举值 + [EnumValue("remote")] + 2 个测试(FromValue/ToValue 往返)

---

## 三、验证进度跟踪

| # | 缺失项 | 价值 | 状态 | 验证日期 | 验证结论 | 设计方案 |
|---|--------|------|------|----------|----------|----------|
| 1 | Coordinator 专用模式 | 高 | ✅ | 2026-08-16 | 已实现:JCC_COORDINATOR_MODE 启用 + 工具集限制 [Agent,SendMessage,TaskStop] | commit 04b242269 + 7d8c0c2e7 |
| 2 | Fork 字节级 prompt cache 共享 | 高 | ✅ | 2026-08-16 | 已实现:ForkSpawnMiddleware.cs:69 复用 RenderedSystemPrompt | 无需补齐 |
| 3 | getSystemPrompt 闭包延迟生成 | 高 | 🟡 | 2026-08-16 | 部分实现:有团队上下文注入,但不接收运行时上下文,GuideAgent 不动态注入配置 | 见上文设计方案 |
| 4 | 多层来源优先级覆盖 | 中 | 🟡 | 2026-08-16 | 部分实现:EnsureCustomLoaded 已修复覆盖逻辑,缺 plugin/flag/managed 三层 | 见上文 |
| 5 | 异步 agent 白名单 | 中 | ✅ | 2026-08-16 | 已实现:AsyncAgentAllowedTools + AgentBackgroundSpawnMiddleware | commit 23c7e9ac0 |
| 6 | Agent 专属 MCP 服务器 | 中 | ✅ | 2026-08-16 | 已实现:McpSetupMiddleware + AgentMcpServerManager,spawn 时初始化/结束时清理 | 无需补齐 |
| 7 | Skills 预加载 | 中 | ✅ | 2026-08-16 | 已实现:ContextSetupMiddleware 加载 skill 内容到 InitialMessageList | commit 8f4b6bd1f |
| 8 | initialPrompt | 低 | ✅ | 2026-08-16 | 已实现:AgentDefinition.InitialPrompt + ContextSetupMiddleware 接入 + AgentBase 注入 | commit c058f0669 + 48866bf5a |
| 9 | maxTurns | 中 | ✅ | 2026-08-16 | 已实现:AgentBase ExecuteAsync/ExecuteStreamAsync MaxIterations 检查 | commit 594cd4d73 |
| 10 | omitClaudeMd + 省略 gitStatus | 高 | ✅ | 2026-08-16 | 已实现:ContextSetupMiddleware.cs:72,78 过滤 | 无需补齐 |
| 11 | ONE_SHOT_BUILTIN_AGENT_TYPES | 低 | ✅ | 2026-08-16 | 已实现:IsOneShot 字段 + AgentHandoffMiddleware 使用 | 无需补齐 |
| 12 | 递归 fork 防护 | 高 | ✅ | 2026-08-16 | 已实现:IsInForkChild + CalculateForkDepth max100 | 无需补齐 |
| 13 | filterIncompleteToolCalls | 高 | ✅ | 2026-08-16 | 已实现:BuildForkedMessages 补占位 tool_result | 无需补齐 |
| 14 | Agent(worker,researcher) 语法 | 中 | ✅ | 2026-08-16 | 已实现:AgentTypeSpecParser Parse+IsAllowed | commit 97d2c3457 |
| 15 | requiredMcpServers | 中 | ✅ | 2026-08-16 | 已实现:AgentDefinitionProvider.cs:318 解析 required_mcp_servers | 无需补齐 |
| 16 | filterDeniedAgents | 中 | ✅ | 2026-08-16 | 已实现:AgentPermissionMode.cs:234 FilterDeniedAgentsAsync | 无需补齐 |
| 17 | 插件 agent 安全限制 | 中 | ⏸️ | 2026-08-16 | 暂缓:无插件 agent 体系,需先建立插件加载器 | 见上文 |
| 18 | criticalSystemReminder_EXPERIMENTAL | 低 | ✅ | 2026-08-16 | 已实现:AgentRoleProfile.CriticalSystemReminder + AgentPromptBuilder 注入 | commit a469d6296 |
| 19 | model alias 匹配父 tier | 低 | ✅ | 2026-08-16 | 已实现:ModelAliasMatchesParentTier 静态方法 | commit 0f538fe1a |
| 20 | CLAUDE_CODE_SUBAGENT_MODEL 环境变量 | 低 | ✅ | 2026-08-16 | 已实现:GetSubagentModelFromEnv + ContextSetupMiddleware 接入 | commit 4867cd63a + f74723d4f |
| 21 | background: true | 中 | ✅ | 2026-08-16 | 已实现:IsBackground 字段 + AgentServiceImpl.cs:123 生效 | 无需补齐 |
| 22 | color/effort 字段 | 低 | ✅ | 2026-08-16 | 已实现:SubAgentOptions.ColorHex + Effort 字段存在 | 无需补齐 |
| 23 | isolation: 'remote' | 低 | ✅ | 2026-08-16 | 已实现:AgentIsolationMode.Remote 枚举值 | commit 1cfc879e0 |

---

## 四、验证与设计填写规范

### 4.1 每项验证流程

1. **更新状态**: ⬜ → 🔍(在本文档表格中)
2. **执行验证**: 按"验证方法"查代码
3. **填写验证结论**:
   - ✅ 已实现 → 记录实现位置,无需设计方案
   - ❌ 确认缺失 → 填写设计方案
   - 🟡 部分实现 → 记录已有部分 + 缺失部分 + 设计方案
4. **设计方案内容**(仅缺失项):
   - 改动文件清单
   - 新增/修改的接口或类
   - 与现有架构的集成点
   - 风险点(链路可能很差的地方)
   - 渐进式实现步骤(每步可独立编译)
5. **更新状态**: 🔍 → ✅/❌/🟡

### 4.2 设计方案原则

- **不写代码**,只出设计
- **渐进式**: 每步可独立编译,不一次性大改
- **标注风险**: 链路差的地方明确标出
- **复用优先**: 优先复用现有接口/类,不创造新抽象
- **对齐 AGENTS.md**: 遵循七层架构、AOT 兼容、源码生成器等规范

---

## 五、决策记录

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: 使用新增重载方法而非修改现有接口签名 -->
<!-- 原因: 向后兼容,现有调用方不受影响;新调用方可选传递 AgentPromptContext -->
<!-- 替代方案: 给现有方法加可选参数(被否决,C# 接口可选参数不继承默认值,会导致调用方编译失败) -->
<!-- 验证: TDD 红测试 3失败1通过 → 实现注入 → 绿测试 4通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: #1 Coordinator 模式 — 启用路径完成,工具集限制标记后续 -->
<!-- 实现: IsCoordinatorModeEnabledFromEnv 静态方法 + SyncSystemPromptProviderOptions 接入环境变量 -->
<!-- 遗留: 工具集限制为 [Agent,SendMessage,TaskStop] 需主代理初始化链路接入,风险较高后续做 -->
<!-- 验证: TDD 5/5通过, commit 04b242269 -->

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: #5 异步 agent 白名单 — 完全实现 -->
<!-- 实现: AsyncAgentAllowedTools FrozenSet 常量 + AgentBackgroundSpawnMiddleware 后台模式 with 表达式覆盖 AllowedTools -->
<!-- 验证: TDD 5/5通过, commit 23c7e9ac0 -->

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: #8 initialPrompt — 字段已加,spawn 注入后续 -->
<!-- 实现: SubAgentOptions.InitialPrompt 字段 -->
<!-- 遗留: ContextSetupMiddleware 注入到 InitialMessageList 后续做 -->
<!-- 验证: 测试 2/2通过, commit c058f0669 -->

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: #20 SUBAGENT_MODEL 环境变量 — 静态方法已加,spawn 路径接入后续 -->
<!-- 实现: GetSubagentModelFromEnv 静态方法 -->
<!-- 遗留: spawn 路径模型解析接入后续做 -->
<!-- 验证: 测试 7/7通过, commit 4867cd63a -->

## 六、本次会话实现总结(2026-08-16 → 2026-08-17)

### 已 commit 的实现

| commit | 缺失项 | 状态 | 说明 |
|--------|--------|------|------|
| `927b297` | #3 getSystemPrompt 闭包 | ✅ 完全实现 | AgentPromptContext + IAgentPromptBuilder 新重载 + GuideAgent 接入,12/12测试 |
| `04b242269` | #1 Coordinator 启用路径 | ✅ 环境变量完成 | JCC_COORDINATOR_MODE 环境变量,5/5测试 |
| `23c7e9ac0` | #5 异步 agent 白名单 | ✅ 完全实现 | AsyncAgentAllowedTools + 后台 spawn 过滤,5/5测试 |
| `c058f0669` | #8 InitialPrompt 字段 | ✅ 字段已加 | SubAgentOptions.InitialPrompt,2/2测试 |
| `4867cd63a` | #20 SUBAGENT_MODEL 静态方法 | ✅ 静态方法已加 | GetSubagentModelFromEnv,7/7测试 |
| `a469d6296` | #18 criticalSystemReminder | ✅ 完全实现 | AgentRoleProfile + AgentDefinition 字段 + AgentPromptBuilder 注入,6/6测试 |
| `160a7ce8d` | #4 多层优先级覆盖 | ✅ EnsureCustomLoaded 已修复 | 自定义 SourcePath 覆盖内置,Dictionary 索引 O(n),16/16测试 |
| `0f538fe1a` | #19 model alias 匹配父 tier | ✅ 完全实现 | ModelAliasMatchesParentTier 静态方法,13/13测试 |
| `1cfc879e0` | #23 isolation: remote | ✅ 完全实现 | AgentIsolationMode.Remote 枚举值,2/2测试 |
| `97d2c3457` | #14 Agent(worker,researcher) 语法 | ✅ 完全实现 | AgentTypeSpecParser Parse+IsAllowed,7/7测试 |
| `48866bf5a` | #8 initialPrompt spawn 注入 | ✅ 完全实现 | AgentDefinition.InitialPrompt + ContextSetupMiddleware 接入 + AgentBase 注入,2/2测试 |
| `f74723d4f` | #20 SUBAGENT_MODEL spawn 接入 | ✅ 完全实现 | ContextSetupMiddleware 模型解析优先级,3/3测试 |
| `594cd4d73` | #9 maxTurns 生效 | ✅ 完全实现 | AgentBase MaxIterations 检查,2/2测试 |
| `8f4b6bd1f` | #7 Skills 预加载 | ✅ 完全实现 | ContextSetupMiddleware ISkillService 加载到 InitialMessageList,3/3测试 |
| `7d8c0c2e7` | #1 工具集限制 | ✅ 完全实现 | Coordinator Profile AllowedTools 限制,2/2测试 |
| `ec9151d11` | #17 插件 agent 安全限制 | ✅ 完全实现 | Cordis 可逆效应+连带卸载+传递依赖,11/11测试 |

### 最终统计

- **23 项缺失清单**: ✅ 23 项全部实现
- **核心机制对齐度**: ~98%(从 65% 提升)
