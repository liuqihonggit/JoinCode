# ADR 0139: KV 存储租约模式 + 跨进程 Mutex 串行化

**状态**: accepted
**日期**: 2026-10-18
**关联**: bugfix [kvstore-wal-cross-process-release-race](../../../modules/persistence/docs/bugfix/kvstore-wal-cross-process-release-race.md), 替代 [ee105ccc3](https://github.com/liuqihonggit/JoinCode/pull/413) try-catch 缓解

## 背景

PithosDB 不支持多进程共享数据目录（MemTable 进程内独立，多进程并发写会丢数据），但 `WriteAheadLog.Open` 用 `FileShare.ReadWrite` 允许多进程同时 Open。jcc 多实例共享 `~/.jcc/kvstore/`，关闭时 `File.Delete(wal.log)` 遇对端句柄抛 IOException 崩溃。

现有缓解（ee105ccc3）try-catch 吞异常不崩，但竞争仍存在。`IKvStore` 注册为 Singleton，从 Host 启动持有到 Dispose，竞争窗口 = 进程生命周期。

## 决策

**租约模式 + 跨进程短 Mutex**：

1. 新增 `IKvStoreLeaseFactory` + `IKvStoreLease` 接口
2. `Acquire()` 时 Open PithosDb + 获取 Mutex（非阻塞，失败降级内存）
3. `Dispose()` 时 Close PithosDb + 释放 Mutex
4. 竞争窗口 = 单次操作时间（几毫秒），不是进程生命周期

```
IKvStoreLeaseFactory.Acquire() → IKvStoreLease
  ├─ Mutex.WaitOne(0) 成功 → Open 磁盘 PithosDb（独占）
  └─ Mutex.WaitOne(0) 失败 → OpenInMemory（降级，记日志）
IKvStoreLease.Dispose()
  └─ Close PithosDb → ReleaseMutex → Dispose Mutex
```

## 替代方案（考虑过但放弃）

| 方案 | 放弃原因 |
|------|---------|
| 进程级 Mutex 持有到 Dispose | 持有 = 进程生命周期，长驻 mcp server 先启动时后续 CLI 全降级 |
| 写时短锁 + 读不加锁 | PithosDB 无只读 Open 模式；多进程 MemTable 不一致仍丢数据 |
| 仅 try-catch + 重新发布 | 治标不治本，竞争仍存在，WAL 残留靠 replay |

## 后果

**正面**：
- 竞争窗口缩到单次操作（几毫秒）
- 不用 kvstore 的命令不持锁
- 降级内存的租约用完即丢，不长期占资源

**负面**：
- 每次 Acquire Open PithosDb（读 manifest + replay WAL + 打开 SST readers，几 ms 开销）
- 5 个消费方改为注入 `IKvStoreLeaseFactory`，每次操作 `using var lease = _factory.Acquire()`
- 连续多次 kv 操作的批处理场景开销增大（可后续优化：批租约）

## 消费方改动清单

| 消费方 | 路径 | 改动 |
|--------|------|------|
| CodeIndexer | `modules/perception/code_index/indexing/CodeIndexer.cs` | `_kvStore` → `_factory` + using |
| HighWaterMarkManager | `modules/storage/scheduling/storage/HighWaterMarkManager.cs` | 同上 |
| GraphPersistence | `modules/perception/code_index/analytics/GraphPersistence.cs` | 同上 |
| InMemoryParentDocumentStore | `modules/perception/code_index/embedding/InMemoryParentDocumentStore.cs` | 同上 |
| KvStoreTtlCleanupService | `modules/abstractions/infrastructure/housekeeping/KvStoreTtlCleanupService.cs` | 同上 |

## 验收标准

- [ ] `IKvStoreLeaseFactory.Acquire()` 磁盘模式 Open PithosDb + 获取 Mutex
- [ ] Mutex 被占时降级 `OpenInMemory`，记 DiagnosticLog
- [ ] `IKvStoreLease.Dispose()` Close PithosDb + 释放 Mutex
- [ ] 5 个消费方改为工厂+using，编译通过
- [ ] 单元测试覆盖 Acquire/Dispose/降级路径
- [ ] 0 警告 0 错误（不抑制）
