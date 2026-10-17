# PithosKvStore 多进程 WAL 释放竞争

**日期**: 2026-10-18
**严重度**: P1（进程崩溃，数据不丢）
**状态**: 已修复（ADR 0139 accepted）

## 问题现象

两个 jcc 进程并发执行（如两个 AI 同时跑 `jcc gh pr create`），其中一个进程退出时崩溃：

```
System.IO.IOException: The process cannot access the file
'C:\Users\54076\.jcc\kvstore\wal.log' because it is being used by another process.
   at System.IO.FileSystem.DeleteFile(String fullPath)
   at PithosDB.Core.PithosDb.FlushForShutdown()
   at PithosDB.Core.PithosDb.Dispose()
   at Infrastructure.Persistence.PithosKvStore.DisposeAsync()
   ... (Host DisposeAsync → Main)
```

PR #418 创建时复现（2026-10-18 03:48:01）。

## 根因分析

### 根因1：PithosDB 文件模式允许多进程 Open

`WriteAheadLog.Open` (`Core/WriteAheadLog.cs:56`)：
```csharp
var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
```

`SSTableReader.Open` (`Storage/SSTableReader.cs:61`)：同样 `FileShare.ReadWrite`。

**多进程可以同时 `PithosDb.Open` 同一目录**，不报文件锁冲突。但 PithosDB 的 MemTable 是进程内的，多进程各自独立 MemTable + WAL 追加写会交错损坏——**文件模式没阻止多进程 Open，但 PithosDB 设计上不支持多进程安全共享**。

### 根因2：IKvStore 单例长期持有

`ServiceRegistration.CoreServices.cs:64-67` 注册 `IKvStore` 为 Singleton：
```csharp
services.AddEnvSwitch<IKvStore>(
    JccEnvVar.FileSystemMode, "InMemory",
    _ => new InMemoryKvStore(),
    _ => new PithosKvStore(Path.Combine(AppDataConstants.Paths.JccDirectory, "kvstore")));
```

5 个消费方（`CodeIndexer`、`HighWaterMarkManager`、`GraphPersistence`、`InMemoryParentDocumentStore`、`KvStoreTtlCleanupService`）构造函数注入 `IKvStore` 字段长期持有。

**kvstore 从 Host 启动持有到 Dispose，竞争窗口 = 整个进程生命周期**。两个 jcc 进程并发几秒，窗口完全重叠。

### 根因3：关闭时 File.Delete 遇到对端句柄

`PithosDb.FlushForShutdown` → `ResetWal` (`PithosDb.cs:621-635`) 关闭时 `File.Delete(wal.log)`。Windows 不允许删除被其他进程打开的文件（即使 `FileShare.ReadWrite`），抛 `IOException`。

### 现有缓解（ee105ccc3 / PR #413, 2026-10-15）

`PithosDb.cs:1037-1038` 已 try-catch `FlushForShutdown`：
```csharp
try { FlushForShutdown(); }
catch (Exception ex) { _options.DiagnosticLog?.Invoke($"[PithosDB.Dispose] FlushForShutdown failed: {ex.Message}"); }
```

`ResetWal` (`PithosDb.cs:625-633`) 已 try-catch `File.Delete` 的 IOException，失败时尝试 Truncate，再失败则保留 WAL（下次 replay 幂等）。

**问题：用户跑的 jcc.exe 是旧版本，未包含此修复，异常逃逸到 Main 崩溃。** 即使包含，也只是"容忍失败不崩"，竞争仍存在。

## 修复方案

### 已选：跨进程 Mutex 串行化

`PithosKvStore` 构造时获取 `Mutex(false, "Global\jcc-pithos-{hash(dirPath)}")`：
- 获取成功 → 磁盘 kvstore（独占），Dispose 时释放
- 获取失败 → 降级 `PithosDb.OpenInMemory()`（短命令够用，写入不持久）

**优点**：彻底消除竞争；符合 PithosDB 单进程假设；改动最小（只改 PithosKvStore 构造/Dispose）。
**缺点**：降级实例写入不持久；Mutex 持有 = 进程生命周期，若长驻 mcp server 先启动，后续 CLI 都降级。

### 待评估：缩短持有时间（快读快释放）

用户指出"持有时间太长"——当前单例从启动到 Dispose 全程持有。可选优化：

| 方案 | 持有窗口 | 改动量 | 代价 |
|------|---------|--------|------|
| 进程级 Mutex（已选） | 进程生命周期 | 小（PithosKvStore 构造/Dispose） | 降级实例不持久 |
| 写时短锁 | 单次写操作 | 中（PithosKvStore 每次写加锁） | 多进程 MemTable 不一致仍丢数据 |
| 快读快释放（租约） | 单次操作 | 大（5 个消费方改工厂+using） | 每次 Open PithosDb 开销几 ms |
| 读不加锁+写独占 | 写操作 | 大（分离读写路径） | PithosDB 不支持只读 Open |

**注**：PithosDB 多进程并发写的根本障碍是 MemTable 进程内独立——即使文件模式允许并发 Open，两个进程的 MemTable 各自积累，flush 时各自写 SST + 重置 WAL，会丢失对端未 flush 数据。所以**写必须独占**，读可并发但 PithosDB 无只读 Open 模式。

## 验收标准

- [ ] 两个 jcc 进程并发执行，退出时无 IOException 崩溃
- [ ] 第一个进程独占磁盘 kvstore，第二个进程降级内存并记日志
- [ ] 降级进程的读写操作正常工作（内存模式）
- [ ] 独占进程 Dispose 后，新进程能获取 Mutex 用磁盘 kvstore
- [ ] 单元测试覆盖 Mutex 获取/释放/降级路径
- [ ] 编译 0 警告 0 错误（不抑制）

## 关联

- 现有 try-catch 缓解：commit `ee105ccc3` (PR #413)
- 决策记录：ADR 0139（待写）
- 复现 PR：[#418](https://github.com/liuqihonggit/JoinCode/pull/418)
