# AsyncLock+文件 I/O 迁移到 Actor 邮箱管道任务清单

> 📍 **导航**: [docs/](../README.md) › [task/](README.md) | **前置**: [plan/](../plan/README.md)
> 🔗 **上游索引**: [task/README.md](README.md) — 修改本文档后须同步更新此索引

> 迁移自原 ADR 0115（误写为 ADR，按 ADR README 规范转为 task）
> 相关 ADR: [0052](../adr/0052-asynclock-unified-mutex-file-access.md)、[0068](../adr/0068-unified-persistence-pipeline-actor.md)、[0074](../adr/0074-actor-supervisor-tree.md)、[0086](../adr/0086-core-tech-selection-lock-design.md)、[0100](../adr/0100-console-actor-serialize-io.md)、[0101](../adr/0101-file-watcher-unified-actor.md)、[0107](../adr/0107-file-mailbox-lock-replace-mutex.md)

## 背景

项目中有 **22 处** `AsyncLock`（`AsyncLock` = `SemaphoreSlim(1,1)` 薄封装）+ 文件 I/O 的"锁内文件访问"模式，散落在 `lib/`、`kit/`、`llm/`、`server/` 各层。虽然 ADR [0052](../adr/0052-asynclock-unified-mutex-file-access.md) 已统一用 `AsyncLock` 替代 `ReaderWriterLockSlim` 解决了跨 await 线程亲和性问题，但"锁+文件 I/O"模式仍存在以下架构问题：

| 问题 | 根因 | 后果 |
|------|------|------|
| 持锁时间长 | 文件 I/O（序列化+写入，尤其大文件如 186MB code_index）在锁持有期间执行 | 其他等待者阻塞，高并发下吞吐量下降 |
| 22 处重复逻辑 | 每个服务各自维护 `AsyncLock` + `TryLockAsync` + 序列化 + 文件 I/O + 异常处理 | 维护成本高，容易再次踩锁坑 |
| 锁+await 死锁风险 | 虽 `AsyncLock` 解决了跨 await 释放，但多个锁的获取顺序、嵌套锁、锁内 fire-and-forget 仍可能死锁（ADR [0060](../adr/0060-asynclock-sync-trylock-fireandforget-deadlock.md) 已记录 fire-and-forget 死锁） | 运行时死锁，排查困难 |
| 不符合架构方向 | AGENTS.md 第7条明确"死锁处理用 Actor 邮箱模型（消息传递替代共享锁）"，但 22 处仍用共享锁 | 架构不一致，新代码可能继续用锁而非 Actor |

## 搜索结果（2026-09-19 并行 4 子代理搜索）

### A 类：锁内文件 I/O（22 处，Actor 管道重构候选目标）

| # | 文件 | 行号 | 锁 | 文件 I/O | 说明 |
|---|------|------|-----|---------|------|
| 1 | `lib\infrastructure\io\file_system\PhysicalFileSystem.cs` | 148 | AsyncLock(路径级) | `FileStream`+Read+Write | EditFileAsync 原子读-改-写 |
| 2 | `lib\infrastructure\io\file_system\InMemoryFileSystem.cs` | 236 | AsyncLock(路径级) | Read+Write bytes | EditFileAsync 内存版同构 |
| 3 | `lib\vault\state\transcript\core\TranscriptFileWriter.cs` | 62,90 | AsyncLock `_writeLock` | ReadAllText+WriteAllText+StreamReader | JSONL 追加写事务 |
| 4 | `lib\vault\state\transcript\core\AgentTranscriptService.cs` | 76 | AsyncLock `_metaLock` | WriteAllTextAsync | agent metadata 持久化 |
| 5 | `lib\vault\memdir\memdir2\operations\core\ThinkingStore.cs` | 130 | AsyncLock `_saveLock` | WriteFileAsync | thinking_store.json 写 |
| 6 | `lib\vault\memdir\memdir2\operations\core\AssistantDailyLog.cs` | 217 | AsyncLock `_writeLock` | Load+Save 日志 | 读旧+追加+写新 |
| 7 | `lib\vault\memdir\memdir2\services\core\SessionTagService.cs` | 137 | AsyncLock `_saveLock` | WriteFileAsync | session_tags.json 写 |
| 8 | `kit\brain\context\core\services\FileHistoryService.cs` | 118 | AsyncLock `_lock` | FileExists+WriteAllTextAsync | 快照回滚 |
| 9 | `kit\brain\context\services\chat\core\DiagnosticLogRecorder.cs` | 213 | AsyncLock `_lock` | AppendAllTextAsync | 诊断日志追加 |
| 10 | `kit\brain\planning\planning2\PlanModeManager.cs` | 291 | AsyncLock `_historyLock` | ReadAllTextAsync+清状态文件 | 退出 plan 模式读 plan |
| 11 | `kit\hands\api\vcr\VcrService.cs` | 63,98 | AsyncLock `_fileLock` | Read+Write cassette | VCR 录制/回放 |
| 12 | `kit\hands\skills\services\skill\SkillService.cs` | 206 | AsyncLock `_reloadLock` | ReadFileAsync 多文件 | 技能重载 |
| 13 | `kit\hands\skills\discovery\SkillDiscoveryService.cs` | 57 | AsyncLock `_discoveryLock` | GetFiles+ReadFile | 技能发现扫描 |
| 14 | `kit\prompts\services\MagicDocsManager.cs` | 85 | AsyncLock `_semaphore` | FileExists+ReadAllTextAsync | Magic Docs 后采样 |
| 15 | `kit\prompts\utils\DynamicKeywordConfigService.cs` | 112 | AsyncLock `_reloadLock` | FileExists+ReadAllText | 配置热重载 |
| 16 | `lib\guard\permission\utils\AgentPermissionMode.cs` | 185,195 | AsyncLock `_lock` | ReadAllTextAsync+SaveRules | 权限规则加载/清空 |
| 17 | `llm\agents\Services\Support\AgentWorktreeService.cs` | 408,421 | AsyncLock `_sessionLock` | Read+WriteFile local settings | worktree 会话 |
| 18 | `llm\agents\Services\Support\AgentDefinitionProvider.cs` | 52 | AsyncLock `_cacheLock` | GetFiles+ReadAllTextAsync | agent 定义加载 |
| 19 | `kit\hands\serialization\McpAuthPersistenceService.cs` | 37,64,80 | AsyncLock `_lock` | Load+Save 配置 | MCP 认证持久化 |
| 20 | `kit\brain\context\services\context\ChatContextManager.cs` | 142 | AsyncLock `_lock` | LoadStateAsync+LoadAsync | 加载聊天上下文 |
| 21 | `server\bridge\session\core\SubprocessIoChannels.cs` | 84 | AsyncLock `_stdinLock` | StandardInput.WriteAsync | 子进程 stdin 写 |
| 22 | `llm\agents\Coordinator\Team\core\TeamManager.cs` | 227 等 8 处 | AsyncLock `_lock` | SaveStateAsync+PersistMessage | **团队管理（重灾区）** |

### 已是 Actor 邮箱模式（无需重构，作为重构模板）

| 文件 | 说明 |
|------|------|
| `llm\agents\Coordinator\Team\core\MailboxActor.cs` | Actor 串行化 + 跨进程 FileMailboxLock，**最佳重构模板** |
| `llm\agents\Coordinator\Core\discovery\AgentDiscoveryService.cs` | FileMailboxLock 保护注册表读写 |
| `server\dream\task\DreamTaskPersistence.cs` | FileLock 保护任务文件 |
| `kit\hands\build\BuildQueueService.cs` | CrossProcessBuildLock + Channel |

### B 类：锁内内存 + 锁外持久化（重构价值低，不在本任务范围）

- `kit\mcp\core\management\ToolInterventionManager.cs` — 锁内改内存，锁外 `SaveToDisk`
- `kit\mcp\core\management\McpServerStateManager.cs` — 锁内改内存，锁外 `PersistAsync`
- `server\dream\task\PersistentDreamTaskRegistry.cs` — 锁内改内存，锁外 `_persistence.SaveAsync`

### 已有 ADR 覆盖的场景（不在本任务范围）

| ADR | 覆盖范围 |
|-----|---------|
| [0068](../adr/0068-unified-persistence-pipeline-actor.md) | MCP 工具 6 个状态型分类的持久化（`PersistencePipeline` Actor） |
| [0100](../adr/0100-console-actor-serialize-io.md) | Console I/O 串行化（`ConsoleActor`） |
| [0101](../adr/0101-file-watcher-unified-actor.md) | 文件监控全面 Actor 化（`FileWatcherActorBase`） |

## 目标

**采用 Actor 邮箱管道替代 22 处 `AsyncLock`+文件 I/O**，复用项目已有的 `ActorBase<TCommand, TOut>`（`lib\async_lock\ActorBase.cs`）。

### 架构

```
多生产者                         单消费者（Actor 线程）
┌─────────────────┐             ┌──────────────────────────┐
│ TeamManager     │──┐          │  TeamManagerActor        │
│ TranscriptWriter│──┤          │  (ActorBase<TeamCmd>)    │
│ ThinkingStore   │──┤ Send/Try │                          │──→ 磁盘
│ SessionTag      │──┤          │  HandleAsync:            │
│ DiagnosticLog   │──┤          │    1. 读文件（如需）      │
│ VcrService      │──┤          │    2. 序列化/反序列化     │
│ ...             │──┘          │    3. 写文件             │
└─────────────────┘             │    4. 更新内存状态        │
     Channel<Command>           └──────────────────────────┘
     （有界 + 背压或无界 FIFO）
```

### 核心设计

1. **Actor 串行化消除锁** — 文件 I/O 操作通过命令投递到 Actor 邮箱，Consumer 单线程串行处理，天然无竞态，无需 `AsyncLock`
2. **快照模式** — 生产者在主线程用读锁构造不可变快照（`ToList` + 序列化），把 JSON 字符串扔进队列。Actor 线程不访问共享可变状态（对齐 ADR [0068](../adr/0068-unified-persistence-pipeline-actor.md)）
3. **ActorBase 复用** — 继承现有 `ActorBase<TCommand>`，单消费者 Channel，命令 FIFO 串行处理，单条异常不退出 Consumer 循环。无需新建并发原语
4. **背压策略** — 写密集型服务用有界通道 + `BoundedChannelFullMode.DropOldest`（只保留最新快照）；读密集型用无界通道
5. **命令类型** — 每个服务定义一组 `record` 命令（如 `SaveTeamStateCmd`、`AddTeamMemberCmd`），Actor 按命令类型路由处理
6. **Ask 语义** — 需要返回值的操作用 `AskAsync`（`TaskCompletionSource` 回复），fire-and-forget 用 `TrySend`

### 分类重构策略

| 分类 | 服务 | 策略 |
|------|------|------|
| **写密集型** | TeamManager、TranscriptFileWriter、ThinkingStore、SessionTagService、AssistantDailyLog、DiagnosticLogRecorder、AgentTranscriptService | Actor 邮箱串行化写，有界通道 + DropOldest |
| **读-改-写事务型** | PhysicalFileSystem.EditFileAsync、InMemoryFileSystem.EditFileAsync、VcrService、FileHistoryService | Actor 邮箱串行化读-改-写事务，无界通道 FIFO |
| **加载/重载型** | SkillService、SkillDiscoveryService、AgentDefinitionProvider、DynamicKeywordConfigService、MagicDocsManager、AgentPermissionMode、ChatContextManager、PlanModeManager、McpAuthPersistenceService、AgentWorktreeService | Actor 邮箱串行化加载，与 ADR [0101](../adr/0101-file-watcher-unified-actor.md) 文件监控 Actor 协同 |
| **流 I/O 型** | SubprocessIoChannels | Actor 邮箱串行化 stdin 写，对齐 ADR [0100](../adr/0100-console-actor-serialize-io.md) ConsoleActor 模式 |
| **跨进程型** | MailboxActor、AgentDiscoveryService、DreamTaskPersistence、BuildQueueService | **已是 Actor+FileMailboxLock 模式，无需重构** |

### 接口示例（TeamManager）

```csharp
// 命令类型
public abstract record TeamCommand;
public sealed record AddMemberCmd(string TeamId, string AgentId, TaskCompletionSource<bool> Reply) : TeamCommand;
public sealed record RemoveMemberCmd(string TeamId, string AgentId, TaskCompletionSource<bool> Reply) : TeamCommand;
public sealed record SaveStateCmd() : TeamCommand;  // fire-and-forget
public sealed record GetMembersCmd(string TeamId, TaskCompletionSource<IReadOnlyList<string>> Reply) : TeamCommand;

// Actor
public sealed class TeamManagerActor : ActorBase<TeamCommand, Unit>
{
    private readonly Dictionary<string, TeamState> _teams = new();  // Actor 独占，无需锁
    private readonly IFileSystem _fs;

    protected override async ValueTask HandleAsync(TeamCommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case AddMemberCmd(var teamId, var agentId, var reply):
                // 内存操作（无锁，Actor 独占）
                if (!_teams.TryGetValue(teamId, out var team)) { reply.SetResult(false); return; }
                team.Members.Add(agentId);
                // 文件持久化（Actor 串行化，无锁）
                await SaveStateAsync(teamId, ct).ConfigureAwait(false);
                reply.SetResult(true);
                break;
            case SaveStateCmd:
                await SaveAllStatesAsync(ct).ConfigureAwait(false);
                break;
            // ...
        }
    }
}
```

## 重构优先级

| 优先级 | 目标 | 理由 |
|--------|------|------|
| **P0** | `TeamManager.cs`（8 处锁） | 重灾区，锁最密集，每次写操作都持久化 |
| **P1** | vault 持久化层（TranscriptFileWriter、AgentTranscriptService、ThinkingStore、AssistantDailyLog、SessionTagService） | 读-改-写事务模式，持锁时间长 |
| **P2** | `PhysicalFileSystem.cs`/`InMemoryFileSystem.cs` EditFileAsync | 文件系统抽象层，路径级锁，影响面广 |
| **P3** | 其余 12 处 | 按使用频率和持锁时长排序 |

## 实现计划

### 阶段0：任务文档（本阶段）

- [x] 写任务文档
- [ ] 记录任务到 `docs/refactor/asynclock-file-io-actor-migration.md`

### 阶段1：P0 — TeamManager Actor 化

- [x] 定义 `TeamCommand` 命令类型
- [x] `TeamManagerActor : ActorBase<TeamCommand, Unit>`
- [x] 迁移 8 处锁+SaveStateAsync 到 Actor 命令
- [x] 编译 + 单元测试 + 提交

### 阶段2：P1 — vault 持久化层 Actor 化

- [x] TranscriptFileWriter → Actor
- [x] AgentTranscriptService → Actor
- [x] ThinkingStore → Actor
- [x] AssistantDailyLog → Actor
- [x] SessionTagService → Actor
- [x] 编译 + 单元测试 + 提交

### 阶段3：P2 — FileSystem EditFileAsync Actor 化

- [x] PhysicalFileSystem.EditFileAsync → Actor
- [x] InMemoryFileSystem.EditFileAsync → Actor
- [x] 编译 + 单元测试 + 提交

### 阶段4：P3 — 其余 14 处 Actor 化

- [x] 按子阶段逐个迁移（14 个文件全部完成）
- [x] 编译 + 单元测试 + 提交

### 阶段5：验收

- [x] 全量编译 `dotnet build` 0 错误 0 警告
- [x] 全量测试通过

## 验收标准

- [ ] 编译通过（Debug + Release）
- [ ] AOT 兼容（无 dynamic、无反射 emit）
- [ ] 全量单元测试通过
- [ ] E2E 测试：TeamManager 并发 AddMember → 无数据丢失、无死锁
- [ ] E2E 测试：TranscriptFileWriter 并发 AppendEntry → JSONL 文件完整、无交错
- [ ] E2E 测试：PhysicalFileSystem.EditFileAsync 并发编辑 → 原子性保证
- [ ] 死锁测试：高并发场景无死锁
- [ ] 性能测试：Actor 模式吞吐量 ≥ AsyncLock 模式（串行化消除锁等待）
