# ADR 0142: 权限确认接口异步化 — ConfirmAsync 统一 CLI/TUI/GUI 交互式确认

> 状态: proposed
> 日期: 2026-10-19
> 关联: [ADR 0139](../0139-permission-pipeline-path-boundary-shortcircuit.md)（权限管道分层）、[ADR 0141](../0141-webui-bridge-protocol.md)（WebUI 桥接协议）、[TASK053](../../../implemented/task/TASK053-DSH像素对齐对比校验体系任务清单.md)（F2 权限对话框）

## 1. 背景

TASK053 `F2 权限对话框` 的真机 E2E（真实 exe + 真实引擎 + OpenAI.MockServer + `JCC_PERMISSION_MODE=ask`）
实测：工具调用**不弹窗**，前端已完成的 `permission/request → permission/response` 链路永远收不到请求。
代码级排查确证引擎侧接线断裂，共 3 处：

1. **无抛出点**：`PermissionPendingConfirmationException` 全仓只有定义 + 注释 + Host 捕获点，
   **0 个 throw**（`abs_guard/security/permission/PermissionPendingConfirmationException.cs:6`）。
   据此，`JccChatSession.StreamWithPermissionRetryAsync`（`JccChatSession.cs:513`）、
   CLI `SessionController.cs:128`、TUI `TuiModeRunner.cs:332` 三处捕获**均为死代码**。
2. **GUI 无确认器**：`IPermissionConfirmationHandler` 全仓仅 CLI 注册
   （`CliPermissionConfirmationHandler`）；GUI/TUI 未注册 → `PermissionAwareToolExecutor.cs:141`
   走 `_confirmationHandler is null` 分支 → 工具被**静默拒绝**
   （"工具 'bash' 需要确认但无确认处理器"），用户与 AI 均无可见提示（违背 AGENTS 规则 9 诱导提示）。
3. **异常被吞**：`ChatToolOrchestrator.cs:176` 的 catch-all 会把该异常转成工具错误文本，
   即使补上抛出点也无法上抛到 Host。

已确认上游确认点是活的：`PermissionChecker.cs:112`（`JCC_PERMISSION_MODE` 生效）
→ `DefaultResultMiddleware`（非 Auto 模式 `_ => PendingConfirmation`）
→ `PermissionAwareToolExecutor.HandlePendingConfirmationAsync`。

接口形态是根因：`IPermissionConfirmationHandler.Confirm` 是**同步阻塞**方法，
只适配 CLI 的 `^` 提示符 + `IConfirmationGate` 单通道输入；GUI 的确认来自
浏览器（WebView2 消息回调，异步），无法在同步方法里安全获取（阻塞 UI 线程 = 死锁；
阻塞引擎线程池线程 = 最长 5 分钟占位）。

## 2. 决策

**`IPermissionConfirmationHandler` 异步化**，三种 Host 共用同一接口：

1. 接口签名改为
   `Task<PermissionConfirmAction> ConfirmAsync(string toolName, string confirmationPrompt, CancellationToken cancellationToken = default)`。
2. `PermissionAwareToolExecutor.HandlePendingConfirmationAsync` 改为 `await ConfirmAsync(...)`
   （该方法本就是 async，管道重跑语义不变：批准 → 重跑管道执行工具 → 同轮继续，不重发 LLM 请求）。
3. CLI 实现内部保留既有阻塞门控（`IConfirmationGate` 单通道路由 stdin），仅形态改为异步。
4. GUI 新增 `GuiPermissionConfirmationHandler`（`GuiInteractionModule` Order=80 注册），
   持有可注入委托 `Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>`；
   `IJccChatSession.PermissionConfirmationHandler` 作为**唯一宿主注入缝**转发到该单例
   （WebUI 模式 = `PermissionPromptBroker`，Avalonia 经典模式 = `PermissionDialog`），
   决策后由适配器完成临时批准（`ApproveToolTemporarily` + `ApproveLevelTemporarily`，与 CLI 同源）。
5. **死代码清理**（无后向兼容）：删除 `PermissionPendingConfirmationException`、
   `JccChatSession.StreamWithPermissionRetryAsync` + `MaxPermissionRetries`、
   CLI `SessionController` / TUI `TuiModeRunner` 的该异常捕获分支。
6. fail-closed 不变：无确认器 / 未注入委托 / 超时 / 窗口关闭 / 非法决策值 → `Deny`。
   用户取消（`OperationCanceledException`）→ `Deny` 且不记录为错误。

## 3. 被视为但放弃的替代方案

- **A. GUI 实现同步 `Confirm` 阻塞等待 broker TCS**：改动最小（不动 mcp/cognition），
  但长时间占用引擎线程池线程（对话框停留时长），且易在单线程同步上下文中死锁；
  异常死代码继续留存。放弃。
- **B. GUI handler 抛 `PermissionPendingConfirmationException`，在 mcp/cognition 两处 catch 显式放行**：
  复用已有"临时批准 + 撤回本轮 + 重发同消息"闭环，不阻塞线程；但每次确认多一次 LLM 往返
  （成本 + 结果不确定性：重发可能得到不同答案），且需在两处 catch-all 加特例、
  保留同步接口的语义错位。放弃。
- **在 WebView2 侧改为同步等待浏览器脚本**（`ExecuteScriptAsync` 阻塞等 JS Promise）：
  跨进程往返 + 死锁风险，且脱离 CLI/TUI 统一形态。放弃。

## 4. 验收（ADR 0080 手动 exe + 单测）

| 要求 | 基建实现 | 消费点 | 已实现 | 已验收 |
|------|----------|--------|--------|--------|
| 异步确认接口 | `IPermissionConfirmationHandler.ConfirmAsync` | CLI/GUI 实现 + `PermissionAwareToolExecutor` | ⬜ | ⬜ |
| GUI 确认器注册 | `GuiPermissionConfirmationHandler` + `GuiInteractionModule` | 引擎 DI（GUI 容器） | ⬜ | ⬜ |
| WebUI 弹窗闭环 | `PermissionPromptBroker` + `permission/request|response` | `WebUiWindow` → bridge.js 弹窗 | ⬜ | ⬜ 真机 E2E：ask 模式弹窗 → 允许 → 工具执行 → 本轮继续 |
| 拒绝路径 fail-closed | 决策映射 + `deny` | 同上（含 Esc → deny） | ⬜ | ⬜ 真机 E2E：拒绝 → 工具未执行 → 输入态恢复 |
| 审批副作用同源 | `ApproveToolTemporarily` + `ApproveLevelTemporarily` | `IToolPermissionManager` | ⬜ | ⬜ 单测 + 真机（同等级免二次确认） |
| 死代码清理 | 删异常 + 重试闭环 + 2 处捕获 | CLI/TUI/GUI | ⬜ | ⬜ 编译 0 警告 + 全量单测 |
| 回归 | — | mcp/guard/gui/cli/tui 测试 | ⬜ | ⬜ dotnet test 全绿 |
