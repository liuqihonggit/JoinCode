# 0045. ConfigureAwait(false) 强制规范

- 状态：proposed
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

.NET 异步代码中 `await` 默认捕获同步上下文（SynchronizationContext），在库代码中会导致不必要的上下文切换和潜在死锁。项目中有 5003 处 `ConfigureAwait(false)` 调用，但仍有部分遗漏。

## 决策

**所有库代码（foundation/infrastructure/core/services/composition）的 `await` 必须追加 `.ConfigureAwait(false)`。**

例外：
- **app/JoinCode** 主工程入口（顶层语句、Main 方法）：可不加，因为主工程无 SynchronizationContext
- **app/JoinCodeGui** GUI 项目：**禁止**加 ConfigureAwait(false)，因为 GUI 需要回到 UI 线程

```csharp
// ✅ 库代码
await _inner.DisposeAsync().ConfigureAwait(false);
var data = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

// ✅ GUI 代码（不加）
var result = await _dialogService.ShowAsync();

// ❌ 库代码遗漏
await _inner.DisposeAsync();
```

## 替代方案

1. **全局不加**：放弃。库代码被 GUI 调用时可能死锁（等待 UI 线程，UI 线程等待库）。
2. **用 `AsyncFlowSuppressor` 全局抑制**：放弃。影响面大，且不兼容第三方库。
3. **仅热路径加**：放弃。死锁不限于热路径，所有 await 都可能触发。

## 后果

- 正面：库代码无 SynchronizationContext 依赖，可安全被任何宿主调用；避免死锁
- 负面：每个 await 多一个 `.ConfigureAwait(false)`，代码略冗长
- 中性：可用分析器（类似 JCC5002）编译期检查遗漏
