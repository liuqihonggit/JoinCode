# TASK108 — DiminishingReturns 判据（TASK103 §2.3 #41 取证）

> 状态：**已裁决 — 判据保留本仓设计（不同语义层）；顺带闭合并发 Reset 缺口** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#41 / TASK071-C1**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「C# 增长率 <0.1 连续3次截断 vs TS delta<500 附续跑 nudge（已修 Reset 泄漏，判据差异未动）」**文字属实，但把两个不同语义层的机制放在同一根轴上比较**：

- 基准的 `delta<500` 不是独立检测器，而是 **Token 预算续跑循环**（`feature('TOKEN_BUDGET')`）的一个**停止判据**：未达阈值时向会话注入 `isMeta` 用户消息「Keep working — do not summarize.」让模型继续干，达到阈值/预算 90% 才 stop 并记 completion event。
- 本仓的 `DiminishingReturnsMiddleware` 是**单次查询内的工具调用级断路器**：连续 3 次token 相对增长率 <0.1 就发警告并 `ShouldBreak = true` 结束本查询，没有续跑循环、没有预算百分比门槛、没有 nudge 消息。
- 因此「把 C# 判据换成 delta<500」单独做没有落点：本仓没有 `continuationCount`/`lastGlobalTurnTokens`/`isMeta` 续跑机制；要对齐就得整套移植 TOKEN_BUDGET 续跑循环，那属于**新行为**（与 #40 dream 同类），不在无人值守「先修已知问题」的边界内。

**裁决：判据差异保留本仓设计、本项关闭。** 但取证中发现一个真缺口，属"Reset 泄漏未修干净"：检测器是 Singleton + 中间件是 Singleton，而 `IQueryEngine` 也是 Singleton 且 fork 子代理会并发调用 `QueryAsync`（`modules/llm/agents.fork/AgentBase.cs:279/423`）——每次查询开始 `Reset()` 会清掉另一条在途查询的计数，两条查询的消耗序列互相污染。本次把检测器改为**无状态**（计数随调用出入参，存到每查询的 `QueryMiddlewareContext`），从结构上消除跨查询共享，而不是继续用 `Reset()` 打补丁。

---

## 1. 四问回答

### 问1 专家隐性实践
"递减回报"在成熟实现里属于**预算续跑循环**的退出条件：先判断"是否值得再推进一轮"，只有推进仍产生足够增量（delta）才继续；增量枯竭才早停。它必须依附于"能发起下一轮"的循环，否则 delta 判据无处安放。独立的工具调用级断路器是另一种有效实践：在单次查询内发现工具-LLM 往返的边际收益骤降就止血，不需要续跑语义。

### 问2 原始素材（一手）
- 基准：`src/query/tokenBudget.ts:3-4`（`COMPLETION_THRESHOLD=0.9`、`DIMINISHING_THRESHOLD=500`）、`:6-11`（`BudgetTracker` 四字段）、`:45-93`（`checkTokenBudget` 全流程）、`:64-76`（未达 diminishing 且未到 90% → `continue` + `getBudgetContinuationMessage`）、`:78-90`（diminishing 或已有续跑 → `stop` + `completionEvent`）；`src/utils/tokenBudget.ts:66-73`（nudge 文案）；`src/query.ts:280`（`feature('TOKEN_BUDGET') ? createBudgetTracker() : null`）、`:1308-1355`（消费 decision：`:1325-1328` 注入 `isMeta: true` 的 nudge 后 `continue`；`:1344-1353` diminishing 早停记 event）。
- 本仓：`modules/cognition/brain.query/query2/token_budget/core/DiminishingReturnsDetector.cs:50-52`（`0.1`/`3`/最小样本 2）、`:81-102`（逐对求 `curr/prev` 比值取均值）、`:104-119`（CAS 累加连续低值、命中给两档建议）、`:135-137`（`Reset`）；`middleware/DiminishingReturnsMiddleware.cs:30-39`（每查询 `Reset()` + 注册 `AfterToolCallHooks`）、`:41-53`（`RecentConsumptions.Count>=3` → 检测 → 警告 + 输出 `⚠️` + `ShouldBreak = true`）；`middleware/CostTrackingMiddleware.cs:46-56`（消费序列由 AfterLlmCall 钩子写入，Amount = `InputTokens + OutputTokens`）；`core/QueryEngine.cs:403-419`（钩子执行后应 `ShouldBreak` 跳出批次/查询）；`middleware/TokenBudgetMiddleware.cs:38-44`（只做预算消耗，无续跑）；`middleware/PreRequestBudgetGuardMiddleware.cs:12-15`（上下文窗口 0.7/1.0 预防裁剪，与 token 目标续跑无关）。
- 并发证据：`modules/llm/agents.fork/AgentBase.cs:279/423-424`（fork 子代理调 `IQueryEngine.QueryAsync`）；`core/QueryEngine.cs:21` + 中间件 `[Register(..., Singleton)]` → 同一实例被多条查询共享。

### 问3 具体方法与工具
两侧源码逐行对照 + 全仓 grep（确认本仓零 `continuation`/`nudge`/`isMeta` 续跑结构、零 `DIMINISHING_THRESHOLD` 等价常量）；对照类名易混淆点：`query-token-budget-mw` 是 `TokenBudgetMiddleware` 的全局 using 别名（`modules/composition/pipelines/GlobalUsings.cs:36`），**不是**基准的 `checkTokenBudget` 续跑控制器。

### 问4 独立验证来源
基准 `query/tokenBudget.ts` 的 decision 类型自带 `ContinueDecision.nudgeMessage` 与 `StopDecision.completionEvent.diminishingReturns`，与 `query.ts:1316/1343` 两种分支一一对应，证明 delta 判据只在续跑循环内有意义。本仓 `DiminishingReturnsDetectorTests.cs` 11 项 + `QueryLifecycleE2ETests.cs:98-125` 锁定的是"同一检测器连续调用 3 次触发"的状态机语义，与基准无对应关系；`DataDrivenPipelineComposition.cs:125` 证明中间件在生产 query-pipeline 中有活消费点（不是死代码）。

---

## 2. 差异与判定

| 维度 | 基准 `checkTokenBudget` | 本仓 `DiminishingReturnsMiddleware` | 判定 |
|---|---|---|---|
| 所处层 | 回合级预算续跑循环（feature switch） | 单次查询内工具调用后钩子 | **不同语义层，不可直接换判据** |
| 判据 | `continuationCount>=3 && delta<500 && lastDelta<500`，配合 90% 预算门槛 | 相邻 LLM 调用 token 比值均值 <0.1，连续 3 次 | 各自内部一致 |
| 命中动作 | stop + completion event（早停仅记日志/埋点） | 警告消息入流 + `ShouldBreak=true` 立即止血 | 行为不同，但各符其层 |
| 未命中动作 | 注入 `isMeta` nudge，模型继续工作 | 什么也不做，正常迭代 | 本仓无续跑概念 |
| 每查询重置 | tracker 每次 query 新建（`query.ts:280`） | Singleton `Reset()`（现改为无状态） | **原实现并发下不成立，本次修复** |
| 配置开关 | `feature('TOKEN_BUDGET')` | 无（中间件随 profile 装配） | 两仓配置体系不同，不强行对齐 |

**不移植理由**：单独搬 delta 阈值会得到一个"没有续跑的 delta 检测"——要么永远等不到 `continuationCount>=3`（恒不触发，假实现），要么得凭空造续跑循环（改响应时机与 token 成本，新行为）。按 #40 同类边界：无人值守不引入需产品权衡的新设计。本仓比值判据虽是启发式（调用 token 变少也可能是任务接近完成），但已有 11+1 项测试锁定且生产一直在用，无证据显示误伤，不动。

---

## 3. 施工：消除跨查询共享（Reset 泄漏的并发残余）

1. `IDiminishingReturnsDetector.CheckDiminishingReturns` 增加显式 `consecutiveLowValueCount` 入参；检测器删除 `_consecutiveLowValueCount` 字段与 `Reset()`，成为**无状态纯计算**（同样的入参必得同样的结果）。
2. `QueryMiddlewareContext` 增加 `DiminishingLowValueIterations`——计数归**查询上下文**所有，随查询创建/销毁，天然隔离并发 fork。
3. `DiminishingReturnsMiddleware` 不再 `Reset()`；从上下文读计数、把新计数写回上下文。
4. 测试改写：原"调用三次才触发"改为显式传计数；新增"两个上下文交错调用互不污染"的并发/隔离回归。

> 该改动不改变判据阈值（0.1/3/最小样本 2）、不改变命中后的警告与 `ShouldBreak`，只是把状态从全局搬到查询局部。

---

## 4. 验收（回填）

- 单测：筛选 `DiminishingReturns|QueryLifecycle` **20/20 绿**；Brain.Other.Tests 全量 **510/510 绿**（`DiminishingReturnsDetectorTests` 13 项，含 2 项新增的上下文隔离/命中隔离回归；E2E 生命周期用例改为显式传计数）。
  - 红→绿过程：中间件实现 `IAsyncDisposable`，新测试首版用 `var` 触发 JCC9108/JCC9305，改 `await using`；首批隔离用例把 `Seq(1000,10)` 喂给中间件（hook 要求 `RecentConsumptions.Count>=3` 直接早退），改为三项序列后通过。**两条都是测试写法问题，不是生产逻辑缺陷。**
- 编译：`Brain.Query.csproj` 0 错误；`Build-AllDomains.ps1 -Changed` 2 项目全通过；CLI `JoinCode.csproj` 全量重编 0 错误（3m17s，单线程）。
- 真机：`jcc.dll --selftest-tui` 输出 `HOST SELFTEST OK` EXIT=0。
- 产物：`JoinCode.Brain.Query.dll` 含 UTF-8 `DiminishingLowValueIterations` / `consecutiveLowValueCount`，旧字段 `_consecutiveLowValueCount` **不存在**——确认无状态实现进了产物（非只看时间戳）。
- 账本：TASK103 #41 划掉并指向本文档。
