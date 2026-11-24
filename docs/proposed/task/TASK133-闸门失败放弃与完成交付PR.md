# TASK133 — 闸门"失败放弃"出口（11 abandon）与完成交付 PR 流程

- 日期：2026-11-24
- 关联 ADR：[0164](../adr/0164-round-gate-state-machine.md)（选项集与语义）
- 前序任务：[TASK132](TASK132-轮次闸门状态机补全.md)

## 用户诉求

1. 确认当前是否有"失败放弃"能力，没有则补；讨论命名（用户提议"失败退避"）。
2. `5 完成` 的提示词里加入交付流程：创建 PR；提 PR 时若缺少 git 凭证则**放弃 PR**并报告用户。

## 调研结论（先查再改）

| 能力 | 是否已有 | 位置 |
|------|---------|------|
| 重试 + 指数退避 | ✅ 已有 | `modules/llm/agents_coordinator_core/RetryPolicy.cs`（最多 3 次、指数退避） |
| 脏读 16 次退避 | ✅ 已有 | `modules/guard/guard_configuration/core/loading/core/DirtyReadRetry.cs` |
| GitHub 5xx 退避 | ✅ 已有 | `modules/tools/github_client/GitHubApiClient.cs` |
| 子代理卡死放弃 | ✅ 已有 | `modules/llm/agents_coordinator_core/SubAgentStallDefenseCoordinator.cs`（L4 压缩后放弃） |
| **任务层面"承认失败并终止"** | ❌ 没有 | 闸门只有 `9 降级`（交可用子集）与 `10 求助`（等外部输入后继续） |

**命名**：不叫"失败退避"——退避（backoff）是重试前的等待，已有；本项语义是"停止投入、宣告失败"，
故取 `abandon`（中文标签"11 放弃（失败终止）"），与 `9 degrade`、`10 escalate` 区分。

## 实现

### 1. `11 放弃`（abandon）

- `RoundGateChoice.Abandon = 11`，`[EnumValue("abandon")]`；`CategoryOf` → Converge；`IsExitChoice` → true；`Describe` → "11 放弃（失败终止）"。
- **失败依据**：`RoundGateSession.FailedSelfAccepts` 累计记录 `3 verdict=fail`（不随返工清零），
  `HasFailureBasis` 为放弃前置。
- `EvaluateStop`：没有失败依据 → 拦截"11 放弃被拒：本会话还没有失败记录…先讲清失败，别把放弃当免验证出口"；
  严格模式还需先 `4` 移交（"放弃前必须先选 4 用户验收"）。
- 工具层：`choice=11` 缺 `note` → 诱导式报错（要求 失败证据 / 已试路径 / 根因类别 / 残留影响 / 可续做建议）。
- 放弃后的下一轮推荐改为 `0 阻塞上报`，不再推荐"1 直接继续"（真机 H 发现并修正）。
- 自验 fail 后的备选列表按失败依据动态给出 `11 放弃`。

### 2. `5 完成` 交付流程

`RoundGatePrompts.Done` 追加第 4 条：

```
a. 先提交并推送到功能分支（禁止 force push 到 main/master）；
b. 用 gh / jcc gh 创建 PR，目标 main，启用 auto-merge（squash），创建后用 pr checks 守 CI；
c. 提 PR 前先确认凭证（gh auth status / 可用 token）；若缺少 git 凭证、无 push 权限或没有 remote，
   **放弃创建 PR**，不得反复重试或改走其它通道；
   在最终总结里明确报告："未创建 PR + 具体原因 + 已完成到哪一步（本地提交/分支/未推送）"，
   并给出用户补好凭证后可执行的下一条命令。
```

系统 Section 同步一句摘要（"5 完成时按 提交 → push → 创建 PR（auto-merge）交付；缺 git 凭证则放弃 PR 并报告"）。

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `Abandon=11` + 类别/出口/标签 | `RoundGatePolicyTests` / 工具描述 / Section | ✅ | ✅ 单测（编号 0~11、abandon 文本值、Converge、exit） |
| 失败依据 `FailedSelfAccepts` + `HasFailureBasis` | `RoundGateSession.Record` → `EvaluateStop` | ✅ | ✅ 单测（无失败被拒 / 有失败放行 / 严格模式需 4） |
| `Abandon` 注入提示词 + 缺 note 报错 | `RoundGateToolHandlers` → 工具结果 | ✅ | ✅ MCP 单测（缺 note 报错、带 note 注入、放弃提示词关键项） |
| 放弃后不推荐继续 | `RoundGateSession.Recommend` | ✅ | ✅ 单测 `Abandon_ThenNextRound_DoesNotRecommendContinuation` |
| 完成交付 PR 提示词 + 凭证缺失放弃 | `RoundGatePrompts.Done` → 真机工具结果 | ✅ | ✅ 单测断言 PR/凭证/放弃/未创建 PR + 真机 A 日志回读 |
| 不变量扩展（放弃需失败记录 / 严格模式需 4 / 失败计数单源） | `RoundGateInvariantTests`（≤4 步全排列） | ✅ | ✅ 约 123 万实例通过 |
| 变异自检（失败依据恒真 / 关掉 note 校验） | `round_gate_mutation_check.py` | ✅ | ✅ 8/8 变异被抓住 |

## 测试与真机证据

```
全量回归：Brain.Other 604 / Mcp 433 / Prompts 229 全通过，0 警告
Gate 子集 126 / RoundGate 工具 27
变异自检 8/8 抓住（M7 HasFailureBasis 恒真、M8 关掉放弃 note 校验）
真机 H（port 18139）：拦截 → 3 fail → 中间态拦截 → 11 缺 note 报错 → 11 带失败报告 → GATE132H_FINAL_OK，EXIT=0
真机 A（重跑）：日志确认 Done 段含 auto-merge / 凭证 / 放弃创建 PR / 未创建 PR
```

真机 H 日志：`.xxx/logs/gate132_h.log`（脚本 `.xxx/gate132_h_mockserver.json`）；
A 重跑日志：`.xxx/logs/gate132_a_pr.log`。

## 未覆盖 / 边界

- 真实 PR 流程未在真机跑（本地无 git 凭证正是该分支的前提）；"缺凭证放弃 PR" 只验证了提示词注入，
  没有验证模型真的会先查 `gh auth status`——这属于真实模型行为，仍需抽样观测。
- 放弃的失败依据只看 `3 verdict=fail`；单纯工具报错（如命令失败无数次）不自动构成放弃依据，
  需要模型先做一次自验 fail，符合"放弃必须有验收结论"的设计。
- 跨回合计数仍随查询清零（与 TASK132 一致，未在本任务处理）。

## 自动决策记录

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 新出口命名 abandon，而不是 fail-backoff -->
<!-- 原因: 退避是重试前等待，项目已有 RetryPolicy/DirtyReadRetry/GitHub5xx；本项要的是"停止投入并宣告失败" -->
<!-- 替代方案: 复用 10 求助（语义不符，求助意味着拿到输入后继续）；复用 9 降级（要求交付可用子集） -->
<!-- 验证: 单测 + 真机 H 通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 放弃必须有 3 verdict=fail 依据，且工具层强制写 note 失败报告 -->
<!-- 原因: 否则 11 会成为比 5 更容易的"免验证出口"，正是闸门要防的摆烂 -->
<!-- 替代方案: 只靠提示词劝阻（无法拦截）；只要求 note（仍可开局就弃） -->
<!-- 验证: 无失败被拒 / 有失败放行 / 缺 note 报错，变异 M7/M8 均被抓 ✅ -->

<!-- 🤖 Auto Decision: 2026-11-24 -->
<!-- 决策: 完成提示词把 PR 写成"先探测凭证，缺则放弃并报告"，明确禁止反复重试或换通道 -->
<!-- 原因: 用户要求缺凭证时放弃 PR 并报告；无凭证时反复重试会烧轮次且必然失败 -->
<!-- 替代方案: 自动创建 PR 的代码逻辑（当前无凭证，无法真机验证，且用户只要求提示词） -->
<!-- 验证: 单测断言关键词 + 真机 A 日志回读 Done 段 ✅ -->
