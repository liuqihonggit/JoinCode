# TASK037 - jcc 启动开销优化

## 问题

jcc 启动开销 ~4.4 秒，接近 `McpInitPlugin` 的 5s 超时上限。瓶颈定位：

1. **515 个工具注册**：`McpService.InitializeAsync` → `RegisterAllMcpToolDispatchAsync`（源码生成器生成）→ 16 批并行，但 16 批竞争同一个 `AsyncLock`，等效串行。每次 `RegisterToolAsync` 获取锁 + 构造 async 状态机 = 515 次。
2. **8 个插件加载**：已并行化（`Task.WhenAll`），不再是瓶颈。
3. **WirePluginSkillBridge**：首次解析深依赖链，待测量。

## 优化方案

### 优化1：批量工具注册（核心）

给 `IToolRegistry` / `LocalToolRegistry` 加 `RegisterToolsBatchAsync(IReadOnlyList<IToolHandler> handlers, ct)`：
- 单次 `AsyncLock` 获取，注册 N 个工具
- 消除 515 次 async 状态机开销 → 1 次
- 消除 514 次锁获取开销

### 优化1b：生成器改用批量注册

改 `McpToolDispatchGenerator.GenerateRegisterAllMethod`：
- 16 批并行**收集** `IToolHandler` 到 `ConcurrentBag`（DI 解析 + schema 构造并行）
- 收集完成后，单次 `RegisterToolsBatchAsync(allHandlers, ct)` 批量注册

### 优化4：超时放宽

`McpInitPlugin.InitializeAsync` 5s → 15s，防止边缘环境超时。

## 验收标准

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `IToolRegistry.RegisterToolsBatchAsync` | 生成器 `RegisterAllMcpToolDispatchAsync` | ❌ | ❌ |
| `LocalToolRegistry.RegisterToolsBatchAsync`（单次锁） | 同上 | ❌ | ❌ |
| 生成器批量注册代码 | `McpService.InitializeAsync` | ❌ | ❌ |
| `McpInitPlugin` 15s 超时 | 启动流程 | ❌ | ❌ |

## 进度

- [ ] 优化1：批量注册方法 + 单元测试
- [ ] 优化1b：生成器改造
- [ ] 优化4：超时放宽
- [ ] 编译 + 测试 + 提交
- [ ] 实测验证
