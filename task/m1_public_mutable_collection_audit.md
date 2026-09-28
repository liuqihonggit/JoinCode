# M1：公开属性暴露可变集合 → 只读接口

> 创建时间：2026-09-29
> 目标：服务类有状态字段暴露可变集合 → 返回只读接口，消除封装泄漏

## 筛选标准

**需要改**：服务类有状态字段（`get;` only 或 `get; set;` 且运行时可变）
**不需要改**：DTO/配置类（`get; init;` 构造后不修改）、测试 mock、JSON 序列化容器

## 待改造清单（按模块分组）

### 1. vault/memdir（4文件 7处）
- [ ] `ThinkingStore.cs:190` — `Entries { get; set; }` Dictionary
- [ ] `MemoryStore.cs:361` — `TypeCounts { get; init; }` Dictionary
- [ ] `MemoryStore.cs:363` — `TagCounts { get; init; }` Dictionary
- [ ] `MemoryScanner.cs:181` — `ByType { get; }` ConcurrentDictionary
- [ ] `MemoryScanner.cs:186` — `ByTag { get; }` ConcurrentDictionary
- [ ] `MemoryScanner.cs:191` — `BySource { get; }` ConcurrentDictionary
- [ ] `SessionTagService.cs:167` — `Entries { get; set; }` Dictionary

### 2. abstractions/abs_hands（1文件 4处）
- [ ] `ToolUseContext.cs:39` — `InvokedSkills { get; }` Dictionary
- [ ] `ToolUseContext.cs:46` — `PendingSedEdits { get; }` Dictionary
- [ ] `ToolUseContext.cs:53` — `RecentlyReadFiles { get; }` Dictionary
- [ ] `ToolUseContext.cs:12` — `AllowedTools { get; init; }` HashSet

### 3. abstractions/abs_core（2文件 5处）
- [ ] `StateDocuments.cs:12` — `Agents { get; set; }` Dictionary
- [ ] `StateDocuments.cs:14` — `Tasks { get; set; }` Dictionary
- [ ] `CrashSnapshot.cs:41` — `Tags { get; }` Dictionary
- [ ] `CrashSnapshot.cs:44` — `Attachments { get; }` Dictionary
- [ ] `CrashSnapshot.cs:190` — `Extra { get; }` Dictionary

### 4. scheduling/tasks（2文件 3处）
- [ ] `TeammateExecutionContext.cs:66` — `ActiveTeammates { get; set; }` ConcurrentDictionary
- [ ] `TeammateExecutionContext.cs:71` — `PendingMessages { get; set; }` ConcurrentDictionary
- [ ] `WorkflowTask.cs:617` — `StepStatuses { get; }` Dictionary

### 5. guard/hooks（1文件 1处）
- [ ] `HookConfigurationManager.cs:427` — `Hooks { get; set; }` Dictionary

### 6. mcp（1文件 1处）
- [ ] `McpOfficialRegistry.cs:147` — `Env { get; set; }` Dictionary

### 7. brain（1文件 1处）
- [ ] `FeatureFlagService.cs:11` — `Features { get; set; }` Dictionary

### 8. reasoning（1文件 1处）
- [ ] `RoleCone.cs:35` — `AllFragments { get; }` Dictionary

### 9. abstractions/abs_ai（1文件 2处）
- [ ] `ContentReplacementState.cs:11` — `SeenIds { get; }` ConcurrentDictionary
- [ ] `ContentReplacementState.cs:16` — `Replacements { get; }` ConcurrentDictionary

### 10. gen（1文件 1处）
- [ ] `McpToolDispatchGenerator.cs:845` — `OptionsTypeNames { get; }` Dictionary

## 总计：27处

## 改造策略

1. 逐模块改造，每改一个模块编译+测试+提交
2. `get; set;` 服务状态 → 改为 `get; }` + 私有字段 + 提供 `AddXxx`/`RemoveXxx` 方法
3. `get; }` 有状态 → 改为 `IReadOnlyDictionary`/`IReadOnlySet` 返回类型 + 私有可变字段
4. ConcurrentDictionary 保留内部可变（并发安全），但公开返回 `IReadOnlyDictionary`
5. 消费者如果直接写入公开属性，改为调用新增的 Add/Remove 方法

## 进度

| 模块 | 状态 | commit |
|------|------|--------|
| 1. vault/memdir | ⏳ | |
| 2. abs_hands | ⏳ | |
| 3. abs_core | ⏳ | |
| 4. scheduling/tasks | ⏳ | |
| 5. guard/hooks | ⏳ | |
| 6. mcp | ⏳ | |
| 7. brain | ⏳ | |
| 8. reasoning | ⏳ | |
| 9. abs_ai | ⏳ | |
| 10. gen | ⏳ | |
