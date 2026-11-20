# TASK062 — 引擎侧问题登记与守卫强化（子代理执行链缺陷后续）

> 状态：P1–P6 已全部修复并守卫、P7/P8 悬置（低优）（2026-11-21 回填 §1 总表状态列；修复记录见 §2/§3，早于本次回填即已完成，本次仅消除总表与验收表的状态不一致）
> 回填依据：`10ba6e250`（P2+P3 AgentMessageRouteLoop，守卫测试 4/4 + agents.tests 632）；P4 `AgentToolNameEnumConstants`（两个 E2E 脚本）；
> P5 `TeammatePaneMiddleware.cs:33` 角色守卫 `isTeammate`；P6 `ci-e2e.yml:85` matrix 已含两个 SubAgentEvent E2E。
> P7 仍 `tokenGain: 0`（`SessionController.cs:511-517`）、P8 未加 `_started` 门闩——确未实施，保持悬置。
> 来源：DSG041 集成验收过程中暴露的引擎侧缺陷与缺陷模式，按"先记录所有问题，再修复；修复加入守卫和单一职责使问题更容易暴露"方法论推进
> 关联：DSG041（docs/proposed/design/DSG041-subagent-event-source-plan.md）、TASK043（流式增量透传缺陷）

## 1. 问题登记总表

| # | 问题 | 性质 | 状态 | 守卫方案 |
|---|------|------|------|---------|
| P1 | ~~ChatService 路径子代理 LLM 流无 AgentId 透传主流~~ **误诊修正**：真实根因=①E2E 未等插件就绪（八插件后台化后 kernel.Plugins 挂载延迟→agent 工具 not found 短路→无子代理事件、主 LLM 二轮直拿 turn2 文本——"串话"假象）②跨 class 并行污染（同 testhost 两引擎 session 进程级干扰→turn 被跨测试消耗）。修复=Completion 等待（通知语义）+ [Collection] 串行 + fork 断言对齐 teammate 演进 | 误诊→已修 | ✅ 3/3 全绿（含 CI 白名单增补） | E2E 断言守护 |
| P2 | **邮箱 ReceiveAsync 漏传 ct 模式**（已修三处：AgentServiceImpl/SwarmPermissionMessageRouter/PlanApprovalMessageRouter）——"ct 挂在参数上但没传给底层"是静默缺陷，Dispose 挂死才暴露 | 缺陷模式（防再犯） | ✅ 已修 | **路由器取消收尾测试**（TDD 防回归）+ 单一职责提取 |
| P3 | **路由循环内嵌大类**——RouteWorkerPermissionResponsesAsync 等三个消息路由循环内嵌在 AgentServiceImpl（900+ 行）等大类里，取消语义/生命周期/分发逻辑耦合，缺陷难以局部暴露 | 结构性风险 | ✅ 已守卫（`10ba6e250`） | 提取 `AgentMessageRouteLoop` 独立类 + ct 构造契约 + 取消收尾测试 4/4 |
| P4 | **E2E/测试工具名 magic string**——SubAgentEventStreamE2ETests/MainAgentPath E2E 脚本手写 "Agent"/"agent_spawn" 均错（真值 `agent`，AgentToolName [EnumValue]）——两轮排障才定位 | 可维护性 | ✅ 已守卫 | 两 E2E 脚本改引用 `AgentToolNameEnumConstants.Agent` 常量（单一数据源） |
| P5 | **TeammatePaneMiddleware 对非 teammate 子代理也尝试创建 tmux pane**——executor 前台子代理 spawn 也走 pane 创建（Windows 无 tmux 每次警告噪音）；判定条件只查 IsMainAgent 不查角色 | 设计缺陷（非致命） | ✅ 已修 | `TeammatePaneMiddleware.cs:33` 加 `isTeammate` 角色守卫（Variant==Teammate 才建 pane） |
| P6 | **两处子代理 E2E 不在 CI 白名单**——ci-e2e matrix 白名单式 filter 从未含 SubAgentEventStreamE2ETests——缺陷长期潜伏（上次手动绿在 #435/#437 大改前） | CI 盲区 | ✅ 已守卫 | `ci-e2e.yml:85` matrix 已含 `SubAgentEventStreamE2ETests\|SubAgentEventMainAgentPathE2ETests` |
| P7 | **tokenGain 恒 0**（DSG041 遗留）——子代理事件无 token 增量数据源（ChatStreamEvent 无该字段；ProgressTracker 差值未接） | 功能缺口（低优） | ⏳ 悬置 | 后续接 ProgressTracker.RecordTokenUsage 差值 |
| P8 | **McpPluginBootstrapHandle.Completion 的 Run 前占位语义**——Run 未调用时 Completion=Task.CompletedTask，消费方误判"就绪"（DI 有 Handle 但 McpInitModule 未跑 ConfigureAsync 的极端组装） | 边界语义 | ⏳ 待守卫 | 文档化契约 + 可选 _started 门闩（等待方阻塞至 Run 或超时） |

## 2. 修复记录

### P2+P3 守卫（第一批）：路由器取消收尾测试 + 单一职责

- **TDD 守卫**：三路由器的"取消后可在时限内收尾"单元测试（无消息场景 ct 取消 → 循环退出 → Dispose 完成——挂死形态回归即红）
- **单一职责**：消息路由循环提取 `AgentMessageRouteLoop` 独立小类（构造注入 broker+agentId+处理器；Start/Stop 生命周期显式；取消令牌单一持有）——AgentServiceImpl/Swarm 两路由器消费该类，ct 传递成为构造契约（编译期可见，漏传即编译错）
- 验收：守卫测试绿 + agents.tests 全量回归

### P4 守卫：E2E 工具名常量化

- 两个 E2E 脚本的 `"tool_name": "agent"` 手写 JSON 段改经 `AgentToolNameEnumConstants.Agent` 拼接（单一数据源——枚举改名/值变即编译错而非 E2E 静默 not found）

### P6 守卫：CI matrix 增补

- ci-e2e.yml matrix 增补 `SubAgentEventStreamE2ETests|SubAgentEventMainAgentPathE2ETests` 条目（当前红——P1 修复后进白名单；先登记 Skip/顺序说明防 CI 红）

## 3. 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| P2 守卫测试（路由器取消收尾） | AgentServiceImpl/Swarm/PlanApproval 三路由器 | ✅（10ba6e250） | ✅ AgentMessageRouteLoopTests 4/4（无消息 Dispose 时限收尾=挂死回归形态/路由派发/停止语义/未注册空流）+ agents.tests 632 |
| P3 单一职责提取 AgentMessageRouteLoop | Swarm/PlanApproval 两路由器重构消费（AgentServiceImpl 保留 BackgroundTaskActor 调度语义） | ✅（10ba6e250） | ✅ ct 传递收敛类内一处构造契约 + analyzer JCC9108 所有权守卫触发验证 + E2E 复验绿 |
| P4 E2E 工具名常量化 | 两 E2E 脚本 | ✅ | ✅ AgentToolNameEnumConstants.Agent + FileToolNameEnumConstants.FileRead 引用 + E2E 复验绿 |
| P5 TeammatePane 角色守卫 | TeammatePaneMiddleware（Variant==Teammate 判定） | ✅ | ✅ E2E 警告噪音归零 + 耗时 16s→7s（免 tmux 进程尝试） |
| P6 CI matrix 增补 | ci-e2e.yml subagent-events 条目（MainAgentPath） | ✅ | ✅ 本地等价验证绿（ChatService 路径待 P1 修复后增补） |

## 4. 遗留

- ~~P1（ChatService 透传缺陷）单独深挖~~ → 已闭环（总表 ✅）：误诊修正为插件就绪时序 + 跨 class 污染，
  E2E 已加 `bootstrapHandle.Completion.WaitAsync(30s)`（`SubAgentEventMainAgentPathE2ETests.cs:80` 等）+ [Collection] 串行，commit `ff9a5e8c8`。
- P7/P8 低优先级悬置（未实施：`SessionController.cs:511-517` tokenGain 仍传 0；`McpPluginBootstrapHandle` 未加 `_started` 门闩）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 按用户"先记录所有问题再修复+守卫+单一职责"方法论——8 问题登记成表（P1-P8），守卫优先级=防再犯的 TDD 测试+编译期可见的构造契约 -->
<!-- 原因: ReceiveAsync 漏传 ct 属静默缺陷模式（Dispose 挂死才暴露）——守卫须让同类错误在测试期/编译期即暴露；magic string 工具名排障两轮的根因是无单一数据源 -->
<!-- 验证: 待逐项实施 -->
