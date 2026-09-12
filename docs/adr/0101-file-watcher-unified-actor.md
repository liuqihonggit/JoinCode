# 0101. 文件监控全面 Actor 化统一

- 状态：accepted
- 日期：2026-09-12
- 决策者：项目架构组
- 关联 ADR：[0074](0074-actor-supervisor-tree.md)、[0086](0086-core-tech-selection-lock-design.md)、[0093](0093-resource-management-exception-style.md)、[0015](0015-config-hotreload-dual-variable.md)

## 背景

项目中有 20+ 处文件监控代码,散落在 foundation/infrastructure/core/services/app 各层。虽然已有统一抽象 `IFileSystemWatcher` + `DebounceTracker` 防抖 + `MarkInternalWrite` 防回声,但存在以下架构问题:

### 问题1: 死循环防护方案不统一(五种并存)

| 方案 | 使用位置 | 缺陷 |
|------|---------|------|
| `MarkInternalWrite` + `ConsumeInternalWrite` | ConfigChangeNotifier/SettingsChangeApplier/ConfigurationService | 仅防护配置热重载链,窗口固定 5000ms |
| `_reloadLock` 串行化 | PluginHotReloader | 锁内执行 IO,持锁时间长 |
| `_isRefreshingConfig` 标志位 | MainViewModel | 非线程安全,竞态窗口 |
| Actor 串行化 | FileCronTaskStore/TeamMemorySyncService | 仅 Actor 化的实现用 |
| 只读不写 | FileWatcherIntegration/SkillDiscoveryService | 隐式依赖,无显式契约 |

### 问题2: 防抖间隔散落硬编码

200ms(DynamicKeywordConfigService) / 500ms(ConfigChangeNotifier/FileWatcherIntegration/SkillDiscoveryService) / 1s(MainViewModel 手动) / 5s(DiagnosticLogWatcher 轮询) — 无统一配置,调参需逐文件修改。

### 问题3: Actor 化不一致

已 Actor 化: `FileCronTaskStore`、`TeamMemorySyncService`、`DiagnosticLogWatcher`、`CronScheduler`(但前两者用原始事件无防抖)。
未 Actor 化: `ConfigChangeNotifier`、`SettingsChangeApplier`、`FileWatcherIntegration`、`PluginHotReloader`、`SkillDiscoveryService`、`DynamicKeywordConfigService`。

未 Actor 化的实现用事件回调 + 锁/标志位,并发安全靠人工保证,易出竞态。

### 问题4: 违规点

`MainViewModel.cs:689` 直接 `new System.IO.FileSystemWatcher()`,用 `#pragma warning disable JCC9005` 抑制分析器警告,绕过 `IFileSystemWatcher` 抽象,InMemory 测试无法模拟。

### 问题5: 已 Actor 化但无防抖

`FileCronTaskStore`、`TeamMemorySyncService` 用原始 Changed/Created/Deleted 事件,文件批量变更时 Actor 邮箱被刷爆,背压触发。

## 决策

### 决策1: 全面 Actor 化 — 所有文件监控统一为 Actor 模型

**选择**: 所有文件监控消费方继承 `ActorBase<TCommand, Unit>`,文件变更事件通过 `TrySend` 命令投递到 Actor 邮箱,Consumer 串行处理。

**统一命令体系**:
```csharp
// 通用文件变更命令(所有文件监控 Actor 复用)
public sealed record FileChangedCmd(string FilePath, FileChangeKind Kind, DateTimeOffset Timestamp) : IFileWatcherCommand;
public sealed record FileRenamedCmd(string OldPath, string NewPath, DateTimeOffset Timestamp) : IFileWatcherCommand;
public sealed record FileWatcherStartCmd(string Path, string? Filter) : IFileWatcherCommand;
public sealed record FileWatcherStopCmd() : IFileWatcherCommand;
public sealed record MarkInternalWriteCmd(string FilePath) : IFileWatcherCommand;
```

**理由**:
- Actor 串行化天然消除竞态:所有可变状态由 Consumer 线程独占,无需锁/标志位
- 统一死循环防护:`MarkInternalWriteCmd` 投递到邮箱,Consumer 串行消费,无窗口竞态
- 背压保护:有界邮箱 + 水位线告警,防止文件批量变更刷爆
- 异常容错:单条命令异常不终止 Consumer 循环(ActorBase 已实现)
- 可组合:可接入 SupervisedActor 监督树(ADR 0074),崩溃自动重启
- 已有完备基础设施:ActorBase/SupervisedActor/RouterActor,20+ 落地用例

**替代方案(放弃)**:
- 分层 Actor 化(有状态改 Actor,只读保持事件) — 两种模式并存增加认知负担,且只读场景也存在并发安全风险(如 FileWatcherIntegration 的 `_pendingUpdates` 字典用 `_pendingLock` 保护)
- 仅统一防护不 Actor 化 — 不解决并发和串行化问题,`_reloadLock`/`_isRefreshingConfig` 等人工锁继续散落

### 决策2: 统一防抖配置 — FileWatcherOptions 集中配置

**选择**:
```csharp
public sealed class FileWatcherOptions
{
    public TimeSpan ConfigDebounce { get; init; } = TimeSpan.FromMilliseconds(500);      // 配置热重载
    public TimeSpan IndexDebounce { get; init; } = TimeSpan.FromMilliseconds(500);       // 代码索引
    public TimeSpan PluginDebounce { get; init; } = TimeSpan.FromMilliseconds(500);      // 插件热重载
    public TimeSpan SkillDebounce { get; init; } = TimeSpan.FromMilliseconds(500);       // 技能发现
    public TimeSpan KeywordDebounce { get; init; } = TimeSpan.FromMilliseconds(200);     // 关键词热加载
    public TimeSpan CronDebounce { get; init; } = TimeSpan.FromMilliseconds(300);        // Cron 任务(新增)
    public TimeSpan SyncDebounce { get; init; } = TimeSpan.FromMilliseconds(300);        // 团队同步(新增)
    public TimeSpan InternalWriteWindow { get; init; } = TimeSpan.FromSeconds(5);        // 防回声窗口
    public int ActorMailboxCapacity { get; init; } = 1000;                               // Actor 邮箱容量
}
```

**理由**: 集中配置,运行时可通过 settings.json 热调整(对齐 ADR 0015 双变量切换),无需逐文件改代码。

### 决策3: 统一死循环防护 — Actor 邮箱 + MarkInternalWriteCmd

**选择**: 所有文件监控 Actor 共用同一防护链:
```
写前: TrySend(MarkInternalWriteCmd(filePath))  // 投递到邮箱,Consumer 串行记录
写入文件
文件变更事件 → TrySend(FileChangedCmd)          // 投递到邮箱
Consumer 处理 FileChangedCmd 时:
  if (ConsumeInternalWrite(filePath)) return;   // 在窗口内,丢弃(防回声)
  else 处理变更                                   // 真外部变更
```

**理由**:
- `MarkInternalWrite` 状态由 Actor Consumer 独占,无窗口竞态(原 `ConcurrentDictionary` 有并发读窗口)
- 串行化保证"写前标记 → 写入 → 事件 → 消费"顺序确定,无重排风险
- 统一所有消费方,消除五种防护方案并存

### 决策4: 已 Actor 化实现补防抖

**选择**: `FileCronTaskStore`、`TeamMemorySyncService` 从原始事件改为 `Debounced*` 事件,防抖间隔取 `FileWatcherOptions.CronDebounce`/`SyncDebounce`。

**理由**: Actor 邮箱有界,批量变更刷爆触发背压,防抖在前端合并事件,减少投递量。

### 决策5: 修复 MainViewModel 违规

**选择**: `MainViewModel.cs:689` 从 `new System.IO.FileSystemWatcher()` 改为 `IFileSystem.Watch(path, filter)`,移除 `#pragma warning disable JCC9005`。GUI 层注入 `IFileSystem`(生产用 `PhysicalFileSystem`,测试用 `InMemoryFileSystem`)。

**理由**: 统一抽象,测试可模拟文件变更事件,消除 JCC9005 违规。

### 决策6: FileWatcherActor 基类 — 封装通用逻辑

**选择**: 新增 `FileWatcherActorBase<TCommand> : ActorBase<TCommand, Unit>`,封装:
- `IFileSystemWatcher` 创建/启动/停止
- 防抖配置(从 FileWatcherOptions 读取)
- `MarkInternalWrite` 状态管理(ConcurrentDictionary 替换为 Actor 状态)
- 事件 → 命令转换(Changed → FileChangedCmd,Renamed → FileRenamedCmd)
- 子类只需 override `HandleFileChangedAsync` / `HandleFileRenamedAsync`

**理由**: 消除每个文件监控 Actor 重复编写 watcher 创建/事件订阅/防抖配置代码,符合"归纳性重构不放弃"(ADR 0017)。

## 替代方案

1. **分层 Actor 化(方案 B)**: 放弃 — 两种模式并存增加认知负担,只读场景也有并发风险
2. **仅统一防护不 Actor 化(方案 C)**: 放弃 — 不解决并发和串行化,人工锁继续散落
3. **引入 Reactive Extensions (System.Reactive)**: 放弃 — 引入外部依赖,需验证 AOT 兼容性,且 Actor 模型已覆盖防抖+串行化+背压
4. **用 Channel<T> 直接替代 Actor**: 放弃 — Actor 封装了异常容错/背压/监督,裸 Channel 需重新实现这些

## 架构图

```
                    ┌──────────────────────────┐
                    │ FileWatcherActorBase<TCmd>│ (新增,基类)
                    │ - IFileSystemWatcher      │
                    │ - DebounceTracker         │
                    │ - MarkInternalWrite 状态  │
                    │ - 事件→命令转换           │
                    └────────┬─────────────────┘
                             │ inherits
       ┌─────────────────────┼─────────────────────────────┐
       │                     │                             │
┌──────▼──────────┐  ┌──────▼──────────┐  ┌──────────────▼──────────┐
│ConfigChange     │  │IndexUpdate      │  │PluginHotReload          │
│NotifierActor    │  │Actor            │  │Actor                    │
│(配置热重载)     │  │(代码索引增量)   │  │(插件热重载)             │
└─────────────────┘  └─────────────────┘  └─────────────────────────┘
       │                     │                             │
┌──────▼──────────┐  ┌──────▼──────────┐  ┌──────────────▼──────────┐
│SkillDiscovery   │  │DynamicKeyword   │  │FileCronTaskStore        │
│Actor            │  │ConfigActor      │  │(已 Actor,补防抖)        │
│(技能发现)       │  │(关键词热加载)   │  └─────────────────────────┘
└─────────────────┘  └─────────────────┘
                                                ┌──────────────────┐
                                                │TeamMemorySync    │
                                                │Service           │
                                                │(已 Actor,补防抖) │
                                                └──────────────────┘
```

## 好处

| 方面 | 说明 |
|------|------|
| 并发安全 | Actor 串行化消除所有竞态,无需锁/标志位 |
| 死循环统一 | MarkInternalWriteCmd 统一防护,消除五种方案并存 |
| 防抖统一 | FileWatcherOptions 集中配置,运行时可热调 |
| 背压保护 | 有界邮箱 + 水位线告警,防批量变更刷爆 |
| 异常容错 | 单条命令异常不终止 Consumer,Actor 自动恢复 |
| 可组合 | 可接入 SupervisedActor 监督树,崩溃重启 |
| 测试统一 | InMemoryFileSystemWatcher + Actor 命令,全链路可测 |
| 违规消除 | MainViewModel 改用 IFileSystem.Watch,JCC9005 消除 |

## 坏处

| 方面 | 应对 |
|------|------|
| 复杂度增加 | FileWatcherActorBase 封装通用逻辑,子类只 override 两个方法 |
| 异步化 | 事件→命令→Consumer 异步,延迟增加约 1-2ms(可接受) |
| 命令类型 | 通用 FileChangedCmd/FileRenamedCmd 复用,不每 Actor 定义 |
| 调试 | Actor 带 Id,结构化日志含 ActorId+FilePath+Kind |
| 迁移工作量 | 渐进式,逐个改造,每步编译+测试+提交 |

## 实现计划

### 阶段0: ADR + 工作文档(本阶段)
- [x] 写 ADR 0101 proposed
- [ ] 记录任务到 docs/refactoring/file-watcher-unification.md

### 阶段1: 修复 MainViewModel 违规(最小改动,独立)
- [ ] 改用 IFileSystem.Watch,移除 pragma
- [ ] 编译+测试+提交

### 阶段2: 统一防抖配置
- [ ] 新增 FileWatcherOptions
- [ ] 各消费方从 options 读取防抖间隔
- [ ] 编译+测试+提交

### 阶段3: FileWatcherActorBase 基类
- [ ] 新增 FileWatcherActorBase<TCommand> in foundation/AsyncLock 或 core
- [ ] 封装 watcher 创建/防抖/MarkInternalWrite/事件→命令
- [ ] 单元测试
- [ ] 编译+测试+提交

### 阶段4: Actor 化 ConfigChangeNotifier(配置热重载核心)
- [ ] 改为 ConfigChangeNotifierActor : FileWatcherActorBase
- [ ] 编译+测试+提交

### 阶段5: Actor 化 SettingsChangeApplier
- [ ] 改为 SettingsChangeApplierActor
- [ ] 编译+测试+提交

### 阶段6: Actor 化 FileWatcherIntegration(代码索引)
- [ ] 改为 IndexUpdateActor
- [ ] 编译+测试+提交

### 阶段7: Actor 化 PluginHotReloader
- [ ] 改为 PluginHotReloadActor
- [ ] 编译+测试+提交

### 阶段8: Actor 化 SkillDiscoveryService
- [ ] 改为 SkillDiscoveryActor
- [ ] 编译+测试+提交

### 阶段9: 已 Actor 化实现补防抖
- [ ] FileCronTaskStore 改用 Debounced* 事件
- [ ] TeamMemorySyncService 改用 Debounced* 事件
- [ ] 编译+测试+提交

### 阶段10: ADR 0101 状态改 accepted + 全量编译验证
- [ ] dotnet build --no-incremental 0 错误 0 警告
- [ ] 全量测试通过
- [ ] ADR 状态改 accepted

## 验证

- [ ] 编译通过(Debug + Release)
- [ ] AOT 兼容(无 dynamic、无反射 emit)
- [ ] 全量单元测试通过
- [ ] E2E 测试:配置热重载(改 settings.json → 内存配置刷新)
- [ ] E2E 测试:代码索引增量(改 .cs 文件 → 索引更新)
- [ ] E2E 测试:插件热重载(改插件文件 → 插件重载)
- [ ] 死循环测试:写 settings.json → 不触发再次写
- [ ] 背压测试:批量变更 1000 文件 → Actor 邮箱不溢出
