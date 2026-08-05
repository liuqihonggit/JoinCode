# 防御性编程缺陷修复任务清单（第二轮）

> 创建时间: 2026-08-06
> 目标: 在第一轮（20 个缺陷 + 纵深防御修复 F1-F9）基础上，继续搜索需要多级报错 / 纵深防御 / 任务断裂的地方并修复。
> 依据: 定向 grep 扫描（fire-and-forget、事件订阅者异常、Trace.WriteLine 不可见）+ 逐文件人工确认。

## 待修复清单

| 编号 | 严重度 | 文件:行 | 类型 | 问题 | 状态 |
|------|--------|---------|------|------|------|
| F1 | HIGH | `infrastructure/Infrastructure/Plugins/Services/PluginHotReloader.cs:130,151` | 任务断裂 | `PluginReloading`/`PluginReloaded` 直接 `?.Invoke`，订阅者抛异常会中断整个 reload 链（unload/load 不执行）且被 watcher 的 fire-and-forget 静默吞掉 | ⬜ |
| F2 | MEDIUM | `services/Mcp/src/Client/Transport/McpNetworkClient.cs:119`; `McpFallbackClient.cs:112` | 多级报错 | `ProcessResponseAsync(response).ConfigureAwait(false);` 裸语句丢弃 Task，内部异常（GetIdAsInt 解析失败、锁已释放）成为未观察异常被静默丢弃 | ⬜ |
| F3 | MEDIUM | `core/safety/Guard/src/Hooks/Execution/AsyncHookRegistry.cs:360,441` | 多级报错 | 类已有 `_logger` 却用 `Trace.WriteLine` 打错误，生产环境无 listener 即不可见（L1/L2 遗留同类问题） | ⬜ |

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 第二轮聚焦 3 个确认缺陷，F1 为核心 -->
<!-- 原因: 全库扫描显示第一轮已高度加固，剩余可验证缺陷集中在事件订阅者异常隔离、丢弃 Task、Trace 不可见三类 -->
<!-- 替代方案: 继续全库 sweep（大量"存疑"无法验证，且改动面大破坏风险高）-->
<!-- 验证: TDD 红绿循环 + 对应测试项目编译 ✅ -->
