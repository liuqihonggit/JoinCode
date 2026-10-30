# ADR 0153: 工具超时唤醒宿主决策

> 📍 **导航**: [docs/](../../README.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adr/README.md](../README.md) — 修改本文档后须同步更新此索引

> 状态: accepted
> 创建: 2026-10-31
> 决策者: 用户主导（工具超时唤醒宿主 AI 决策继续等候/结束运行）
> 影响: modules/cognition/hands/system_actuator/abstractions/SessionToolMap.cs + ADR 0146 steer 注入 + ADR 0106 卡死防护 + AbsoluteTimeoutMiddleware 统一超时
> 关联: [ADR 0152](0152-session-tool-lifecycle-map.md)（SessionToolMap）、[ADR 0146](0146-steer-in-turn-injection-mailbox.md)（steer 轮内注入）、[ADR 0106](../implemented/adr/data_core_selection/0106-subagent-stall-defense-in-depth.md)（卡死防护）

## 背景

ADR 0152 的 `SessionToolMap.StartMonitoring` 有后台监控循环 + `onWarn`/`onKill` 回调，但：
1. **没人调用 `StartMonitoring`** — 监控循环未启动
2. **`onWarn` 回调未实现** — WarningThreshold 到达时无唤醒机制
3. **工具执行是同步的** — 宿主 AI 在 `await ExecuteAsync` 等待工具返回，不会进入下一步步首

用户需求：
1. 工具超时到 WarningThreshold（30s）时，**唤醒宿主 AI**
2. 让宿主 AI 决定**继续等候**还是**结束运行**
3. 保证**唤醒策略**（如何中断当前步 + 注入提示）
4. **提示词注入给宿主**（"工具 X 已运行 Ys，是否继续等候？"）

现有可复用机制（ADR 0146 已验收）：
- `ISteerInputMailbox.Enqueue(sessionId, text)` — 轮内注入，步首 drain
- `_steerRequested` volatile + `CancellationTokenSource.Cancel()` — 中断当前步尽快收尾
- `ISystemActuatorCommandContext.Interrupt()` — 工具进程不杀转后台

## 决策

### 1. 唤醒策略 — 中断 + 注入

WarningThreshold 到达时，`onWarn` 回调执行：
1. **注入 steer 提示** — `ISteerInputMailbox.Enqueue(sessionId, 提示词)`，提示词模板见下
2. **中断当前步** — 置位 `_steerRequested` + `CancellationTokenSource.Cancel()`，当前步尽快收尾（不杀工具进程，转后台）
3. **下一步步首** — 宿主 AI drain steer 提示，LLM 请求携带注入文本
4. **LLM 决策** — "继续等候"或"结束运行"
5. **执行决策** — "结束"调 `KillByAgent` 杀死工具进程；"继续"工具继续后台运行

### 2. 提示词模板

```
<tool-timeout-warning>
工具 "{ToolName}" 已运行 {ElapsedSeconds} 秒（PID={ProcessId}）。
当前命令: {Command}
请决定:
- 回复"继续" — 工具继续运行，下次警报在 {KillThresholdSeconds} 秒
- 回复"结束" — 终止工具进程，返回已收集的输出
</tool-timeout-warning>
```

### 3. SDK 接口 — IToolTimeoutNotifier

在 Abstractions 定义接口，解耦 SessionToolMap（Hands 域）与 SteerInputMailbox（Brain 域）：

```csharp
public interface IToolTimeoutNotifier {
    /// <summary>工具到达 WarningThreshold 时唤醒宿主 AI（注入 steer 提示 + 中断当前步）</summary>
    Task NotifyWarningAsync(ToolLifecycleInfo info, CancellationToken ct = default);

    /// <summary>工具到达 KillThreshold 时强制杀死（已有 KillByAgent 覆盖，此方法为日志/通知补充）</summary>
    Task NotifyKillAsync(ToolLifecycleInfo info, CancellationToken ct = default);
}
```

`SessionToolMap.StartMonitoring` 接收 `IToolTimeoutNotifier`，在 `onWarn`/`onKill` 回调中调用。

### 4. 实现 — ToolTimeoutNotifier

在 Composition（依赖 Hands + Brain）中实现 `IToolTimeoutNotifier`：
- 注入 `ISteerInputMailbox`（steer 注入）
- 注入 `ISubAgentContextAccessor`（获取当前 sessionId + agentId）
- `NotifyWarningAsync` — 构造提示词 → `Enqueue(sessionId, 提示词)` → 置位 `_steerRequested`（通过 `IAgentLifecycleManager` 获取 agent → 设置 steer 标志）

### 5. 启动监控

在 session 启动时（`SessionInitStep` 或 `SubAgentStallDefenseHostedService`）调 `SessionToolMap.StartMonitoring(notifier.NotifyWarningAsync, notifier.NotifyKillAsync)`。

## 替代方案

1. **纯异步通知（IAgentNotificationQueue）** — 后台代理完成通知队列。但语义不符（是"完成通知"不是"超时唤醒"），全局单例不按会话寻址，ADR 0146 已明确放弃复用。弃
2. **不中断当前步，等工具返回后附加提示** — 宿主在工具运行期间无感知，无法主动决策。用户明确要求"唤醒"。弃
3. **中断当前 LLM HTTP 请求** — 无独立机制，依赖 CancellationToken 透传。与方案 1 的 `_steerRequested` + CT.Cancel 等价但更粗暴（直接 cancel HTTP 连接）。弃

## 后果

### 正面
- 工具超时时宿主 AI 被唤醒，主动决策继续/结束
- 复用已验收的 steer 注入机制（ADR 0146）
- 工具进程不被杀死（转后台），宿主决策"继续"时无缝恢复
- 提示词模板可定制

### 负面
- 中断当前步有开销（LLM 请求被 cancel，重新发送带提示的请求）
- steer 注入只在步首生效，当前步需先收尾（可能有延迟）
- 多工具同时超时时多个 steer 提示排队，LLM 一次看到多个决策请求

### 实现顺序
1. ✅ IToolTimeoutNotifier 接口（Abstractions）
2. ✅ ToolTimeoutNotifier 实现（Composition）
3. ✅ SessionToolMap.StartMonitoring 接收 IToolTimeoutNotifier
4. ✅ SessionToolMapMonitoringHostedService 自动启动监控
5. ✅ 提示词模板 + 单元测试（15 测试全绿）
6. ✅ 集成测试（真实工具超时 → steer 注入 → 宿主决策）
7. ✅ AbsoluteTimeoutMiddleware 统一超时 — 超时后通过 SessionToolMap.KillByCommand 杀死进程

### 统一 AbsoluteTimeoutMiddleware（ADR 0153 扩展）

**问题**: `AbsoluteTimeoutMiddleware`（管道级，120s 同步超时截断）与 `SessionToolMap`（session 级，30s 警告 + 5min 杀死）两套超时机制并行，配置不关联。120s 超时后只设置错误结果不杀进程，进程空跑到 5min 才被 `SessionToolMap` 杀。

**统一方案**:
1. `SessionToolMap.BuildToolName(command)` — 提取命名格式为公共静态方法，注册方和查找方共用
2. `SessionToolMap.KillByCommand(command)` — 按命令查找并杀死对应进程
3. `AbsoluteTimeoutMiddleware` 注入 `SessionToolMap?`（可选），超时后调 `KillByCommand` 杀死进程
4. `SessionToolMap.KillThreshold`（5min）保留作为兜底（非 shell 工具的唯一超时保障）

**统一后时间线**:
- 30s: `SessionToolMap` 警告 → steer 注入唤醒宿主
- 120s: `AbsoluteTimeoutMiddleware` 超时 → `KillByCommand` 杀死进程 + 设置错误结果 + 提示续期
- 5min: `SessionToolMap.KillThreshold` 兜底（正常情况不会到达，因为 120s 已杀）
