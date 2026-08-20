# 多Agent代码协作防冲突改造任务

> 基于 PRD v2.1 + jcc 现状调研 + Kimi Code 调研 + 用户逐条对齐决策
> 产出日期：2026-08-19（逐条对齐后重写）
> 改造策略：**渐进式、复用优先、TDD 驱动**

---

## 一、核心对齐结论（用户逐条确认）

1. **队长 = mainAgent**（不新建 Captain 类），**队员 = subAgent**（Worker/秘书都是 subAgent 变体）
2. **编译走消息队列**（`BuildQueueService` 已实现 bash 拦截），不需要专职 CompileWorker 角色
3. **热点识别取代文件锁**（文件锁死锁高），用"上报+计数阈值+队长收口"替代 `FileLock`/`BatchLock`
4. **广播复用现有 IMailbox 邮箱**，不新建 Broadcaster 类
5. **热文件变就广播**（热文件检测区分），不做双版本编译检测
6. **队长改热文件连带改所有调用点**（完整修复，秘书执行），队员只同步不修复，避免重复劳动+冲突循环
7. **队长秘书**：常驻 subAgent（复用 Teammate 变体），做杂活+记任务，队长专注决策
8. **任务派发复用现有 TodoWrite DAG + GoalGraphEngine**，不新建队列类
9. **角色权限**：先定权限矩阵（tools/disallowedTools 白名单+参数范围），再由权限系统过滤，push 限制是其中一条规则
10. **worktree 路径复用现状**（`.jcc/worktrees/{agentId}` + 分支 `worktree-{agentId}`），不改
11. **worktree 开启两层决策**：LLM 全局决策 `enableWorktree`（任务难度）+ 节点类型自动判断（`Variant==Code` 开、`Explore`/`Coordinator` 不开），探索/审查只读不改不开 worktree
12. **改造 /goal 为多Agent协作任务图引擎**（不新建命令、不加参数），复用现有 team MCP 组件，/goal 与 team MCP **共享底层组件，改一处两边都修**。单Agent = 团队只有1人的退化情况。现状 /goal 是"四不像"（既不像单agent loop，也不像能处理大型任务图+文件冲突），改造后明确为多Agent协作引擎

### 共享组件清单（/goal 与 team MCP 复用，改一处两边都修）

| 组件 | 路径 | 职责 | 改造方向 |
|------|------|------|----------|
| `ITeamManager`/`TeamManager` | `core/ai/Agents/src/Coordinator/Team/` | 团队/成员/消息管理（内存） | /goal 分解任务后建团队，节点派发给团队成员 |
| `IMailbox`/`TeammateMailboxService` | `core/ai/Agents/src/Coordinator/Core/Messaging/` | 中央邮箱（定向投递+跨进程） | 意图上报/契约广播/延迟邮件全走此通道 |
| `ITeammateInitService`/`TeammateInitService` | `core/ai/Agents/src/Coordinator/Team/` | teammate 上下文构建（团队+其他成员+允许路径） | /goal 派发节点时构建 sub-agent 的团队上下文 |
| `AgentWorktreeManager` | `core/ai/Agents/src/Coordinator/Core/Lifecycle/` | worktree 创建/清理 | T9.2 两层决策后按需创建 worktree |
| `ISubAgentCoordinator`/`AgentCoordinator` | `core/ai/Agents/src/Coordinator/Core/` | sub-agent 派生/停止/观察 | /goal 节点执行器调用 SpawnSubAgentAsync 派发 |
| `GoalGraphEngine` | `composition/Clock/src/Goal/Core/` | DAG 执行（DrainReadyBatch 就绪批次并行） | /goal 主引擎，节点执行从"自执行"改为"派发 sub-agent" |
| `WorktreeMergeService` | `core/ai/Agents/src/Services/Support/` | worktree 合并 | T6.0 队长串行合并复用 |
| `BuildQueueService` | `core/execution/Hands/src/Build/` | 编译队列（跨进程串行） | 所有 Agent 共享，已实现 |

---

## 二、改造任务清单（按依赖排序）

### 阶段 0：基础枚举与热文件检测

#### T0.3 ModifyIntent 枚举
- **新建** `foundation/Abstractions/00-core/Models/Agent/ModifyIntent.cs`：`{ InternalChange, ContractChange }` + `[EnumValue]`
- 热点识别的双意图基础，源码生成器自动生成 Extensions
- 验证：编译 Foundation.slnx + 枚举往返测试

#### T0.4 热文件检测器（通用，不限语言）
- **新建** `IHotFileDetector` + `HotFileDetector` 实现
- **启发式规则判断** `IsHotFile(path)`，通用支持 C#/Java/Python/JS/Go 等（不依赖目标项目的源码标记）
- 检测规则：
  - **目录约定**：`abstractions/`、`contracts/`、`foundation/`、`interfaces/`、`api/` 下的文件
  - **命名约定**：`I*.cs`（接口）、`*Enum*`、`*Constant*`、`*Base*`、`*Abstract*`、`__init__.py`、`index.ts`、`package-info.java`
  - **配置文件**：`*.json`/`*.yaml`/`*.toml` 配置、`settings.*`
  - **可配扩展**：用户/队长可配置额外热文件路径/模式（项目级 `.jcc/hotfiles.json`）
- `FrozenSet<string>` 缓存常用规则结果，线程安全
- 验证：编译 + 各语言热文件检测测试（C# 接口/Java 枚举/Python `__init__.py`/JS `index.ts`）

#### T0.5 FileLock 职责调整（保留单进程并发保护，多Agent冲突走热点识别）
- **调研结论**：FileLock/BatchLock 用于 FileWriter/FileEditor/DreamTaskPersistence/HighWaterMarkManager 等**单进程内文件操作并发保护**（防数据损坏），不是多Agent协作冲突防护
- **不删代码**：FileLock 保留用于单进程内并发写保护（防数据损坏），这是技术层面的正确用途
- **职责边界**：多Agent协作的文件冲突防护改用"热点识别+队长收口+worktree物理隔离"（T0.3-T1.4 已实现），不走 FileLock
- **保留** `BuildQueueService` 编译队列锁（防并发编译炸资源，这是资源防护非文件冲突防护）
- 验证：确认 FileLock 仅用于单进程并发写，不用于多Agent冲突防护

---

### 阶段 1：双意图上报与热点识别（取代文件锁）

#### T1.1 双意图数据模型
- **新建** `FileModifyIntent { FilePath, Intent, WorkerId, ReportedAt }`，线程安全记录

#### T1.2 IntentCollector
- **新建** `IIntentCollector` + `IntentCollector` 实现
- `ReportAsync(workerId, FileModifyIntent[])` 收集 Worker 上报
- `GetIntents(filePath)` 查询；`RemoveWorkerAsync(workerId)` 清理
- `ConcurrentDictionary` 线程安全

#### T1.3 HotSpotTracker
- **新建** `IHotSpotTracker` + `HotSpotTracker` 实现
- 双计数器：`contract_claim_count`/`internal_claim_count`（集合大小）
- 核心和非核心**统一计数阈值**判断（热文件阈值=1 即归队长，非核心阈值=3，可配）
- `internal_claim_count` 不触发热点（允许并行内部改）
- 队长修改不计入认领集合；会话结束清空

#### T1.4 热点触发后处置（通知+软中断+保留半成品+队长接管）
- 热点触发 → `IMailbox` 通知正在改该文件契约的 Worker
- Worker 主循环每轮生成前查邮箱（软检查点）→ 收到通知 → 停止当前契约改生成 → `git commit` 半成品到 worktree 分支 → 回消息给队长"请接管" → 继续改内部部分
- 队长收集半成品分支 → 在队长 worktree 用 `WorktreeMergeService` 合并 + 统一改完 + 自检 + push
- **前置防新**：队长派发新任务前查热点表，热点文件契约改自己揽，不派新 Worker

#### T1.5 扩展 TeammateMessageType（合并原 T1.5+T4.2）
- **改造** `TeammateMessageTypes.cs`：新增 `IntentReport`/`ContractChanged`/`ForceSync`/`DeferredMail` 四种消息类型
- Worker 通过 `IMailbox.SendAsync` 上报意图（不用 MCP 工具），异步不阻塞，跨进程支持
- 验证：编译 + 枚举测试

#### T1.6 执行中实时上报（新增）
- Worker 执行过程中遇到要改热文件 → 实时通过 `IMailbox` 发 `IntentReport` 给队长
- 与 T1.2 的启动时上报互补：启动时上报计划 + 执行中遇到热文件实时上报

#### T1.7 文件监控兜底（新增）
- 文件系统事件监听（带防抖窗口）：发现 Worker 私自改热文件未上报 → 告警/阻断
- 仅用于兜底纠错，不增加认领计数（PRD：上报制而非磁盘触发）

---

### 阶段 2：队长与任务派发

#### T2.1 扩展 mainAgent 能力（不新建 CaptainAgent 类）
- 在现有 mainAgent 派发流程中插入：热点检查 + 契约收口 + push 权限校验
- mainAgent 就是队长，复用 `ISubAgentCoordinator.SpawnSubAgentAsync` 派发 Worker

#### T2.2 复用 TodoWrite DAG + GoalGraphEngine 派发
- 任务表 = TodoWrite 的 DAG（`dependsOn` 表达依赖，`ownedFiles` 表达任务拥有文件）
- 派发调度 = `GoalGraphEngine.DrainReadyBatch`（就绪批次并行派发）
- 树状生长 = `GoalGraphEngine` 动态添加节点（Worker 嵌套派发子任务）
- `ownedFiles` 复用于热点识别（任务声明的拥有文件即计划修改文件）
- 派发前查热点表集成（热点文件契约改自己揽）

#### T2.4 队长秘书（新增）
- 常驻 subAgent，复用 `ExecutorVariant.Teammate` 变体 + system prompt 定义秘书职责
- 职责（杂活）：队长改热文件时找所有调用点+批量改+跑编译自检；整理任务表(DAG)；发广播邮件；记录任务状态
- 通信：队长通过 `IMailbox` 给秘书派活，秘书做完回结果
- 任务记忆：复用 `TodoWrite` DAG + `GoalState` 持久化
- 队长启动时 spawn 秘书常驻

#### T2.5 队长改热文件连带改所有调用点（新增）
- 队长改热文件（接口/枚举/公共签名）时，**用 CodeSemanticSearch + grep 找所有调用点，批量改**（完整修复）
- 秘书执行此杂活（T2.4）
- 队长改完接口+所有调用点 → 自检编译通过 → push → 广播
- 队员 pull 同步 → 编译已通过（队长已修复调用点）→ 继续自己任务，不重复修复

---

### 阶段 3：编译（已实现，无新任务）

- `BuildQueueService` 消息队列已实现：跨进程串行 + 源码指纹缓冲
- `ShellBuildInterceptMiddleware` bash 拦截已实现：所有 `dotnet build/test/publish` 转发队列
- 所有 Agent 共享同一编译队列，队列串行执行，不需要专职 CompileWorker
- 编译不做沙箱（jcc 是通用 coding agent，编译产物在 artifacts/ 不污染 worktree）
- **标记已完成，无新任务**

---

### 阶段 4：契约变更广播

#### T4.0 队长 push 热文件后广播（合并原 T4.1+T4.3+T4.4）
- 队长 push 热文件后 → 秘书通过 `IMailbox.SendAsync` 给依赖 Worker 发 `ContractChanged` 消息
- **热文件变就广播**（热文件检测区分），非热文件不广播
- **定向投递**给依赖该文件的 Worker，不全局广播
- 不做双版本编译检测（T4.1 删）——热文件变就广播，队员同步后自己编译验证
- 复用现有 `IMailbox`，不新建 Broadcaster 类

---

### 阶段 5：Worker 同步中断（合并为一个任务）

#### T5.0 Worker 主循环查邮箱 + 同步
- Worker 主循环每轮生成前查邮箱
- 收到 `ContractChanged` 消息 → `git pull` 同步主干 → **保留本地内部半成品**（不作废）→ 清除消息 → 继续
- pull 产生本地冲突时以主干为准
- 软中断非硬停（不杀 LLM 进程，下一生成检查点自行处理）

---

### 阶段 6：串行合并（合并为一个任务）

#### T6.0 队长串行处理合并队列
- 队长独占 push（T10 权限矩阵限制 Worker 禁 push）
- Worker 完成任务后提交产出到合并队列（不是 push）
- 队长串行处理：取一个 Worker 产出 → 提交 `BuildQueueService` 编译校验 → 通过则 `WorktreeMergeService` 合并到主干 → push → 通知队列内剩余 Worker 同步
- 编译/测试不通过拒绝合并
- 复用现有 `WorktreeMergeService` + `BuildQueueService`，薄包装队列调度

---

### 阶段 7：延迟邮件

#### T7.1 延迟邮件数据模型
- `DeferredMail { To, Subject, Body, OpenAfterTurns, Marker, CreatedAt }`
- `MailMarker` 枚举 `{ HotFileConflict, TestFileConflict, ResourceRefChange }`
- Marker 分类便于队员判断优先级（热文件冲突/测试文件冲突/资源引用变更）

#### T7.2 IDeferredMailService 延迟投递
- 延迟投递：邮件标记"将在 N 轮后自动打开，或任务结束之后注入"（默认 N=20）
- Worker 可继续当前任务稍后再看或立即查看，减少中断
- 复用 `IAgentNotificationQueue` 队列模式 + 轮次计数/任务结束钩子触发

#### T7.3 定向投递（不广播全局）
- 队长发给队员、队员发给队员，**发给对应的人，不广播全局**
- 复用 `IMailbox.SendAsync`（定向）而非 `BroadcastAsync`
- **中央邮箱解耦模式**（用户确认）：用户 → mainAgent → 中央邮箱 → subAgent；subAgent → 中央邮箱 → subAgent
  - 所有通信经中央邮箱中转，发件方投到邮箱就完事，收件方轮询/通知取
  - 不需要点对点直连，不需要 mainAgent 手动转发，中央邮箱自动路由
  - jcc 现有 `IMailbox`/`TeammateMailboxService` 已经是中央邮箱模式（`SendAsync` 指定 `ToAgentId`，`ReceiveAsync` 按 `AgentId` 取，文件邮箱支持跨进程）
  - **零扩展**：subAgent 间通信 = subAgent A `SendAsync(To=B)` → 中央邮箱 → subAgent B `ReceiveAsync`

---

### 阶段 8：goal 命令改造（改造 /goal 为多Agent协作引擎）

> **改造方向**（用户确认）：/goal 从"单Agent DAG loop"升级为"多Agent协作任务图引擎+文件冲突防护"。不加参数、不新建命令，/goal 直接就是多Agent协作的（单Agent = 团队只有1人的退化情况）。复用上方共享组件清单的 team MCP 组件，/goal 与 team MCP 共享底层，改一处两边都修。
>
> **核心改动点**：`GoalGraphEngine.ExecuteViaAgentServiceAsync`（`GoalGraphEngine.cs:390-403`）的节点执行器，从"当前 agent 自执行"改为"通过 `ISubAgentCoordinator.SpawnSubAgentAsync` 派发 sub-agent + `ITeamManager` 建团队 + `ITeammateInitService` 构建上下文"。单Agent任务时团队只有队长1人，退化为自执行。

#### T8.1 GoalSpecPromptBuilder（维持现状，无新任务）
- 现有固定 6 字段（Outcome/Verification/Constraints/Boundaries/IterationLog/FailureCircuit）设计已精美，维持现状
- **标记已完成，无新任务**

#### T8.2 任务表/任务清单文档生成（保留并深化）
- 从 GoalEngine 分解结果产出结构化任务表.md，**深化方向**（用户确认）：
  - 任务表字段：任务编号/描述/**涉及文件**/**修改意图**/**负责角色**/依赖/**验证方式**/**是否涉及热文件/热点标注**
  - **增量更新**：任务状态变化时更新文档（非一次性生成）
  - **热点识别集成**：标注哪些任务涉及热文件/热点，队长提前收口
- 任务表作为 T9.2 worktree 决策的输入（LLM 读任务表判断 enableWorktree）

#### T8.3 goal 命令与任务派发联动（接入 team 组件）
- goal 分解后 → 生成任务表.md → 队长读取 → T2.2 派发 Worker
- **接入 team 组件**：`GoalGraphEngine` 节点执行器调用 `ITeamManager.CreateTeamAsync` 建团队 → `ISubAgentCoordinator.SpawnSubAgentAsync` 派发节点为 sub-agent → `ITeammateInitService.BuildInitContextAsync` 构建每个 sub-agent 的团队上下文（团队ID+其他成员+允许路径）
- **共享组件**：/goal 与 team MCP 共用 `ITeamManager`/`IMailbox`/`ITeammateInitService`，改一处两边都修
- 参考 Kimi `/goal next` 任务队列模式（当前完成自动开始下一个）

---

### 阶段 9：goal 命令 worktree 自动决策

#### T9.2 LLM 决策 + Variant==Code 自动开 worktree（两层）
- **现状缺口**：只有 cluster_expand 模板显式设 `IsolationMode=Worktree`（`GoalGraphTemplates.cs:520`），其他 5 个预置模板（refactor/bugfix/research/codereview/testgen）默认 `None` 不开 worktree；`GoalNodePayload.IsolationMode` 默认 `None`（`GoalNodePayload.cs:21`）
- **第一层 LLM 全局决策**：`/goal <任务>` → LLM 分解任务填 TODO → LLM 根据任务难度（TODO 数量/涉及热文件数/预估并行度）判断 `enableWorktree` 全局开关
  - 开启 = 任务大，多 agent 并行 + 各自 worktree 物理隔离
  - 不开启 = 小任务，单 agent 顺序执行即可
  - 选择权交给 LLM，阈值可配（如 TODO≥3 或涉及热文件≥1 才开）
- **第二层节点类型自动判断**：若全局开，则按 `Variant==Code` 的改代码节点开 worktree，`Explore`（只读探索）/`Coordinator`（审查协调）不开
  - 规则：`IsolationMode = (enableWorktree && Variant == ExecutorVariant.Code) ? Worktree : None`
  - 依据：探索/审查只读不改，开 worktree 纯浪费；改代码节点需要物理隔离防冲突
- **实现位置**：`GoalGraphEngine.ExecuteViaAgentServiceAsync`（`GoalGraphEngine.cs:390-403`）派发时按规则注入 `AgentSpawnOptions.IsolationMode`，一处修复覆盖所有模板
- **复用现状**：worktree 路径 `.jcc/worktrees/{agentId}` + 分支 `worktree-{agentId}`（`WorktreeSpawnMiddleware` 已实现按需创建）；树状生长（嵌套 subagent 自动开子 worktree）；任务信息通过 `AgentSpawnOptions.Prompt` 传递（不污染队长上下文）
- **DAG 依赖正确性**：`GoalGraphEngine.DrainReadyBatch` 按就绪批次派发，前置完成才开后置 worktree，不空等不浪费
- 验证：编译 + 各模板 worktree 开启规则测试（refactor 的 implement/commit 开、explore/review 不开；bugfix 的 reproduce/fix 开、locate/verify 不开；research/codereview 全不开；testgen 的 write_tests 开、run_tests 不开）

---

### 阶段 10：角色权限（已实现，无新任务）

- jcc 权限系统已完善：`AgentDefinitionProvider` 为每个角色/变体定义 `Tools` 白名单 + `DisallowedTools` 黑名单，`AgentRestrictionMiddleware` + `PermissionAwareToolExecutor` + `ToolFilterPolicy` 三层过滤
- **push 限制**：T6.0 串行合并已隐含"队长独占 push，Worker 不 push"（Worker 产出进合并队列不直接 push），如需显式可在 Worker `DisallowedTools` 加 `GitPush`（工具级，已有能力）
- **热文件限制**：由热点识别系统（T1.3/T1.4）+ 热文件检测（T0.4）运行时动态限制，不进静态权限矩阵
- **秘书权限**：复用 subagent 变体 profile 机制，队长赋予杂活工具（T2.4 覆盖）
- 不新建参数级/文件路径级权限框架
- **标记已完成，无新任务**

---

### 阶段 11：工具超图扩展（已删除，无新任务）

- 原提议新增 `agent_coordination` 超边建模队长↔Worker↔秘书关联
- **删除理由**（用户确认）：
  1. 语义不符：现有 `ToolHyperedge` 建模**工具链**（read→edit→write），非 Agent 协作关系
  2. Agent 协作顺序归 DAG（`GoalGraphEngine`）管，不需要超图
  3. Agent 协作关系固定（队长派发 Worker、指挥秘书），不需要"链路推荐"
  4. AGENTS.md 规则1：超图管评分共享+链路推荐，Agent 协作属执行顺序范畴，归 DAG
- **标记已删除，无新任务**

---

## 三、严格禁止的错误逻辑

| # | ❌ 禁止 | ✅ 正确 |
|---|--------|--------|
| 1 | 等磁盘文件被修改后再统计热点 | 任务上报携带修改意图即统计热度 |
| 2 | Worker 本地直接调用 dotnet build 绕过队列 | 全部编译提交 BuildQueueService |
| 3 | 队长分发广播消息（亲自发） | 秘书通过 IMailbox 发广播 |
| 4 | 内部/注释改动触发广播 | 热文件变才广播（热文件检测） |
| 5 | 多 Agent 并行合并到主干 | 队长串行合并，一次一个 |
| 6 | Worker 直接 push 到主干 | Worker 只读主干，仅队长可 push |
| 7 | 队长编译未通过就 push 并广播 | 队长自检通过后才能 push |
| 8 | 契约热点触发后一刀切禁止 Worker 所有修改、作废内部半成品 | 仅回收契约修改权限，内部修改允许继续，保留内部半成品 |
| 9 | 队长只改接口不改调用点（让队员擦屁股） | 队长改热文件连带改所有调用点（完整修复） |
| 10 | 用文件锁防并发写（死锁高） | 用热点识别+队长收口+worktree 隔离替代 |

---

## 四、风险点与应对

| 风险 | 应对 |
|------|------|
| 移除 FileLock 后并发写防护依赖热点识别准确性 | 文件监控兜底（T1.7）+ worktree 物理隔离 + 串行合并 |
| 队长改热文件连带改调用点工作量大 | 秘书执行（T2.4），用 CodeSemanticSearch 自动找调用点 |
| 热'点阈值需调参（核心=1，非核心=3） | 配置化，会话级可调 |
| 多 Agent 并发上报意图线程安全 | `ConcurrentDictionary` + 集合大小作 claim_count |
| 队员间通信（延迟邮件"队员发给队员"） | 中央邮箱解耦：subAgent → 中央邮箱 → subAgent，复用现有 IMailbox，零扩展 |

---

## 五、重构单元分组与实施顺序

按相关性将 20 个代码任务整合为 **6 个重构单元**，每个单元内任务紧密相关可一起重构：

### 单元 A：热点识别核心（取代文件锁）

| 任务 | 内容 | 角色 |
|------|------|------|
| T0.3 | ModifyIntent 枚举（InternalChange/ContractChange） | 基础枚举 |
| T0.4 | IHotFileDetector 热文件检测（通用不限语言） | 基础检测 |
| T1.1 | FileModifyIntent 数据模型 | 数据载体 |
| T1.2 | IntentCollector（收集上报） | 收集器 |
| T1.3 | HotSpotTracker（双计数器+阈值） | 识别器 |
| T1.4 | 热点触发处置（通知+软中断+队长接管） | 处置逻辑 |
| T0.5 | 移除 FileLock/BatchLock（收尾） | 收尾 |

**内聚理由**：T0.3 是双意图基础，T0.4 是核心/非核心判断依据，T1.1-T1.3 是识别主链路，T1.4 是触发处置，T0.5 移除文件锁是热点识别替代的收尾。

### 单元 B：意图上报与广播通信

| 任务 | 内容 | 角色 |
|------|------|------|
| T1.5 | 扩展 TeammateMessageType（IntentReport/ContractChanged/ForceSync/DeferredMail） | 通信基础 |
| T1.6 | 执行中实时上报 IntentReport | 上报 |
| T1.7 | 文件监控兜底（防漏报） | 兜底 |
| T4.0 | 队长 push 后广播 ContractChanged | 广播 |

**内聚理由**：T1.5 扩展消息类型是 T1.6/T4.0/T1.7 的通信基础，共用 `IMailbox` 通道。

### 单元 C：队长与派发

| 任务 | 内容 | 角色 |
|------|------|------|
| T2.1 | 扩展 mainAgent（热点检查+契约收口+push校验） | 队长能力 |
| T2.2 | 复用 TodoWrite DAG + GoalGraphEngine 派发 | 派发机制 |
| T2.4 | 队长秘书（常驻 subAgent） | 队长助手 |
| T2.5 | 队长改热文件连带改所有调用点 | 秘书执行 |

**内聚理由**：T2.1 队长核心能力，T2.2 派发机制，T2.4 秘书常驻，T2.5 秘书执行连带改——都是"队长"主题。

### 单元 D：Worker 同步与串行合并

| 任务 | 内容 | 角色 |
|------|------|------|
| T5.0 | Worker 主循环查邮箱 + pull 同步保留半成品 | Worker 同步 |
| T6.0 | 队长串行处理合并队列 | 串行合并 |

**内聚理由**：T5.0 Worker 收 ContractChanged 后 pull 同步，T6.0 产出进合并队列队长串行合并——都是合并阶段。

### 单元 E：延迟邮件

| 任务 | 内容 | 角色 |
|------|------|------|
| T7.1 | DeferredMail 模型 + MailMarker 枚举 | 数据模型 |
| T7.2 | IDeferredMailService 延迟投递 | 投递服务 |
| T7.3 | 定向投递（复用 IMailbox 中央邮箱，零扩展） | 定向投递 |

**内聚理由**：T7.1-T7.3 配套，T7.3 零扩展主要是确认复用现有中央邮箱。

### 单元 F：goal 与 worktree 决策

| 任务 | 内容 | 角色 |
|------|------|------|
| T8.2 | 任务表深化（涉及文件/修改意图/负责角色/依赖/验证/热点标注+增量更新） | 任务表生成 |
| T8.3 | goal→任务表→派发联动 | goal 联动 |
| T9.2 | LLM 决策 enableWorktree + Variant==Code 自动开 worktree | worktree 决策 |

**内聚理由**：T8.2 任务表是 T8.3/T9.2 的输入，T9.2 依赖 T8.2 任务表（LLM 读任务表判断 enableWorktree）+ 单元 C 的 T2.2 派发。

### 依赖顺序

```
A（热点识别核心） → B（通信） → C（队长派发） → D（同步合并）
                                    ↓
                                    F（goal/worktree，依赖 C 的 T2.2 派发）

E（延迟邮件，相对独立，可在 A 之后任意插入）
```

- **A 必须最先**：T0.4 热文件检测被 B/C/F 依赖
- **E 相对独立**：可在 A 之后任意插入
- **F 依赖 C**：T9.2 worktree 决策依赖 T2.2 派发机制
- 每单元独立编译+测试+提交，单元内任务一起重构

阶段3（编译）、阶段10（角色权限）、阶段11（超图）已实现/已删除，无新任务。

---

## 六、业务闭环

### 核心流程：串行-并行-串行-并行交替

```
① 串行：subAgent 上报要改什么（双意图：InternalChange/ContractChange）
② 串行：队长先改热文件（连带改所有调用点，完整修复）+ 自检 + push
③ 并行：队员 pull 同步主干 → 各自改剩下的（内部修改自由并行）
④ 串行：队员完成 → 产出进合并队列 → 队长串行合并（一次一个，编译校验）
⑤ 并行：下一批任务派发（DAG 就绪批次并行）
... 循环
```

- **串行段**：上报收集、队长改热文件、队长串行合并——需要全局协调，串行防冲突
- **并行段**：队员改内部、下一批任务——worktree 物理隔离，并行提效
- 热点识别决定"哪些归队长串行改"（热文件契约改）vs"哪些队员并行改"（内部改）
- 编译走消息队列防资源爆炸，worktree 隔离防文件冲突

### 一句话闭环

Worker 上报双意图 → 热点识别（取代文件锁）→ 热文件契约改队长串行收口、内部改队员自由并行 → 队长改热文件连带改所有调用点（完整修复）+ 自检 + push → 热文件变广播给依赖 Worker → Worker pull 同步保留内部半成品 → 编译走消息队列防资源爆炸 → 队长串行合并防冲突爆炸。

---

## 七、Kimi Code 调研结论摘要

- Kimi Code 验证"队长=mainAgent，队员=subAgent"对齐
- Kimi 用 custom agent(Markdown) + tools/disallowedTools/subagents 白名单定义角色
- Kimi `/goal next` 是任务队列模式（当前完成自动开始下一个）
- **Kimi 没有 PRD 那套冲突治理**（无广播/热点/串行合并），靠上下文隔离+权限白名单+自动调度
- PRD 冲突治理是视频作者方法论，本改造采用"热点识别取代文件锁+队长收口"的混合方案

---

<!-- 🤖 Auto Decision: 2026-08-19 逐条对齐后重写 -->
<!-- 决策: 删T0.1/T0.2/T3.1/T3.2/T3.4/T4.1/T8.1/T10.0/T11.1, 保留热点识别取代文件锁, 编译走消息队列, 队长秘书常驻, 队长改核心连带改调用点, 广播复用邮箱, 任务派发复用DAG -->
<!-- 原因: 用户逐条对齐,文件锁死锁高用热点识别替代,编译已实现bash拦截,队长太忙配秘书 -->
<!-- 验证: 全部任务逐条对齐完成,进入实施 -->

<!-- 🤖 Auto Decision: 2026-08-20 T0.4 改为热文件检测 + 串行-并行流程 -->
<!-- 决策: 删[CoreFile]特性标记+源码生成器扫描(C#专属不通用), 改为IHotFileDetector启发式规则检测(目录/命名/配置/可配,通用不限语言); "核心文件"统一改"热文件"; MailMarker.CoreFileConflict改HotFileConflict; 业务闭环加串行-并行-串行-并行交替流程 -->
<!-- 原因: jcc是通用agent不支持Java/Python的源码标记,用热文件检测替代; 用户明确串行(队长改热文件)-并行(队员改内部)-串行(合并)-并行(下一批)交替 -->
<!-- 验证: 文档更新,全部[CoreFile]引用已清除,待提交 -->

<!-- 🤖 Auto Decision: 2026-08-20 改造 /goal 为多Agent协作引擎 -->
<!-- 决策: 不新建/team斜杠命令、不加参数, 改造/goal本身从"单Agent DAG loop"升级为"多Agent协作任务图引擎+文件冲突防护"; 复用现有team MCP组件(ITeamManager/IMailbox/ITeammateInitService等), /goal与team MCP共享底层组件改一处两边都修; 单Agent=团队只有1人的退化情况 -->
<!-- 原因: 用户指出/goal现状是"四不像"(既不像单agent loop也不像能处理大型任务图+文件冲突); 现有team MCP已有完整组件(ITeamManager+IMailbox+TeammateInitService), /goal只需接入而非重造; 用户不喜欢加参数; 共享组件让两边错误一起修 -->
<!-- 替代方案: 新建/team斜杠命令(用户否决,想改造/goal); /goal加--collab参数(用户否决,不喜欢参数) -->
<!-- 验证: 待编译+提交 -->

---

## 八、实施进度与集成说明

### 已完成组件（纯新增 + 集成，150测试全绿，零破坏）

| 单元 | 任务 | 组件 | 测试数 |
|------|------|------|--------|
| A | T0.3 | ModifyIntent 枚举 | — |
| A | T0.4 | IHotFileDetector + HotFileDetector | 37 |
| A | T1.1 | FileModifyIntent DTO | — |
| A | T1.2 | IIntentCollector + IntentCollector | 12 |
| A | T1.3 | IHotSpotTracker + HotSpotTracker | 15 |
| A | T1.4 | IHotSpotResolutionPolicy + 实现 | 7 |
| A | T0.5 | FileLock 职责调整（文档） | — |
| B | T1.5 | TeammateMessageType 扩展4类型 | — |
| B | T1.6 | IIntentReporter + IntentReporter | 7 |
| B | T1.7 | IHotFileWatchdog + HotFileWatchdog | 8 |
| B | T4.0 | IContractChangeBroadcaster + 实现 | 6 |
| C | T2.1 | ICaptainDispatchGuard + CaptainDispatchGuard | 5 |
| C | T2.2 | GoalGraphEngine 派发接入热点守卫 | 3 |
| C | T2.4 | AgentCoordinator EnsureSecretaryAsync 秘书常驻 | 6 |
| C | T2.5 | ICallSiteFinder + CallSiteFinder + CodeCallSite | 3 |
| D | T5.0 | AgentBase ContractChangeNotifications 队列消费 | 5 |
| D | T6.0 | IMergeQueueService + MergeQueueService | 7 |
| E | T7.1 | DeferredMail + MailMarker | — |
| E | T7.2 | IDeferredMailService + DeferredMailService | 7 |
| E | T7.3 | 定向投递（零扩展，复用 IMailbox） | — |
| F | T8.2 | ITaskTableGenerator + TaskTableGenerator | 7 |
| F | T8.3 | GoalGraphEngine 接入 ITeamManager 建团队 | 3 |
| F | T9.2 | IWorktreeDecisionPolicy + WorktreeDecisionPolicy | 16 |

### 待集成任务

✅ **全部完成** — 所有纯新增组件和集成任务已实现，150测试全绿，零破坏。

### 集成顺序（已全部完成）

1. ✅ **T2.2 DAG派发** → 2. ✅ **T8.3 goal联动** → 3. ✅ **T2.4 秘书spawn** → 4. ✅ **T5.0 Worker同步**
