# TASK130 — 剩余事项清单与验收步骤（防遗忘台账）

> 用途：总账 [TASK103](TASK103-未清事项总账.md) 已把 #40/#42/#45/#46/#50/#53 全部闭合，
> 但「剩余」散落在 TASK103 的决策记录、各子任务文档和 MEMORY 里，容易漏。本文把
> **每一项的口径、证据、可复制验收命令、判定标准** 一次写全，施工前不需要再考古。
>
> 纪律：先写本文，再动代码。每闭合一项，回来划掉并写证据行。

## 0. 分类速查

| 类 | 含义 | 项 |
|---|---|---|
| A | 本机可施工、可自证 | A1 M1 用户消息前缀选中色（**核实＝早已实现**，本轮补测试）· A2 D7 `suppressOutput`（**已核实＝不适用**）· A3 斜杠命令取参审计（**已施工**）· A4 flag settings 策略锁（**已施工**）· A5 hook `systemMessage`（**已施工**）· A6 SessionStart `additionalContext` 零消费（**已施工 + 真机端到端**）· A7 HTTP hook 不解析 `hookSpecificOutput`（**已施工 + 真机端到端**）· A8 chatinit 其余节点主链零执行（**新发现，未闭合**）· A9 hook 配置 `$hook_type` 与基准 `type` 不一致且静默 0 条（**已施工**） |
| B | 只能人工 / 需外部条件 | B1 #52 双终端矩阵、B2 #51 Ctrl+B、B3 macOS Seatbelt |
| C | 需凭据或 MockServer 才能跑完 | C1 #40 回合级真机探针、C2 #53 低限额进程级（**已施工**，见 §C2） |
| D | 台账自身错误，需纠正 | D1「#15 待施工」已过期、D2 #53 表格行未划线、D3 D7 与总账 §3 矛盾 |

---

## A. 本机可施工

### A1. M1 — 用户消息 `❯` 选中态改 suggestion 色 —— **已实现，本轮补测试并纠正台账**

**一手证据（本轮核对）**
- 实现早已在：`MessageItems.cs` 的 `UserMessageItem.RenderSelectedAccent`（把 `❯` 前沿由 subtle 提到
  `theme.Suggestion`，正文前景不动），由 `ScrollBox.cs:164-167` 对 `HighlightItem` 在 `Render` 之后调用；
  `ScrollBox` 再叠一层 `ApplyHighlightBackground`（背景换 `MessageActionsBackground`）。
- 基准 `HighlightedThinkingText.tsx:23-24` 的 `isSelected ? suggestion : subtle` 已等价落地。
- **台账原写「待施工」是错的**（与 D1「#15 待施工」同类错误：实现走的是「渲染后覆写」而非「入参传 isSelected」，
  按函数签名搜不到就误判为缺失）。

**本轮补的东西**：`native_tui/tests/NativeTui.Tests/repl/MessageSelectionAccentTests.cs`
- 选中用户消息 → `❯` 前景 = `Suggestion`，正文前景不变；
- 未选中用户消息 → `❯` 保持 `Subtle`；
- 选中助手消息 → `●` 前景仍 `Text`（防「顺手改错另一个前缀」）。

**验收**：`dotnet test native_tui/tests/NativeTui.Tests -c Debug --no-build --filter "FullyQualifiedName~MessageSelectionAccentTests"` → 3/3 绿。

**教训**：判「某特性是否已实现」别只 grep 函数签名，要先找**渲染/消费侧的覆写钩子**
（`RenderSelectedAccent`、`ApplyHighlightBackground` 这类「后处理」机制）。

---

### A2. D7 `suppressOutput` — 已核实：**不适用**（2026-11-23）

**双侧证据**

| 侧 | 事实 |
|---|---|
| 基准 | `utils/hooks.ts:2557-2562`：`suppressOutput` 只抑制「hook 有**纯文本 stdout** 且 `exit=0` 时那条 `hook_success` attachment（含 `<hookName> completed`）」。`types/hooks.ts:56` 的字段描述就是 *Hide stdout from transcript* |
| 本仓 | `HookExecutorBase.cs:205` 注释声称「suppressOutput → 标记」，但 207–237 行**没有该分支**；`HookOrchestrator.cs:104` 把 `Stdout = null`，全仓无「hook stdout 进 transcript」的通道 |

**结论**：本仓不存在「hook stdout 被塞进 transcript」这一可抑制对象 —— 照 AGENTS 判定纪律
（实现不同 ≠ bug），`suppressOutput` **不适用**，D7 关闭，不补字段。
`HookExecutorBase.cs:205` 那句注释需改成「本仓 stdout 不入 transcript，故不实现」。

**顺带查出的真缺口（登记为 A5，非本项）**：同一段解析里的 `systemMessage` **解析了但没人消费**。

---

### A5. hook `systemMessage` 无生产消费点 —— **已施工（2026-11-23），含一处必须一起修的真断链**

**原始证据**

| 侧 | 事实 |
|---|---|
| 基准 | `utils/hooks.ts:2770-2780`：`if (result.systemMessage) yield createAttachmentMessage({type:'hook_system_message', content, hookName, toolUseID, hookEvent})` —— 与 `suppressOutput` **无关**，必定展示 |
| 本仓（改前） | `HookResult.SystemMessage`（`HookResult.cs:62`）已定义、`HookExecutorBase.cs:208-211` 已解析、单测 `HookOutputParsingTests.cs:49` 已断言；**生产侧零读取** |

**施工中发现的两个断链（比字段本身重要）**
1. **`SessionController.ProcessEvent` 用 `evt.Switch(...)` 但没传 `onSystemNotice`** →
   `ChatStreamEvent.SystemNotice` 在 CLI/TUI 主入口被**静默丢弃**（`Switch` 的 SystemNotice 分支对 null 回调是 no-op），
   连既有 `LoopInterventionMiddleware` 发的旁白也到不了用户。
2. **主代理路径完全不执行 UserPromptSubmit hook**：那条 hook 只在
   `ChatTelemetryPreHook`（chat 管道前置钩子）里跑，而 chat 管道只在 `_mainAgent is null` 的回落路径执行
   （`SessionController.StreamResponseAsync` 的分支 + 复制的 `PreProcessMainAgentAsync`）。生产主链（有主代理）
   上 `additionalContext` / `systemMessage` **从未生效** —— 同 #53 的「声明≠消费」变体。

**落点（已实现，14 个文件）**

| 层 | 改动 |
|---|---|
| 单实现 | 新增 `modules/apps/engine/app/middlewares/UserPromptSubmitHookRunner.cs`：一处执行 hook，产出 `(Blocked, AdditionalContext, SystemNotices)`；回落管道与主代理路径**共用**（避免两条链各写一份再漂移） |
| 生产方 | ① `ChatTelemetryPreHook` 改为调 Runner 并把旁白写进 `ChatMiddlewareContext.UserPromptSubmitSystemNotices`；② `SessionController.PreProcessMainAgentAsync` 调同一 Runner（阻断→`SessionTurnResult.Error`、注入→追加到 `PromptInjection`、旁白→`_consumer.OnSystemNotice`） |
| 投影 | `PreChatMiddleware` 把旁白投影成 `ChatStreamEvent.SystemNotice`（**只上屏、不进助手正文、不进上下文历史**） |
| 出口契约 | `IEventConsumer.OnSystemNotice`（DIM 空实现，既有实现方零改动） |
| 主入口 | `SessionController.ProcessEvent` 传 `onSystemNotice` → `_consumer.OnSystemNotice`（不写 `fullResponse`） |
| TUI | `BridgeEvent.SystemNotice`（两端镜像 + `system_notice` 判别值）、`InProcessEventConsumer`/`BridgeEventConsumer` 显式实现、`ReplBridgeDriver` 落 `SystemNoticeItem`（dim 整行、**不触发 EndTurn**） |
| CLI | `CliEventConsumer.OnSystemNotice` 交互模式 dim 行打印；NDJSON 模式暂不投影（SDK 协议无 attachment 消息类型，不臆造字段） |

**验收（真跑）**

| 项 | 结果 |
|---|---|
| `UserPromptSubmitHookRunnerTests` | 6/6（阻断/PreventContinuation/两出口分离/空字段/无编排器 fail-soft） |
| `SessionControllerTests`（新增 `SystemNotice_DispatchedWithoutEnteringResponse`） | 40/40；**负向验证**：临时把 `onSystemNotice` 改成空 lambda → 该条转红（`Assert.Single() Failure: The collection was empty`），恢复后转绿 |
| `BridgeSystemNoticeTests` | 3/3（落 item / 不结束回合 / 不进 assistant_message） |
| `MessageSelectionAccentTests` | 3/3（A1） |
| `InProcessEventConsumerTests` | 23/23（含新增空串不发） |
| 编译 | `Host.Tests`、`NativeTui.Tests`、`NativeTui.Host.Tests`、`JoinCode.csproj`（jcc）均 0 错误 |
| 真机冒烟 | `JCC_DEBUGLOG=1 jcc -p hi` → 16 条 `[ORDER] middleware`，照常打到模型 401（无 key），无新崩溃 |

**仍缺（如实登记，未做）**
- ~~其余 hook 事件（PreToolUse/PostToolUse/Stop/SessionStart…）的 `systemMessage` 仍未投影~~ — **已由 TASK135 闭合**（统一旁白通道 + 顶层 `systemMessage` 解析；Guard 643/643、Host 1375/1375、真机两条 `[HOOK-NOTICE]`）。
- NDJSON/agentMode 无投影（协议缺 attachment 类型）。
- **新发现 A6**：`SessionStartHookResult.AdditionalConfig` **零读取** —— `SessionStartHookManager` 把
  `additionalContext` / `initialUserMessage` 聚合进这个字典，但全仓没有任何消费方读取它
  （`grep "AdditionalConfig"` 只有定义与写入两处）。TASK130 早前把「写入」误记成「有消费点」。

---

### A6. `SessionStartHookResult.AdditionalConfig` 零消费 —— **已施工（2026-11-24）**

**三处断链（自上而下；P0 才是根因，只修 P2 等于白改）**

| # | 层 | 事实（file:line） | 后果 |
|---|---|---|---|
| P0 | 解析 | `HookExecutorBase.ParseHookSpecificOutput`（`guard_hooks/execution/hook_executors/HookExecutorBase.cs:207-238`）只读 `systemMessage` / `permissionDecision` / `updatedInput`，**不读 `additionalContext` / `initialUserMessage`**；exit-0 纯文本分支把 stdout 映射成 `Message`（:148） | 命令型 hook（bash/function）**根本无法产出**这两个字段 → 下游全是从未被执行过的死代码。**A5 的 `UserPromptSubmit.additionalContext` 对命令型 hook 同样是死的**：只有 `AgentHookExecutor`/`PromptHookExecutor` 内部走 `HookResult.Success(additionalContext:)` 才生效 —— A5 验收没覆盖这一层 |
| P1 | 聚合 | `SessionStartHookManager.cs:61-67` 写进弱类型 `AdditionalConfig` 字典 | 键名靠字符串约定，改键名不会编译失败 |
| P2 | 消费 | `SessionStartHookMiddleware.InvokeAsync:29-43` 只读 `ShouldProceed` / `Message` | 内容被丢弃；`grep AdditionalConfig` 全仓只有「定义 + 写入」两处 |

**通道决策（两条更省事的路都被证据否掉）**

| 候选 | 否决理由 |
|---|---|
| 写进会话日志 `IChatContextManager.AddSystemMessageAsync` | ① resume 时 `ContextPersistenceService.LoadAsync:57-68` 把 system 消息当**静态提示词**处理 —— prompt 非空则静默丢弃、为空则被它顶替；② 进 transcript |
| 只写一条动态系统消息（`AddDynamicSystemMessageAsync`） | `SystemPromptMiddleware:51` **每回合**先 `ClearDynamicSystemMessagesAsync` 再重建 → 首轮之后消失（台账预警的「注入了但被覆盖」实测为真） |
| **采用：会话级 store + 独立中间件每回合补写一条动态系统消息** | 不被清（每回合重写）、不进 transcript、文本恒定故前缀缓存仍命中、按 sessionId 隔离防串会话 |

**落点（已实现）**

| 层 | 改动 |
|---|---|
| 解析 | `HookExecutorBase.ParseHookSpecificOutput` 增读 `additionalContext` / `initialUserMessage`（`TryGetString` 安全取值，非字符串不抛）；`ParseHookOutput` 增可选参数 `HookEvent? sourceEvent`，exit-0 纯文本 stdout 对 **SessionStart / UserPromptSubmit** 映射为 `AdditionalContext`（其余事件保持 `Message`，不扩大影响面）；调用方 `BashCommandHookExecutor` 传 `input.Event` |
| 契约 | `SessionStartHookResult` 增类型化 `AdditionalContext` / `InitialUserMessage`（替代按字符串键读 `AdditionalConfig`）；`AdditionalConfig` 继续承载 `updatedInput` |
| 聚合 | `SessionStartHookManager` 多条 hook 的 additionalContext 以空行拼接（顺序 = 配置顺序） |
| 承接 | 新增 `ISessionStartContextStore` + `SessionStartContextStore`（单例、`Volatile` 换引用、按 sessionId 命中；**不用 `lock`** —— JCC4008 基线只减不增） |
| 写入 | 新增 `SessionStartHookRunner`（两处调用点共用一份逻辑）；`SessionStartHookMiddleware`（chatinit 链，每 init 一次）与 `SessionController.EnsureSessionStartHookAsync`（主链，每会话一次，先置位再执行防重复副作用）都调它；无内容时写 null 防残留 |
| 消费 | 新增 `session-start-context-mw`（`preprocess-prepare-pipeline`，priority 110，紧跟 `system-prompt-mw`）→ 仅当 store 的 sessionId 与当前 `IChatContextManager.SessionId` 一致才补写动态系统消息（fail-soft：写失败只打点不拦回合） |
| 登记 | `resources/profiles/base.yaml` 加节点 + `DataDrivenPipelineComposition` 加 `F("preprocess-prepare-pipeline:session-start-context-mw", ...)` + `TestPipelineRegistration` 同步（实例型节点：显式登记 + profile 声明，符合 TASK100 S8 规程） |
| 附带修 | `ProcessStartInfoBuilder.Build` 补 `RedirectStandardInput`（见下方 P0-1）——不加这一行，整条 hook 链路真机永远跑不通 |

**`initialUserMessage` 判定：不适用（解析但不消费）**
- 语义是「会话起始插一条 user 消息」。本仓**没有**「无输入自动开轮」模式：`SessionInitStep` 之后一律等用户输入，
  `jcc -p <prompt>` 也必须有输入。自动插入会凭空产生一个用户回合 + 一次模型调用（计费、transcript 污染）。
- 因此：解析进 `HookResult` / `SessionStartHookResult` 让契约成立，**不接消费端**；`SessionStartHookMiddleware`
  遇到非空 `InitialUserMessage` 记一条 `LogDebug` 说明已忽略（判定在 `SessionStartHookRunner`），并留负向测试（断言它不进 store、不进动态系统消息）。
- 与 D7 `suppressOutput` 的区别：那个是「本仓无可抑制对象」，这个是「本仓有对象但语义会伤害主链」——所以要留防回归测试。

**验收（已跑，含真机）**

| 项 | 结果 |
|---|---|
| `HookOutputParsingTests` 增 7 条（3 事实 + 1 非字符串容错 + 3 纯文本分事件） | 全绿；`Guard.Hooks.Tests` 全量 **612/612** |
| `SessionStartHookManagerTests`（新建 6 条） | 单 hook 进类型化字段、两条空行拼接、`updatedInput` 留在 `AdditionalConfig`、阻断不带上下文 |
| `SessionStartContextTests`（新建 9 条） | 同 session 恰一条动态消息、store 空零条、**不同 session 零条（防串会话）**、写失败仍调 next、无依赖不抛 |
| `SessionStartHookMiddleware` 写 store / 清空 / `initialUserMessage` 不入 store | 3 条覆盖 |
| **负向验证** | 把 `AddDynamicSystemMessageAsync` 换成空操作 → `Preprocess_SameSession_InjectsSingleDynamicMessage` 转红（`ContainSingle` 失败），还原转绿 |
| 门禁 | ExplicitOrder / RegistrationOrder / NodeFactoryCoverage（**257 节点 / 257 工厂**）/ NoLockPrimitives / StyleGate 全绿 |
| `--dump-nodes` | `preprocess-prepare-pipeline` = 7 节点，`session-start-context-mw (priority=110)` 紧跟 `system-prompt-mw` |
| **真机端到端**（临时在 `~/.jcc/settings.json` 配 SessionStart hook，跑完已按备份还原、md5 一致） | `hook result outcome=Success addCtx=26` → `[SESSION-CONTEXT] 收到 additionalContext 26 字符` → `[SESSION-CONTEXT] 注入 26 字符`（即真的进了 `AddDynamicSystemMessageAsync`）。清理打点后复跑：`[ORDER] middleware preprocess-prepare-pipeline: … > SystemPromptMiddleware > SessionStartContextMiddleware > MemoryRecallMiddleware > …`，无残留打点 |

**施工中另外挖出的两个 P0（都不在 A6 原诊断里，必须一起记账）**

1. **`ProcessStartInfoBuilder.Build` 从不置 `RedirectStandardInput`** —— 而 `ProcessOptions.StandardInput` 的契约注释明写
   「非 null = RedirectStandardInput=true + 写入 + 关闭」，`PhysicalProcessService.ExecuteAsync:45-48` 也无条件访问
   `process.StandardInput`。结果：**任何带 stdin 的一次性进程直接抛「StandardIn has not been redirected」**，
   也就是**全部 bash 型 hook 从未执行成功过**（A5 的 systemMessage、A6 的 additionalContext 都因此只有代码没有真机证据；
   表现是 `HookOutcome.NonBlockingError`，被当成「hook 自己失败了」而静默吞掉）。
   已修（`ProcessStartInfoBuilder.cs:56-63`）：`RedirectStandardInput = options.StandardInput is not null`，
   并补 ① 构建器 2 条断言 ② **真机进程用例**（`bash -c cat` + stdin 回显 `stdin-probe-42`）→ `Infra.IO.Tests` **330/330**。

2. **chatinit-pipeline 在主链上从未执行** —— `IChatInitializer` 在生产主链只有两个调用方：`InitializeHandler`（admin `Initialize`，
   由 `ChatService.InitializeCoreAsync` 懒触发），而 `ChatService` 只在 `_mainAgent is null` 的回落链跑。
   `SessionInitStep.cs:55` 把 `IChatInitializer` 解析进局部变量 `ci` 后**从未使用**（git 历史无 `ci.InitializeAsync`，一出生就是死变量）。
   所以主链上 SessionStart hook 连「被调用」都没有 —— 这是 A6 真机第一次跑无任何日志的原因。
   本轮只接了 `SessionStart` 这一半（`SessionController.PreProcessMainAgentAsync` 前的一会话一次调用，逻辑复用
   `SessionStartHookRunner`，不复制实现）；**其余三个节点仍未在主链执行**，单列 A8，不夹带。

---

### A8. chatinit-pipeline 其余节点在主链仍零执行（**已分诊：2 项不缺、1 项真缺已修**）

**证据**：见上条 P0-2。主链（`_mainAgent is not null`）只补了 SessionStart hook 一次；`context-load-mw`、
`cost-restore-mw`、`config-change-start-mw` 仍只在回落链（`ChatService.EnsureInitializedAsync` → admin `Initialize`）跑。

**分诊（逐个查「有没有等价实现」，不按「中间件没跑＝功能缺失」一刀切）**

| 节点 | 主链缺不缺 | 证据 |
|---|---|---|
| `context-load-mw` | **不缺** | 历史恢复由宿主各自完成：CLI `SessionResumeStep` → `chatService.LoadSessionMessagesAsync`；TUI `HistorySeeder` → `LoadTranscriptAsync`。`LoadContextAsync` 的生产调用点只有这个中间件本身 —— 接上反而会**重复加载**同一份历史 |
| `cost-restore-mw` | **不缺** | `SeedCarryover` 真实调用点有三处：`ContextPersistenceService.LoadAsync`（随消息恢复走）、`ResumeCommand`（恢复会话时）、以及本中间件。前两处覆盖主链 |
| `config-change-start-mw` | **真缺** | `IConfigChangeNotifier.StartMonitoring` 全仓**唯一**调用点就在此中间件；`SettingsChangeApplier` 自己在构造时订阅了 `ConfigChanged`，但没人启动 watcher → **AGENTS.md / rules / commands / settings.json 的热重载在主链从未生效**（app 层也没有第二个 FileSystemWatcher） |

**施工面（只修真缺的那一个）**：`EngineSessionFactory.CreateSessionAsync`（CLI 与 TUI 共用入口）在
`StartModelFetchBackground` 旁启动一次监控：`services.GetService<IConfigChangeNotifier>()?.StartMonitoring(fs.GetCurrentDirectory())`，
并打 `[CONFIG-WATCH]` 打点。不接整条 chatinit —— `ChatInitContext.ToolUseContext` 是 `required`，而 app 层
**从不构造** `ToolUseContext`（生产里只有 `ChatService._toolUseContext` 一个实例，且为 `private`），
强行接整链只会拿到一个与工具循环无关的孤儿实例，`ContentReplacementState` 装配变成无效动作。

**验收**

| 项 | 判据 |
|---|---|
| 真机打点 | `JCC_DEBUGLOG=1 jcc -p hi` 出现 `[CONFIG-WATCH] start`（此前 `StartMonitoring` 在整份日志里零命中） |
| 回归 | `dotnet test tests?` 相关：`Guard.Hooks.Tests`（含 `ConfigChangeStartMiddlewareTest` 既往用例）全绿；`--dump-nodes` 节点数不变（未新增节点） |
| 门禁 | StyleGate + 四道节点门禁 |
| 不做 | 不接 `context-load` / `cost-restore`（已有等价路径，接上即重复）；`session-start-hook-mw` 已在 A6 单独接（见 P0-2） |

**回头修正（CI #734 稳定红，2026-11-24）**

初版把 `StartConfigChangeMonitoring` 无条件放在 `EngineSessionFactory.CreateSessionAsync`，CLI 一次性
`-p` 进程也被迫构造 watcher Actor。CI 上 `e2e / E2E - Provider + Cluster` 连续两次稳定失败，退出码 **210**：

| 证据 | 值 |
|---|---|
| `210` 的来源 | `ExitCode.AwaitTimeout = 1234`，Linux 进程退出码截低 8 位：`1234 & 0xFF = 210` |
| 主流程 | `[RUN] pipeline done, exitCode=0` **已打出**，jcc 自测启动耗时 1.9~3.3s |
| 测试耗时 | 32s —— 主流程结束后进程未退出，被自己的 `--await 30s` 看门狗强杀 |
| 对照 | #733（无 A8）该 job 3~6s 通过；#734（含 A8）两次稳定 32s 失败；A7/A3 均不碰进程生命周期 |

根因不是热重载功能本身错了，而是**启动点选错**：热重载是长驻会话（GUI/TUI）的能力，一次性进程跑完即退出，
`ConfigChanged` 没有存活的消费者；watcher 的 `Actor.DisposeAsync`（consumer 退出 + in-flight 收尾 +
`FileSystemWatcher.DisposeAsync`）被塞进 Main 返回后的退出关键路径，Linux inotify 上拖过 30s 看门狗。

**修正**：策略下沉到 `CommandLineOptions.NeedsConfigHotReload => !IsNonInteractiveMode`（唯一数据源），
会话工厂只在长驻会话启动监控；`-p` / 非交互 / headless 一律跳过。

**修正后验收**

| 项 | 判据 |
|---|---|
| 红测试 | 先加 4 条 `NeedsConfigHotReload_*`，编译报 CS1061（属性不存在） |
| 绿测试 | `dotnet test test/unit/host.tests --filter FullyQualifiedName~NeedsConfigHotReload` → 4/4 |
| 真机（一次性） | `JCC_DEBUGLOG=1 jcc -p hi` 的 `[CONFIG-WATCH]` 命中数 = **0**（改前为 1）；`pipeline done` 正常 |
| 策略对侧 | 单元测试锁定交互 / `ForceInteractive` 时 `NeedsConfigHotReload = true`，调用点 `if (options.NeedsConfigHotReload)` 已代码审查确认。TUI 需真实终端，重定向 stdin 会在进入会话工厂前被拒（`交互模式需要真实终端`），故不做假真机验收 |
| CI | `e2e / E2E - Provider + Cluster` 恢复 3~6s 通过；退出码不再是 210 |

---

### A9. 用户侧 hook 配置格式与基准不一致（`$hook_type` vs `type`），且错写时**静默 0 条**（**已施工**）

**证据**：`HookCommand` 的多态判别名是 `$hook_type`（`HookCommand.cs:6`），而基准/官方文档写 `"type": "command"`。
按官方格式写配置 → `JsonFileHookConfigurationProvider.LoadHooksAsync` 反序列化失败 → 该来源 **0 条**，
且没有任何 warning（异常被 `catch` 后只 `LogError` 进日志文件，CLI 侧完全无感）。本轮真机第一次冒烟就踩了：
`hooks found config=0`，排查了三轮才发现是判别名不同。

**落点（已实现）**

| 环节 | 实现 |
|---|---|
| 别名接受 | `NormalizeHookTypeDiscriminator(json)` 在反序列化前把每个 hook 对象的 `type` 重写为 `$hook_type`（已有该键则不动，两种写法可混用）。**判别名必须是对象的第一个属性**，所以是「新建 JsonObject 后按原顺序搬字段」而不是就地追加 —— 就地追加 STJ 会抛 `TypeDiscriminatorPropertyName` 位置错误 |
| 唯一来源 | `JsonFileHookConfigurationProvider.DiscriminatorProperty` 常量 + 单测断言它与 `HookCommand` 上 `JsonPolymorphicAttribute.TypeDiscriminatorPropertyName` 一致（防两处漂移） |
| 静默失败告警 | 先算 `declaredCount`（配置里声明的 hook 条数），解析完成若 `declaredCount > 0 && hooks.Count == 0` → `LogWarning` 明示「声明了 N 条但一条也没解析成功」 |
| 非法 JSON | `NormalizeHookTypeDiscriminator` 捕获 `JsonException` 后原样返回，仍由原解析路径报错（不吞错） |

**验收**：`LoadHooksAsync_基准type写法_解析成功`（`type` → `BashCommandHook`、事件 = SessionStart）、
`LoadHooksAsync_两种写法混用_各自解析`（2 条）、`DiscriminatorProperty_应与HookCommand多态特性一致`；
负向验证：把 `normalized = json` 关掉 → 前两条转红，还原后 `Guard.Hooks.Tests` 615/615 绿。
注意：**改完要重编测试项目**，只编 `Guard.Hooks` 时 `--no-build` 的测试跑的是测试输出目录里那份旧 DLL（会假绿）。

---

### A7. HTTP 型 hook 不解析 `hookSpecificOutput`（**已施工 + 真机端到端**）

**证据**：`HttpHookExecutor.ParseJsonResponse`（:129-155）是**另一份**实现，只取 `decision/reason/continue`，
不调基类的 `ParseHookSpecificOutput` → http hook 同样产不出 `systemMessage` / `permissionDecision` /
`additionalContext`。与 A6 的 P0 同源（协议解析两份实现）。
**而且它没有一行测试**（`grep HttpHookExecutor` 只在生产代码里命中），所以漂移了也没人发现。

**逐项差异表（删复制品前必须逐条判去向）**

| # | 差异 | 基类行为 | HTTP 现行为 | 处置 |
|---|---|---|---|---|
| 1 | `hookSpecificOutput` | 解析（A6 已补全） | **不解析** | 采用基类（这就是修复本体） |
| 2 | `message = Reason ?? Message` | 只取 `Reason` | `Reason ?? Message` | **把基类改成 `Reason ?? Message`**（`HookDecision.Message` 在 DTO 注释里就是「面向用户的提示消息」，只取 Reason 是漏读；现有测试无一断言该分支，属纯增益） |
| 3 | `stopReason` | 透传 | 丢弃 | 采用基类 |
| 4 | `continue` 与 `decision=block` 同时出现时谁赢 | block 赢（后赋值） | continue 赢（后赋值） | 采用基类：显式 block + `continue:true` 是自相矛盾输入，取更保守的 block |
| 5 | `LlmJsonHelper.Deserialize` 是否带 Logger | 带（宽容修复提示进日志） | 不带 | 采用基类 |
| 6 | 非 JSON 的 2xx 响应 | `Success(message: body)` | `Success(message: "HTTP 200: body")` | **保留 HTTP 特有的状态前缀**（HTTP hook 的 body 常是纯文本，前缀是排查线索） |
| 7 | 非 2xx | 无对应语义 | 403 → Blocking；其余 → NonBlockingError | **保留**（HTTP 状态码语义，基类无此概念） |

**落点**：删 `HttpHookExecutor.ParseJsonResponse`，`ParseHttpResponse` 的 JSON 分支改调基类
`ParseJsonResponse(body, body, string.Empty)`（protected virtual，同程序集内可见），捕获异常后回落
`HTTP {code}: {body}`；基类 `ParseJsonResponse` 的 `Message` 改 `Reason ?? Message`。

**验收（先红后绿）**

| 项 | 判据 |
|---|---|
| 新增 `HttpHookExecutorTests`（此前 0 条） | ①2xx + `hookSpecificOutput.additionalContext` → 字段落地（**修复前必红**）；②2xx + `permissionDecision:deny` → `PermissionDenyResult`；③2xx + `systemMessage` → 落地；④2xx + `{"message":"..."}` → `Message` 非空（#2 回归保护）；⑤2xx + 纯文本 → `Message` 带状态前缀（#6 不被改掉）；⑥403 → Blocking；⑦500 → NonBlockingError；⑧`stopReason` 透传 |
| 基类回归 | `HookOutputParsingTests` 现有 7 条不变绿；`Guard.Hooks.Tests` 全量绿 |
| 负向验证 | 把 `ParseHttpResponse` 的 JSON 分支改回旧实现（只读 decision/reason）→ ①③ 转红 |

**验收结果（2026-11-24）**

| 项 | 结果 |
|---|---|
| `HttpHookExecutorTests`（新建，11 条） | 11/11 绿 |
| `Guard.Hooks.Tests` 全量 | **626/626**（原 615 + 11） |
| 负向验证 | 临时覆写 `ParseJsonResponse` 为旧实现 → **5 条转红**（additionalContext / systemMessage / updatedInput / permissionDecision / stopReason），删除后转绿 |
| `Verify-StyleGate.py` | 1814 项全绿 |
| 解析实现份数 | 全仓只剩基类一处（`grep "override HookResult ParseJsonResponse"` 零命中） |
| 真机端到端 | 本机起 python HTTP 服务（只回 `hookSpecificOutput.additionalContext`），`~/.jcc/settings.json` 临时加 `{"$hook_type":"http","url":"http://127.0.0.1:9913/hook"}`：请求体 189 字节 snake_case 送达服务端 → `[SESSION-CONTEXT] 收到 additionalContext 31 字符` → `注入 31 字符`。跑完 `settings.json` 按备份还原（md5 一致）、删除临时 `trusted_folders.json`、停服务 |
| 注意 | 纯文本 2xx 的 Message 实际是 `HTTP OK: body`（`StatusCode` 渲染的是 ReasonPhrase 不是数字），断言按实际形态写 |
| 门禁 | StyleGate + 四道节点门禁 |

---

### A3. 斜杠命令取参审计（46 个 `GetNormalizedArgs` 调用点）

**背景**：`slash_call` 把参数 JSON 整体放进 `Arguments`。`GetNormalizedArgs`（`IChatCommand.cs:108-120`）
**已经**处理了单层 JSON：以 `{` 开头时用 `GetSplitArgs` 抽值再**用空格拼回**；非 JSON 才 Trim。
所以「JSON 整串被当参数值」这个原始隐患已不存在 —— 剩下的真风险只有一种：
**多字段 JSON（如 `{"model":"x","persist":true}`）被拼成 "x true"**，单值命令若把整串当值就会失败
（`/model` 已改 `GetSplitArgs()[0]`）。审计时按此口径判断，不要把已修好的情形重复计入。

**取证命令（可复制）**
```bash
cd /d/project/w2
grep -rn "GetNormalizedArgs" --include=*.cs modules | grep -v "/obj/" | wc -l   # 46
grep -rn "GetNormalizedArgs" --include=*.cs modules | grep -v "/obj/"
```
**本轮已测的口径（2026-11-24）**：46 处 = 1 处定义（`IChatCommand.GetNormalizedArgs`）+ 1 处测试引用 +
**44 个真实调用点**。按「拿到之后怎么用」粗分：

| 用法 | 处数 | 结论 |
|---|---|---|
| 继续 `.Split(...)` / `ParseFlags(...)` 做多段解析（Resume/Goal/SandboxToggle/Hooks/Diag） | 5 | **已等价安全**：`GetNormalizedArgs` 的 JSON 分支本来就是「抽值再空格拼回」，与 TUI 手输形态一致 |
| 整串当单值（Color/Profile/Vendor/Effort/Statusline/Init/Rewind/Tag/Btw/Rename/Compact/Fork…） | 36 | **分两类**：自由文本类（`Btw`/`Rename`/`Compact` 的自定义指令）必须整串 → 保留；单值枚举类只要 `slash_call` 只送一个字段就正常，**只有多字段 JSON 会拼成 `"x true"` 才出错** |
| 已显式绕开（ModelCommand 注释 + `GetSplitArgs()[0]`、ThinkbackCommand 重复调用） | 2 | 参照实现，可作为 ② 类改法模板 |

**结论（把「46 处隐患」收缩成可判定的一小撮）**：绝大多数调用点**不需要改**。
真需要逐条判定的是「**同时满足**：有 ≥2 个参数 且 把整串当单值用」的命令（如 `Export`、`Review`、`Install` 这类
`<子动作> <目标>` 形态）。下一步按此筛选再逐条补 `GetSplitArgs()` 用例，而不是 44 个文件一刀切。
**逐条判定口径（三分类，写进审计表）**
- **① 需要整串**（自由文本类，如 `/plan` 的描述、`/memory search` 的查询词）→ 保留；
- **② 需要单值**（如 `/model <name>`、`/theme <name>`）→ 改 `GetSplitArgs()[0]`，并补用例覆盖
  「JSON 形态入参」与「裸文本入参」两种；
- **③ 二者混用**（先整串判空、关键分支再取值）→ 逐分支确认，不能一刀切。

**验收**：每个 ② 类改动配 `composition_tests`（`ColorCommandTest` 同目录）用例；
改完跑
```bash
dotnet test modules/composition/composition_tests/Composition.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~Command"
```

**逐条审计结果（2026-11-24，43 个命令文件）**

按「是否真的会因空格拼接而取错值」逐条看，结论分两类：

| 类 | 命令 | 判定 |
|---|---|---|
| 自由文本整串（必须整串，**不改**） | `Btw` `Rename` `Compact` `Feedback` `Plan` 的描述、`Falv` 的假定内容 | 正确：这些就是用户原话 |
| 多参数 `子命令 + 目标`（**重点查**） | `Plugin`(install/uninstall/enable/disable) `Tag`(add/remove) `Workflows`(run/status) `Hooks`(add/remove/test) | **发现真 bug（见下）**，非 JSON 拼接问题 |
| 单值枚举（`slash_call` 只送一字段即可） | `Color` `Profile` `Vendor` `Effort` `Statusline` `Init` `Rewind` `Copy` `Advisor` `Switch` `Trust` `Review` `Install` … | 无隐患：整串即唯一值 |

**真正的 bug 不在「多字段 JSON」，而在「前缀命中」**：这些命令原来是
`args.StartsWith("add")` + `args["add".Length..]`，于是

- `/tag address` → 命中 `add`，标签名变成 `ress`（**静默写入一个错误标签**）
- `/workflows runner` → 命中 `run`，工作流名变成 `ner`
- `/plugin installer.exe` → 命中 `install`，路径变成 `er.exe`

**修法（已实现）**：`ChatCommandBase.TryTakeSubCommand(args, subCommand, out rest)` —— 子命令后必须是串尾或空白；
Plugin(4)/Tag(2)/Workflows(2)/Hooks(3) 共 11 处分支改用它。**全仓只剩这一份前缀判定**。

**验收结果**

| 项 | 结果 |
|---|---|
| 新增 `SubCommandBoundaryTest`（13 条：11 组词边界理论值 + 2 条 `/tag` 行为） | 13/13 绿；`/tag address` 断言 `AddTag` **零调用** |
| 负向验证 | 删掉词边界那 3 行 → **5 条转红**（address/addx/runner/installer.exe + `/tag address`），还原转绿 |
| `Composition.Tests` 全量 | **1172/1172** 绿 |
| 注意 | 该测试项目首次构建报大批 `CS0234`（引用程序集过期），`dotnet restore --force` 后正常 —— 与本改动无关 |

---

### A4. flag settings 路径的策略锁（**已施工，2026-11-24**）

**判定：真安全缺口，不是边界说明**

基准 `src/utils/sandbox/sandbox-adapter.ts:647-664` 的 `areSandboxSettingsLockedByPolicy()` 使用
`overridingSources = ['flagSettings','policySettings']`：任一源显式定义
`sandbox.enabled` / `sandbox.autoAllowBashIfSandboxed` / `sandbox.allowUnsandboxedCommands` 即锁定本地 `/sandbox` 修改。
`src/main.tsx:438-474` 的 `--settings` 支持文件路径或内联 JSON，非法 JSON / 文件不存在直接启动报错退出。
本仓此前既无 `--settings` 入口，`ConfigLoader` 也从未传 `flagSettingsSource`，属于「打印出来≠生效」的真缺口。

**落地**

| 层 | 实现 |
|---|---|
| CLI | `JccCliArg.Settings` + `CliArg.Settings`，`jcc --settings <file\|inline-json>`；`-h options` 已显示 |
| 装配 | `CommandLineOptions.Settings` → `ApplicationBuilder.ParseArgs` → `EngineApplicationBuilder` 直接连接路径传 `options.Settings` |
| 加载 | `SettingsLoader.LoadFlagSettingsAsync` 支持文件/内联 JSON；显式来源 fail-fast（缺失/非法 JSON/解析失败抛 `ConfigurationException`） |
| 投影 | `SandboxPolicyResolver.Apply(config, policy, flag)`：flag + policy 合并锁定键并去重；`AllowManagedDomainsOnly` / `AllowManagedReadPathsOnly` 仍只认 policy |
| 来源提示 | `SandboxPolicyConfig` 新增 `FlagLockedKeys` / `PolicyLockedKeys`，`/sandbox` 按 `managed-settings.json` / `--settings` 分句提示下一步 |
| Hub | `ConfigLoader.ApplySandboxConfigAsync` 仅投影 sandbox，`LoadHubConfigAsync` 调用它；Hub 模式不再绕过 policy/flag 锁 |

**验收（基建实现 → 消费点 → 已实现 → 已验收）**

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `SettingsLoader.LoadFlagSettingsAsync`（文件/内联 JSON/fail-fast） | `ConfigLoader.LoadConfigAsync`、`ApplySandboxConfigAsync`、`config-print --settings` | ✅ | ✅ 定向 14/14 + 真实 `jcc --settings` 文件/内联 JSON 日志 |
| `SandboxPolicyResolver` flag+policy 锁定键合并 | `WorkflowConfig.SandboxPolicy`、`/sandbox on/off/switch/exclude` | ✅ | ✅ 单测（含来源字段）+ `/sandbox` 26/26 |
| `ConfigLoader.ApplySandboxConfigAsync` | `EngineApplicationBuilder.LoadHubConfigAsync` | ✅ | ✅ 单测 `ApplySandboxConfig_FlagSettings_AppliesPolicyLockForHubPath`；⚠️ 真实 Hub 进程未跑（见下） |

**证据命令与结果**

```powershell
# 真实进程：内联 JSON（直接连接路径）
jcc --settings '{"current":{"sandbox":{"enabled":true}}}' -p ping
# 日志： [SANDBOX-POLICY] locked=True keys=[sandbox.enabled]
#       [SANDBOX] startup warning: 已启用沙箱（sandbox.enabled=true）...

# 真实进程：文件形式
jcc --settings .xxx\a4-home\flag-settings.json -p ping
# 日志： [SANDBOX-POLICY] locked=True keys=[sandbox.enabled]（enabled=false 故无 startup warning）

# 真实进程：缺失文件 fail-fast
jcc --settings .xxx\a4-home\missing-flag.json -p ping
# 输出：⚠ 警告: Settings file not found for --settings: ...；exit=2（ConfigurationError）

dotnet test modules\guard\guard_config_tests\Guard.Config.Tests.csproj --no-build
# 1358/1358
dotnet test modules\composition\composition_tests\Composition.Tests.csproj --no-build --filter "FullyQualifiedName~SandboxToggleCommandTest"
# 26/26
```

**遗留**：真实 Hub 进程验收未跑（运行 `jcc` 时 A1 Windows 环境反复掉线，无法稳定完成 Hub 启动链）；
Hub 投影目前由 `ApplySandboxConfigAsync` 单测覆盖，不能当作进程级已验收。真实 `/sandbox` 来源文案由命令单测覆盖。

---

## B. 只能人工 / 需外部条件

### B1. #52 双终端键位矩阵（探针已就绪）

**现状**：`jcc --probe-terminal-input` 已可用（[TASK129](TASK129双终端输入验收.md)），
输出用例 4/4 绿，重定向守卫真机 exit=1；**矩阵 10 行 × 2 终端全部待测**。
采集卡、产物哈希登记命令、证据目录与空白判定表见 [TASK129 §3](TASK129双终端输入验收.md)（2026-11-25 就绪）。
**必须人工**：Windows Terminal（`wt.exe -w new powershell.exe -NoProfile`）与经典 Console Host
各开一个真实窗口，逐格记录「应用已消费 / 仅解码通过 / 宿主截获-未上报 / 未测」，附产物哈希。
**判定**：矩阵每格有结论 + 证据（窗口身份、按键、最小诊断输出），回填 TASK066 B6 与 TASK121。

### B2. #51 Ctrl+B 主会话后台化（已由 w2 分支 DSG054 承接并裁决）

**现状**：取证结论是「七类型/前缀、后台 agent 执行/通知/面板/停止均已等价；`AgentTask` 空壳不硬接」，
真正缺的是 Ctrl+B 把主会话整体后台化 —— 这会改变**并发查询语义 / transcript 归属 / UI 控制权**。
**裁决与承接**：设计稿与用户纠偏已落在 w2 分支
`docs/proposed/design/DSG054-主会话后台化Ctrl+B设计.md`（2026-11-24：用户纠偏「万物皆后台」，
阶段 0 查询身份地基先行），w4 B2 不再重复设计；实现进度以 DSG054 为准。

**设计定稿（2026-11-24）**：[DSG054](../../proposed/design/DSG054-主会话后台化Ctrl+B设计.md) §9。
§7「两查询并发需 7 项改造」是误判；§8「abort 前台 + 独立 query 续跑」虽与 Claude Code 基准
（`REPL.tsx:2555-2606` → `LocalMainSessionTask.ts:338-479`）一致，但用户明确否定其打断语义。
最终裁决为**万物皆后台**：查询从第一刻就是有身份的 task，前台只是订阅当前聚焦 task 的异步视图；
Ctrl+B 是纯视图切换，不 abort、不重跑、不重复计费。`ReplShell.cs:1926-1928` 的 Ctrl+B 当前实为光标左移，
阶段 1 需与视图切换分层处理。

**阶段 0（查询身份地基，2026-11-24 开工 → 2026-11-25 完成）**：ADR
[0165](../../proposed/adr/0165-everything-is-background-query-identity.md) 已 accepted。
红测试先行（四项目分别在新增 API/属性处编译失败），实现后全绿：

| 测试项目 | 结果 |
|----------|------|
| `NativeTui.Tests` | 3193 / 0 失败 |
| `Host.Tests` | 1379 / 0 失败 |
| `NativeTui.Host.Tests` | 177 / 0 失败 |
| `Brain.Context.Tests` | 967 / 0 失败 |

落地内容：
- `IReplEngine.Submit(...)` 返回 queryId、`Interrupt(queryId)` 定向取消；9 个实现/装饰器与测试桩全部透传。
- `BridgeEvent`/`BridgeRequest` 双端镜像新增 `QueryId`（`WhenWritingNull`）；`submit` 必带，
  生命周期与查询内部事件统一归因，会话级事件（`session_info`/`permission_mode_changed`/`background_task`）不带。
- `EngineTurnClient` 单回合槽 → `ConcurrentDictionary<queryId, QueryRun>`；**每条查询独立 consumer**
  （`_fullResponse`/`_thinkingContent` 不再跨查询串写）；忙时不再拒绝。
- 唯一仍拒绝：同 queryId 并发复用（身份冲突），错误文案含「占用者 + Interrupt(id) + 换新 id」可执行诱导。
- `bridge_serve` 的 `interrupt` 带不匹配 queryId 时不再误杀在途回合（迟到的 Esc 不杀新提交）。
- `ChatService` 回合状态入 `ChatQueryScope`（AsyncLocal）：`ToolUseContext` per-query；
  `ConversationTurn` 从会话级计数器原子分配后注入（**不**在 scope 内归零，否则每个查询都自称第 0 轮）。
- `SessionController` 改「每查询新建实例」，会话级 `SessionStart` hook 标记外置共享。

实现期处置的旧断言（避免被误读为「改测试变绿」）：
- 「忙时提交拒绝 + 回 Esc 诱导」前提被 ADR 0165 废除 → 改写为「同 queryId 并发复用被拒 + 诱导可执行」。
- 协议字节断言从「与无 queryId 旧形态等价」→「必带 queryId；`images` 为空仍不带字段」。
- 模拟引擎断言改用 `Submit` 返回值核对 `TurnStarted.QueryId`。

**阶段 1 前台侧（2026-11-25 完成，#100）** — Ctrl+B 纯视图切换：

- `ReplShell` 新增 `_foregroundQueryId`（前台订阅的身份）+ `_backgroundQueries`（每查询一个事件桶）；
  `DrainEngine` 首行拦截：归因到后台桶的事件只入缓冲不渲染，终态记完成并提示「ctrl+b 切回查看」。
- `Ctrl+B` 一个键两个互斥语义：前台忙 → 转后台（`_driver.EndTurn()` + 释放前台身份，**不调用 `Interrupt`**）；
  否则切回最近后台桶（未完成先 `BeginTurn`，再按序 `Apply` 补投）。
- 键位分层按基准 `schema.ts:48`：`task:background`（默认 `ctrl+b`）属 `Task` 上下文，`Chat` 上下文的
  `ctrl+b` 仍是光标左移。`HandleCtrlKey` 在全局单键分派**之前**判断，条件为
  `IsBusy || _backgroundQueries.Count > 0`（闲且无后台不接管，保住既有编辑行为）；
  `DispatchCtrlAction` 补上原先缺失的 `TaskBackground` 分支。
- 两个只有跑测试才暴露的坑：① 前台身份必须在 `TurnCompleted`/`TurnInterrupted` 时释放，
  否则下次提交被误判「前台仍有主」而静默吞掉（9 条既有提交测试同红）；② 不能用
  `_foregroundQueryId ??= Submit(...)`，同拍连提会丢输入 —— 改为「总是提交 → 有空位接管 → 否则注册为后台查询」。

验收：`NativeTui.Tests` **3199 / 0 失败**（新增 `QueryBackgroundTests` 5 条）、`NativeTui.Host.Tests` 178、
`Verify-StyleGate.py` 1817（新文件零违规）、`Verify-NoLockPrimitives.py` 64 处存量（未加锁）、
`Verify-ExplicitOrder.py` / `Verify-RegistrationOrder.py` 双向一致。

阶段 1 剩余：transcript 按 queryId 归属 + 独立 transcript 视图 + 真机 TUI 手工验收（#101）；
阶段 2：SDK/NDJSON 协议、`/clear` symlink 重链、逐 token 落盘。

### B3. macOS Seatbelt（需 macOS 真机 + 独立 ADR）

**证据**：TASK103:254 —— 当前显式报「未实现」，不进 provider 链。**外部条件缺失，不可在本机施工。**

---

## C. 需凭据 / MockServer

### C1. #40 回合级真机探针

**现状**：接线的两处缺陷（时间门假值、无原子占锁）已修，并补了「延迟解析到就绪」两条用例；
但**真实回合**没跑到 `OnCompleteHooks`（本机 7 个 vendor 的 apiKey 全空）。
**补验步骤**（[TASK126 §5.3](TASK126-dream自动触发-三问决策与接线.md)）：
配任一 vendor key → `JCC_DEBUGLOG=1 jcc -p hi` → grep `[DREAM-AUTO]`
（应看到 `hook resolved` 而非 `disabled`，且同一回合只触发一次）。

### C2. #53 低限额进程级验证 — **已施工（2026-11-24，`Wq01` E2E 常驻回归）**

**结果**：同进程用例（第 2 次查询抛 `RateLimitExceededException`、模型调用数停在 1）之外，
进程级证据已补齐 —— 两次提交只允许第 1 次真实进入 MockServer。

**实现**（比原计划的「连续两次 `-p hi`」更严格）：`-p` 每次都是新进程，限流计数器
（QueryEngine 管道单例）随之重置，两次独立进程**永远打不满限额** —— 原步骤设计不成立。
改用 bridge_serve 单进程双回合（与 CI E2E 同一 harness）：

- `Wq01Tests`（`test/mock/mock_server.e2e.tests/e2e/a_to_c/`）+ `WqQueryRateLimitScripts`：
  外部 profile `c2.yaml`（`inherits: base` + 覆写 `query-rate-limit-mw.max-requests=1`，
  经 `JCC_PROFILE_DIR` 注入）→ `bridge_serve --profile c2` → 同一 jcc 进程提交两回合：
  第 1 回合正常回复并消耗唯一限额；第 2 回合报「速率限制」且拿不到模型回复；
  断言 MockServer `/__control__/requests` 增量 **== 1**（被拒查询 0 请求）。
- **顺带修出真 bug**：`bridge_serve --profile X` 的 `--profile` 此前被忽略
  （子命令路由优先于全局 ParseArgs，BridgeServeCommand 自建 options 只读 vendor/model），
  引擎静默回退 base profile。已修：与 `--vendor`/`--model` 同构解析并带入 options.Profile。

**证据**：`.xxx/logs/p1-1-e2e-evidence.txt`（Wq01 + Ve06 多轮 + Streaming 超时验收 + Vd02 异常处理 4/4 通过）。

---

## D. 台账纠错（本次已改）

| 项 | 原状 | 纠正 |
|---|---|---|
| D1 | TASK103:152「#15 ultrathink 彩虹 → 待施工」 | **已实现**：`native_tui/src/NativeTui.Core/repl/UltrathinkHighlighter.cs`（`\bultrathink\b` + `charIndex % 7`），接入点在 `MessageRenderer.cs:32-36`，用例 `ReplComponentTests.cs:35` → 划掉 |
| D2 | TASK103:97 #53 表格行未划线 | 划掉并指向 [TASK128](TASK128-query管道profile生产接线.md) |
| D3 | TASK103:112 D7「待核实」与 §3「D1-D9 全落地」矛盾 | **已裁决（本轮）**：`suppressOutput` 不适用（无对象可抑制），§3 行改写为已关闭；同段 `systemMessage` 零消费属真缺口 → A5 |
