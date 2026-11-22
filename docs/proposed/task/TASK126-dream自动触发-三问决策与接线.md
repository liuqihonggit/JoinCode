# TASK126 — dream 回合结束自动触发（三问决策 + 接线）

> 状态：**已施工** · 日期 2026-11-23 · 总账编号：TASK103 §2.3 **#40**（承 TASK107 裁决「需专项设计，本轮不做」）
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src` · 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

TASK107 把 #40 挂起时留了三问作为启动条件。三问在本文件 §1 已答，结论是**可以施工，且不需要产品权衡**——因为三问的答案全都落在「基准已有明确规则」上，而不是「延迟 vs 成本」的取舍。

真正缺的不是一条线，而是**四件不同的事**（缺任一件都会烧钱或出错）：

| # | 缺什么 | 症状 | 本轮 |
|---|---|---|---|
| ① | 回合结束 → dream 的触发点 | 用户不敲 `/dream` 就永远不整合 | 接上 |
| ② | **时间门是假值** | `DreamFeature.ExecuteDirectAsync` 用 `UtcNow-MinHours` 当「上次合并时刻」→ 只要 24h 内有 5 个会话，**每一回合都放行** | 接真锁 |
| ③ | **并发互斥** | 一次 dream 跑几分钟，期间每个回合都通过时间门 → 多份 LLM 调用同时在跑 | 加原子占锁 |
| ④ | **子代理门** | 基准 `!toolUseContext.agentId`，本仓 `QueryOptions` 无 agent 标识 | 记偏差（§4） |

② 是最贵的坑：把 ① 单独接上而不修 ②，等于给每次对话末尾加一次 LLM 调用。**本轮的验收重点是「②③ 在有触发点之后依然是闭的」**，而不是「能触发」。

---

## 1. 三问回答

### 问 ①：自动触发是否用 sideQuery（不污染会话历史）

**是，且本仓已结构性满足，不需要新机制。**

基准要求见 `query/stopHooks.ts:102` 注释（分类器只对 `repl_main_thread` 生效，理由是 "background forks (extract-memories, auto-dream) don't pollute the timeline"），实现上 `autoDream.ts:244` 走 `runForkedAgent({ ..., skipTranscript: true })`。

本仓 `DreamFeature.ExecuteAsync` **自建** `MessageList`（`DreamFeature.cs:116-118`：`chatHistory.AddSystemMessage/AddUserMessage` 后直接 `_chatCompletionClient.GetCompletionAsync`），全程不接触调用方的 `ChatHistory`，只返回 `DreamResult`。所以「不污染时间线」不是要靠 `skipTranscript` 开关保证，而是**类型上做不到**——这是比基准更强的保证。

对照的偏差：基准在 dream 完成后向主 transcript 追加一条 `Improved N memories` 系统消息（`autoDream.ts:239-247`）。本仓**不追**：我们的 dream 在回合返回之后才结束，此时往会话里塞消息会插到下一次回合中间，语义位置本身是错的。要呈现结果走 `/dream-tasks`。

### 问 ②：触发失败如何降级

**四层全部 fail-open（对回合），一层 fail-closed（对成本）**：

| 失败点 | 行为 |
|---|---|
| 触发函数本身抛异常 | 后台任务内全捕获 + 记日志，**不向回合传播**（`catch (Exception)`，含 `OperationCanceledException`） |
| dream 执行失败 | `DreamResult.Failure` 只记日志；**回滚锁**让下个回合可重试（`RollbackAsync(priorMtime)`） |
| 进程退出时正在跑 | 锁体留着 pid，超过 `HolderStaleMs` 或被判定为死 pid 后由下一个进程 reclaim —— 不会永久卡住 |
| 门控读不到（锁文件损坏/会话扫描抛错） | 按「从未合并」处理（`0`）＝放行；扫描失败返回空列表＝会话门拦住，**不会误触** |

回合侧的保证是结构性的：`AutoDreamStopHook.OnStopAsync` **不 await** dream 任务，只 `_ = TriggerAsync(...)` 后立刻返回。

### 问 ③：默认开关与频率门

基准：默认开（`autoDream/config.ts`）+ `minHours=24` + `minSessions=5` + `SESSION_SCAN_INTERVAL_MS=10min` 扫描节流 + 锁/pid 互斥。
本仓：`AutoDreamConfig` 同默认值，且 `WorkflowConfig.AutoDreamEnabled/AutoMemoryEnabled` 已桥接（`ServiceRegistration.AddDreamPluginServices`）。

本轮**不加新开关**，只把已有的四个门真正接到执行路径上（现状：时间门是假的，扫描节流与会话门只活在 `DreamGateCheckMiddleware`，而 `_pipeline` 在生产里恒为 null → 中间件全不跑）。

门序（严格按基准 `autoDream.ts:118-200`，顺序本身是语义）：

1. `Enabled` && `AutoMemoryEnabled`
2. **时间门**：`now - lastConsolidatedAt >= MinHours*3600_000`
3. **扫描节流**：`now - lastScanAt >= SessionScanIntervalMs`（进程内，`Interlocked`）
4. **会话门**：`ListSessionsTouchedSince(lastConsolidatedAt)` 去掉当前会话，`>= MinSessions`
5. **占锁**：原子 acquire，失败（别人在跑）直接放弃
6. 执行 → 成功落锁 / 失败回滚

---

## 2. 落点：为什么是 `IQueryStopHook` 而不是加一条 query-pipeline 中间件

第一反应是「给 `query-pipeline` 加个 `dream-trigger-mw` 节点」，因为那套是 profile 治理的正规入口。**这是错的**，原因是一次负向实验（§5.1）：

生产链的 query 中间件来自 **DI 集合**，不是 profile 组装的那条链：

```
Chat → QueryLoopMiddleware → IQueryEngine(QueryEngine)
      → _serviceProvider.GetService<IEnumerable<IQueryMiddleware>>()   ← 生产实际执行的是这个
      → QueryCoreMiddleware
```

而 `AddDataDrivenPipelines` 注册的 `MiddlewarePipeline<QueryMiddlewareContext>`（按 profile 组装、`DataDrivenPipelineComposition.cs:473`）**没有任何消费方**。也就是说：

- 往 `query-pipeline` 加节点 → `--dump-nodes` 里有、门禁绿、运行期**永不执行**（正是 TASK100 §25 记过的「声明 ≠ 消费」）；
- 顺带暴露一个独立缺陷：`query-rate-limit-mw` / `stop-hook-mw` 等 profile 声明对真实查询链**都不生效**（`stop-hook-mw` 之所以还在跑，是因为它自己有 `[Register(typeof(IQueryMiddleware))]`，走的是 DI 集合）。此项登记为总账新条目，不在本轮修。

`IQueryStopHook` 则是一个**真的空着的**扩展点：

- `QueryStopHookManager.ExecuteStopHooksAsync` ← `StopHookMiddleware`（`[Register(typeof(IQueryMiddleware))]`，DI 集合成员，**确实在执行**）← `QueryEngine.cs:276` 的 `OnCompleteHooks`；
- `resources/orders.yaml` 里 `query_stop_hooks: []` 已经占好位，注释写着「目前没有实现」；
- `IQueryStopHook.Priority` 的注释已经指定了取值方式（`OrderIndex.Default.OrderOf("query_stop_hooks", ...)`），说明这个位置本来就是给实现类留的 —— 只是没人填，也没人把 DI 里的实现收集进管理器。

所以本轮做两件事把这条空链接通：`QueryStopHookManager` 构造时吃 `IEnumerable<IQueryStopHook>`；`Pipelines` 项目（唯一同时引用 dream 与 `Core.Query` 的装配层）实现 `AutoDreamStopHook`。

`OnStopAsync` 返回 **`Continue()` 而不是 `Stop()`**：管理器遇到 `ShouldStop=true` 会短路后续 hook（`QueryStopHookManager.cs:164`），返回 `Stop()` 等于把「我干完了」误表达成「到此为止」。返回 `Continue()` 时若无其他 hook 要求停止，管理器末尾仍返回 `Stop()`（`:175`），结果相同但不会抢占别人。

---

## 3. 实现落点

| 文件 | 改动 |
|---|---|
| `modules/services/dream/client/ConsolidationLock.cs` | 新增 `TryAcquireAsync` / `ReleaseAsync`；锁体改 `{pid}:{tsMs}`；`ReadLastConsolidatedAtAsync` 兼容旧纯数字 |
| `modules/services/dream/core/services/AutoDreamTrigger.cs` | 新增。六个门 + fire-and-forget + 成功落锁/失败回滚；返回 `AutoDreamTriggerOutcome`（可测） |
| `modules/cognition/brain.query/query2/stop_hooks/QueryStopHookManager.cs` | 构造注入 `IEnumerable<IQueryStopHook>?`，逐个 `RegisterStopHook` |
| `modules/composition/pipelines/middlewares/AutoDreamStopHook.cs` | 新增。`Name`、`Priority`（orders.yaml 派生）、`OnStopAsync` 只投递不等待 |
| `modules/composition/pipelines/registration/DataDrivenPipelineComposition.cs` | `RegisterBaseMiddlewareServices` 里显式注册 `AutoDreamStopHook` 到 `IQueryStopHook` |
| `resources/orders.yaml` | `query_stop_hooks: [AutoDreamStopHook]` |

---

## 4. 记录在案的偏差

1. **子代理门缺失**：基准 `if (!toolUseContext.agentId) void executeAutoDream(...)`。本仓 `QueryOptions` 没有 agent 标识，`StopHookContext` 只有 `SessionId`/`Reason`/`Metadata`，运行期拿不到「这一轮是不是子代理」。加字段属契约扩张，本轮不做。实际影响被 ③（扫描节流 + 时间门 + 占锁）压到 0：子代理回合最多多发一次 `stat` + 一次目录枚举。
2. **锁体编码不同**：基准借文件 mtime 承载 `lastConsolidatedAt`、体里放 pid；本仓 `IFileSystem` 无文件级 mtime setter（TASK071 已记录），改为体内显式存毫秒时间戳。加占锁后体变成 `{pid}:{tsMs}`，仍然是「一次读全拿到两个值」，比 stat+read 两次更省。
3. **不追加 `Improved N memories`**：见 §1 问①。
4. **`_pipeline` 在生产恒为 null**：`MiddlewarePipeline<DreamContext>` 无人注册，`DreamFeature` 永远走 `ExecuteDirectAsync`。所以 `DreamGateCheckMiddleware` 那套「真时间门」在生产里一次都没跑过——这正是 ② 的来源。本轮不改 dream 的管道结构（属另一条线），只让自动触发路径自己持有完整的六门。

---

## 5. 验证

### 5.1 「profile 的 query-pipeline 声明不治理生产链」的取证

不是靠猜，是三条静态事实叠出来的：

1. `QueryEngine.BuildPipeline()`（`QueryEngine.cs:66-80`）取的是 `_serviceProvider.GetService<IEnumerable<IQueryMiddleware>>()`，**不是** `MiddlewarePipeline<QueryMiddlewareContext>`；
2. 全仓 `MiddlewarePipeline<QueryMiddlewareContext>` 只有两处引用：`DataDrivenPipelineComposition.cs:473` 的注册、`TestPipelineRegistration.cs:97` 的测试用注册 —— **没有任何 `GetRequiredService`/`GetService` 消费它**；
3. `StopHookMiddleware` 自身带 `[Register(typeof(IQueryMiddleware))]`，所以它之所以真的在跑，靠的是第 1 条那条 DI 集合，与 profile 声明无关。

反过来说：往 `query-pipeline` 加节点会得到「`--dump-nodes` 有、门禁绿、运行期不执行」。这条已登记为总账新条目（§6），不在本轮修。

### 5.2 单测

| 项目 | 结果 | 覆盖 |
|---|---|---|
| `Dream.Tests` | **212/212** | `AutoDreamTriggerTests` 12 项（六门逐条 + 并发互斥 + 失败/取消回滚 + 不抛异常）；`ConsolidationLockBehaviorTests` 新增 7 项（占锁互斥、活 pid 让出、死 pid 回收、超时回收、旧格式可抢、`ParseBody` 双格式、落锁后自己也不再抢） |
| `Pipelines.Tests` | **74/74** | `AutoDreamStopHookTests` 4 项（orders.yaml 登记 → `OrderIndex` 不抛；无 trigger 时 Continue；**不 await dream**；缺依赖降级为永不触发）+ 装配级串通 1 项（注册 → DI → 管理器构造期收集 → 钩子 → 触发器 → `IDreamFeature`） |
| `Brain.Other.Tests` | **13/13**（`QueryStopHookManagerTests`） | 构造期收集 DI hook、注入顺序无关而执行顺序按 `Priority` |

### 5.3 真机探针：本机缺一个前提，如实记录

原计划用 `jcc -p "..."` 跑一个真实回合看 `[DREAM-AUTO]` 三态。**本机没有任何供应商 API key**（`~/.jcc/settings.json` 里 7 个 vendor 的 `apiKey` 全为空，`profile=sensenova`），真实回合拿不到 LLM 响应就到不了 `OnCompleteHooks`，因此**回合级真机探针本轮没做**。

取而代之的证据是 §5.2 里那条**装配级串通用例**：它用真实 DI 容器 + 真实 `QueryStopHookManager` + 真实 `AutoDreamTrigger`，只把 `IDreamFeature` 与 `ISessionScanner` 换成假体（LLM 不是被测对象），断言「注册过的钩子确实被管理器收集并真的调到执行体」。剩下没被覆盖的只有「`StopHookMiddleware` 在 `OnCompleteHooks` 里调管理器」这一段，那是存量代码，已有 `StopHookReplyLoopTests` 覆盖。

配好 key 后要补的真机步骤（三行）：

```bash
JCC_DEBUGLOG=1 jcc -p "ok"     # 期望无 [DREAM-AUTO] 之外的副作用；会话数不足 → skip: session-gate
# 注入 MinHours=0 / MinSessions=1 后再跑一次 → fire ... / done ...
# 第三次 → skip: time-gate（证明时间门真闭，不是每回合都放行）
```

### 5.4 编译与产物核验

- 单项目：`Dream` / `Brain.Query` / `Pipelines` / `JoinCode(cli)` 均 0 警告 0 错误；跨域 `Build-AllDomains.ps1 -Changed` 见提交前记录。
- 产物：`Abstractions.Primitives.Utils.dll`（内嵌 orders.yaml）含 `AutoDreamStopHook`；`JoinCode.Pipelines.dll` 含 `AutoDreamStopHook` 且已进 `artifacts/bin/JoinCode/Debug/net10.0/`。

### 5.5 顺序类门禁（改 `orders.yaml` 的必付成本）

新增 `query_stop_hooks: [AutoDreamStopHook]` 落在两个 CI 门禁的管辖范围，必须跑：

| 门禁 | 实测 | 结论 |
|---|---|---|
| `Verify-ExplicitOrder.py` | **6.9s**，exit=0 | 真机比对：`jcc prompt` 段落 49/68 vs `sections.yaml`、`jcc --debuglog -p` 模块 9/9 vs `modules.yaml`，且特性里无 Order 数字 |
| `Verify-RegistrationOrder.py` | **11.3s**，exit=0 | 5 个列表 / 17 项与源码实现双向一致 |

跑的时候修掉一个**门禁自身缺陷**（不是本轮功能的一部分，但不修就带病）：`implementations()` 排除测试路径只写了 `/tests/`，而本仓测试项目目录名是 `xxx.tests/`（如 `guard.hooks.tests`），于是测试替身 `StubInterceptor`（`private sealed class ... : ICommandInterceptor`）一直被当成生产实现。此前没暴露，是因为 `command_interceptors` 声明了目录范围把它挡掉了；`query_stop_hooks` 未声明目录范围，一放开就露出来。修法两处：①排除条件补 `.tests/`；②未声明目录范围的列表做全仓对账（原逻辑 `if dirs else {}` 让这类列表的 `scoped` 恒空，「登记了不存在的实现」必然误报，等于永远填不进去）。修完仍是 17 项双向对账，`Verify-ExplicitOrder.py` 一行未改也依然通过。

> 性能教训（与功能无关但会反复踩）：这两个门禁合计 18 秒，曾被 `python xxx.py 2>&1 | tail -10` 拖成后台 16 分 26 秒后被杀、0 字节输出。原因是 `tail` 要等 stdin EOF，而 `Verify-ExplicitOrder.py` 内部 `subprocess` 起的 `jcc.exe` 会留下继承 stdout 的孙进程，EOF 迟迟不到。**跑门禁不要接 `tail`**，重定向到文件再读；两个门禁互不依赖，可以并行（墙钟取 Max 而非 Sum）。此教训已上升为强制条款：`AGENTS.md` §平台专属操作禁令「短任务脚本必须 30 秒内结束（超时即缺陷）」。


---

## 6. 后续（不在本轮）

- **`query-pipeline` profile 声明对 `QueryEngine` 不生效**（§2 的副产品）：要么把 `QueryEngine` 改成吃 profile 管道，要么把 `query-pipeline` 从 profile 里撤掉。两条都得动主链，单独立档。
