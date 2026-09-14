# ADR 0107: 文件邮箱锁替代跨进程共享锁

## 状态

accepted

## 背景

### 问题1：Microsoft.VisualStudio.Threading 死依赖

`lib/infrastructure/Infrastructure.csproj` 引用 `Microsoft.VisualStudio.Threading` 17.13.2，但全项目无任何代码显式使用其类型（`JoinableTaskFactory`/`AsyncLazy<T>`/`AsyncQueue<T>` 等）。

唯一实际使用的是 `AsyncCrossProcessMutex`（跨进程命名互斥量），在 `lib/infrastructure/async_file_lock/FileLock.cs` 中用于实现文件锁。

该包传递引入 `Microsoft.VisualStudio.Threading.Analyzers`，产生 VSTHRD003 警告（"Avoid awaiting foreign tasks"），导致全项目 29 处需要屏蔽（18 个 `[SuppressMessage]` + 11 个 `#pragma` + 3 个 csproj `<NoWarn>`）。

### 问题2：共享锁的环形等待风险

`AsyncCrossProcessMutex` 基于 Windows 命名 Mutex，是共享锁机制。多个进程争抢同一 Mutex，可能产生环形等待：
- 进程 A 持有锁1 等待锁2
- 进程 B 持有锁2 等待锁1
- 死锁

当前 `BatchLock.cs` 通过路径排序防死锁（所有进程按统一顺序获取锁），但这是预防措施而非架构保证。

### 问题3：VSTHRD003 屏蔽争议

VSTHRD003 是 Visual Studio SDK 的线程分析规则，为 VS 扩展设计。jcc.exe 是 CLI 工具，不是 VS 扩展。之前的处理方式（`[SuppressMessage]`/`.editorconfig` 禁用/csproj `<NoWarn>`）都是屏蔽，用户认为正常工程不应屏蔽任何警告。

## 决策

### 1. 用文件独占打开替代命名 Mutex

跨进程互斥用 `File.Open` 独占模式（`FileShare.None`）实现：
- 创建锁文件 = 获取锁（OS 保证原子性）
- 关闭文件句柄 = 释放锁
- 进程崩溃 = OS 自动释放文件句柄（无需超时清理）

**优势**：
- 不需要命名 Mutex，不依赖 `Microsoft.VisualStudio.Threading`
- OS 保证原子性，无竞态
- 进程崩溃自动恢复（OS 回收文件句柄）
- 跨平台兼容（Windows 强制锁，Linux advisory lock 但项目主要在 Windows）

### 2. 用 Actor 邮箱串行化进程内锁请求

继承 `ActorBase<FileLockCommand, FileLockEvent>`，串行化同一进程内的锁请求：
- 请求锁 = 向 Actor 发送 `RequestLockCmd`
- Actor Consumer 串行处理，用文件独占打开尝试获取锁
- 获取失败 = 等待重试（带指数退避）
- 获取成功 = 返回 `LockAcquiredEvent`

**优势**：
- 进程内无锁竞争（Actor 单 Consumer 串行处理）
- 复用现有 `ActorBase` 基础设施
- 符合 AGENTS.md "死锁处理用 Actor 邮箱模型"

### 3. 保留批量锁路径排序

`BatchLock.cs` 的路径排序防死锁策略保留，作为额外保障。文件独占打开 + Actor 串行化 + 路径排序 = 三重防死锁。

### 4. 移除 Microsoft.VisualStudio.Threading 包

- 移除 `Infrastructure.csproj` 中的包引用
- 移除 `GlobalUsings.cs` 中的 `global using Microsoft.VisualStudio.Threading;`
- 移除所有 29 处 VSTHRD003 屏蔽代码
- 移除 3 个 csproj 中的 `<NoWarn>VSTHRD003</NoWarn>`

## 替代方案

### 方案A：真正的文件邮箱排队

每个文件路径对应一个邮箱目录，请求文件按时间戳排队，进程轮询自己是否队首。

**放弃原因**：
- 轮询延迟（文件系统操作不是即时的）
- 竞态检测复杂（多个进程同时检查队首）
- 实现复杂度高，收益不明显（当前项目跨进程位置不多）

### 方案B：命名管道锁服务

独立锁服务进程监听命名管道，Actor 邮箱串行处理请求。

**放弃原因**：
- 需要独立服务进程，部署复杂
- 服务进程崩溃 = 所有锁失效
- 过度工程化

### 方案C：仅禁用分析器保留包

保留 `Microsoft.VisualStudio.Threading` 包但禁用分析器。

**放弃原因**：
- 保留死依赖，违反"不保留无用依赖"原则
- 仍是屏蔽而非正面解决

## 影响

- `lib/infrastructure/async_file_lock/` — 新增 `FileMailboxLock.cs`（public）+ 修改 `FileLock.cs`
- `lib/infrastructure/Infrastructure.csproj` — 移除包引用
- `lib/Fusion/GlobalUsings.cs` — 移除 global using
- `llm/agents/Coordinator/Team/core/TeammateMailboxService.cs` — 升级支持跨进程模式
- 26 个文件 — 移除 VSTHRD003 屏蔽代码
- 3 个 csproj — 移除 NoWarn
- 编译产物减小（不再包含 VS Threading DLL）

## 邮箱机制升级

### TeammateMailboxService 双模式

升级 `TeammateMailboxService` 支持跨进程和不跨进程两种模式：

| 模式 | 构造参数 | 写操作锁 | 适用场景 |
|------|---------|---------|---------|
| 不跨进程（默认） | `crossProcess=false` | `AsyncLock`（进程内） | 单 jcc.exe 进程，零跨进程开销 |
| 跨进程 | `crossProcess=true` | `FileMailboxLock` + `AsyncLock` | 多 jcc.exe 进程并发，文件邮箱安全读写 |

- **写操作**（`SendAsync`/`RewriteMailboxFileAsync`）：`crossProcess=true` 时先获取 `FileMailboxLock` 跨进程互斥，再获取 `AsyncLock` 进程内互斥
- **读操作**（`ReadSinceAsync`/`ReadUnreadAsync`）：仅 `AsyncLock` 进程内互斥，读是幂等的不需要跨进程锁
- **接口兼容**：`ITeammateMailboxService` 接口不变，`crossProcess` 是构造函数可选参数
