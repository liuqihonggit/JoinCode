# TASK102 — 轮次闸门（round_gate）与循环合并立项

- 日期：2026-11-21
- 关联 ADR：[0159](../adr/0159-round-gate-turn-termination.md)
- 后续任务：[TASK103 — ReAct 循环合并（已完成）](../../implemented/task/TASK103-ReAct循环合并-两套归一.md)

## 用户诉求

1. ReAct 循环每轮收尾前，AI 必须通过一个工具做强制选择，不选不允许继续/停止，避免任务积极性丧失与早退。
2. 选项固定为：`1 直接继续 / 2 TODO 继续 / 3 规划任务 / 4 验收 / 5 结束`，每个选项注入对应提示词。
3. 选项 4（验收）必须启动真实 exe，**禁止用单元测试做挡箭牌**；需要用户亲自验收的部分用 **○** 标记并提示用户。
4. 两套 ReAct 循环太复杂，规则要一套；循环本身是否合并需给方案（本次立项，后续执行）。

## 实现

| 组件 | 位置 |
|------|------|
| 策略单源（选项解析/开关/上限/阻断消息） | `modules/abstractions/abstractions_utils/core/misc/RoundGatePolicy.cs` |
| 提示词目录（5 个选项注入文本） | `modules/abstractions/abstractions_utils/core/misc/RoundGatePrompts.cs` |
| `round_gate` 工具 | `modules/mcp/mcp_tool_handlers/agent/core/RoundGateToolHandlers.cs` |
| 工具名枚举 | `SystemToolName.RoundGate`（`round_gate`） |
| 系统提示 Section | `modules/cognition/prompts/sections/workflow/RoundGateSection.cs` + `resources/prompts/sections.yaml` |
| 开关 | `JCC_ROUND_GATE`（on/off/auto）、`JCC_ROUND_GATE_MAX_REJECT`（默认 6，0=不限制） |
| QueryEngine 拦截 | 完成分支 `EvaluateRoundGateStop`（独立于 Stop hook 计数） |
| QueryLoopMiddleware 拦截 | 纯文本分支 `EvaluateRoundGateStopAsync`（同一策略） |
| 作用域解析 | `QueryOptions.RequireRoundGate`（AgentBase：仅 Coordinator）；`ChatMiddlewareContext.RequireRoundGate`（ChatService：仅显式 on） |

### 五段注入提示词（摘要）

| 选项 | 注入要点 |
|------|----------|
| 1 直接继续 | 禁止总结/重新规划/等待确认，直接推进 |
| 2 TODO 继续 | 取下一项未完成项，标记进行中，完成后更新文档 |
| 3 规划任务 | 先写剩余任务清单 + 四列验收表，再执行第一项 |
| 4 验收 | 编译 → 启动真实 exe → 记录证据 → 勾选验收表；禁止单元测试当验收；○ 标记用户亲自验收项并提示 |
| 5 结束 | 仅在任务完成/验收完成（或剩余项已标 ○）时可选，随后输出收尾总结 |

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `RoundGatePolicy` + `RoundGatePrompts` | QueryEngine `ExecuteCoreLoopAsync` 收尾分支 | ✅ | ✅ 单测 41 通过 + 真机（见下） |
| `RoundGatePolicy` + `RoundGatePrompts` | QueryLoopMiddleware 纯文本收尾分支 | ✅ | ✅ 单测 2 通过（拦截→过闸门→收尾 / 关闭不拦截） |
| `round_gate` 工具处理器 | 工具注册表（源码生成器） | ✅ | ✅ 真机日志 `[Tool] round_gate({"choice":5})` → `[OK]` |
| `RoundGateSection` | 系统提示词装配 | ✅ | ✅ MockServer dump `req_0000` 含"轮次闸门（round_gate）"；`JCC_ROUND_GATE=off` 时 `req_0003` 计数为 0 |
| `QueryOptions.RequireRoundGate` + AgentBase 解析 | 主代理请求 | ✅ | ✅ 真机 `JCC_ROUND_GATE=on` 拦截、`off` 不拦截 |
| `ChatMiddlewareContext.RequireRoundGate` + ChatService | ChatService 回退路径 | ✅ | ✅ 单测；显式 `on` 才启用（默认路径不打扰 slash 命令） |
| `JCC_ROUND_GATE` / `JCC_ROUND_GATE_MAX_REJECT` | JccEnvVar | ✅ | ✅ 真机 on/off 对比；上限单测覆盖 0/1/6 语义 |
| `LoopLimits`（上限配置化） | `QueryEngineConfig.MaxToolCallIterations/MaxStopHookReplyRounds` + `QueryLoopMiddleware` | ✅ | ✅ 单测 6（默认/覆盖/非法回退）+ 全量回归 |
| TODO 注入（choice 2） | `RoundGateToolHandlers` → `ITodoService.GetReadyTodosAsync` | ✅ | ✅ 单测 5 + 真机 dump 含"当前没有就绪 TODO"回退 |
| 选项日志 + 并行调用提示 | `RoundGateToolHandlers` / 工具描述 | ✅ | ✅ 真机日志 `[Tool] round_gate(...)`；描述含同轮并行提示 |

### 真机验收记录（ADR 0080）

**编排**：MockServer 三轮脚本（`.xxx/gate_e2e_mockserver.json`）

1. 纯文本 `GATE_TURN1_WANT_STOP`
2. 工具调用 `round_gate(choice=5)`
3. 纯文本 `GATE_FINAL_OK 收尾总结...`

**开闸门**：

```bash
JCC_ALLOW_DIRECT_FALLBACK=1 JCC_ENDPOINT=http://localhost:18099 OPENAI_API_KEY=sk-test \
JCC_VENDOR=openai JCC_MODEL_ID=gpt-4o JCC_ROUND_GATE=on JCC_PERMISSION_MODE=bypass \
JCC_DEBUGLOG=1 ./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe --trust --await 60 -p "请推进任务"
```

证据（`gate_e2e_on.log`）：

```
[QueryEngine] 轮次闸门拦截收尾（第 1/6 次）：未调用 round_gate 选择结束
[Tool] round_gate({"choice":5})
[OK] round_gate
GATE_FINAL_OK 收尾总结：闸门验收通过。
EXIT=0
```

**关闸门**：`JCC_ROUND_GATE=off` → 单轮默认响应、0 次拦截、EXIT=0；系统提示 dump 中"轮次闸门"出现次数为 0。

**第二轮补充（TODO 分支）**：MockServer 四轮脚本（`.xxx/gate_todo_e2e_mockserver.json`）——纯文本 → `round_gate(2)` → `round_gate(5)` → 纯文本。
真机 dump `req_0003` 中 choice=2 的工具结果包含"【闸门 · 2 TODO 继续】…"与"当前没有就绪 TODO：请改用 3 规划任务，或 1 直接继续。"，EXIT=0。

## 收尾补充（第二轮：推荐项落地）

| 推荐项 | 落地方式 | 状态 |
|--------|----------|------|
| 硬编码上限配置化 | `JCC_MAX_TOOL_CALL_ITERATIONS` / `JCC_MAX_STOP_HOOK_REPLY_ROUNDS` → `LoopLimits` 单源；QueryEngineConfig 与 QueryLoopMiddleware 共同消费（原 128 / 5） | ✅ |
| 闸门接通真实 TODO | choice=2 时从 `ITodoService.GetReadyTodosAsync` 注入就绪项（最多 5 条）；空列表给出改选 3 的引导；服务缺失/异常回退通用提示 | ✅ |
| 闸门可观测性 | 拦截时 QueryEngine 日志 + QueryLoop `[GATE]` Diag；工具选择记录 `[RoundGate] choice=…`；MockServer dump 可回放完整注入链 | ✅ |
| 成本提示 | 工具描述明确"可与最后一个动作同轮并行调用，避免额外往返" | ✅ |
| 两套循环合并 | 已产出的能力矩阵与迁移顺序：TASK103（已完成，见 implemented/task） | ✅ 完成 |

### ○ 待用户验收项

- ○ 真实 TUI 交互会话下 `auto` 模式的开关判定（需用户在真实终端启动 `jcc`，确认 base profile + 非重定向时闸门生效）。
- ○ GUI 会话路径若需要闸门，需显式 `JCC_ROUND_GATE=on`；请用户确认是否希望 GUI 默认开启。
- ○ 选项 4 的实际使用体验：AI 是否按"真实 exe + ○ 标记"执行，需长期观察后再决定是否加严（例如无 exe 证据禁止选 5）。

## 风险与边界

- `auto` 模式依赖 profile 与 stdin 重定向判定；CI/E2E 默认不启用，避免改变既有 LLM 调用轮数。
- 拦截上限触顶后会放行并上屏警告（防止无限烧钱）；`0` 表示不限制，供无人值守长任务使用。
- 模型始终不调用闸门时，最多被拦截上限次数后强制结束，不会死循环。

## 自动决策记录

<!-- 🤖 Auto Decision: 2026-11-21 -->
<!-- 决策: 闸门只在收尾点强制，不要求每次工具调用后都过闸门 -->
<!-- 原因: 收尾点已覆盖"早退"问题；每次强制会显著增加 token 与延迟 -->
<!-- 替代方案: 每 N 次工具调用强制（保留为后续可选开关，未实现） -->
<!-- 验证: QueryEngine/QueryLoopMiddleware 单测 + 真机 on/off 均通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-21 -->
<!-- 决策: 闸门作用域为主代理（Coordinator），子代理恒不启用；ChatService 路径仅显式 on -->
<!-- 原因: 子代理任务生命周期独立；slash/短问答不应被多跑一轮 -->
<!-- 替代方案: 全局默认开启（会被 E2E/slash 行为变化否决） -->
<!-- 验证: AgentBase 单测 626 通过；真机行为符合预期 ✅ -->
