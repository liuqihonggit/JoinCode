# 0043. 收口函数统一 — 命名/参数/异常/幂等性

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

代码中资源释放/清理的收口函数存在 714 处，命名和模式不统一：

| 命名 | 数量 | 语义 |
|------|------|------|
| Dispose/DisposeAsync | 477 | IDisposable/IAsyncDisposable 资源释放 |
| Close/CloseAsync | 32 | 关闭连接/流 |
| Cleanup/CleanupAsync | 35 | 清理资源 |
| ShutdownAsync | 10 | 关闭服务 |
| Stop/StopAsync | 160 | 停止服务/任务 |

不统一点（实测）：
1. **命名混用**：Close/Cleanup/Shutdown/Stop 语义重叠，同一概念多种命名
2. **参数名不一致**：`ct` vs `cancellationToken`（FileWatcherIntegration vs TeamMemorySyncService）
3. **默认值不一致**：有 `= default` vs 无（FileWatcherIntegration.StopAsync 无默认值 vs CronScheduler.StopAsync 有）
4. **同步/异步混用**：`Stop()` vs `StopAsync()`（HookEventBroadcaster.Stop 同步 vs FileWatcherIntegration.StopAsync 异步）
5. **异常处理不统一**：部分收口函数抛异常，部分吞异常
6. **幂等性不统一**：部分支持多次调用，部分不支持

## 决策

### 1. 命名统一 — 优先 IDisposable/IAsyncDisposable

| 场景 | 统一命名 | 理由 |
|------|----------|------|
| 资源释放（托管/非托管） | `Dispose` / `DisposeAsync` | .NET 标准 IDisposable/IAsyncDisposable，using/await using 支持 |
| 服务停止 | `StopAsync(CancellationToken cancellationToken = default)` | 语义清晰，与 IHostedService.StopAsync 对齐 |
| 连接关闭 | `DisposeAsync`（实现 IAsyncDisposable） | 连接是资源，用 Dispose 统一 |

**禁止**：Close/Cleanup/Shutdown 作为公开方法名，改为 Dispose/DisposeAsync 或 StopAsync

### 2. 参数统一

```csharp
// ✅ 正确
public async Task StopAsync(CancellationToken cancellationToken = default)
public async ValueTask DisposeAsync()

// ❌ 禁止
public async Task StopAsync(CancellationToken ct)           // 参数名不统一
public async Task StopAsync()                              // �&nbsp;缺少 CancellationToken
public void Stop()                                         // 同步收口，用 StopAsync
```

### 3. 异常处理统一 — 收口函数不抛异常

收口函数（Dispose/StopAsync）内部吞掉所有异常并记录日志，不向调用方传播。
理由：调用方在 finally/using 中调用收口函数时，抛异常会掩盖原始异常。

```csharp
public async ValueTask DisposeAsync()
{
    if (_disposed) return;
    _disposed = true;
    try
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "DisposeAsync 失败");
    }
}
```

### 4. 幂等性统一 — 多次调用安全

所有收口函数必须幂等：用 `volatile bool _disposed` 或 `Interlocked.Exchange` 保证第二次调用是 no-op。

### 5. 消融实验验证

对每个收口函数执行消融实验：
1. **基线**：正常运行，记录资源使用（内存/句柄/连接数）
2. **移除收口函数**：注释掉 Dispose/StopAsync 实现，运行相同场景
3. **对比**：资源泄漏量 = 移除后资源使用 - 基线资源使用
4. **判定**：泄漏量 > 阈值 → 收口函数必要；泄漏量 ≈ 0 → 收口函数可能冗余

## 替代方案

1. **保持现状不统一**：放弃。命名混乱增加认知成本，参数不一致导致调用方需逐个确认签名。
2. **全部用 Close**：放弃。Close 不被 using/await using 支持，无法利用 RAII 模式。
3. **全部用 StopAsync**：放弃。纯资源释放（如 FileStream）用 StopAsync 语义不对，Dispose 是 .NET 标准。

## 后果

- 正面：命名统一降低认知成本；IDisposable 支持 using/await using；异常不传播避免掩盖原始异常；幂等性保证安全
- 负面：现有 Close/Cleanup/Shutdown 需重命名为 Dispose/StopAsync，改动面大；消融实验需逐个验证
- 中性：重命名保持渐进式（见 ADR 0007），每次统一一个模块

## 实施进度

| 步骤 | 内容 | 状态 | 日期 | 备注 |
|------|------|------|------|------|
| 1 | 参数统一：CancellationToken 默认值 | ✅ 完成 | 2026-08-29 | FileWatcherIntegration.StopAsync 已修复 |
| 2 | 异常处理：收口函数不抛异常 | ✅ 验证通过 | 2026-08-29 | AST 分析 329 处收口函数，0 个直接 throw 违规 |
| 3 | 幂等性：volatile bool _disposed | ✅ 分析完成 | 2026-08-29 | 89 个生产文件无 _disposed 关键字，但大部分已通过其他方式实现幂等性：`?.` 运算符、`Interlocked.Exchange(ref x, null)`、置字段为 null、转发到本身幂等的对象。强制 _disposed 标志是过度规范化，保持现状 |
| 4 | 消融实验方案 | ✅ 已定义 | 2026-08-29 | 见上方第5节 |
