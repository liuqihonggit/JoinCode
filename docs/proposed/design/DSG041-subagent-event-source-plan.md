# DSG041 — 引擎侧子代理事件源改造计划（三缺口③）

> 状态：proposed（2026-10-21）
> 关联：TASK055 M7-7（子代理分组渲染已落地）、ADR 0143（协议层唯一数据源=IEventConsumer 事件模型）、ADR 0141（bridge 事件投影）
> 前置调研：2026-10-21 引擎侧执行链全量探索（探索代理报告，全部结论已核到文件:行号）

## 1. 问题

native_tui（TUI 宿主）子代理分组渲染全链已落地（SubagentGroupCoordinator + BridgeEvent 三事件 + ReplBridgeDriver 路由），但**真实引擎链路无子代理事件源**——目前仅 Demo 的 SimulatedEngine 模拟发射。真实链路表现：TUI 跑真实引擎时子代理调用不产生任何分组显示。

## 2. 调研结论（现状事实）

**核心发现：子代理事件源在引擎侧已存在且已发射**——`AgentStreamExecutionMiddleware`（前台）/ `AgentForkMiddleware`（fork/teammate）/ `ForkSubAgentManagerActor`（后台完成）持续向 `SubAgentEventChannel` Emit `AgentStarted` / 带 AgentId 的活动事件 / `AgentFinished`（ChatStreamEvent 层）。ChatService 管道路径（GUI/E2E 已验证：`SubAgentEventStreamE2ETests`）事件正常流动。

**native_tui 主代理路径丢事件的三个断点**：

| # | 断点 | 位置 | 性质 |
|---|------|------|------|
| ① | **作用域断点（根因）** | 主代理路径 `AgentBase.ExecuteStreamAsync → QueryEngine.QueryAsync` 不经 ChatService 管道；`ToolExecutionHandler.cs:80` 的 `EnterScope()` 不在链上 | `SubAgentEventChannel.Current`（AsyncLocal）为 null → 发射侧 `channel?.Emit` 全部静默跳过 |
| ② | **IEventConsumer 协议断点** | `IEventConsumer.cs`（8 方法）无子代理回调；`SessionController.ProcessEvent` 的 Switch 不传 onAgentStarted/onAgentFinished；`AgentStreamChunkAdapter` 不传播 chunk.AgentId | 事件模型（协议本体）缺子代理粒度 |
| ③ | **桥协议断点** | 引擎侧 `Cli.Bridge.BridgeEvent`（modules/apps/engine/bridge/BridgeProtocol.cs）无 subagent 三事件；`BridgeEventConsumer`/`InProcessEventConsumer` 无映射 | bridge 形态（JSON 投影）发不出来 |

**子代理执行链事实**：子代理是独立 `AgentBase` 实例（非工具内嵌套回合），内部走完整 QueryEngine LLM+工具循环；数据可得性全齐（SubagentId=chunk.AgentId、AgentType/Description=spawnOptions、ToolName/Summary=chunk 字段、TokenGain=Usage/ProgressTracker 差值、IsError=succeeded）。

## 3. 方案 A（主干推荐）— 沿既有 SubAgentEventChannel 机制打通

**原则：发射侧零新代码**（三个发射点已在 Emit），每层挂点均有先例可循。

### 四层改造（自底向上）

| 层 | 改造 | 落点 | 先例 |
|----|------|------|------|
| L1 通道接通 | `AgentBase.ExecuteStreamAsync` 持有 SubAgentEventChannel（挂 Options/字段），QueryAsync 循环内逐 chunk 排空通道转 AgentStreamChunk（新增 AgentStreamChunkType.AgentStarted/AgentFinished）随主流 yield；工具执行段进作用域 | `AgentBase.cs:340-469`（循环）+ `QueryEngine.cs`（`QueryMiddlewareContext` 挂通道 + `QueryCoreMiddleware` 工具段排空，或 AgentBase 层排空二选一——实施时按侵入度定） | `QueryLoopMiddleware.WaitForTaskWithDrainAsync`（50ms 轮询排空先例）+ `ToolExecutionHandler.cs:80` EnterScope |
| L2 chunk 传播 | `AgentStreamChunkAdapter.ToChatStreamEvent` 传播 `chunk.AgentId` + 新 chunk 类型映射 → `ChatStreamEvent.AgentStarted/AgentFinished`（工厂已存在 ChatStreamEvent.cs:166/177） | `AgentStreamChunkAdapter.cs:12-51` | 工厂现成 |
| L3 协议扩展 | `IEventConsumer` 新增 `OnSubagentStarted(subagentId, agentType, description)` / `OnSubagentToolUse(subagentId, toolName, summary, tokenGain)` / `OnSubagentFinished(subagentId, isError)`；`SessionController.ProcessEvent` Switch 传入三回调 | `IEventConsumer.cs` + `SessionController.cs:157-185` | **ADR 0143 不变式：先改事件模型（本体）再同步投影** |
| L4 双消费者映射 | 引擎侧 `Cli.Bridge.BridgeEvent` 增 subagent 三 record（字段对齐 native_tui BridgeEvent.cs:83/89/92，JSON 形状进 BridgeJsonContractTests 锁定）；`BridgeEventConsumer` + `InProcessEventConsumer` 实现 L3 三方法 | `BridgeProtocol.cs:34-127` + 两消费者 | 两消费者语义对齐先例 |

### 消费端（native_tui，零改动验证面）

`ReplBridgeDriver.cs:110-131` 已路由三事件到 `SubagentGroupCoordinator`——L1-L4 打通后真实链路自动出分组显示，TUI 侧**零代码改动**（验收面）。

## 4. 方案 B（补充位）— IAgentService 轮询聚合

`AgentServiceImpl` 已有 `AgentCompleted` 事件 + `GetAgentProgressAsync`/`GetAgentActivityHistoryAsync` 轮询面。仅在宿主层起轮询器投影终态。

**定位：不作为主方案**——轮询延迟、拿不到精确 Started 时刻、tokenGain 非增量语义，且与方案 A 通道机制构成两套实现（违反唯一数据源规范）。fork 后台路径完成事件已在 `ForkSubAgentManagerActor.cs:467` 发射、通道接通即覆盖，无需 B。

## 5. 实施批次（渐进式，每批编译+测试+commit）

| 批次 | 内容 | 验证 |
|------|------|------|
| 批1 | L3 协议扩展：IEventConsumer 三方法（默认空实现——不破坏既有实现方）+ SessionController.ProcessEvent 三回调 | host.tests 新测试（fake consumer 断言回调） |
| 批2 | L1 通道接通：AgentBase/QueryEngine 段排空 + EnterScope + AgentStreamChunk 新类型 | agents 域单测 + E2E 复制 SubAgentEventStreamE2ETests 先例 |
| 批3 | L2 chunk 传播：AgentStreamChunkAdapter AgentId + 类型映射 | adapters 单测 |
| 批4 | L4 引擎侧 DTO + BridgeEventConsumer 映射 | BridgeJsonContractTests 锁形状 |
| 批5 | InProcessEventConsumer 映射 + native_tui 真实链路验收 | Host --selftest 扩子代理断言（真实 spawn，不调 LLM 可用 MockServer） |

**顺序理由**：L3 先行（事件模型=协议本体，ADR 0143 演进次序铁律"先本体后投影"）；L1 最重（执行链侵入）单独成批；批5 端到端收口。

## 6. 风险与决策点

1. **L1 挂点二选一**（批2 实施时定）：`QueryMiddlewareContext`+QueryCoreMiddleware 排空（对齐 QueryLoopMiddleware 先例，但 QueryEngine 是 cognition 域共享组件侵入面大）vs AgentBase 循环内排空（侵入小但 fork 后台路径需另接）。倾向后者先行 + fork 路径复用通道。
2. **AsyncLocal 作用域传播**：EnterScope 须覆盖子代理内全部工具执行段（含嵌套 spawn）；E2E 先例已验证前台/fork 两条路径。
3. **tokenGain 增量语义**：ProgressTracker.RecordTokenUsage 原子累加 vs 差值发射——对齐 native_tui 消费语义（TokenDelta 逐次累加）取差值。
4. **无消费方场景零影响**：通道无作用域静默丢弃的既有语义保留（CLI 纯文本等场景）。
5. **IEventConsumer 默认实现**：三方法走接口默认实现（DIM），既有 8+ 实现方零改动；注意 DIM 须经接口类型调用才可见（项目已有先例坑）。

## 7. 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| L3 IEventConsumer 三方法（DIM 默认空）+ SessionController RouteSubagentEvent 前置分流 | BridgeEventConsumer / InProcessEventConsumer（批4/批5 接映射） | ✅ 批1（hash 已随 PR #439 squash，内容在库） | ✅ SessionControllerSubagentEventTests 5/5（Started/Finished/工具分流/普通零回归/混合流）+ host.tests 1371 + abs.tests 694 + engine/cli/Host 三链编译绿 |
| L1 SubAgentEventChannel 接通主代理路径（**宿主层直投方案**） | AgentStreamExecutionMiddleware/ForkSubAgentManagerActor 发射侧（零改动） | ✅ 批2（6b762c145） | ✅ 单元级：MainAgentPath 2/2（通道事件全序列直投三回调 + 子代理工具事件不走普通回调）+ host.tests 1373 全绿 + Host 编译绿；⏳ 集成级：E2E 复制先例（批2 收尾项） |
| ~~L2 AgentStreamChunkAdapter AgentId 传播~~ | ~~SessionController chunk→ChatStreamEvent 翻译~~ | **✅ 被批2 宿主层直投方案吸收**（通道事件不经 chunk 转换层，AgentBase/adapter 零改动；批3 撤销） | —（无独立交付物） |
| L4 引擎侧 BridgeEvent 三 record + BridgeEventConsumer 显式映射 | bridge_serve JSON 协议消费方 | ✅ 批3（6d55a8215） | ✅ BridgeJsonContractTests JSON 形状快照锁定（subagent_started/subagent_tool_use/subagent_finished camelCase）+ BridgeEventConsumerTests 映射断言 + host.tests 1375 + engine/cli/Host 三链编译绿 |
| InProcessEventConsumer 映射 | EngineTurnClient → ReplBridgeDriver（既有路由零改动） | ✅ 批4（3f7f88919） | ✅ InProcessEventConsumerTests 映射断言（三 record 字段逐一对齐）+ NativeTui.Host.Tests 61 全绿 + Host 编译绿；⏳ Host --selftest 扩断言（真实运行收口） |
| TUI 真实链路分组显示 | SubagentGroupCoordinator → 分组渲染（既有零改动） | ✅ 链路闭合（批1-4） | ⏳ 用户真实终端（agents 场景真实引擎 spawn） |

## 8. 后续

- ~~批1（L3 协议扩展）~~ **✅ 已完成（内容随 PR #439 squash 入库）**
- ~~批2（L1 通道接通）~~ **✅ 已完成（6b762c145，宿主层直投方案 — 见批2 实施记录）**
- ~~批3（L2 chunk 传播）~~ **✅ 撤销 — 被批2 宿主层直投方案吸收**
- ~~批4（原批3 引擎侧 DTO + BridgeEventConsumer 映射）~~ **✅ 已完成（6d55a8215）**
- ~~批5（原批4 InProcessEventConsumer 映射）~~ **✅ 已完成（3f7f88919）— 代码批次全部落地，链路闭合**
- **剩余集成级验收**（收尾项）：① 批2 E2E（复制 SubAgentEventStreamE2ETests 先例改造为 QueryEngine 主代理路径）② Host --selftest 扩子代理断言 ③ 用户真实终端收口
- 本 DSG 落地后 TASK055 三缺口③关闭

### 批1 实施记录（hash 已随 PR #439 squash，内容在库）

- IEventConsumer 三方法走 **DIM 默认空实现**（既有 8+ 实现方零改动；默认实现须经接口类型调用的既有坑已规避）
- SessionController.ProcessEvent 前置分流 **RouteSubagentEvent**：AgentId 非 null 即子代理事件——AgentStarted→OnSubagentStarted（身份三元组）、AgentFinished→OnSubagentFinished（**AgentSuccess 成功语义取反为 isError**）、带 AgentId 工具事件→OnSubagentToolUse（tokenGain 恒 0 兜底，批2 填真实值）；返回 true 跳过主 Switch
- 实施坑：AgentSuccess→isError 语义取反（首轮直传 false 导致 finished.IsError 断言反）；mixed 流中子代理 Content 事件暂不投影返回 false 走主路径（消费面仅三回调）

### 批2 实施记录（6b762c145 — 方案变更：宿主层直投）

- **挂点决策结果**：既非 AgentBase 循环排空也非 QueryCoreMiddleware 排空，而是 **SessionController 主代理路径**（第三方案，实施时发现的最优解）——通道作用域 + 排空全部落宿主层，AgentBase/QueryEngine/adapter 全零改动
- 结构：`StreamResponseAsync` 主代理分支内 `new SubAgentEventChannel() + EnterScope()`（AsyncLocal 沿 await 链传播覆盖 QueryEngine 工具执行段 → Agent 工具中间件 Emit 点可见）→ chunk 循环逐拍 `TryDrain` 直投 `ProcessEvent`（复用批1 分流）→ 循环后末批冲刷 + `Complete()`（迟到 Emit 静默丢弃防泄漏，对齐 QueryLoopMiddleware 先例）
- **L2 被吸收**：通道事件不经 chunk 转换层直接是 ChatStreamEvent，AgentStreamChunkAdapter 不需要传播 AgentId（主代理 chunk 的 AgentId 是主代理自身 id，传播反而会误分流——这也是直投方案的正确性优势）
- 实施坑：Moq 泛型回调返回类型必须精确匹配（QueryAsync 返回 IAsyncEnumerable<QueryStreamChunk> 非 AgentStreamChunk）；QueryStreamChunk.AgentId 非必填（与 AgentStreamChunk 不同）
- tokenGain：批2 后仍恒 0（通道事件无 token 语义字段；真实 token 增量待 ChatStreamEvent.Usage 传播或 TUI 侧 TokenDelta 近似，列为低优先后续项）

## 9. 各批验收方式定义（2026-10-21 用户确认"先定义验收方式再逐个进行"）

> 原则：TDD 红绿循环（每批先红测试后实现）+ 层级递进验收（单测 → E2E → 真实运行），全链最终以 ADR 0080 真实运行收口。

| 批次 | 单元级验收（红绿循环） | 集成级验收 | 通过判定 |
|------|----------------------|-----------|---------|
| 批2 L1 通道接通 | AgentBase/AgentServiceImpl 新单测：通道挂载后子代理事件随主流 yield（EnterScope 前后 Current 值断言 + 排空转 chunk 断言） | **E2E 复制 SubAgentEventStreamE2ETests 先例**（test/mock/mock_server.e2e.tests）——改造为 QueryEngine 主代理路径（AgentBase.ExecuteStreamAsync 直连，不经 ChatService 管道），断言 AgentStarted/带 AgentId 活动/AgentFinished 全序列可观察 | E2E 绿 + 既有全量回归零破坏（agents/llm/brain 域测试） |
| 批3 L2 chunk 传播 | AgentStreamChunkAdapter 单测：chunk.AgentId → ChatStreamEvent.AgentId、新 chunk 类型（AgentStarted/AgentFinished）→ 对应事件工厂映射 | SessionControllerSubagentEventTests 扩 chunk 路径用例（主代理路径 chunk→事件→回调全链） | adapter 输出事件 AgentId 非空 + 类型映射正确 |
| 批4 L4 引擎侧 DTO | BridgeEventConsumer 映射单测（三方法→三 record 字段对齐）+ **BridgeJsonContractTests 锁 JSON 形状**（subagent_started/subagent_tool_use/subagent_finished 序列化快照） | bridge_serve 形态冒烟（进程级 stdout JSON 投影） | JSON 形状锁定 + 双端字段一致（与 native_tui BridgeEvent 逐字段核对） |
| 批5 TUI 映射+真链 | InProcessEventConsumer 映射单测（NativeTui.Host.Tests：三方法→BridgeEvent 三事件投影） | **Host --selftest 扩子代理断言**（MockServer 假 LLM + 真实 spawn 路径，不依赖真实 API Key）→ 用户真实终端收口（agents 场景真实分组显示） | selftest 绿 + 用户真机确认分组渲染正确 |

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 方案A主干=沿既有 SubAgentEventChannel 机制打通（发射侧零新代码），四层改造 L3协议→L1通道→L2传播→L4投影（ADR 0143 先本体后投影次序）；方案B轮询仅补充位不采纳 -->
<!-- 原因: 事件源已存在（AgentStreamExecutionMiddleware 等三点已 Emit），根因是主代理路径无通道作用域+协议三层缺子代理粒度；轮询方案延迟/非增量/双实现三违 -->
<!-- 替代方案: B=IAgentService 轮询聚合（延迟+两套实现否决）；从零新造事件源（既有通道机制浪费否决） -->
<!-- 验证: 调研报告全部结论核到文件:行号；spec 待批1 开工后逐批验收 -->
