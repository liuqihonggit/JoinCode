# 防御性编程缺陷修复任务清单（第三轮）

> 创建时间: 2026-08-06
> 目标: 扩大扫描范围到 app/ 与 services/ 层（第二轮覆盖 infrastructure/core/services 其余部分）。
> 依据: 定向 grep 扫描（app + services 共 ~60 处 Trace.WriteLine 与 fire-and-forget + 逐文件确认）。

## 扫描结论

app/ 与 services/ 层经过前两轮加固后已高度防御化。主要残留为：

1. **不可见错误（LOW）**：catch/fallback 块用 `System.Diagnostics.Trace.WriteLine`（无 listener 即不可见），其中一部分类**已有 ILogger**，改为 `_logger?.LogXxx` 即可零风险保留可见性（与前两轮 L1/L2/F3 同主题）。
2. **fire-and-forget**：BridgeMain 会话监视、BridgeWorkPollLoop 轮询等均有内部 try/catch + 退避保护，未发现新断裂点。
3. **一处微守卫缺陷（MEDIUM）**：`BridgeWorkPollLoop.SetTransport` 中旧传输 `DisposeAsync()` 置于 try/catch **之外**，在 `Task.Run` 内抛异常会成为未观察异常被静默吞掉。

## 本轮修复列表

| 编号 | 严重度 | 文件:行 | 类型 | 问题 | 修复 |
|------|--------|---------|------|------|------|
| R1 | MEDIUM | `services/Bridge/src/Client/BridgeWorkPollLoop.cs:270-281,423` | 纵深防御守卫 | 旧传输 `DisposeAsync()` 在 `Task.Run` 内位于 try/catch 之外，异常成未观察异常 | 将 `CloseAsync`+`DisposeAsync` 同置于 try/catch，3 处 Trace→ILogger |
| R2 | LOW | `services/Mcp/src/Auth/OAuth/McpOAuthService.cs:127` | 不可见错误 | 已有 `_logger` 却用 Trace.WriteLine | 改 `_logger?.LogWarning` |
| R3 | LOW | `services/Bridge/src/Client/BridgeApiClient.cs:1136` | 不可见错误 | 已有 `_logger` 却用 Trace.WriteLine（标题同步失败） | 改 `_logger?.LogDebug` |

> R1 为行为保持、防御加固；R2/R3 为行为中性的日志可见性替换，与前两轮 L1/L2/F3 同主题。无新增单元测试（纯日志/守卫变更，已验证对应测试套件无回归）。

## 验证

- `Bridge.Tests` 594 通过 ✅
- `Mcp.Tests` 139 通过 ✅
- 改动项目 Debug 编译 0 错误 0 警告 ✅

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 第三轮仅做低风险日志可见性 + 微守卫修复，不扩大横扫 -->
<!-- 原因: app/services 层经前两轮加固已高度防御化，剩余 Trace.WriteLine 多为 best-effort catch；仅在帧已有 ILogger 的类内统一改为 _logger，避免给无 logger 的类加注入（大改动低价值） -->
<!-- 替代方案: 给无 logger 类逐一加构造注入（改动面大、破坏 DI，收益低，不采用）-->
<!-- 验证: Bridge 594 + Mcp 139 通过，Debug 编译 0 错误 ✅ -->