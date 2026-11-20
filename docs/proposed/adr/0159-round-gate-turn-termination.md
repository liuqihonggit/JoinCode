# 0159. 轮次闸门 — 收尾强制选择与终止策略单源

- 状态：accepted（已实现，TASK102）
- 日期：2026-11-21
- 决策者：项目架构组

## 背景

ReAct 循环在"模型无工具调用"时直接判定回合结束（QueryEngine `ExecuteCoreLoopAsync` 完成分支、QueryLoopMiddleware 纯文本分支）。模型可以只输出一段"已完成"的文本就提前收尾，运行时没有机制要求它声明下一步，也没有机制区分"真完成"与"早退"。

同时存在两套循环：

| 循环 | 消费方 |
|------|--------|
| `QueryEngine`（cognition/brain.query） | 主代理、子代理、工作流、-p |
| `QueryLoopMiddleware`（cognition/brain.context_services） | ChatService、GUI 回退、slash 命令 |

两套循环各自维护终止判定（空响应、Stop hook、循环检测），任何新增的结束策略都必须实现两次，必然漂移。

## 决策

1. **新增 `round_gate` 工具**（AI 自选，不阻塞用户）：收尾前必须调用并选择
   `1 直接继续 / 2 TODO 继续 / 3 规划任务 / 4 验收 / 5 结束`。
   工具结果只是把对应工作流提示词注入上下文（选项 4 强制真实 exe 验收、禁止单元测试挡箭牌、○ 标记用户亲自验收项）。
2. **规则单源**：新增 `RoundGatePolicy`（`modules/abstractions/abstractions.utils/core/misc/`），
   两套循环只调用它做"是否允许收尾"裁决，禁止各自实现。
3. **只在收尾点强制**：模型想结束回合但本轮未选择 `5 结束` → 注入阻断消息并继续循环；
   连续拦截达到上限（默认 6，可配 `0=不限制`）后放行并上屏警告，避免模型死不调用闸门时无限烧 token。
4. **作用域**：主代理（`AgentRole.Coordinator`）启用；子代理恒不启用；
   ChatService 路径只在显式 `JCC_ROUND_GATE=on` 时启用（防止简单问答/slash 命令被多跑一轮）。
5. **开关**：`JCC_ROUND_GATE=on/off/auto`（auto = base profile 且非 stdin 重定向的交互式主代理）
   + `JCC_ROUND_GATE_MAX_REJECT`。开关关闭时系统提示 Section 不注入。
6. **系统提示**：新增 `round_gate` Section（AgentMode 组），让 AI 首轮就知道协议，减少第一次被拦截的浪费。

## 考虑过的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 在 QueryEngine / QueryLoopMiddleware 各实现一份闸门 | 规则漂移，行为不一致（用户明确反对"两套太复杂"） |
| 复用 `ask_user_question` | 阻塞等待用户输入，且 `UnattendedDenied=true`，无人值守不可用；闸门是 AI 自选自省，不应等用户 |
| 每次 LLM 调用都必须过闸门 | 每次多一次工具往返，token/延迟成本高；收尾点强制已覆盖"早退"问题 |
| 本次直接合并两套循环 | 迁移面覆盖 GUI 回退/slash/bridge，风险与闸门功能不匹配；已单独立项 TASK103 做特征矩阵与迁移 |

## 后果

- 正面：早退被运行时拦截；验收阶段被制度化（真实 exe、○ 标记用户验收项）；两套循环共用同一套闸门规则。
- 代价：每次收尾尝试多一次工具往返（仅在未选择 5 时）；`auto` 下无人值守会话不启用，需要显式 `on` 才强制。
- 遗留：两套循环仍然存在，终止策略（空响应/循环检测/闸门）后续统一收敛到共享策略组件（TASK103）。

## 关联

- 任务文档：`docs/proposed/task/TASK102-轮次闸门与循环合并立项.md`
- 循环合并计划（已完成）：`docs/implemented/task/TASK103-ReAct循环合并-两套归一.md`
- 手动验收：[ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md)
