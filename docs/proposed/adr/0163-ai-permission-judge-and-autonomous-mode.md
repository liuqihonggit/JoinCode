# 0163 — AI 灰区权限判定与 Autonomous 模式

- 状态：**Phase 1 已实施（2026-11-23）；Phase 2-4 待做**
- 实施说明：`autonomous` 以 `[EnumValue]` 别名映射到 `Unattended`，未新增第 6 个枚举；AI Judge 复用 400 `auto-class-permission-mw`，未新增 450 节点；`PendingConfirmationQueue` 与 Supervisor 仍待做。
- 日期：2026-11-22
- 关联：[DSG053](../design/DSG053-unified-permission-decision-architecture.md)、[ADR 0162](0162-task-scoped-permission-overlay.md)、[TASK105](../task/TASK105-ask后台任务临时关闭-现状取证与修复方案.md)
- 影响域：`guard.security`、`guard.permission`、`mcp_tool_dispatch`、`storage.clock.goal`、`native_tui`、`native_gui`、`apps.cli`

---

## 背景

当前权限判定是“静态规则 + 模式”：

- `PermissionMode` 只有 Plan/Auto/Ask/Bypass/Unattended；
- `AutoModeClassifier` 用工具名/操作类型/危险命令/敏感路径做粗分类（`AutoModeClassifier.cs:115-180`）；
- `LlmAutoModeClassifier` 已实现“规则 + LLM”两阶段（`LlmAutoModeClassifier.cs:40-129`），但**生产零消费**，且 Stage 2 用 `IQueryEngine.ExecuteQueryAsync`（`:111`），在权限检查内部再跑完整查询引擎，存在递归/成本/延迟三重风险；
- `AutoClassifierMiddleware` 只注入规则分类器、只在 `Auto` 生效（`:20-30`）。

结果是：

1. 灰区只能落回静态 `ask` → 弹窗多；
2. 用户想要“给完全访问权限、由 AI 监控”时，只能选 `Bypass`（连硬规则都跳过）或 `Unattended`（红灯自动执行、无监控层）；
3. 后台任务无法表达“允许执行但不要弹窗”的中间态（TASK105 已取证）。

---

## 决策

采用三层权限判定，并把 AI 限定在“灰区、可审计、可回滚”的位置：

1. **HardPolicy 层（最高优先级）**：黑灯命令、路径逃逸、模式/工具集禁用。AI **不可推翻**。
2. **AI GrayJudge 层（可选）**：只处理规则产出的 `gray`（MediumRisk/Unknown）。可把静态 `ask` 降为 `allow`（减少弹出），也可把可疑 `allow` 升为 `ask`。超时/失败/低置信度 → 回落静态结果，绝不默认放行。
3. **Supervisor 层（异步）**：旁路监控工具流/文件 diff/命令输出，异常时暂停或入队 ask；不能替代不可逆操作的 PreJudge。

同时：

- **引入 `autonomous` 自动化模式（Phase 1 用 `Unattended` 别名承载）**：硬规则内全部自动批准 + AI 灰区判定 + 监控；与 `Bypass` 的区别是保留硬规则。
- **Ask 三出口**：前台同步 ask（保留 TASK103 Defer）/ 后台 `PendingConfirmationQueue`（Phase 2-3 待做；Phase 1 后台直接 deny + 诱导）/ deny + 诱导。
- **复用现有基础设施**：`AutoModeClassifier` 保留为 Stage 1；`LlmAutoModeClassifier` 已改为独立 `IPermissionJudgeClient`（不经过 `IQueryEngine`）；`AutoClassifierMiddleware` 已接线 LLM 阶段并支持 `Auto/Unattended`；未新增 450 节点。
- **作用域**：所有层消费 `PermissionContext`（mode/judge/allowAsk/denied/reason），由 ADR 0162 的 `PermissionScope` 提供。

---

## 备选方案（考虑过但放弃）

### A. 纯规则 + 调阈值（不引入 AI）

- 做法：只调整 `AutoModeClassifier` 阈值、扩大 auto-approve 集合。
- 放弃原因：无法理解上下文（同一 `rm -rf build` 与 `rm -rf ~` 规则无法区分）；调松阈值等于降低安全性，且无法解释。

### B. 纯 AI 判定（规则只做输入）

- 做法：所有工具调用都交给 LLM judge。
- 放弃原因：成本/延迟不可接受；prompt injection 可绕过；不可逆操作不能靠“事后监控”；且违反“硬规则不可协商”。

### C. 直接把现有 `LlmAutoModeClassifier` 接进 `AutoClassifierMiddleware`

- 做法：不动调用面，直接把 `ILlmAutoModeClassifier` 注入中间件。
- 放弃原因：Stage 2 走 `IQueryEngine` → 工具调用 → 权限检查 → 再次 Judge，递归风险；单次判定可能触发完整对话回合，延迟与成本不可控。必须先换成独立 `IPermissionJudgeClient`。

### D. 只靠 sandbox 自动允许（不引入 AI）

- 做法：扩大 `SandboxAutoAllowMiddleware` 覆盖范围。
- 放弃原因：Windows 原生无真隔离；沙箱外/无沙箱场景仍需判定；且沙箱不能判断“操作是否偏离用户目标”。

### E. 继续保持 `Bypass` 作为自动化模式

- 做法：用户自动化时用 `Bypass` + 事后审计。
- 放弃原因：`BypassPermissionMiddleware` 在 priority 100 短路，连 `dangerous-command-mw`（300）都不执行；不可逆操作无兜底。`Autonomous` 保留硬规则，才是“完全访问 + 监控”的安全表达。

---

## 影响

| 模块 | 变更 |
|---|---|
| `guard.security` | `LlmAutoModeClassifier` 已改用 `IPermissionJudgeClient`；规则 Block/Dangerous 不被 AI 改判；3s 超时 + 30min 缓存 |
| `guard.permission` | `AutoClassifierMiddleware` 已注入 LLM 阶段；`Auto/Unattended` 生效；AI 高风险不得自动批准 |
| `resources/profiles/base.yaml` | Phase 1 未新增 450 节点（复用 400）；Phase 2 如拆独立 Judge 再加 |
| `abstractions.primitives.security` | `autonomous` 作为 `Unattended` 的 `[EnumValue]` 别名 |
| `storage.clock.goal` | Goal 已切 `Unattended`；`GoalUserInteractionService` 在 Unattended/Bypass 跳过交互 |
| `native_tui` / `native_gui` / `apps.cli` | footer / 审计查看 / DSH 接后台通知仍待 Phase 2-4 |
| `infrastructure` | 通知队列已按 target 分桶；`PendingConfirmationQueue` 待做 |

---

## 验收

以 [DSG053 §8](../design/DSG053-unified-permission-decision-architecture.md) 为准：

- Shadow 对比：Phase 1 未做 shadow（`LlmJudgeEnabled` 默认 true，直接 pre），后续用固定样本补一致率报告；
- Phase 1：已通过 1270 项 Guard.Security 单测 + 定向构建；ask 率下降与 false-allow 需真机采样，尚未验收；
- Phase 2：`autonomous` 别名单测通过，独立模式矩阵/TUI 真机未做；
- Phase 3：后台任务零阻塞仅完成权限层与通知分桶，`PendingConfirmationQueue` 未做；
- 全仓编译 `Build-AllDomains.ps1` 本轮已跑一次通过（后续按用户要求改为按需编译）。

---

## 后果

**正面**：

- 减少弹窗，同时保留确定性安全边界；
- 后台任务不再被 ask 挂死；
- 判定可审计、可学习、可回滚；
- 复用已有分类器与管道，不新增一套权限体系。

**负面 / 风险**：

- LLM 判定引入延迟与成本；需要缓存/预算/超时；
- 模型漂移可能放宽判定；需要 shadow + 固定测试集；
- 用户可能误解“AI 放行 = 绝对安全”；UI 必须显示监控状态与审计入口。

---

## 待决策

1. Judge 模型与阈值（建议小模型 + 0.75 起步，shadow 调参）。
2. `Autonomous` 的 `allowDowngradeAsk` 默认值（建议 true，仅 `risk<=medium` 可逆操作）。
3. “完全访问”的边界（建议工作区 + 硬规则 + 可选沙箱；网络出站单独配置）。
4. `Bypass` 是否在 UI 标记为不安全并推荐 `Autonomous`（建议是）。
