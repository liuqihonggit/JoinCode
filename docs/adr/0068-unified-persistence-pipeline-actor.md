# 0068. 统一持久化管道（Actor 模型）

- 状态：accepted
- 日期：2026-09-06
- 决策者：AI + 用户确认

## 背景

MCP 工具的 6 个状态型分类（code_index/memory/permission/task/notebook/structured_output）各自维护内存状态，跨进程不共享。`jcc mcp call` 每次是新进程，状态在进程退出后丢失。

首轮验证（交接文档 A-组1）将此标记为"设计限制非bug"，但实际是未实现持久化。用户要求全部状态型落盘可跨进程复用。

直接给每个分类各自加 SaveAsync/LoadAsync 会产生以下问题：

| 问题 | 根因 | 后果 |
|------|------|------|
| 锁跨 await 释放失败 | `ReaderWriterLockSlim` 线程亲和，`using` scope 跨 `await WriteAllTextAsync` 续体回到另一线程，`ExitReadLock` 报 "The read lock is being released without being held" | 持久化静默失败（code_index 首次验证即踩此坑） |
| 每个分类各自处理锁 | 6 个分类 × 各自的读写锁 + 序列化 + 文件 IO = 6 套重复逻辑 | 维护成本高，容易再次踩锁坑 |
| 大快照阻塞调用线程 | code_index 索引 186MB，序列化+写文件在调用线程同步执行 | rebuild 返回慢，交互卡顿 |

## 决策

采用 **Actor 模型统一持久化管道**：一个 `PersistencePipeline` Actor（单消费者 Channel），所有分类的持久化请求统一入队，Actor 单线程串行写文件。

### 架构

```
多生产者                    单消费者（Actor 线程）
┌─────────────┐            ┌──────────────────────┐
│ code_index  │──┐         │  PersistencePipeline  │
│ memory      │──┤ Enqueue │  (ActorBase<Persist>) │──→ 磁盘
│ permission  │──┤         │  HandleAsync:         │
│ task        │──┤         │    CreateDirectory    │
│ notebook    │──┤         │    WriteAllTextAsync  │
│ structured  │──┘         └──────────────────────┘
└─────────────┘
     Channel<PersistRequest>（有界 + DropOldest）
```

### 核心设计

1. **快照模式** — 生产者在主线程用读锁构造不可变快照（`ToList` + 序列化），锁内同步完成不跨 await，释放锁后把 JSON 字符串扔进队列。Actor 线程不访问共享可变状态，无并发问题。

2. **ActorBase 复用** — 继承现有 `ActorBase<TCommand>`（foundation/AsyncLock/src/ActorBase.cs），单消费者 Channel，命令 FIFO 串行处理，单条异常不退出 Consumer 循环。无需新建并发原语。

3. **背压策略** — 有界通道 + `BoundedChannelFullMode.DropOldest`：索引快照只保留最新，旧的丢弃合理（rebuild 产生的旧快照无需持久化）。

4. **统一入口** — `IPersistencePipeline.EnqueueAsync(PersistRequest)`，所有分类通过同一入口持久化。读操作不走管道，各分类自行 LoadAsync（文件读，无并发）。

5. **分类路由** — `PersistRequest.Category` 标识来源（"code_index"/"memory"/...），Actor 可按分类做差异化处理（如日志、监控、优先级）。

### 接口

```csharp
// foundation/Abstractions/05-memory/FileIO/IPersistencePipeline.cs
public interface IPersistencePipeline : IAsyncDisposable
{
    ValueTask EnqueueAsync(PersistRequest request, CancellationToken ct = default);
    bool TryEnqueue(PersistRequest request);
}

public sealed record PersistRequest(
    string Category,    // "code_index" / "memory" / ...
    string Directory,   // 目标目录
    string FileName,    // 文件名
    string Content      // 已序列化的 JSON
);
```

### 实现

```csharp
// infrastructure/Infrastructure/IO/PersistencePipeline.cs
[Register(typeof(IPersistencePipeline), ServiceLifetime.Singleton)]
public sealed class PersistencePipeline : ActorBase<PersistRequest>, IPersistencePipeline
{
    protected override async ValueTask HandleAsync(PersistRequest req, CancellationToken ct)
    {
        if (!_fs.DirectoryExists(req.Directory)) _fs.CreateDirectory(req.Directory);
        var path = _fs.CombinePath(req.Directory, req.FileName);
        await _fs.WriteAllTextAsync(path, req.Content, ct).ConfigureAwait(false);
    }
}
```

## 替代方案

| 方案 | 放弃原因 |
|------|----------|
| 各分类各自 SaveAsync/LoadAsync | 6 套重复锁逻辑，已踩 ReaderWriterLockSlim 跨 await 坑 |
| 用 AsyncLock 替代 ReaderWriterLockSlim | 解决跨 await 但仍各自重复序列化+写文件逻辑 |
| 同步写文件（不入队） | 大快照（186MB）阻塞调用线程，交互卡顿 |
| 用 BlockingCollection | 同步阻塞队列，无法 async，且 ActorBase 已用 Channel 更优 |

## 后果

- 正面：统一入口、无锁、不卡死、Actor 单线程串行写文件无并发问题
- 正面：复用现有 ActorBase，无新并发原语
- 负面：快照在队列里占内存（code_index 186MB），有界+DropOldest 缓解
- 负面：fire-and-forget 模式下写入失败只记日志不通知调用方（可加 TaskCompletionSource 回调解决，暂不需要）

## 验证

code_index 首迁验证：rebuild → EnqueueAsync 落盘 → 新进程 EnsureIndexLoadedAsync 加载 → search 命中。验证通过后推广至 memory/permission/task/notebook/structured_output。
