# 防御性编程缺陷修复任务清单（第二轮）

> 创建时间: 2026-08-06
> 目标: 在第一轮（20 个缺陷 + 纵深防御修复 F1-F9）基础上，继续搜索需要多级报错 / 纵深防御 / 任务断裂的地方并修复。
> 依据: 定向 grep 扫描（fire-and-forget、事件订阅者异常、Trace.WriteLine 不可见）+ 逐文件人工确认。

## 待修复清单

| 编号 | 严重度 | 文件:行 | 类型 | 问题 | 状态 |
|------|--------|---------|------|------|------|
| F1 | HIGH | `infrastructure/Infrastructure/Plugins/Services/PluginHotReloader.cs:130,151` | 任务断裂 | `PluginReloading`/`PluginReloaded` 直接 `?.Invoke`，订阅者抛异常会中断整个 reload 链（unload/load 不执行）且被 watcher 的 fire-and-forget 静默吞掉 | ✅ 6af1ccdc |
| F2 | MEDIUM | `services/Mcp/src/Client/Transport/McpNetworkClient.cs:119`; `McpFallbackClient.cs:112` | 多级报错 | `ProcessResponseAsync(response).ConfigureAwait(false);` 裸语句丢弃 Task，内部异常（GetIdAsInt 解析失败、锁已释放）成为未观察异常被静默丢弃 | ✅ 728c0d276 |
| F3 | MEDIUM | `core/safety/Guard/src/Hooks/Execution/AsyncHookRegistry.cs:360,441` | 多级报错 | 类已有 `_logger` 却用 `Trace.WriteLine` 打错误，生产环境无 listener 即不可见（L1/L2 遗留同类问题） | ✅ 6aaf1ccdc |

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 第二轮聚焦 3 个确认缺陷，F1 为核心 -->
<!-- 原因: 全库扫描显示第一轮已高度加固，剩余可验证缺陷集中在事件订阅者异常隔离、丢弃 Task、Trace 不可见三类 -->
<!-- 替代方案: 继续全库 sweep（大量"存疑"无法验证，且改动面大破坏风险高）-->
<!-- 验证: TDD 红绿循环 + 对应测试项目编译 ✅ -->
<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: F1 对齐 ThreadSafeListenerList.Notify 约定，快照订阅者逐个 try/catch 隔离，ReloadPluginAsync 改为 internal 供测试直接调用 -->
<!-- 原因: 基础设施已有逐订阅者隔离约定，PluginHotReloader 绕过它导致订阅者异常中断重载链; internal + InternalsVisibleTo 提供确定性测试入口（避免依赖真实文件 watcher 时序） -->
<!-- 替代方案: 通过真实 watcher 触发（非确定性，慢）; 拒绝 -->
<!-- 验证: Guard.Config.Tests 783 通过，新增订阅者隔离测试 ✅ -->
<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: F2 新增 FireAndForgetProcessResponseAsync 包裹器，传输层接收循环改用 `_ =` 调用 -->
<!-- 原因: 客户端释放后到达的响应在 _requestLock.WaitAsync 抛 ObjectDisposedException，裸语句丢弃成为未观察异常; 包裹器捕获并 LogWarning 保留可见性 -->
<!-- 替代方案: 接收循环改为 await（会阻塞消息泵，改变传输语义）; 拒绝 -->
<!-- 验证: Mcp.Tests 139 通过，新增 3 个回归测试（复现 + 安全包裹 + 正常路径）✅ -->
<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: F3 AsyncHookRegistry 两处 Trace.WriteLine 改 _logger?.LogWarning -->
<!-- 原因: 类已有 ILogger 注入却用 Trace.WriteLine（无 listener 即不可见），与第一轮 L1/L2 修复同一主题 -->
<!-- 替代方案: 不处理（保持不可见错误）; 拒绝 -->
<!-- 验证: Guard.Hooks.Tests 206 通过，新增坏 JSON 日志可见性测试 ✅ -->
