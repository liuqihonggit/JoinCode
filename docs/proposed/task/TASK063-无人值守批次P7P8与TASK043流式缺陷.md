# TASK063 — 无人值守批次：P7/P8 收尾 + TASK043 流式透传缺陷

> 状态：进行中（2026-10-22 创建；用户指令："先记录任务到文档，要有验收标准，全部按顺序制作"）
> 模式：无人值守（用户睡眠）——主路径→边缘场景顺序推进，不停批不询问，每任务走「文档→代码→测试→真实运行验证→提交」全流程

## 任务池盘点（2026-10-22 核实）

| 任务 | 核实结果 | 证据 |
|------|---------|------|
| ~~A 结构化消息格式~~ | **✅ 已 implemented 勿重做** | docs/proposed/task/TASK062-结构化消息设计.md 状态 implemented（817+64 全绿，2026-10-21 落地）；唯余用户真机验收（思考模型场景） |
| ~~D DecidePreflight 接线~~ | **✅ 已接线勿重做** | QueryLoopMiddleware.cs:130-133/222-223（TASK061 批次落地） |
| **B TASK062 P7/P8** | 待做（本批第 1 项） | docs/proposed/task/TASK062-引擎侧问题登记与守卫强化.md P7/P8 悬置 |
| **E TASK043 流式透传缺陷** | 待做（本批第 2 项） | ~2s TaskCanceledException + StreamingFallbackDecorator 全量收集根因；CI E2E 白名单登记（PR #435） |
| **F 用户真机验收汇总** | 留用户（醒后执行） | 见文末验收清单 |

## 任务 B：TASK062 P7 + P8（收尾完善）

### P7 — tokenGain 真实值（子代理工具活动事件 token 增量）

**现状**：`SessionController.RouteSubagentEvent` 的工具事件分支 tokenGain 恒 0 兜底（批1 实施时 ChatStreamEvent 无 token 语义字段）。

**调研先行**（动手前核实数据源）：
- 引擎侧候选：子代理 ProgressTracker.RecordTokenUsage 原子累加（AgentServiceImpl.GetAgentProgressAsync 可查）/ ChatStreamEvent.Complete.Usage（仅完成态有总额）/ AgentStreamChunk.Usage（仅 Complete chunk）
- 选型判据：逐活动增量的可得性 vs 完成时总额补发（协议 SubagentFinished 无 token 字段需扩展）

**实施方向**（按调研结论二选一）：
1. 逐活动增量：工具事件发射点取该子代理当前 token 累计，消费侧维护上次值取差
2. 完成态总额：SubagentFinished 扩展 TokenTotal 字段（协议+双消费者+TUI 统计列接线——组统计 TokenCount 已有累计语义，直接对齐）

**验收标准**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| token 数据源接线（调研选型落地） | RouteSubagentEvent / SubagentFinished 投影 | ⏳ | ⏳ 单元测试：token 值非零且单调递增（fake 时钟确定性） |
| 协议字段（若走方案2） | BridgeEvent.SubagentFinished + 双消费者 + BridgeJsonContractTests | ⏳ | ⏳ JSON 快照锁定 |
| E2E 集成断言 | SubAgentEventMainAgentPathE2ETests | ⏳ | ⏳ MockServer 脚本含工具轮后 tokenGain/TokenTotal > 0 断言 |

### P8 — Completion Run 前占位语义（边界守卫）

**现状**：`McpPluginBootstrapHandle.Completion` Run 前返回 Task.CompletedTask——DI 有 Handle 但 McpInitModule 未跑 ConfigureAsync 的极端组装下，消费方 await Completion 立即返回误判"就绪"。

**实施方向**：契约文档化 + 守卫测试补强（Run 未调用时 Completion 应表达"无可等之物"而非"就绪"——语义边界写进 xml 注释与专用测试；如需行为修正则加 `_started` 门闩等待）。

**验收标准**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| Completion 契约明确（注释+测试） | E2E/宿主等待方 | ⏳ | ⏳ 守卫测试：Run 前/后语义各自断言 |

## 任务 E：TASK043 流式透传缺陷

**已知症状**（记忆线索）：
1. ~2s `TaskCanceledException`——连接/发送级取消非 100s 整体超时（流式 HttpClient 路径）
2. StreamingFallbackDecorator **全量收集后整发**——流式增量透传断裂（GUI 会话流 E2E 曾见"流中串话/超时回错"）
3. CI E2E 白名单登记（PR #435 提交说明明示）

**调研先行**：
- 定位 StreamingFallbackDecorator（GUI 侧装饰器）与流式链路（QueryEngine→IChatService 流式管道→消费端）
- 复现路径：MockServer 流式脚本（chunked 逐段返回）→ 断言消费端逐段收到（非全量后到）
- ~2s 取消：HttpClient 配置链（连接超时/首个 chunk 超时）排查

**验收标准**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 流式增量透传修复 | GUI 会话流/StreamingFallback 链路 | ⏳ | ⏳ E2E：chunked 脚本逐段到达断言（时序戳非全量后到） |
| ~2s 取消根因修复（若复现） | HttpClient 流式配置 | ⏳ | ⏳ 超时语义测试 |
| CI 白名单摘除 | ci-e2e 配置 | ⏳ | ⏳ 白名单移除后 CI 绿 |

## 任务 F：用户真机验收清单（醒后执行）

| # | 验收点 | 来源任务 |
|---|--------|---------|
| 1 | agents 场景真实引擎 spawn 分组显示（agents / agents 300） | DSG041 |
| 2 | Shift+↑ 进入消息导航→↑↓ 导航→Enter 展开分组→Esc 两级退出 | 交互层 Step1+2 |
| 3 | 思考模型场景：`⎿ 思考 3.2s · ctrl+o 展开推理` + Ctrl+O 展开 CoT | 结构化消息 |
| 4 | Host.exe 启动提速体感（~1.6s）+ 退出零噪音 | 启动优化 A 项 |

## 执行顺序与无人值守说明

按 B → E → F（F 留用户）顺序推进。B 完成即编译+测试+提交；E 调研先行（StreamingFallbackDecorator 定位+复现）后分批实施。每任务完成更新本文档验收表。遇不可逆操作（删除/push 冲突）按 AGENTS.md 规则挂起留用户，其余自主决策。

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 任务池先核实再登记——A（结构化消息）与 D（DecidePreflight）经 git/文档核实已完成，从本批清单划掉避免重做；B/E/F 三项按"小收尾→大缺陷→用户验收"排序 -->
<!-- 原因: 无人值守模式防重复造轮子（TASK062 结构化消息设计.md 状态 implemented + QueryLoopMiddleware 接线实证）；B 先行因其与 DSG041 上下文连续且体量小 -->
<!-- 验证: 核实证据已列（文档状态/文件行号） -->
