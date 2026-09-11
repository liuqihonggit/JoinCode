# 文件监控全面 Actor 化统一

> ADR: [0101](../adr/0101-file-watcher-unified-actor.md) — 架构决策详情
> 日期: 2026-09-12
> 状态: 进行中

## 目标

统一全部文件监控代码为 Actor 模型,解决:
1. 死循环防护方案不统一(五种并存)
2. 防抖间隔散落硬编码(200ms/500ms/1s/5s)
3. Actor 化不一致(部分 Actor 化,部分事件驱动)
4. MainViewModel 违规(直接 new FileSystemWatcher)
5. 已 Actor 化实现无防抖(FileCronTaskStore/TeamMemorySyncService)

## 现状清单

| 文件 | 类名 | 用途 | 防抖 | Actor化 | 死循环防护 |
|------|------|------|------|---------|-----------|
| PhysicalFileSystemWatcher.cs | PhysicalFileSystemWatcher | 物理FS监视器 | 500ms | 否 | MarkInternalWrite |
| InMemoryFileSystemWatcher.cs | InMemoryFileSystemWatcher | 内存FS监视器 | 500ms | 否 | MarkInternalWrite |
| ConfigChangeNotifier.cs | ConfigChangeNotifier | 配置热重载 | 500ms | 否 | MarkInternalWrite |
| SettingsChangeApplier.cs | SettingsChangeApplier | 设置应用器 | 继承 | 否 | 继承 |
| ConfigurationService.cs | ConfigurationService | 配置服务 | 无 | 否 | MarkInternalWrite |
| SettingsJsonModelWriter.cs | SettingsJsonModelWriter | settings.json写回 | 无 | 否 | MarkInternalWrite |
| ModelFetchStartupService.cs | ModelFetchStartupService | 模型拉取 | 无 | 否 | MarkInternalWrite+显式刷新 |
| ConfigChangeStartMiddleware.cs | ConfigChangeStartMiddleware | 配置监控中间件 | 继承 | 否 | 继承 |
| DynamicKeywordConfigService.cs | DynamicKeywordConfigService | 关键词热加载 | 200ms | 否 | 只读 |
| FileWatcherIntegration.cs | FileWatcherIntegration | 代码索引增量 | 500ms | 否 | 只读索引 |
| FileWatcherIntegrationRegistry.cs | FileWatcherIntegrationRegistry | 多仓库注册表 | 继承 | 否 | 继承 |
| PluginHotReloader.cs | PluginHotReloader | 插件热重载 | 继承 | 否 | _reloadLock |
| SkillDiscoveryService.cs | SkillDiscoveryService | 技能发现 | 500ms | 否 | 只读 |
| FileCronTaskStore.cs | FileCronTaskStore | Cron任务存储 | 无 | 是 | Actor串行 |
| TeamMemorySyncService.cs | TeamMemorySyncService | 团队内存同步 | 无 | 是 | Actor串行 |
| FileWatcherMiddleware.cs | FileWatcherMiddleware | 同步管道中间件 | 无 | 否 | 无 |
| DiagnosticLogWatcher.cs | DiagnosticLogWatcher | 诊断日志监控 | 无 | 是 | 只读 |
| CronScheduler.cs | CronScheduler | Cron调度 | 无 | 是 | Actor串行 |
| MainViewModel.cs | MainViewModel | GUI配置刷新 | 1s手动 | 否 | _isRefreshingConfig(违规) |

## 执行计划

### 阶段0: ADR + 工作文档 ✅
- [x] ADR 0101 proposed
- [x] 工作文档

### 阶段1: 修复 MainViewModel 违规
- [ ] 改用 IFileSystem.Watch,移除 pragma
- [ ] 编译+测试+提交

### 阶段2: 统一防抖配置 FileWatcherOptions
- [ ] 新增 FileWatcherOptions
- [ ] 各消费方从 options 读取
- [ ] 编译+测试+提交

### 阶段3: FileWatcherActorBase 基类
- [ ] 新增 FileWatcherActorBase<TCommand>
- [ ] 封装 watcher/防抖/MarkInternalWrite/事件→命令
- [ ] 单元测试
- [ ] 编译+测试+提交

### 阶段4: Actor 化 ConfigChangeNotifier
- [ ] 编译+测试+提交

### 阶段5: Actor 化 SettingsChangeApplier
- [ ] 编译+测试+提交

### 阶段6: Actor 化 FileWatcherIntegration
- [ ] 编译+测试+提交

### 阶段7: Actor 化 PluginHotReloader
- [ ] 编译+测试+提交

### 阶段8: Actor 化 SkillDiscoveryService
- [ ] 编译+测试+提交

### 阶段9: 已 Actor 化实现补防抖
- [ ] FileCronTaskStore 改用 Debounced* 事件
- [ ] TeamMemorySyncService 改用 Debounced* 事件
- [ ] 编译+测试+提交

### 阶段10: ADR 0101 accepted + 全量验证
- [ ] dotnet build --no-incremental
- [ ] 全量测试
- [ ] ADR 状态改 accepted

## 进度日志

<!-- 🤖 Auto Decision: 2026-09-12 -->
<!-- 决策: 选择全面 Actor 化(方案A)而非分层 Actor 化(方案B) -->
<!-- 原因: 用户选择方案A;两种模式并存增加认知负担,只读场景也有并发风险 -->
<!-- 替代方案: 分层 Actor 化(方案B,放弃)、仅统一防护不 Actor 化(方案C,放弃) -->
<!-- 验证: ADR 0101 proposed 已写,待实现后验证 -->
