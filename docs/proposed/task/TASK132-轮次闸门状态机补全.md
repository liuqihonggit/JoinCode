# TASK132 — 轮次闸门状态机补全（四类出口 + 强制验证边 + 计数熔断）

- 日期：2026-11-24
- 关联 ADR：[0164](../adr/0164-round-gate-state-machine.md)、[0159](../adr/0159-round-gate-turn-termination.md)
- 前序任务：[TASK102](TASK102-轮次闸门与循环合并立项.md)

## 用户诉求

1. 闸门选项要补全成状态机：执行（1/2）、验证（3/4）、元操作（7/8）、收敛（9/10）、挂起（6）、出口（5/隐式 0）。
2. 每个选项绑定独立提示词；"产出必须过验证才能碰完成"；意外完成也要二次/三次确认。
3. 补"往回走"（重规划/回退）与"降级/求助"出口，卡死时不再只能硬编或摆烂。
4. 用计数熔断 + "推荐 + 申诉"替代平铺选项与重复确认，并加防"假进展"三问护栏。

## 实现

| 组件 | 位置 | 变更 |
|------|------|------|
| 选项集/类别/出口判定/verdict 解析/调用解析 | `modules/abstractions/abstractions_utils/core/misc/RoundGatePolicy.cs` | `RoundGateChoice` 扩到 0/1~10；新增 `RoundGateCategory`、`RoundGateVerdict`、`RoundGateInvocation`、`ReadStrictMode`、`Describe` |
| 状态机 | `modules/abstractions/abstractions_utils/core/misc/RoundGateSession.cs`（新增） | 计数器、推荐表、粘性熔断、完成终验、收尾裁决 |
| 提示词目录 | `modules/abstractions/abstractions_utils/core/misc/RoundGatePrompts.cs` | 11 段选项提示词 + 三问护栏 + 状态提示 + 阻断/非法参数诱导式报错 |
| 系统提示 Section | `modules/cognition/prompts/sections/workflow/RoundGateSection.cs` | 选项一览、状态机规则、熔断阈值、全局宪法 |
| 工具 | `modules/mcp/mcp_tool_handlers/agent/core/RoundGateToolHandlers.cs` | 新增 `evidence`/`subject`/`verdict`；choice=3 强制 verdict；三问护栏随调用注入 |
| 循环接线 | `modules/cognition/brain_query/query2/token_budget/core/QueryEngine.cs` | 持有一个 `RoundGateSession`；记录选择 → 更新状态机 → 把推荐/熔断并入工具结果；收尾用 `EvaluateStop` 裁决 |
| 开关 | `modules/abstractions/abstractions_primitives_configuration/src/app_data/JccEnvVar.cs` | 新增 `JCC_ROUND_GATE_STRICT` |

### 选项与语义

| 选项 | 类别 | 注入要点 |
|------|------|----------|
| 1 直接继续 | 执行 | 禁止总结/重规划/等确认，直接推进 |
| 2 TODO 继续 | 执行 | 取下一项（真实 TODO 注入），每项绑验收标准 |
| 3 自验 | 验证 | 逐条对照原始需求 + 证据，`verdict=pass/fail` 必填 |
| 4 用户验收 | 验证 | ○ 待用户验收清单移交用户，本轮可收尾等回复 |
| 5 完成 | 出口 | 三层终验（自验报告 / 自动核查 / 用户终验），需先有 3 pass |
| 6 等候后台 | 挂起 | 必须有真实句柄；信号到达后先 3 |
| 7 重新规划 | 元操作 | 失效证据 + 卡点分类 + ≤5 步新计划 |
| 8 回退/修正 | 元操作 | 缺陷标识 + 根因 + 修正范围 + 回归验证，改完必须重跑 3 |
| 9 降级交付 | 收敛 | 可用子集 + 未完成原因 + 临时/永久取舍 + 影响 + 下一步 |
| 10 求助/升级 | 收敛 | 已尝试方案 + 精确卡点 + 需要的资源，禁摆烂 |
| 0 阻塞上报 | 出口（隐式） | 只有用户能提供的输入缺失时才用 |

### 熔断阈值

| 触发条件 | 强制转出 |
|---------|---------|
| 同一任务重规划 > 2 次 | 10 求助/升级 |
| 同一缺陷返工 > 2 次（`subject` 计数） | 7 重新规划 |
| 自验连续 3 轮不通过 | 10 求助/升级 |
| 连续 2 轮无证据（空转） | 7 重新规划 |
| 用户验收连续 2 次驳回（4 后紧跟 8） | 7 重新规划 |
| 连续偏离推荐 2 次（申诉） | 锁定推荐项（不下手强制，只提示） |

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `RoundGatePolicy`（0~10、类别、出口能力、verdict/调用解析） | `RoundGateSession` / `QueryEngine` / 工具处理器 | ✅ | ✅ 单测 `RoundGatePolicyTests`（解析、类别、出口、开关） |
| `RoundGateSession`（计数/推荐/粘性熔断/终验） | `QueryEngine.ExecuteCoreLoopAsync` 收尾分支 | ✅ | ✅ 单测 `RoundGateSessionTests`（自验强制边、严格模式、降级、各熔断、申诉锁定） |
| 按需注入：所选选项提示词 + 空证据才加三问 + 状态提示 | `round_gate` 工具结果 / 系统 Section（仅骨架） | ✅ | ✅ 单测（只注入所选项、有证据不注入护栏、阻断消息内容）+ 真机 A/B/C/D 复跑 |
| `round_gate` 新增 `evidence`/`subject`/`verdict` | 工具注册表（源码生成器） | ✅ | ✅ 单测 `RoundGateToolHandlersTests`（23 通过）+ 编译通过 |
| 状态机推荐/熔断并入工具结果 | `QueryEngine.AppendRoundGateAdvisory` | ✅ | ✅ 集成测试断言工具被调用 2 次、注入阻断消息 |
| `JCC_ROUND_GATE_STRICT` | `RoundGatePolicy.ReadStrictMode` → 会话 | ✅ | ✅ 单测（严格模式完成需 4 移交） |
| 收尾裁决（验证链 + 熔断 + 出口） | QueryEngine 完成分支 | ✅ | ✅ 集成测试（未自验选 5 被拦截、自验后选 5 收尾） |
| 真机 exe 验收（ADR 0080） | `artifacts/bin/JoinCode/Debug/net10.0/jcc.exe` + OpenAI.MockServer 脚本 | ✅ | ✅ 四条真机链路见下方"真机验收记录" |

### 真机验收记录（ADR 0080，2026-11-24）

编排：OpenAI.MockServer（`--port 1813x --config .xxx/gate132_x_mockserver.json`）+ 真实 jcc，
环境 `JCC_ROUND_GATE=on`、`JCC_ALLOW_DIRECT_FALLBACK=1`、`JCC_PERMISSION_MODE=bypass`、`JCC_DEBUGLOG=1`，
日志落在 `.xxx/logs/gate132_{a,b,c,d}.log`，四条场景均 `EXIT=0`。

| 场景 | 脚本 | 真机证据 |
|------|------|----------|
| A 完整验证链 | 纯文本想停 → `round_gate(3)` 无 verdict → 纯文本想停 → `round_gate(3, pass)` → `round_gate(5)` → 纯文本 | `拦截收尾（第 1/6 次）：你还没有调用 round_gate` → 工具报错`round_gate 参数错误: verdict=未提供 不是合法的自验结论` → `拦截收尾（第 2/6 次）：3 自验 是中间态，不允许收尾` → `GATE132_FINAL_OK` 正常收尾 |
| B 跳过自验直接完成 | 纯文本想停 → `round_gate(5)` → 纯文本想停 | `拦截收尾（第 2/2 次）：5 完成被拒：本会话还没有通过的自验（3 + verdict=pass）` → 触顶 `⚠ 轮次闸门连续 2 次未通过…强制结束` |
| C 严格模式推荐纠偏 | `round_gate(3, pass)` → `round_gate(5)` → `round_gate(4)` → `round_gate(5)` | 两次工具结果均带 `【闸门 · 状态机】推荐下一轮：4 用户验收 —— 严格模式：完成前必须移交用户验收`，按推荐走完后 `GATE132C_FINAL_OK` 收尾 |
| D 严格模式拦截完成 | 纯文本想停 → `round_gate(3, pass)` → `round_gate(5)` → 纯文本 | `拦截收尾（第 2/3 次）：严格模式：完成前必须先选 4 用户验收`（第 3 次同因）→ 触顶强制结束 |

> 注：任务处理器日志中的 `[RoundGate] choice=… recommended=…` 未上屏（该 logger 在 `-p` 模式下无 sink），
> 选择与推荐事实由工具结果注入文本 + 拦截日志共同证明。

### 注入瘦身与两处状态机修正（2026-11-24，用户要求"选完才注入"）

用户明确：**一个工具不同参数是对的**，要改的是"不要全量注入提示词"。据此做了四项：

| 改动 | 之前 | 之后 |
|------|------|------|
| 系统 Section | 常驻 11 项 `AllOptions` + 全量状态机条文（~25 行） | 只保留协议骨架 + 收尾白名单 + 验证边 + 熔断 + 申诉（~10 行） |
| 工具结果 | 每次注入 `EvidenceGuardrail` 三问 | 仅当本轮 `evidence` 为空才注入；有证据不再说教 |
| 阻断消息 | 拼 `AllOptions` 全表 + 常驻护栏 | 只留"拦截原因 + 推荐/备选 + 一条验证边提醒"；非法参数报错仍列允许值（诱导式报错例外） |
| 推荐计算 | `Record` 先算推荐、后做状态迁移 → 注入的"下一轮推荐"滞后一格（真机 A 中 `3 pass` 后仍推 8 返工） | 迁移后再算推荐；申诉判定仍对比"迁移前"的推荐 |

对应的红→绿测试：

- `RoundGateSessionTests.Record_SelfAcceptPass_RecommendsDoneForNextRound`（先红：期望 5、实得 8）。
- `RoundGateToolHandlersTests.RoundGateAsync_WithEvidence_DoesNotPreloadGuardrail` /
  `RoundGateAsync_InjectsOnlySelectedOptionPrompt`（断言只出现 `【闸门 · 3 自验】`，不含 1/9 的段落）。
- `QueryEngineRoundGateTests.ErroredGateCall_DoesNotMutateGateState`（先红：参数报错的 `choice=3` 被记成一次失败自验）→ 修复：QueryEngine 仅在 `!toolResult.IsError` 时 `Record`。

真机复跑（同 A/B/C/D 脚本，日志 `.xxx/logs/gate132_{a,b,c,d}_slim3.log`）：四条均 `EXIT=0`；
A 现在在 `3 verdict=pass` 之后直接注入 `推荐下一轮：5 完成 —— 自验通过`，不再出现滞后的"8 回退"；C 在严格模式下 `3 pass` 后推荐 `4 用户验收`。

### 已跑验证

```bash
dotnet build modules/abstractions/abstractions_utils/Abstractions.Utils.csproj      # 0 警告 0 错误
dotnet build modules/mcp/mcp_tests/Mcp.Tests.csproj                               # 0 警告 0 错误
dotnet build modules/cognition/brain_other_tests/Brain.Other.Tests.csproj         # 0 警告 0 错误
dotnet test  modules/cognition/brain_other_tests/... --filter "FullyQualifiedName~Core.Tests.Query.Gate"  # 111 通过
dotnet test  modules/mcp/mcp_tests/... --filter "FullyQualifiedName~RoundGate"    # 25 通过
dotnet test  modules/cognition/brain_prompts_tests/...                            # 229 通过
pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1 -Configuration Debug      # 全仓 4 项全通过
```

### ○ 待用户验收项（真机链路已验证，剩余是交互体感）

- ✅ 已真机验证：`round_gate(1)`→`3 verdict=pass`→`5` 只在最后一次放行（场景 A）。
- ✅ 已真机验证：会话内没有 `3 pass` 时选 `5` 被拦截并提示"5 完成被拒"（场景 B）。
- ✅ 已真机验证：`3` 不带 `verdict` 工具报错并给出 `verdict="pass"` 正确写法（场景 A）。
- ✅ 已真机验证：`JCC_ROUND_GATE_STRICT=1` 下 `5` 需先 `4`（场景 C/D）。
- ○ 待用户：真实 TUI 交互会话里看"推荐 + 备选"在工具结果里的可读性，以及 0/6/9/10 各出口的用户体感是否需要调文案。

## 风险与边界

- 现有会话/历史工具调用记录若仍是旧编号（3 规划 / 4 验收 / 5 结束），语义已变化：旧 `3` 现在表示自验，
  旧 `4` 表示用户验收，旧 `5` 表示完成（需自验前置）。项目无后向兼容要求，不做兼容映射。
- `RoundGateSession` 生命周期 = 单次查询；跨用户回合的计数器会清零（符合"每回合重新推进"语义）。
- 熔断粘性只在"改选强制项"时解除，若强制项本身不可行（如强制 10 但无外部资源），仍可继续选 10 并收尾。

## 自动决策记录

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 系统 Section 只放协议骨架，11 项细则改为"选完哪项才注入哪项"，三问护栏按"无 evidence"条件注入 -->
<!-- 原因: 用户明确"单工具不同参数是对的，不要全量注入提示词"；全量常驻既费 token，也让模型每轮重读同一份说明 -->
<!-- 替代方案: 拆成多个工具（被用户否决）；Section 保留全表只压缩措辞（仍是全量注入，不解决诉求） -->
<!-- 验证: 工具测试断言只注入所选项 + 真机 A/B/C/D 复跑 EXIT=0 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: Record 推荐改为"状态迁移后计算"，申诉判定保留"迁移前"的基线推荐 -->
<!-- 原因: 真机发现 3 verdict=pass 之后仍推荐 8 返工（推荐滞后一格），会把 AI 引向错误的下一步 -->
<!-- 替代方案: 保持滞后但在文本里标注"上一轮推荐"（语义更绕，且工具结果就是给下一轮用的） -->
<!-- 验证: 新增红测试 Record_SelfAcceptPass_RecommendsDoneForNextRound 转绿，真机 A 复跑显示推荐 5 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 报错的 round_gate 调用不推进状态机（QueryEngine 仅在 !toolResult.IsError 时 Record） -->
<!-- 原因: 参数写错（如 choice=3 漏 verdict）会被记成一次失败自验，虚增 SelfAcceptFailStreak 直至误触熔断 -->
<!-- 替代方案: 工具内吞掉错误只回提示（违反"报错必须带正确写法"的诱导式报错规则，且剥夺运行时可见性） -->
<!-- 验证: 新增红测试 ErroredGateCall_DoesNotMutateGateState 转绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 状态机放在 RoundGateSession（abstractions_utils），而不是工具处理器内部 -->
<!-- 原因: 工具处理器没有会话标识，多会话并发会串状态；查询级实例天然隔离，且两套循环可共用 -->
<!-- 替代方案: 工具内静态字典按 session_id 记状态（需把 session_id 注入工具参数，耦合更大） -->
<!-- 验证: 单测 108 + 集成测试 5 通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 9/10 与 4 允许收尾，但 5 必须有自验、9 必须已移交 4；严格模式下 5 也要 4 -->
<!-- 原因: 用户提案里"收敛出口必须经用户验收或显式确认才能终止"，同时不能让无人值守会话被永久卡死 -->
<!-- 替代方案: 全部出口一律要用户确认（会让无人值守模式失效）；一律放行（等于没约束） -->
<!-- 验证: 单测覆盖 blocked/allowed 双向路径 ✅ -->
