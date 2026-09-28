# 不可变字典 CAS 原子化审核与修复任务

> 审核时间：2026-09-28
> 范围：全项目 `ImmutableHamT` / `FrozenDictionary` / `ImmutableList` 等不可变容器的读-改-写是否原子化（CAS 循环比较）
> 用户偏好：不可变 record + CAS 整体替换，而非 Interlocked 字段级原子更新（feedback_immutable_cas_preference.md）

## 修复清单（按风险等级排序）

### 🔴 H1 — `SessionHookStore.Clear()` 违反 CAS 协议（高风险）

- **文件**：`lib/guard/hooks/session/SessionHookManager.cs:100-102`
- **字段**：`_hooks`（L61，非 volatile）
- **问题**：`AddHook`/`RemoveHook` 用 `ImmutableInterlocked.Update` CAS，但 `Clear` 直接赋值 `_hooks = ...Empty`，并发会丢失更新
- **读取端**：`GetHooks`(L88) / `GetAllHooks`(L95) 无 `Volatile.Read`
- **修复**：
  1. `Clear` 改 `Volatile.Write(ref _hooks, ImmutableHamT<...>.Empty)`（整体替换，无需 CAS 循环，因为目标是固定值 Empty）
  2. `GetHooks` / `GetAllHooks` 读端改 `Volatile.Read(ref _hooks)`
- **状态**：✅ 已合并（PR #325）

### 🟡 M1 — `HookConfigurationManager` 伪 CAS（误导，锁内安全）

- **文件**：`lib/guard/hooks/configuration/HookConfigurationManager.cs:134`
- **字段**：`_cache`（L62，volatile）
- **问题**：`Interlocked.Exchange(ref _cache, _cache.SetItem(...))` 是先读后无条件覆盖，非 CAS。在 `_lock` 锁内实际安全，但写法误导
- **修复**：锁内直接 `_cache = _cache.SetItem(...)` + 注释说明锁保护；或改 `ImmutableInterlocked.Update`
- **状态**：✅ 已合并（PR #325）

### 🟡 M2 — `McpClientToolHandlers` 伪 CAS（3 处，误导，锁内安全）

- **文件**：`kit/mcp/core/handlers/McpClientToolHandlers.cs:136, 204, 241`
- **字段**：`_connectionConfigs`（Persistence.cs:12，非 volatile）
- **问题**：同 M1，`Interlocked.Exchange(ref _field, _field.SetItem(...))` 伪 CAS，在 `_clientLock` 锁内
- **修复**：锁内直接赋值 + 注释；或改 `ImmutableInterlocked.Update`
- **状态**：✅ 已合并（PR #325）

### 🟡 M3 — `AgentRoleProfileRegistry` 读端无锁无 Volatile.Read

- **文件**：`llm/agents/services/support/AgentRoleProfileRegistry.cs:16, 76`
- **字段**：`_profileMap`（FrozenDictionary，非 volatile）
- **问题**：写端在 `_loadLock` 内整体重建（L29/49/60/118/173），读端 `GetProfile`(L76) 无锁直接 `TryGetValue`
- **修复**：字段加 `volatile`（FrozenDictionary 是引用类型，volatile 保证可见性）；读端无需改
- **状态**：✅ 已合并（PR #325）

### 🟡 M4 — `AgentServiceImpl` 读端无 Volatile.Read

- **文件**：`llm/agents/services/core/AgentServiceImpl.cs:46`
- **字段**：`_runtimeStates`（写端 CAS L97 + Exchange L822）
- **问题**：读端 L94/105/818 直接读
- **修复**：读端改 `Volatile.Read(ref _runtimeStates)`；或字段加 `volatile`
- **状态**：✅ 已合并（PR #325）

### 🟡 M5 — `RemoteToolSpecCache` 读端无 Volatile.Read

- **文件**：`kit/mcp/remote/core/RemoteToolSpecCache.cs:8`
- **字段**：`_specs`（写端 CAS L20/30 + Exchange L36）
- **问题**：读端 L12 直接读
- **修复**：读端改 `Volatile.Read(ref _specs)`；或字段加 `volatile`
- **状态**：✅ 已合并（PR #325）

### 🟡 M6 — `SystemActuatorRegistry` 静态字段初始化竞态

- **文件**：`kit/hands/system_actuator/abstractions/SystemActuatorRegistry.cs:9-10`
- **字段**：`_factories`（非 volatile）+ `_factoriesLoaded` 标志（非 volatile）
- **问题**：静态字段初始化竞态，可能看到部分初始化状态
- **修复**：`_factoriesLoaded` 改 `volatile`；`_factories` 写端用 `Volatile.Write`，读端用 `Volatile.Read`
- **状态**：✅ 已合并（PR #325）

### 🟡 M7 — `MonitorMcpTask` 静态字段初始化竞态

- **文件**：`lib/scheduling/tasks/core/MonitorMcpTask.cs:51-52`
- **字段**：`_eventFilterSet`（非 volatile）+ `_eventFilterSetInitialized` 标志（非 volatile）
- **问题**：同 M6
- **修复**：标志改 `volatile`；字段写端 `Volatile.Write`，读端 `Volatile.Read`
- **状态**：⬜ 待修复

### 🟡 M8 — `ProviderDefinitionRegistry` 直接赋值无同步

- **文件**：`lib/guard/configuration/configuration2/core/providers/shared/ProviderDefinitionRegistry.cs:10, 23`
- **字段**：`_definitions`（FrozenDictionary，非 volatile）
- **问题**：写端 L23 直接赋值，读端 L30/40/48 直接读
- **修复**：需先确认是否单线程上下文；若多线程则字段加 `volatile` 或读写用 `Volatile`
- **状态**：✅ 已合并（PR #325）

### ⚠️ W1 — `ConcurrentDictionary<K, ImmutableList<V>>` 值同步

- **文件**：`lib/clock/hosting/AppEventBus.cs:9` / `ServiceMessageBus.cs:44-45`
- **问题**：`ConcurrentDictionary` 只保证键线程安全，`ImmutableList` 值的读-改-写需同步
- **修复**：需确认值的更新是否用了 `ImmutableInterlocked.Update` 或 `ConcurrentDictionary.AddOrUpdate`
- **状态**：✅ 已确认安全（AddOrUpdate + 纯函数更新工厂，线程安全）

## 修复策略

- 渐进式：逐个修复 → 编译 → 单元测试 →（用户许可后）git commit
- 优先级：H1 → M1-M2（误导）→ M3-M5（读端 Volatile.Read）→ M6-M7（静态初始化）→ M8/W1（待确认）
- 禁止未经许可 commit

## 进度

> 全部修复已合并到 main（PR #325，commit 8552518f）
> 额外修复：HookEventBroadcaster ImmutableArray→ImmutableList（NativeAOT 兼容，commit 3d1a9a14）

- [x] H1 ✅ 已合并
- [x] M1 ✅ 已合并
- [x] M2 ✅ 已合并
- [x] M3 ✅ 已合并
- [x] M4 ✅ 已合并
- [x] M5 ✅ 已合并
- [x] M6 ✅ 已合并
- [x] M7 ✅ 已合并
- [x] M8 ✅ 已合并
- [x] W1 ✅ 已确认安全（ConcurrentDictionary.AddOrUpdate + ImmutableList 纯函数更新工厂，CAS 重试+无副作用，线程安全）

---

<!-- 🤖 Auto Decision: 2026-09-28 -->
<!-- 决策: Clear() 用 Volatile.Write 而非 ImmutableInterlocked.Update -->
<!-- 原因: Clear 目标是固定值 Empty，无需 CAS 循环比较；Volatile.Write 保证可见性即可 -->
<!-- 替代方案: ImmutableInterlocked.Update(ref _hooks, _ => Empty) 也可，但对固定值目标 CAS 循环无意义 -->
