# TASK063 — 无人值守批次：P7/P8 收尾 + TASK043 流式透传缺陷

> 状态：**implemented（2026-10-22 无人值守批次全部完成——提交链 853d9e75b(P7) / c3c5ec77a(P8) / a9236b754(E)）**
> 模式：无人值守（用户睡眠）——主路径→边缘场景顺序推进，不停批不询问，每任务走「文档→代码→测试→真实运行验证→提交」全流程
> 唯余：F 用户真机验收清单（见文末，醒后执行）

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
| token 数据源接线（完成态权威校正） | RouteSubagentEvent / SubagentFinished 投影 | ✅ 853d9e75b | ✅ 单元：BuildCompletionChunk usage 透传×2 + SubagentGroupTests 权威校正（200→350 覆盖/0 保持） |
| 协议字段（方案2 TokenTotal） | BridgeEvent.SubagentFinished + 双消费者 + BridgeJsonContractTests | ✅ | ✅ JSON 快照锁定（tokenTotal:1250） |
| E2E 集成断言 | SubAgentEventMainAgentPathE2ETests | ✅ | ✅ 实弹 E2E：tokenTotal>0 断言通过（连带根因双修：QueryEngine 孤儿 key 改读 provider 统一 metadata["Usage"] + 跨轮累计） |

### P7 实施记录（2026-10-22）

- **选型落定方案2（完成态权威校正）**：引擎逐活动 token 增量不存在（RecordTokenUsage 仅完成时调用），AgentFinished 携带 Usage.TotalTokens 为权威源
- **E2E 红暴露双根因**：①QueryEngine 读孤儿 key `Metadata["InputTokens"/"OutputTokens"]`（全仓无生产者——OpenAI/Anthropic QueryService 统一写 `metadata["Usage"]` TokenUsage JSON）②BuildCompletionChunk 不带 usage 参数
- **修复**：QueryEngine 改读 metadata["Usage"]（对齐 ChatStreamChunkProcessor 先例反序列化）+ 主循环跨轮累计（metadata 未提供时保持 null 禁伪 0）+ BuildCompletionChunk 补 usage 参数
- **意外收获**：ChatService 路径 E2E（SubAgentEventStreamE2ETests，TASK043 嫌疑红）连带转绿——3/3 全绿

### P8 — Completion Run 前占位语义（边界守卫）

**现状**：`McpPluginBootstrapHandle.Completion` Run 前返回 Task.CompletedTask——DI 有 Handle 但 McpInitModule 未跑 ConfigureAsync 的极端组装下，消费方 await Completion 立即返回误判"就绪"。

**实施方向**：契约文档化 + 守卫测试补强（Run 未调用时 Completion 应表达"无可等之物"而非"就绪"——语义边界写进 xml 注释与专用测试；如需行为修正则加 `_started` 门闩等待）。

**验收标准**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| Completion 契约明确（注释+测试） | E2E/宿主等待方 | ✅ c3c5ec77a | ✅ 守卫测试：Run 前占位立即完成（无可等之物≠就绪）+ Run 后 Completion 切换为链 Task 实例（引用变更守卫防退化恒假完成）；13/13 绿 |

### P8 实施记录（2026-10-22）

- 契约明确化而非语义变更（Run 前占位是设计决定）：xml 注释写全"无可等之物 ≠ 就绪"+ 误用形态（Run 前假就绪→空工具表→Tool not found 假象）+ 正确写法诱导（对齐 AGENTS 规则9）
- 新增契约锁测试 `Completion_BeforeRun_Placeholder_NotReadinessSignal`（Assert.NotSame 守卫引用切换）

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
| 流式增量透传修复 | StreamingFallbackDecorator（两阶段重构 ADR 0146） | ✅ a9236b754 | ✅ 单元：门闩确定性增量红→绿 + 中途失败上抛守卫；实弹 E2E：StreamingIncrementalE2ETests（MockServer 50 分片×20ms，首末 Content 时间差反映真实流时长） |
| ~2s 取消根因修复（若复现） | HttpClient 流式配置 | ✅ 未复现 | ✅ StreamInterruption/ApiErrorRecovery E2E 全绿（含 P7 metadata 修复后 3/3 子代理 E2E 连带转绿——原嫌疑症状消失） |
| CI 白名单 | ci-e2e 配置 | ✅ | ✅ 新 E2E 已进 errors-permission 组白名单持续跑（TASK053 F1-x 真机脚本未入库，以实弹 E2E 等价覆盖） |

### E 实施记录（2026-10-22）

- **ADR 0146**（proposed→accepted）：两阶段结构 = 预热（TryPrimeAsync 拉"首事件"，非迭代器可 try-catch，失败仍 fallback——连接级错误无损降级）+ 透传（迭代器仅 try-finally，已 yield 后失败上抛防重复输出）；watchdog/enumerator 所有权经 PrimeOutcome 转移由 finally 统一释放
- **语义变化**（有意，无后向兼容负担）：中途失败 fallback→上抛；透传期用户取消吞 OCE 返已收事件→标准 OCE 上抛
- **测试改造**：现有 fallback 用例（529/超时/IOException/空流）从"先 partial 后 throw"构造切到 FailAtStart 首事件前失败形态（对齐真实连接错误）；新增门闩增量红测试 + 中途上抛守卫
- **回归**：llm.tests 605 + brain.other 412 + brain.context 933 + host.tests 1382 + E2E（子代理 3 + StreamInterruption + ApiErrorRecovery）11 全绿

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

<!-- 🤖 Auto Decision: 2026-10-22（P7） -->
<!-- 决策: 方案2完成态权威校正 + E2E 红暴露后追加根因双修（QueryEngine 读孤儿 key 改读 metadata["Usage"] + BuildCompletionChunk 补 usage） -->
<!-- 原因: 引擎逐活动 token 增量不存在（RecordTokenUsage 仅完成时调用），AgentFinished.Usage 为唯一权威源；孤儿 key 属"消费侧读法与生产侧写法两套约定"断裂，修消费侧单点（OpenAI/Anthropic 双 provider 统一约定不动） -->
<!-- 替代方案: 逐活动增量（数据源不存在，需引擎侧新建统计通道——体量大且与权威源重复） -->
<!-- 验证: 单元 2 新增 + 实弹 E2E tokenTotal>0 + ChatService 路径连带转绿 3/3 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-22（P8） -->
<!-- 决策: 契约文档化而非行为修正（不加 _started 门闩等待） -->
<!-- 原因: Run 前占位是启动开销优化的设计决定；真实宿主序列 ConfigureAsync 先于消费方查询，占位误判仅在"极端组装"出现——写全契约+守卫锁防退化即可，行为变更反而引入等待挂死风险 -->
<!-- 验证: 守卫测试 13/13 绿（含引用切换 Assert.NotSame 锁）✅ -->

<!-- 🤖 Auto Decision: 2026-10-22（E/TASK043） -->
<!-- 决策: 两阶段重构（预热可 fallback + 透传上抛）替代全量收集，ADR 0146 先行；真机 CDP 脚本未入库以实弹 E2E 等价覆盖 -->
<!-- 原因: CS1626 限制的正解是拆阶段而非收全量；"中途失败也 fallback"弃用因已 yield 内容与新流拼接会重复输出；实弹 E2E（真实 MockServer 进程+HTTP+SSE+引擎全管道）与 CDP 真机同链路等价且可进 CI -->
<!-- 替代方案: ①消费方缓冲窗口（治标不治首字延迟）②取消装饰器（丢降级能力）③中途也 fallback（重复输出） -->
<!-- 验证: 门闩确定性红→绿 + llm 605 + 实弹 E2E 增量时间差判据绿 + 全域回归 3342+ 全绿 ✅ -->
