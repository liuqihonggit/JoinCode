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
| 11 段选项提示词 + 三问护栏 + 状态提示 | `round_gate` 工具结果 / 系统 Section | ✅ | ✅ 单测（提示词关键约束、阻断消息内容） |
| `round_gate` 新增 `evidence`/`subject`/`verdict` | 工具注册表（源码生成器） | ✅ | ✅ 单测 `RoundGateToolHandlersTests`（23 通过）+ 编译通过 |
| 状态机推荐/熔断并入工具结果 | `QueryEngine.AppendRoundGateAdvisory` | ✅ | ✅ 集成测试断言工具被调用 2 次、注入阻断消息 |
| `JCC_ROUND_GATE_STRICT` | `RoundGatePolicy.ReadStrictMode` → 会话 | ✅ | ✅ 单测（严格模式完成需 4 移交） |
| 收尾裁决（验证链 + 熔断 + 出口） | QueryEngine 完成分支 | ✅ | ✅ 集成测试（未自验选 5 被拦截、自验后选 5 收尾） |
| 真机 exe 验收（ADR 0080） | `artifacts/bin/JoinCode/Debug/net10.0/jcc.exe` | ✅ 代码可编译 | ❌ 待补（见下方 ○ 项） |

### 已跑验证

```bash
dotnet build modules/abstractions/abstractions_utils/Abstractions.Utils.csproj      # 0 警告 0 错误
dotnet build modules/mcp/mcp_tests/Mcp.Tests.csproj                               # 0 警告 0 错误
dotnet build modules/cognition/brain_other_tests/Brain.Other.Tests.csproj         # 0 警告 0 错误
dotnet test  modules/cognition/brain_other_tests/... --filter "FullyQualifiedName~Core.Tests.Query.Gate"  # 108 通过
dotnet test  modules/mcp/mcp_tests/... --filter "FullyQualifiedName~RoundGate"    # 23 通过
```

### ○ 待用户验收项

- ○ 真实 TUI 会话下 `JCC_ROUND_GATE=on` 跑一轮：先 `round_gate(1)`，再 `round_gate(3, verdict="pass")`，再 `round_gate(5)`，确认只在最后一次允许收尾。
- ○ 选 `round_gate(5)` 但会话内没有 `3 pass` 时，确认被拦截并注入"5 完成被拒"提示。
- ○ 选 `round_gate(3)` 不带 `verdict` 时，确认工具报错并给出 `verdict="pass"` 的正确写法。
- ○ `JCC_ROUND_GATE_STRICT=1` 下确认 `5`/`9` 需要先 `4`。

## 风险与边界

- 现有会话/历史工具调用记录若仍是旧编号（3 规划 / 4 验收 / 5 结束），语义已变化：旧 `3` 现在表示自验，
  旧 `4` 表示用户验收，旧 `5` 表示完成（需自验前置）。项目无后向兼容要求，不做兼容映射。
- `RoundGateSession` 生命周期 = 单次查询；跨用户回合的计数器会清零（符合"每回合重新推进"语义）。
- 熔断粘性只在"改选强制项"时解除，若强制项本身不可行（如强制 10 但无外部资源），仍可继续选 10 并收尾。

## 自动决策记录

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
