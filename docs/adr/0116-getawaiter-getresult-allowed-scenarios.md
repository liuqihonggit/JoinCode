# 0116. .GetAwaiter().GetResult() 允许场景与异步污染防护

- 状态：accepted
- 日期：2026-09-22
- 决策者：AI + 用户

## 背景

AGENTS.md 规定"禁止 .GetAwaiter().GetResult() — 全部用 async/await，仅 Main 入口和属性 getter 例外"。
但在实际代码库中存在约 40 处 .GetAwaiter().GetResult()，分布在构造函数、Lazy<T> 初始化器、ThreadStart 委托、同步委托回调等上下文中。
这些场景无法简单改成 async/await，因为异步化会导致**异步污染**：改方法签名 → 返回类型变 → 调用方也要 async → 一路传播到根。

这些知识只有通过经验和报错才能获得（如 Lazy<T> 接受 Func<T> 不接受 Func<Task<T>>，ThreadStart 必须是 void，构造函数不能 async）。
需要用 ADR 记录这些允许场景，避免未来重复踩坑。

## 决策

### 允许 .GetAwaiter().GetResult() 的场景（白名单）

| 场景 | 原因 | 示例 |
|------|------|------|
| Main 入口 | AGENTS.md 明确例外 | `host.DisposeAsync().GetAwaiter().GetResult()` |
| 属性 getter | AGENTS.md 明确例外 | `return task.GetAwaiter().GetResult() == true` |
| 构造函数 | 不能 async，无法用 await | `InitializeAsync().GetAwaiter().GetResult()` |
| Lazy<T> 初始化器 | `Func<T>` 必须 sync，不接受 `Func<Task<T>>` | `new Lazy<T>(DetectAvailableLanguages)` |
| ThreadStart 委托 | 必须 `void`，不能 `async Task` | `new Thread(ScanLoop)` |
| 同步委托/回调 | 委托类型固定为 `Func<T>`/`Action`，不是 `Func<Task<T>>` | `() => computeAsync().GetAwaiter().GetResult()` |
| SyncFileReader 适配器 | 设计目的就是隔离 .GetAwaiter().GetResult() 到此 class | `fs.ReadAllText(path).GetAwaiter().GetResult()` |
| 分析器规则字符串 | 不是实际调用，是帮助文本 | `"DisposeAsync().GetAwaiter().GetResult()"` |

### 禁止 .GetAwaiter().GetResult() 的场景

| 场景 | 修复方式 |
|------|---------|
| 已 async 方法中 | 直接改 `await expr.ConfigureAwait(false)` |
| 可改 async 的同步方法 | 改方法 async + `await expr.ConfigureAwait(false)` + 更新调用方 |

### 工具支持

`ast_cli fix-getawaiter-getresult` 命令实现保守策略：
- **只在已 async 方法中替换** — 避免改方法签名导致异步污染
- 跳过 lambda（改 async lambda 会改变委托类型）
- 跳过 Main 入口、属性 getter（AGENTS.md 例外）
- 跳过 SyncFileReader、bcl_bridge、分析器代码

## 替代方案

1. **全量异步化** — 把所有含 .GetAwaiter().GetResult() 的方法改成 async，一路传播到根
   - 放弃原因：异步污染范围不可控，可能需要重构 Lazy<T>→AsyncLazy<T>、Thread→Task.Run、构造函数→async factory pattern，是架构级别改动

2. **用 SyncFileReader 统一隔离** — 把所有 .GetAwaiter().GetResult() 收拢到 SyncFileReader
   - 放弃原因：不是所有调用都是文件 IO，有些是任意 async 操作（如 GetAvailableToolsAsync、FindExecutableAsync）

3. **pragma 屏蔽** — 在违规行加 #pragma warning disable
   - 放弃原因：AGENTS.md 禁止 #pragma/NoWarn 屏蔽

## 后果

- 正面：明确了 .GetAwaiter().GetResult() 的合法边界，减少未来重复踩坑；工具保守策略避免异步污染
- 负面：约 40 处 .GetAwaiter().GetResult() 保留在代码库中，无法完全消除
- 中性：未来如果重构架构（如 async factory pattern），可以逐步消除这些例外
