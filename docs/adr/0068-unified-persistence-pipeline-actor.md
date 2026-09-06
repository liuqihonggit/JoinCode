# 0068. 统一持久化管道（Actor 模型）

- 状态：accepted
- 日期：2026-09-06
- 决策者：AI + 用户确认

## 背景

MCP 工具的 6 个状态型分类（code_index/memory/permission/task/notebook/structured_output）各自维护内存状态，跨进程不共享。`jcc mcp_call` 每次是新进程，状态在进程退出后丢失。

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

## 各分类持久化状态（2026-09-06）

| 分类 | 工具数 | 持久化方式 | 落盘路径 | 跨进程验证 | commit |
|------|--------|-----------|----------|-----------|--------|
| code_index | 20 | 统一管道 | `.jcc/code-index/code-index.json` | ✅ rebuild→新进程search命中 | `9c347f088` `ccf1438c7` |
| memory (team paths) | 3 | 统一管道 | `.jcc/memory/team-paths.json` | ✅ add→新进程list命中 | `a67273059` |
| permission | 7 | 统一管道 | `.jcc/permission/rules.json` | ✅ add_rule→新进程list命中 | `f618ab8ca` |
| task | 12 | 文件系统（天然） | `~/.jcc/tasks/task-*.json` | ✅ TaskCreate→新进程TaskList命中 | 已有 FileBasedTaskService |
| notebook | 10 | .ipynb 文件（天然）+ Read-before-Edit 自动读取 | .ipynb 文件本身 | ✅ create→add_cell→read→edit→read | `f1e977b15` |
| structured_output | 2 | 统一管道 | `.jcc/structured-output/schemas.json` | ✅ register→新进程validate命中 | `df7d7e718` |
| todo | 4 | 统一管道 | `.jcc/todo/todos.json` | ✅ TodoWrite→新进程todo_list命中 | `a61120474` |
| goal | 3 | 运行时会话状态（不需持久化） | — | N/A | — |
| config/network/planning/notification | 6 | 无状态/运行时 | — | N/A | — |
| web/desktop | 37 | 无状态外部操作 | — | N/A | — |
| **git** | **9** | **跳过（用户正在改）** | — | **⏳ 待验证** | — |
| **gh_cache** | **7** | **统一管道** | `.jcc/gh_cache/` | ✅ 首次下载→缓存命中 | `统一到 PersistencePipeline, 删除 GitHubCacheWriteActor` |

## git 持久化后续参考

git 9 个工具（git_add/git_branch/git_clone/git_commit/git_diff/git_log/git_pull/git_push/git_status）操作 git 仓库，状态在 `.git` 目录本身，天然跨进程共享，**预期不需要额外持久化**。

### 跳过原因

用户正在修改 git 工具相关代码，验证暂时跳过。

### 后续验证步骤

1. **等用户完成 git 工具修改后**，重新编译 `dotnet build app/JoinCode/JoinCode.csproj -c Debug`
2. **逐个验证 9 个 git 工具的成功路径**（在 `D:\project\w1` 仓库上操作）：
   - `git_status` → 返回当前工作区状态
   - `git_add` → 添加文件到暂存区
   - `git_commit` → 提交暂存更改
   - `git_log` → 查看提交历史
   - `git_diff` → 查看文件差异
   - `git_branch` → 创建或切换分支
   - `git_pull` / `git_push` → 需要远程仓库，可跳过或 mock
   - `git_clone` → 需要远程仓库，可跳过或 mock
3. **跨进程验证**：git 工具操作 .git 目录，新进程应能看到前一个进程的 git 操作结果（如 git_add 后新进程 git_status 应显示已暂存）

### 需要阅读的代码

| 文件 | 用途 |
|------|------|
| `core/execution/McpToolDispatch/src/**/GitToolHandlers*.cs` | git 工具 handler 实现 |
| `docs/plans/mcp/验证交接A-组1.md` 第 145-155 行 | git 9 工具交接验证记录（已 ✅ OK，修复 1 个 bug：空字符串 working_dir 未回退默认目录） |
| `docs/adr/0068-unified-persistence-pipeline-actor.md` 本节 | 持久化管道架构参考 |

### 已知 bug（交接文档记录）

- `git_commit` 空字符串 `working_dir` 未回退默认目录导致 `ArgumentException`（MCP 框架把未传可选参数设为空字符串而非 null，`??` 运算符不拦截空字符串）— **此根因已在 `ce16d38ab` 修复生成器 nullable 误判，git_commit 的 working_dir 参数应同步验证是否已修复**
