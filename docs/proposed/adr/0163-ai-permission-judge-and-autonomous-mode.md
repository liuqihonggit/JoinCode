# 0163 — AI 灰区权限判定与 Autonomous 模式

- 状态：**Phase 1、Phase 3、Phase 4 基础版已实施（2026-11-23）；2026-11-24 补齐 AI Judge 每任务预算、策略版本/上下文指纹与 2.5s 默认超时；Phase 2 独立模式枚举与 Phase 4 文件 diff/行为监控待做**
- 实施说明：`autonomous` 以 `[EnumValue]` 别名映射到 `Unattended`，未新增第 6 个枚举；AI Judge 复用 `auto-class-permission-mw`，节点移到 1150（所有硬规则之后、兜底之前）；`PendingConfirmationQueue` 已落地并接入执行器，完成 TTL/上限/脱敏；`IPendingConfirmationResolver` + DSH 收件箱 + TUI `/permissions pending` 闭环；执行后 Supervisor 基础版、AI 判定 JSONL 审计与 `LlmJudgeShadow` 已落地；AI Judge 默认 2.5s 超时、每任务 10 次预算，审计携带 `policyVersion/contextHash/budgetRemaining/budgetExhausted`。
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
3. **Supervisor 层（执行后同步基础版）**：工具结果返回后扫描输出中的高置信度凭据，命中登记待复核票据；不阻塞、不改变结果。文件 diff/命令输出行为异常/执行中暂停仍待做，不能替代不可逆操作的 PreJudge。

同时：

- **引入 `autonomous` 自动化模式（Phase 1 用 `Unattended` 别名承载）**：硬规则内全部自动批准 + AI 灰区判定 + 监控；与 `Bypass` 的区别是保留硬规则。
- **Ask 三出口**：前台同步 ask（保留 TASK103 Defer）/ 后台 `PendingConfirmationQueue`（已实现：只登记不等待，前台可快照）/ deny + 诱导。
- **复用现有基础设施**：`AutoModeClassifier` 保留为 Stage 1；`LlmAutoModeClassifier` 已改为独立 `IPermissionJudgeClient`（不经过 `IQueryEngine`）；`AutoClassifierMiddleware` 已接线 LLM 阶段并支持 `Auto/Unattended`，节点从 400 移到 1150，避免 `AutoApprove` 短路 1000 的危险操作判定。
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
| `guard.security` | `LlmAutoModeClassifier` 已改用 `IPermissionJudgeClient`；规则 Block/Dangerous 不被 AI 改判；默认 2.5s 超时（客户端 3s 兜底）+ 30min 缓存 + 每任务 10 次预算；非 Shell 工具只传路径/URL/查询摘要；新增 `LlmJudgeShadow` 与 JSONL 审计（含 `policyVersion/contextHash/budget`） |
| `guard.permission` | `AutoClassifierMiddleware` 已注入 LLM 阶段；`Auto/Unattended` 生效；AI 高风险不得自动批准 |
| `resources/profiles/base.yaml` | `auto-class-permission-mw` 从 400 移到 1150（硬规则之后、兜底之前）；未新增独立 Judge 节点 |
| `abstractions.primitives.security` | `autonomous` 作为 `Unattended` 的 `[EnumValue]` 别名 |
| `storage.clock.goal` | Goal 已切 `Unattended`；`GoalUserInteractionService` 在 Unattended/Bypass 跳过交互 |
| `native_tui` / `native_gui` / `apps.cli` | DSH/TUI 挂起提问已接后台通知 Defer + 取消令牌；DSH 待确认收件箱（RPC + 注入式面板）与 TUI `/permissions pending` 已实现；footer / 指标看板待做 |
| `infrastructure` | 通知队列已按 target 分桶；`PendingConfirmationQueue` 已实现 TTL/上限/脱敏，释放时统一 `Defer`；`IPendingConfirmationResolver` 提供批准/拒绝出口 |
| `mcp_tool_dispatch` | `PermissionAwareToolExecutor` 前台登记/结算待确认票据；无人值守登记后立即 `Defer`，不弹窗不阻塞；执行后 `ToolExecutionSupervisor` 凭据扫描登记复核票据 |

---

## 验收

以 [DSG053 §8](../design/DSG053-unified-permission-decision-architecture.md) 为准：

- Shadow 对比：`PermissionConfig.LlmJudgeShadow` 已实现（默认 false，shadow 仍调 AI 并写审计但返回规则结果）；一致率报告需真机采样；
- Phase 1：Guard.Security 全量单测通过；本轮分类器 50 项 + 中间件 9 项通过；ask 率下降与 false-allow 需真机采样，尚未验收；
- Phase 2：`autonomous` 别名单测通过，独立模式矩阵/TUI 真机未做；DSH/TUI 后台 Defer 与取消令牌已补单测；
- Phase 3：`PendingConfirmationQueue` 已实现并接入执行器；队列 9 项、DSH 收件箱 7 项、TUI pending 24 项单测通过；后台任务零阻塞；真实 WebView2 与真实 TTY 目视验收已完成；
- Phase 4：执行后 Supervisor 凭据扫描 1 项定向测试通过；AI 审计/shadow、预算、超时、策略指纹相关用例已补，`Guard.Security.Tests` 全量 1,322 项、`Guard.Config.Tests` 全量 1,349 项通过；真实模型延迟分布与高并发预算采样未做；文件 diff/行为监控未做；
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
5. 队列 TTL 30 分钟/上限 200 为当前默认，是否按项目/模式调整；过期当前语义为“完成 Defer 后移除”，是否需要保留审计快照。
6. 批准后产品语义当前为“复核通过 + 通知发起代理，不自动恢复原后台调用”，是否需要代理自动前台重试。
7. `LlmJudgeShadow` 当前是全局 `PermissionConfig` 开关，是否需要按模式/项目粒度。
8. 每任务 10 次预算当前按代理任务 `SessionId | AgentId | CreatedAt` 计数；长主会话是否需要严格按用户 turn 重置。
