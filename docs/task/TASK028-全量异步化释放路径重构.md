# TASK028: 全量异步化释放路径重构

## 架构决策

**禁止双接口,同步异步二选一,只选异步**

1. 项目内类型禁止同时实现 `IDisposable` + `IAsyncDisposable`
2. 项目内类型只实现 `IAsyncDisposable`(异步释放)
3. BCL 类型(FileStream/CTS/MemoryStream 等)改不了,白名单豁免
4. 分析器 JCC9104/JCC9107 覆盖所有 IAsyncDisposable 类型(白名单只豁免 BCL)

## 根因

- `Entity` 同时实现 `IDisposable` + `IAsyncDisposable`,`DisposeAsync()` 是 trivial(委托 Dispose)
- 双接口类型导致 `using var`/`Dispose()` 语义模糊(同步还是异步?)
- 用户决策:禁止双接口,只选异步,消除模糊性

## 执行计划

### 第0步:分析器增强(已完成)
- JCC9104/JCC9107 覆盖双接口类型
- 白名单:CTS/MemoryStream/CancellationTokenRegistration(BCL 类型,项目无关)
- JCC9107 排除 lambda 内的 Dispose 调用(CancellationToken 回调不能 async)

### 第1步:Entity 基类改造
- 移除 `IDisposable`,只保留 `IAsyncDisposable`
- `Dispose()` 逻辑移到 `DisposeAsync()`
- 移除 `GC.SuppressFinalize(this)`(没有析构函数)

### 第2步:Entity 子类改造(17个直接子类)
- 移除 `IDisposable` 实现(继承自 Entity)
- 移除 `Dispose()` override,逻辑移到 `DisposeAsync()` override
- 69 个覆写 `DisposeAsync()` 的子类:检查是否有 `Dispose()` override,移除

### 第3步:调用方改造
- 所有 `entity.Dispose()` → `await entity.DisposeAsync()`
- 所有 `using var entity = ...` → `await using var entity = ...`
- 所在方法必须是 async(如果不是,改成 async)

### 第4步:SessionCache/ISessionCache 改造
- `Set<T>`/`Remove`/`Clear` 改成 async
- `ISessionCache` 接口改成 async
- 所有调用方改成 async

### 第5步:全量编译通过 + 提交

## 进展

- 2026-09-21: 分析器增强已完成(JCC9104/JCC9107 覆盖双接口 + BCL 白名单 + lambda 排除)
- 2026-09-21: 开始 Entity 基类改造
