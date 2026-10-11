# ADR 0128: 命名 Mutex 替代文件邮箱锁（回归内核 Mutex）

- 状态：accepted
- 日期：2026-10-02
- 决策者：liuqihong
- 取代：[0107](0107-file-mailbox-lock-replace-mutex.md) 的锁机制部分

## 背景

### 问题：FileMailboxLock 在 Windows 上 flaky

ADR 0107 决策"用文件独占打开（`FileMode.CreateNew` + `DeleteFile`）替代命名 Mutex"，落地后 `FileMailboxLock` 在 Windows 上出现 flaky：

- **杀毒软件拦截锁文件删除**：`Dispose` 时 `DeleteFile(lockFilePath)` 被杀毒软件占用/拦截，锁文件残留，下次 `FileMode.CreateNew` 失败，触发 stale lock 清理逻辑（5 分钟超时），导致后续获取锁长时间失败。
- **根因**：文件系统操作不是原子的跨进程同步原语，杀毒软件、索引服务、备份程序都可能短暂占用文件句柄，导致 `CreateNew`/`DeleteFile` 失败。这是 Windows 文件系统的固有特性，无法通过代码规避。
- **表现**：LINK_001/LINK_003 链路测试间歇性失败，已临时标记 `[Trait("Category", "Integration")]` 排除 CI。

### 0107 的反 Mutex 论据重新评估

0107 当初放弃命名 Mutex 的三个理由：

| 0107 的论据 | 重新评估 |
|------------|---------|
| 依赖 `Microsoft.VisualStudio.Threading` 包 | 已移除该包，直接用 BCL `System.Threading.Mutex`，零外部依赖 |
| VSTHRD003 警蔽争议 | 不再使用 VS Threading，无 VSTHRD003 警蔽问题 |
| 共享锁环形等待风险 | `BatchLock` 路径排序防死锁策略保留，命名 Mutex + 路径排序 = 双重防死锁 |

三个论据均已不成立，回归命名 Mutex 无副作用。

## 决策

### 1. 多层锁：进程内 SemaphoreSlim + 跨进程命名 Mutex

命名 Mutex 是**递归锁**（同线程可重入），而原文件锁是不可重入的（同进程不同线程也互斥）。为保持语义一致，采用多层锁：

| 层 | 锁类型 | 作用域 | 可重入 | 职责 |
|----|--------|--------|--------|------|
| 第1层 | `SemaphoreSlim(1,1)` | 进程内 | 否 | 同进程多线程互斥（替代原文件锁的不可重入语义） |
| 第2层 | `System.Threading.Mutex`（命名） | 跨进程 | 是 | 不同进程互斥（内核对象，杀毒软件不干预） |

- **获取顺序**：先 SemaphoreSlim（进程内）再 Mutex（跨进程）
- **释放顺序**：先 Mutex 再 SemaphoreSlim（相反，避免临时让其他进程空等）
- **SemaphoreSlim 静态字典**：`ConcurrentDictionary<string, SemaphoreSlim>`，key=文件路径小写规范化，按路径复用信号量实例

### 2. 命名 Mutex 细节

- **获取锁** = `Mutex.WaitOne(timeout)`，OS 内核保证原子性
- **释放锁** = `Mutex.ReleaseMutex()` + `Dispose()`
- **进程崩溃** = OS 自动回收 Mutex 内核对象，下一个等待者收到 `AbandonedMutexException` 并视为获取成功（无需超时清理）
- **等待锁** = 内核等待（不占 CPU），超时返回 false

### 3. Mutex 作用域：Global 优先，Local 回退

- **优先 `Global\`**：文件是全机器可见的，Mutex 亦应全机器作用域，覆盖跨终端会话场景（SSH 登录、多用户）
- **回退 `Local\`**：非交互式服务账户可能无 `SeCreateGlobalPrivilege`，捕获 `UnauthorizedAccessException`/`IOException` 回退 `Local\`（per-session），保证可用

### 4. Mutex 名字规范化

- 名字 = `jcc_mailbox_{SHA256(fullPath.ToLowerInvariant())}`（hex 编码）
- `ToLowerInvariant()` 用于 Windows 路径大小写不敏感规范化
- SHA256 保证不同路径映射到不同 Mutex，无碰撞

### 5. 异步 API 保持

- `Mutex.WaitOne` 是同步阻塞内核调用，用 `Task.Run` 包装为异步，避免占用调用线程
- `AbandonedMutexException` 在 `Task.Run` 内捕获并返回 `true`（视为获取成功）

### 6. 保留 BatchLock 路径排序

`BatchLock.cs` 的路径排序防死锁策略保留。命名 Mutex + 路径排序 = 双重防死锁。

## 替代方案

### 方案A：保留 FileMailboxLock，增加重试

增加 `DeleteFile` 重试次数和退避时间。

**放弃原因**：
- 治标不治本：杀毒软件占用时间不可预测，重试仍可能失败
- flaky 根因是架构错误（文件系统不是同步原语），不是参数调优能解决

### 方案B：用文件独占打开但不删除锁文件

获取锁 = `File.Open(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)`，释放 = 关闭流，不删除锁文件。

**放弃原因**：
- 进程崩溃后锁文件残留，下次 `FileShare.None` 打开仍成功（OS 不检查文件内容），但其他进程无法打开 → 实际上变成了"第一个打开的进程永久持有锁"
- 需要额外的 stale lock 检测（写 PID + 心跳），复杂度等于重新实现一遍 Mutex

### 方案C：用 Semaphore 跨进程

`Semaphore(1, 1, name)` 也能跨进程互斥。

**放弃原因**：
- Semaphore 不跟踪持有者，任何进程都能 `Release`（即使不是持有者），易误用
- Mutex 的"只有持有者能 ReleaseMutex"语义更安全
- AbandonedMutexException 提供崩溃检测，Semaphore 无此机制

## 影响

- 新增 `lib/infrastructure/async_file_lock/NamedMutexMailboxLock.cs`（public）
- 归档旧 `FileMailboxLock.cs` 到 `.xxx/`（禁删规则）
- `FileLock.cs` 委托从 `FileMailboxLock` 改为 `NamedMutexMailboxLock`
- `MailboxActor.cs`、`AgentDiscoveryService.cs`、`TeammateMailboxService.cs`、`IAgentDiscovery.cs` 引用更新
- LINK_001/LINK_003 测试可移除 `[Trait("Category", "Integration")]` 标记，回归 CI
- 继承 0107 的 Actor 邮箱模型、Agent 发现、消息去重设计（仅锁机制变更）

## 验证

- 编译通过（Debug 模式）
- LINK_001/LINK_003/LINK_004/LINK_006 链路测试通过（命名 Mutex 互斥正确）
- 跨进程并发安全（OS 内核保证）
