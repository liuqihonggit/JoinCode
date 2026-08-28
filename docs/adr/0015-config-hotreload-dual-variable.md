# 0015. 配置热重载双变量切换

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

`IConfigChangeNotifier` + `SettingsChangeApplier` 管道已监控 settings.json 变更，但只更新部分字段（EffortLevel、Hook 缓存、Permission 缓存），**不重建 WorkflowConfig**。直接修改活跃配置会有并发风险。

## 决策

**双变量切换模式**：

1. 每个可热重载的配置项维护两个变量：`_active`（当前生效）和 `_staging`（新值待切换）
2. 文件变更时：加载新值到 `_staging` → 验证合法性 → 原子交换 `_active = _staging`
3. 交换用 `Interlocked.Exchange` 或 `lock`，确保读取端无锁
4. WorkflowConfig 中的可热重载字段改为 `volatile` 或用 `FrozenDictionary` 不可变快照

新增热重载字段：ToolScoreSettings、BlacklistedTools、ToolPenalties、HyperedgeSettings（评分配置变更最频繁）。

**禁止**：直接修改 `_active` 而不经过 `_staging` 验证。

## 替代方案

1. **直接修改 _active**：放弃。并发读写不安全，可能读到半更新状态。
2. **全量重建 WorkflowConfig**：放弃。成本高，且大部分字段未变。
3. **用读写锁保护 _active**：放弃。读取端加锁影响热路径性能，双变量无锁读取更优。

## 后果

- 正面：读取端无锁，热路径性能好；原子交换保证一致性
- 负面：每个可热重载字段需维护两个变量，代码量增加
- 中性：`FrozenDictionary` 快照在 AOT 下友好（见 ADR 0011）
