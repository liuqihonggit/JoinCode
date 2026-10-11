# TASK030 — 分析器 JCC9104/JCC9107 增强 + 双向 CodeFixProvider

## 用户明确要求

### 1. 分析器报告规则："要么同步要么异步"
- JCC9104/JCC9107 报告**所有** IAsyncDisposable 类型，**不豁免双接口**
- BCL 白名单豁免（CancellationTokenRegistration/MemoryStream/CancellationTokenSource/StreamReader/StreamWriter）
- 状态：✅ 已改完

### 2. IFileSystem 异步化保留
- 状态：✅ 生产代码已改完并提交

### 3. 双向 CodeFixProvider
- **同步→异步**：`x.Dispose()` → `await x.DisposeAsync().ConfigureAwait(false)`，`using var` → `await using var`
- **异步→同步**：当异步传播连锁放大时，改回同步（用 SyncFileReader 封装阻塞）
- 根据影响范围决定方向

### 4. 同步阻塞封装在单独 class
- SyncFileReader 已创建，封装 .GetAwaiter().GetResult()

## 执行步骤

1. [x] 分析器改为报告所有 IAsyncDisposable
2. [x] SyncFileReader 封装同步阻塞
3. [x] ISessionCache 改 async（SetAsync/RemoveAsync/ClearAsync）
4. [x] SessionScope 改 IAsyncDisposable
5. [x] SessionRouter 改 async（RemoveScopeAsync/ClearAsync）
6. [x] WorkflowPluginBase.Unload → UnloadAsync
7. [ ] 写双向 CodeFixProvider
8. [ ] 用 CodeFixProvider 自动修剩余 84 个错误
9. [ ] 全量编译通过 + 提交
