# TASK062 — 引擎侧问题登记与守卫强化（子代理执行链缺陷后续）

> 状态：进行中（2026-10-21）
> 来源：DSG041 集成验收过程中暴露的引擎侧缺陷与缺陷模式，按"先记录所有问题，再修复；修复加入守卫和单一职责使问题更容易暴露"方法论推进
> 关联：DSG041（docs/proposed/design/DSG041-subagent-event-source-plan.md）、TASK043（流式增量透传缺陷）

## 1. 问题登记总表

| # | 问题 | 性质 | 状态 | 守卫方案 |
|---|------|------|------|---------|
| P1 | **ChatService 路径子代理 LLM 流无 AgentId 透传主流**——SubAgentEventStreamE2ETests 红：子代理输出以无标记 Content 串入主流、无 AgentStarted（疑 StreamingFallbackDecorator 全量收集透传——TASK043 域） | 独立缺陷 | ⏳ 待修 | E2E 断言（已有红测试守护）+ 透传点单一职责化 |
| P2 | **邮箱 ReceiveAsync 漏传 ct 模式**（已修三处：AgentServiceImpl/SwarmPermissionMessageRouter/PlanApprovalMessageRouter）——"ct 挂在参数上但没传给底层"是静默缺陷，Dispose 挂死才暴露 | 缺陷模式（防再犯） | ✅ 已修 | **路由器取消收尾测试**（TDD 防回归）+ 单一职责提取 |
| P3 | **路由循环内嵌大类**——RouteWorkerPermissionResponsesAsync 等三个消息路由循环内嵌在 AgentServiceImpl（900+ 行）等大类里，取消语义/生命周期/分发逻辑耦合，缺陷难以局部暴露 | 结构性风险 | ⏳ 待守卫 | 提取独立路由类（单一职责）+ 取消收尾单元测试 |
| P4 | **E2E/测试工具名 magic string**——SubAgentEventStreamE2ETests/MainAgentPath E2E 脚本手写 "Agent"/"agent_spawn" 均错（真值 `agent`，AgentToolName [EnumValue]）——两轮排障才定位 | 可维护性 | ⏳ 待守卫 | 脚本改引用 AgentToolNameEnumConstants 常量（单一数据源） |
| P5 | **TeammatePaneMiddleware 对非 teammate 子代理也尝试创建 tmux pane**——executor 前台子代理 spawn 也走 pane 创建（Windows 无 tmux 每次警告噪音）；判定条件只查 IsMainAgent 不查角色 | 设计缺陷（非致命） | ⏳ 待修 | 判定加角色守卫（teammate 才创建） |
| P6 | **两处子代理 E2E 不在 CI 白名单**——ci-e2e matrix 白名单式 filter 从未含 SubAgentEventStreamE2ETests——缺陷长期潜伏（上次手动绿在 #435/#437 大改前） | CI 盲区 | ⏳ 待守卫 | ci-e2e matrix 增补两个 E2E 条目 |
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
| P2 守卫测试（路由器取消收尾） | AgentServiceImpl/Swarm/PlanApproval 三路由器 | ⏳ | ⏳ |
| P3 单一职责提取 AgentMessageRouteLoop | 三路由器重构消费 | ⏳ | ⏳ |
| P4 E2E 工具名常量化 | 两 E2E 脚本 | ⏳ | ⏳ |
| P5 TeammatePane 角色守卫 | TeammatePaneMiddleware | ⏳ | ⏳ |
| P6 CI matrix 增补 | ci-e2e.yml | ⏳ | ⏳ |

## 4. 遗留

- P1（ChatService 透传缺陷）单独深挖（TASK043 域——StreamingFallbackDecorator 调查）
- P7/P8 低优先级悬置

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 按用户"先记录所有问题再修复+守卫+单一职责"方法论——8 问题登记成表（P1-P8），守卫优先级=防再犯的 TDD 测试+编译期可见的构造契约 -->
<!-- 原因: ReceiveAsync 漏传 ct 属静默缺陷模式（Dispose 挂死才暴露）——守卫须让同类错误在测试期/编译期即暴露；magic string 工具名排障两轮的根因是无单一数据源 -->
<!-- 验证: 待逐项实施 -->
