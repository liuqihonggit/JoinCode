# TASK130 — 剩余事项清单与验收步骤（防遗忘台账）

> 用途：总账 [TASK103](TASK103-未清事项总账.md) 已把 #40/#42/#45/#46/#50/#53 全部闭合，
> 但「剩余」散落在 TASK103 的决策记录、各子任务文档和 MEMORY 里，容易漏。本文把
> **每一项的口径、证据、可复制验收命令、判定标准** 一次写全，施工前不需要再考古。
>
> 纪律：先写本文，再动代码。每闭合一项，回来划掉并写证据行。

## 0. 分类速查

| 类 | 含义 | 项 |
|---|---|---|
| A | 本机可施工、可自证 | A1 M1 用户消息前缀选中色、A2 D7 `suppressOutput`（**已核实＝不适用**）、A3 斜杠命令取参审计、A4 flag settings 策略锁、A5 hook `systemMessage` 无消费点（**本轮新发现**） |
| B | 只能人工 / 需外部条件 | B1 #52 双终端矩阵、B2 #51 Ctrl+B、B3 macOS Seatbelt |
| C | 需凭据或 MockServer 才能跑完 | C1 #40 回合级真机探针、C2 #53 低限额进程级 |
| D | 台账自身错误，需纠正 | D1「#15 待施工」已过期、D2 #53 表格行未划线、D3 D7 与总账 §3 矛盾 |

---

## A. 本机可施工

### A1. M1 — 用户消息 `❯` 在消息光标选中时改 suggestion 色（待施工）

**证据（一手源码，优先级最高）**
- 基准 `UserPromptMessage.tsx:71` + `HighlightedThinkingText.tsx:23-24`：`isSelected ? suggestion : subtle`；
  `Messages.tsx:628` 每行一个 Provider —— 即「选中态」是逐消息的，不是全局。
- `ScrollBox.HighlightItem` / `MessageCursor`（Shift+↑）是基准 MessageActions 导航的对应实现，
  **不是** w2 自创；TASK103:151 已据此把原「维持现状」纠正为待施工。

**当前实现（缺口）**
- `native_tui/src/NativeTui.Core/repl/MessageRenderer.cs:20` `RenderUser(...)` 里 `❯` 恒用
  `theme.Subtle`（第 31 行 `pointer` 样式），函数签名没有 `isSelected` 入参 →
  光标选中用户消息时前缀不变色，与基准不一致。
- 对照：picker/dialog 的选中态已经用 `theme.Suggestion`（`MessageRenderer.cs:470,514`），
  所以样式表里颜色是现成的，缺的只是「把选中态传进 RenderUser」。

**施工面（预计 3 处）**
1. `RenderUser` 增加 `bool isSelected`（或传 `TextStyle` 决策函数），`pointer` 按选中态取色；
2. 调用点 `native_tui/src/NativeTui.Core/repl/MessageItems.cs:30`（`UserMessageItem` 渲染）拿到当前光标态；
   光标态来源是 `ReplShell` 的 MessageCursor（`ReplShell.cs:2465` 一带）——**先确认它是渲染期可读的**，
   不可读就先把它提到可读状态，不要为了改色去加全局可变状态；
3. 追加测试：`ReplComponentTests`（现有 ultrathink 用例同文件）断言「选中 → suggestion；
   未选中 → subtle」，并断言**助手消息 `●` 不受影响**（避免顺手改错另一个前缀）。

**验收**
```bash
dotnet build native_tui/tests/NativeTui.Tests/NativeTui.Tests.csproj -c Debug -m:1 --nologo
dotnet test  native_tui/tests/NativeTui.Tests/NativeTui.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~MessageRenderer|FullyQualifiedName~ReplComponent"
```
判定：新增用例红→绿；`NativeTui.Host.Tests` 全量绿；`jcc --selftest-tui` 仍 `HOST SELFTEST OK`。

**风险**：`MessageCursor` 是键盘导航态，若它只在按键处理期存在，渲染期拿不到 —— 那就是
「先补状态暴露」而不是「改颜色」，需另开一步，别硬塞。

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

### A5. hook `systemMessage` 无生产消费点（本轮 A2 核实过程中发现，待施工）

**双侧证据**

| 侧 | 事实 |
|---|---|
| 基准 | `utils/hooks.ts:2770-2780`：`if (result.systemMessage) yield createAttachmentMessage({type:'hook_system_message', content, hookName, toolUseID, hookEvent})` —— 与 `suppressOutput` **无关**，必定展示 |
| 本仓 | `HookResult.SystemMessage`（`HookResult.cs:62`）已定义、`HookExecutorBase.cs:208-211` 已解析、单测 `HookOutputParsingTests.cs:49` 已断言；**生产侧零读取**（`grep "\.SystemMessage" modules --exclude-dir=obj` 只命中一个 collapse 枚举分支和那条单测） |

**对照**：同批字段里 `AdditionalContext` 有消费点（`ChatTelemetryPreHook.cs:55`、`SessionStartHookManager.cs:61`）、
`InitialUserMessage` 有消费点（`SessionStartHookManager.cs:65`），**只有 SystemMessage 掉链**——
说明不是「整块协议没做」，而是这一条漏了，属典型「解析 ≠ 消费」。

**施工前必须定的一个决策（落点二选一，需拍板）**
- ① 走 transcript attachment（对齐基准语义，用户能在对话里看到 hook 的话）→ 需要在聊天事件/消息模型里
  加一种 attachment 类型，改动面比 ② 大，但语义一致；
- ② 走 `IPresentationAdapter.DisplaySystemMessage`（终端告警行，`ConsolePresentationAdapter.cs:32`）
  → 改动极小，但 GUI/transcript 里看不到，等于只在 CLI 侧生效。

**验收（两种落点共用前半段）**：先写红测试（构造 `{"systemMessage":"x"}` 的 hook 输出 →
断言该系统消息出现在选定出口），再实现；`Guard.Hooks` 与相关 host 测试全绿；
真机可用 `--selftest-*` 或临时 hook 命令（`echo '{"systemMessage":"probe"}'`）验证。

---

### A3. 斜杠命令取参审计（46 个 `GetNormalizedArgs` 调用点）

**背景**：`slash_call` 把参数 JSON 整体放进 `Arguments`，`GetNormalizedArgs` 拿到的是**整串**。
单值命令应改用 `GetSplitArgs()[0]`（`/model` 已按此修好）。其余调用点未逐条判断，
存在「按空格切分后仍把 JSON 当值用」的隐患。

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
