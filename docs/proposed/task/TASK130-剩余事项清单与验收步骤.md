# TASK130 — 剩余事项清单与验收步骤（防遗忘台账）

> 用途：总账 [TASK103](TASK103-未清事项总账.md) 已把 #40/#42/#45/#46/#50/#53 全部闭合，
> 但「剩余」散落在 TASK103 的决策记录、各子任务文档和 MEMORY 里，容易漏。本文把
> **每一项的口径、证据、可复制验收命令、判定标准** 一次写全，施工前不需要再考古。
>
> 纪律：先写本文，再动代码。每闭合一项，回来划掉并写证据行。

## 0. 分类速查

| 类 | 含义 | 项 |
|---|---|---|
| A | 本机可施工、可自证 | A1 M1 用户消息前缀选中色（**核实＝早已实现**，本轮补测试）· A2 D7 `suppressOutput`（**已核实＝不适用**）· A3 斜杠命令取参审计 · A4 flag settings 策略锁 · A5 hook `systemMessage`（**已施工**）· A6 SessionStart `AdditionalConfig` 零消费（**本轮新发现**） |
| B | 只能人工 / 需外部条件 | B1 #52 双终端矩阵、B2 #51 Ctrl+B、B3 macOS Seatbelt |
| C | 需凭据或 MockServer 才能跑完 | C1 #40 回合级真机探针、C2 #53 低限额进程级 |
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
- 其余 hook 事件（PreToolUse/PostToolUse/Stop/SessionStart…）的 `systemMessage` 仍未投影 —— 本轮只接
  `UserPromptSubmit`（唯一每回合都跑、用户最常写 notice 的事件）。要做需在 `HookOrchestrator` 统一出口加 sink，另立项。
- NDJSON/agentMode 无投影（协议缺 attachment 类型）。
- **新发现 A6**：`SessionStartHookResult.AdditionalConfig` **零读取** —— `SessionStartHookManager` 把
  `additionalContext` / `initialUserMessage` 聚合进这个字典，但全仓没有任何消费方读取它
  （`grep "AdditionalConfig"` 只有定义与写入两处）。TASK130 早前把「写入」误记成「有消费点」。

---

### A6. `SessionStartHookResult.AdditionalConfig` 零消费（本轮新发现，待施工）

**证据**
- `SessionStartHookManager.cs:62,66` 把 `additionalContext` / `initialUserMessage` 写进 `additionalConfig`，
  返回 `SessionStartHookResult { AdditionalConfig = additionalConfig }`；
- `grep "AdditionalConfig"` 全仓只有两处：`ISessionStartHookManager.cs:30`（定义）与上面这句（写入）。
  **没有任何读取方** —— `SessionStartHookMiddleware` 只读 `ShouldProceed` / `Message`；
- 对照：`UserPromptSubmit` 的 `additionalContext` 有真消费链（`ChatTelemetryPreHook` → `PreChatMiddleware` →
  `OnText`），所以「同一个字段在 SessionStart 上不生效」不是协议没做，是这条链路没接。

**施工面（待定，先拍板再动）**
- 二选一：① 让 `SessionStartHookMiddleware` 读取 `additionalContext` 并注入首轮 prompt（对齐基准 sessionStart hook 的
  additionalContext 语义）；② `initialUserMessage` 需要「会话起始插一条 user 消息」的通道，可能落在
  `IChatContextManager`。
- 风险：SessionStart 在会话建立早期执行，注入时机与历史初始化顺序耦合 —— 必须先确认 `ChatInitContext` 时序，
  否则会变成「注入了但被覆盖」。

**判定标准**：先写红测试（构造 hook 返回 `additionalContext` → 断言它出现在首轮模型输入/历史里），
再决定通道；只写文档不接线的做法不接受（本轮 A5 的教训）。

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

---

### A4. flag settings 路径的策略锁（ADR 0161 注明未接）

**证据**：TASK103:254 记「flag settings 路径的策略锁未接入（ADR 0161 已注明，现只消费
`managed-settings.json`）」。
**先做**：读 ADR 0161 确认「策略锁」的准确语义与目标文件，再判断是施工还是补文档说明。
**判定**：若属「管理策略应覆盖命令行 flag」这一类安全语义，必须施工；若只是 ADR 里的一句
边界说明，则在总账关闭并注明理由。

---

## B. 只能人工 / 需外部条件

### B1. #52 双终端键位矩阵（探针已就绪）

**现状**：`jcc --probe-terminal-input` 已可用（[TASK129](TASK129双终端输入验收.md)），
输出用例 4/4 绿，重定向守卫真机 exit=1；**矩阵 10 行 × 2 终端全部待测**。
**必须人工**：Windows Terminal（`wt.exe -w new powershell.exe -NoProfile`）与经典 Console Host
各开一个真实窗口，逐格记录「应用已消费 / 仅解码通过 / 宿主截获-未上报 / 未测」，附产物哈希。
**判定**：矩阵每格有结论 + 证据（窗口身份、按键、最小诊断输出），回填 TASK066 B6 与 TASK121。

### B2. #51 Ctrl+B 主会话后台化（需产品决策）

**现状**：取证结论是「七类型/前缀、后台 agent 执行/通知/面板/停止均已等价；`AgentTask` 空壳不硬接」，
真正缺的是 Ctrl+B 把主会话整体后台化 —— 这会改变**并发查询语义 / transcript 归属 / UI 控制权**。
**下一步**：先给一页设计（并发模型、transcript 写谁、Esc/Ctrl+B 再按回来的状态机、失败回滚），
由用户裁决后再开工。**禁止**直接照基准按键行为照搬。

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

### C2. #53 低限额进程级验证

**现状**：同进程用例已证明「第 2 次查询抛 `RateLimitExceededException`，模型调用数停在 1」；
但「低限额时请求**不进入** MockServer」的进程级证据没做（本机无 key）。
**补验步骤**（[ADR 0077](../../implemented/adr/engineering_ci_test/0077-mockserver-jcc-joint-testing.md)）：
起本地 MockServer → 临时 profile 把 `query-rate-limit-mw.max-requests` 设 1 →
连续两次 `-p hi` → 判定：第二次**没有**新的 MockServer 请求记录，且 CLI 报限流错误。

---

## D. 台账纠错（本次已改）

| 项 | 原状 | 纠正 |
|---|---|---|
| D1 | TASK103:152「#15 ultrathink 彩虹 → 待施工」 | **已实现**：`native_tui/src/NativeTui.Core/repl/UltrathinkHighlighter.cs`（`\bultrathink\b` + `charIndex % 7`），接入点在 `MessageRenderer.cs:32-36`，用例 `ReplComponentTests.cs:35` → 划掉 |
| D2 | TASK103:97 #53 表格行未划线 | 划掉并指向 [TASK128](TASK128-query管道profile生产接线.md) |
| D3 | TASK103:112 D7「待核实」与 §3「D1-D9 全落地」矛盾 | **已裁决（本轮）**：`suppressOutput` 不适用（无对象可抑制），§3 行改写为已关闭；同段 `systemMessage` 零消费属真缺口 → A5 |
