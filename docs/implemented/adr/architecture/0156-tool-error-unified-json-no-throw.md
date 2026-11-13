# ADR 0156: 工具错误统一 JSON 格式化 — 禁止上抛异常

**状态**: accepted  
**日期**: 202)6>11-14  
**分类**: architecture  

## 背景

当前 Agent 的工具=计要求每个工具都不能上抛错误，而是转为 JSON 给 AI 识别。但原有架构中存在以下问题：

1. **错误格式不统一** — 中间件层用纯文本/`<tool_use_error>` 标签，工具内部用 `ToolResultBuilder.Error()` + `ToolDiagnostic`（`[JsonIgnore]` 不传给 LLM），两套格式并存
2. **OperationCanceledException 上抛** — `PermissionAwareToolExecutor` 和 `ToolExecutionMiddleware` 中 `OperationCanceledException` 被 rethrow，违反"不上抛错误"原则
3. **AI 无法结构化解析错误** — LLM 收到的是纯文本错误消息，需要从文本中猜测错误含义，无法直接解析错误类型、原因、建议
4. **无错误码体系** — 无法精确标识每个错误场景，日志和监控难以关联

## 决策

### 1. 统一 JSON 错误格式

定义 `ToolErrorJson` DTO，所有工具执行错误统一序列化为此格式，放入 `ToolResult.Content[0].Text`：

```json
{
  "type": "parameter_missing",
  "error_code": "PM001",
  "tool": "bash",
  "message": "Missing required parameter: command",
  "details": [{"key": "param", "value": "command"}],
  "suggestions": ["Provide the 'command' parameter"],
  "retryable": false
}
```

AI 可直接解析 `type` 判断错误性质，根据 `retryable` 决定是否重试，根据 `suggestions` 获取操作建议。

### 2. 15 种错误类型 + 20 种错误码

- `ToolErrorType` 枚举：15 种错误类型，每种对应管道中一个特定失败点
- `ToolErrorCode` 枚举：20 种精确错误码（前缀+编号），如 `PM001`（必填参数缺失）、`PD002`（用户拒绝权限）

### 3. ToolErrorJsonBuilder 统一构建器

提供便捷方法：`Build()`/`Serialize()`/`ToToolResult()`/`FromException()`/`FromCancellation()`，所有错误产生点统一使用。

### 4. ToolResultBuilder.Build() 自动 JSON 化

`ToolResultBuilder.Build()` 在 `IsError=true` 时自动将 `Content[0].Text` 转为 JSON 格式。工具内部无需改变调用方式（继续用 `ToolResultBuilder.Error().WithText(...).WithDiagnostic(...).Build()`），输出格式自动统一为 JSON。可选通过 `WithErrorType(ToolErrorType, ToolErrorCode)` 设置精确错误类型和码。

### 5. OperationCanceledException 不再 rethrow

`PermissionAwareToolExecutor` 和 `ToolExecutionMiddleware` 中 `OperationCanceledException` 转为 `cancelled` 类型 JSON 结果，不再 rethrow。

## 替代方案（考虑过但放弃）

### A. 仅在中间件层 JSON 化，工具内部不变

**放弃原因**：工具内部错误（占 90%+ 的错误产生量）仍为纯文本，AI 收到的绝大多数错误不是 JSON 格式，目标未达成。

### B. 让所有工具内部手动改用 ToolErrorJsonBuilder

**放弃原因**：需修改 193+ 处 `ToolDiagnostic.Create` 调用，改动量巨大且容易遗漏。`ToolResultBuilder.Build()` 自动转换方案零侵入，工具内部不需要任何改动。

### C. 在 ChatToolOrchestrator 层统一转换

**放弃原因**：该层无法知道错误的 `ToolErrorType` 和 `ToolErrorCode`，只能从 `Diagnostic.Reason` 推断，精度不足。且 `StreamingToolExecutorActor` 旁路了 `ChatToolOrchestrator`，覆盖不全。

## 影响面

| 层级 | 变更 | 文件数 |
|------|------|--------|
| 新增 DTO + 枚举 | `ToolErrorJson`/`ToolErrorType`/`ToolErrorCode`/`ToolErrorJsonContext`/`ToolErrorJsonBuilder` | 5 |
| 中间件层 | 15 件中间件 + `ToolExecutionContext` + `PermissionAwareToolExecutor` | 10 |
| 编排器层 | `ChatToolOrchestrator` + `StreamingToolExecutorActor` | 2 |
| 工具内部 | `ToolResultBuilder.Build()` 自动 JSON 化（零侵入） | 1 |
| 测试更新 | 26 个测试断言适配 JSON 格式 | 12 |

## 验证

- 全量编译通过（16 功能域 + slnx）
- 524 个 McpToolDispatch 单元测试全部通过
- `OperationCanceledException` 不再 rethrow，转为 `cancelled` 类型 JSON 结果
