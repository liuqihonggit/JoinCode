# TASK125 — #46 `permission_denials` 结构化收集（文本启发式退场）

> 状态：已闭合 · 日期：2026-11-23 · 基线：`w2`（#50 提交之后）
> 前置取证：[TASK114](TASK114-Complete协议字段复核与api时长修正.md) §0/§2。

## 一、问题

`SdkResultMessage.permission_denials` 字段存在，但填充逻辑是：

```csharp
// CliEventConsumer.OnToolEnd（改造前）
if (isError && resultText is not null && resultText.Contains("拒绝")) { ...记录 denial... }
```

两个方向的错判：

- **漏报**：英文/其他措辞（`user rejected`、`denied by policy`、hook 拒绝）不含「拒绝」二字 → 不记录；
- **误报**：任何工具结果文本里恰好出现「拒绝」二字的业务错误（例如 grep 到含该词的日志行）→ 记成用户拒绝。

根因不是文案，是**判定依据选错了层**：权限决策在工具执行层已经是结构化的（`ToolErrorCode.PD001/PD002/PD00B/PD003/PD004/PD005`），
但这条信息一路被丢在 `ToolResult` 外面，到 CLI 只剩一段文本。

## 二、链路与改动

```text
QueryEngine（ToolResult）
  → QueryStreamChunk.ToolResult          ← 本来就有 ToolResult
  → QueryStreamChunkAdapter              ← 之前丢弃
  → ChatStreamEvent.ToolErrorCode        ← 新增（string，PD 码）
  → SessionController.ProcessEvent       ← 透传
  → IEventConsumer.OnToolEnd(..., errorCode)
  → CliEventConsumer                     ← 按码判定
```

| 文件 | 改动 |
|------|------|
| `abstractions.models/.../ToolModels.cs` | `ToolResult.ErrorCode`（`[JsonIgnore]`，进程内元数据，不改线上 JSON） |
| `abstractions.models/.../ToolErrorJsonBuilder.cs` | `ToToolResult` 把 `error.ErrorCode` 带进 `ToolResult` |
| `hands/tools/models/ToolResultBuilder.cs` | 错误路径 `ErrorCode = _errorCode?.ToValue()`（此前只写进 JSON 文本） |
| `abstractions.models/.../ToolErrorCode.cs` | 新增 `ToolErrorCodes.IsPermissionDenial(string?)` —— 码口径唯一入口 |
| `abstractions.primitives.models/.../ChatStreamEvent.cs` | `ToolErrorCode`（string）+ `ToolEnd(..., toolErrorCode)` |
| `abstractions.models/.../AgentStreamChunk.cs` | 同名字段 + 双向映射 |
| `abstractions.interfaces/.../QueryStreamChunkAdapter.cs`、`apps/engine/.../AgentStreamChunkAdapter.cs` | 取 `ToolResult.ErrorCode` 下传 |
| `abstractions.primitives.interfaces/src/IEventConsumer.cs` | `OnToolEnd(..., string? errorCode = null)` |
| `apps/cli/adapters/CliEventConsumer.cs` | 按码判定；仅当无码时才文本兜底 |
| `apps/engine/.../BridgeEventConsumer.cs`、`native_tui/apps/NativeTui.Host/*` | 跟随签名（2 个 host 实现） |

**为什么事件里用 `string` 而不是枚举**：`ChatStreamEvent` 在 `primitives.models`（更低层），
反向依赖 `models` 的 `ToolErrorCode` 会造成层间环；字符串取的就是枚举的 `[EnumValue]` 值（`PD002`），
比较一律经 `ToolErrorCodes`，不散落字面量。

**口径**：收录全部 `DeniedBy*`（用户/策略/代理限制/远端策略/特性开关/hook）；
`PR002 PermissionRequiredPendingConfirmation` 是「待确认」不是拒绝，**不收录** —— 否则每次弹确认框都会被记成拒绝。

## 三、验收

| 项 | 结果 |
|----|------|
| `Build-AllDomains.ps1 -Changed` | 8 个项目全部通过 |
| `Hands.ToolHandlers.Tests` | 626/626（新增 `ToolErrorCodeTests` 15 项） |
| `NativeTui.Tests` | 3182/3182（host 两个实现跟随签名） |

新增测试锁的点：

- `PD001/PD002/PD00B/PD003/PD004/PD005` → denial；`PR001/PR002/SV001/EF002/null/""` → 非 denial；
- `ToolResultBuilder` 错误路径与 `ToolErrorJsonBuilder.ToToolResult` 都**真的**把码写进 `ToolResult.ErrorCode`；
- 成功路径不带码。

**未做的真机端到端（如实记录）**：跑一次「模型请求 bash → 用户拒绝 → result JSON 含 `permission_denials`」
需要一次真实 LLM 回合（本机无可用 key）。本项验证停在「码从工具结果一路到达 CLI 消费者」的结构链路 +
单元锁；端到端由 CI 的 `e2e / E2E - Tools` 覆盖。

## 四、残余

- TUI/GUI 侧目前只跟随签名，未消费 `errorCode`（拒绝卡片沿用通用错误样式）；若要「因权限被拒」专门样式，属 UI 层另立项。
- 旧路径（无码）保留文本兜底一行，属过渡；待所有错误产生点都经 `ToolErrorJsonBuilder`/`ToolResultBuilder` 后可删除。
