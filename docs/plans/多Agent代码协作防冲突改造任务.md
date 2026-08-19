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
5. **核心文件变就广播**（`[CoreFile]` 标记区分），不做双版本编译检测
6. **队长改核心文件连带改所有调用点**（完整修复，秘书执行），队员只同步不修复，避免重复劳动+冲突循环
7. **队长秘书**：常驻 subAgent（复用 Teammate 变体），做杂活+记任务，队长专注决策
8. **任务派发复用现有 TodoWrite DAG + GoalGraphEngine**，不新建队列类
9. **角色权限**：先定权限矩阵（tools/disallowedTools 白名单+参数范围），再由权限系统过滤，push 限制是其中一条规则
10. **worktree 路径复用现状**（`.jcc/worktrees/{agentId}` + 分支 `worktree-{agentId}`），不改
11. **worktree 开启两层决策**：LLM 全局决策 `enableWorktree`（任务难度）+ 节点类型自动判断（`Variant==Code` 开、`Explore`/`Coordinator` 不开），探索/审查只读不改不开 worktree

---

## 二、改造任务清单（按依赖排序）

### 阶段 0：基础枚举与标记

#### T0.3 ModifyIntent 枚举
- **新建** `foundation/Abstractions/00-core/Models/Agent/ModifyIntent.cs`：`{ InternalChange, ContractChange }` + `[EnumValue]`
- 热点识别的双意图基础，源码生成器自动生成 Extensions
- 验证：编译 Foundation.slnx + 枚举往返测试

#### T0.4 静态核心文件标记
- **新建** `CoreFileAttribute.cs` + `ICoreFileRegistry` + `CoreFileRegistry` 实现
- `[CoreFile]` 特性标记 Entity/Enum/Constant/Interface/Configuration/BaseAbstract
- `ICoreFileRegistry.IsCoreFile(path)`/`GetAllCoreFiles()`，`FrozenSet<string>` 缓存，源码生成器扫描收集
- 验证：编译 + 标记文件查询测试

#### T0.5 移除 FileLock/BatchLock，用热点识别替代
- **改造**：移除/弱化现有 `infrastructure/Infrastructure/AsyncFileLock/` 的 `FileLock`/`BatchLock` 使用
- 文件并发写防护改为"热点识别+队长收口"（阶段1）+ worktree 物理隔离
- **保留** `BuildQueueService` 编译队列锁（防并发编译炸资源，这是资源防护非文件冲突防护）
- 验证：编译 + 确认无 FileLock 死锁路径

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
- 核心和非核心**统一计数阈值**判断（核心文件阈值=1 即归队长，非核心阈值=3，可配）
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
- Worker 执行过程中遇到要改核心文件 → 实时通过 `IMailbox` 发 `IntentReport` 给队长
- 与 T1.2 的启动时上报互补：启动时上报计划 + 执行中遇到核心文件实时上报

#### T1.7 文件监控兜底（新增）
- 文件系统事件监听（带防抖窗口）：发现 Worker 私自改核心文件未上报 → 告警/阻断
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
- 职责（杂活）：队长改核心文件时找所有调用点+批量改+跑编译自检；整理任务表(DAG)；发广播邮件；记录任务状态
- 通信：队长通过 `IMailbox` 给秘书派活，秘书做完回结果
- 任务记忆：复用 `TodoWrite` DAG + `GoalState` 持久化
- 队长启动时 spawn 秘书常驻

#### T2.5 队长改核心文件连带改所有调用点（新增）
- 队长改核心文件（接口/枚举/公共签名）时，**用 CodeSemanticSearch + grep 找所有调用点，批量改**（完整修复）
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

#### T4.0 队长 push 核心文件后广播（合并原 T4.1+T4.3+T4.4）
- 队长 push 核心文件后 → 秘书通过 `IMailbox.SendAsync` 给依赖 Worker 发 `ContractChanged` 消息
- **核心文件变就广播**（`[CoreFile]` 标记区分），非核心不广播
- **定向投递**给依赖该文件的 Worker，不全局广播
- 不做双版本编译检测（T4.1 删）——核心文件变就广播，队员同步后自己编译验证
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
- `MailMarker` 枚举 `{ CoreFileConflict, TestFileConflict, ResourceRefChange }`
- Marker 分类便于队员判断优先级（核心文件冲突/测试文件冲突/资源引用变更）

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

### 阶段 8：goal 命令改造

#### T8.1 GoalSpecPromptBuilder（维持现状，无新任务）
- 现有固定 6 字段（Outcome/Verification/Constraints/Boundaries/IterationLog/FailureCircuit）设计已精美，维持现状
- **标记已完成，无新任务**

#### T8.2 任务表/任务清单文档生成（保留并深化）
- 从 GoalEngine 分解结果产出结构化任务表.md，**深化方向**（用户确认）：
  - 任务表字段：任务编号/描述/**涉及文件**/**修改意图**/**负责角色**/依赖/**验证方式**/**是否涉及核心文件/热点标注**
  - **增量更新**：任务状态变化时更新文档（非一次性生成）
  - **热点识别集成**：标注哪些任务涉及核心文件/热点，队长提前收口
- 任务表作为 T9.2 worktree 决策的输入（LLM 读任务表判断 enableWorktree）

#### T8.3 goal 命令与任务派发联动
- goal 分解后 → 生成任务表.md → 队长读取 → T2.2 派发 Worker
- 参考 Kimi `/goal next` 任务队列模式（当前完成自动开始下一个）

---

### 阶段 9：goal 命令 worktree 自动决策

#### T9.2 LLM 决策 + Variant==Code 自动开 worktree（两层）
- **现状缺口**：只有 cluster_expand 模板显式设 `IsolationMode=Worktree`（`GoalGraphTemplates.cs:520`），其他 5 个预置模板（refactor/bugfix/research/codereview/testgen）默认 `None` 不开 worktree；`GoalNodePayload.IsolationMode` 默认 `None`（`GoalNodePayload.cs:21`）
- **第一层 LLM 全局决策**：`/goal <任务>` → LLM 分解任务填 TODO → LLM 根据任务难度（TODO 数量/涉及核心文件数/预估并行度）判断 `enableWorktree` 全局开关
  - 开启 = 任务大，多 agent 并行 + 各自 worktree 物理隔离
  - 不开启 = 小任务，单 agent 顺序执行即可
  - 选择权交给 LLM，阈值可配（如 TODO≥3 或涉及核心文件≥1 才开）
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
- **核心文件限制**：由热点识别系统（T1.3/T1.4）+ `[CoreFile]` 标记（T0.4）运行时动态限制，不进静态权限矩阵
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
| 4 | 内部/注释改动触发广播 | 核心文件变才广播（[CoreFile] 标记） |
| 5 | 多 Agent 并行合并到主干 | 队长串行合并，一次一个 |
| 6 | Worker 直接 push 到主干 | Worker 只读主干，仅队长可 push |
| 7 | 队长编译未通过就 push 并广播 | 队长自检通过后才能 push |
| 8 | 契约热点触发后一刀切禁止 Worker 所有修改、作废内部半成品 | 仅回收契约修改权限，内部修改允许继续，保留内部半成品 |
| 9 | 队长只改接口不改调用点（让队员擦屁股） | 队长改核心文件连带改所有调用点（完整修复） |
| 10 | 用文件锁防并发写（死锁高） | 用热点识别+队长收口+worktree 隔离替代 |

---

## 四、风险点与应对

| 风险 | 应对 |
|------|------|
| 移除 FileLock 后并发写防护依赖热点识别准确性 | 文件监控兜底（T1.7）+ worktree 物理隔离 + 串行合并 |
| 队长改核心文件连带改调用点工作量大 | 秘书执行（T2.4），用 CodeSemanticSearch 自动找调用点 |
| 热'点阈值需调参（核心=1，非核心=3） | 配置化，会话级可调 |
| 多 Agent 并发上报意图线程安全 | `ConcurrentDictionary` + 集合大小作 claim_count |
| 队员间通信（延迟邮件"队员发给队员"） | 中央邮箱解耦：subAgent → 中央邮箱 → subAgent，复用现有 IMailbox，零扩展 |

---

## 五、渐进式实施顺序

```
阶段0（枚举+标记+移除文件锁） → 阶段1（双意图+热点） → 阶段2（队长+秘书+派发） → 阶段4（广播）
                                                                        ↓
                                              阶段5（Worker同步） → 阶段6（串行合并）
                                                                        ↓
                                              阶段7（延迟邮件） → 阶段8（goal+任务表） → 阶段9（worktree决策）
```

阶段3（编译）、阶段10（角色权限）、阶段11（超图）已实现/已删除，无新任务。每阶段独立编译+测试+提交。

---

## 六、业务闭环

Worker 上报双意图 → 热点识别（取代文件锁）→ 多人抢契约则队长收口、多人改内部自由并行 → 队长改核心文件连带改所有调用点（完整修复）+ 自检 + push → 核心文件变广播给依赖 Worker → Worker pull 同步保留内部半成品 → 编译走消息队列防资源爆炸 → 队长串行合并防冲突爆炸。

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

<!-- 🤖 Auto Decision: 2026-08-20 T7/T8/T9.2/T10.0/T11.1 对齐完成 -->
<!-- 决策: T7.1-T7.3保留(中央邮箱解耦零扩展), T8.1删(6字段已精美), T8.2深化(涉及文件/修改意图/负责角色/依赖/验证/热点标注+增量更新+热点集成), T8.3保留, T9.2两层(LLM全局决策enableWorktree+Variant==Code自动开), T10.0删(权限已完善,push限制工具级,核心文件限制交热点识别), T11.1删(语义不符,Agent协作归DAG) -->
<!-- 原因: 用户逐条确认,中央邮箱已解耦不需要扩展路由,6字段设计精美,worktree改代码才开探索审查不开,权限系统已完善不新建框架,超图建模工具链非Agent协作 -->
<!-- 验证: 文档更新提交,全部任务对齐完成,可进入阶段0代码实施 -->
