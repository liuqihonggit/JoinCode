# 0093. 资源管理与异常控制风格规范

- 状态：proposed
- 日期：2026-09-09
- 决策者：用户（liuqihonggit）+ AI

## 背景

项目代码库中存在两类高频不良风格，影响可读性、维护性，且容易引发资源泄漏：

1. **裸 `new` + 手动 `Dispose`**：创建 `IDisposable` 对象后用 `try-finally` 或显式 `Dispose()` 释放，而非 `using var`。典型反例：`Mcp.MockServer/Program.cs` 中 `await new StreamReader(ctx.Request.Body).ReadToEndAsync()` 未用 using，`StreamReader` 自身 buffer 不释放。
2. **一个方法多个 `try-catch`**：尤其 `Dispose()` 方法里用 3 个独立 `try-catch` 包裹每个资源的 `Cancel`/`Dispose`（如 `FileToolHandlers.cs:1246-1251`），以及业务方法内嵌套 `try-catch`（如 `QueryLoopMiddleware.cs` 占位结果写入）。

用户明确偏好：
- `using var xx` 手法释放内存（确定性释放 + 作用域清晰 + 0 样板）
- 一个方法尽量一个 `try-catch`，多个通常可化解为"一个函数一个 using 释放"

## 决策

### 决策1：资源释放强制 `using var` / `await using var`

任何 `IDisposable`/`IAsyncDisposable` 对象，在当前作用域内创建且不逃逸出该方法，**必须**用 `using var` / `await using var` 声明。禁止裸 `new` 后手动 `Dispose` 或 `try-finally` 释放。

**例外**（允许手动释放，需注释说明）：
- 字段持有的长生命周期资源 → 在 `Dispose(bool disposing)` 中释放
- 工厂方法返回可释放对象（如 `OpenRead()` 返回 `Stream`）→ 调用方负责
- 故意不释放底层流（`leaveOpen: true`）→ 注释说明为何不释放

### 决策2：一个方法一个 `try-catch`（尽量）

一个方法内尽量只保留一个 `try-catch` 块。化解手段（按优先级）：

1. **用 `using var` 消除 `finally`**：资源释放交给 using，去掉 `try-finally`
2. **提取辅助方法**：每个资源操作独立成方法，各自 `using`，主方法只编排
3. **合并相邻 `try-catch`**：异常处理逻辑相同时合并，用 `when` 子句区分类型
4. **`Dispose` 合并**：`Dispose()` 方法里多资源释放，用 `DisposeSafe()` 扩展方法统一吞 `ObjectDisposedException`，禁止每个资源一个 `try-catch`

**嵌套 `try-catch`**（try 内套 try）：内层通常是"补偿/回退"逻辑（如写占位结果），应提取为独立方法（如 `WritePlaceholderResultAsync`），外层 `catch` 调用它。

### 决策3：提供 `DisposeSafe` 扩展方法（消除 Dispose 样板）

在 `Abstractions/00-core` 新增 `DisposeSafeExtensions`，提供：

```csharp
public static void DisposeSafe(this IDisposable? obj, ILogger? logger = null, [CallerMemberName] string? caller = null)
{
    try { obj?.Dispose(); }
    catch (ObjectDisposedException) { /* 已释放，幂等忽略 */ }
    catch (Exception ex) { logger?.LogWarning(ex, "[{Caller}] Dispose 失败", caller); }
}

public static void CancelAndDisposeSafe(this CancellationTokenSource? cts, ILogger? logger = null)
{
    try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    cts.DisposeSafe(logger);
}
```

所有 `Dispose()` 方法禁止再写 `try { x.Dispose(); } catch (ObjectDisposedException) ...` 样板，统一调 `x.DisposeSafe(_logger)`。

## 替代方案

### 替代方案A：全靠文档约定，不提供辅助方法

- 放弃原因：用户记忆"已知坑应在代码中直接抛异常作防御"表明文档约定不可靠，后来者不看文档照样写多 try-catch。提供 `DisposeSafe` 扩展方法让"正确写法"比"错误写法"更短（`x.DisposeSafe()` vs `try { x.Dispose(); } catch ...`），用便利性引导合规。

### 替代方案B：Roslyn 分析器编译期强制 `using var`

- 放弃原因：过度工程。`using var` 强制需要分析器识别"创建后未 using 且未逃逸"的复杂数据流分析，实现成本高、误报风险大。先用规范 + 代码审查 + `DisposeSafe` 便利性引导，待反例反复出现再考虑分析器。符合用户"抽象方法足够时不写分析器，避免过度工程"的偏好。

### 替代方案C：用 `SafeHandle` 封装所有资源

- 放弃原因：`SafeHandle` 适合非托管资源互操作，对托管 `IDisposable` 是杀鸡用牛刀，增加抽象层无收益。

## 后果

- 正面：
  - 资源泄漏风险降低（`using var` 编译期保证释放）
  - `Dispose()` 方法从 3-5 行 try-catch 压缩到 1-2 行 `DisposeSafe` 调用
  - 异常控制流扁平化，可读性提升
  - 统一 `ObjectDisposedException` 处理策略（幂等忽略 + 可选日志）
- 负面：
  - 现有大量反例需渐进式重构（禁止一次性大规模重构，每次一个文件/方法）
  - `DisposeSafe` 扩展方法新增一处公共 API（但粒度极小，可接受）
- 中性：
  - 规范依赖代码审查执行，无编译期强制（待反例反复出现再加分析器）

## 验证清单

- [ ] `DisposeSafeExtensions` 实现并通过单元测试
- [ ] `FileToolHandlers.Dispose()` 改用 `DisposeSafe`（3 try-catch → 1 行）
- [ ] `Mcp.MockServer/Program.cs` 改用 `using var reader`
- [ ] `McpServer.RunAsync` 的 `reader`/`writer` 加注释说明为何不 using（Console 流）
- [ ] AGENTS.md 插入规范章节并引用本 ADR
