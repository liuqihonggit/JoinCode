# 多数据源类违规调查报告

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3`
**命中类总数**: 46 个(含 ≥2 个数据源字段) | **真违规**: 11 | **部分违规**: 2 | **例外**: 33

---

## 一、真违规类(11 个,存在冗余/重复/需同步关系)

### 1. TeamRegistry — `llm\agents\Coordinator\Team\core\TeamRegistry.cs:9-13`
- `_rooms`: ImmutableDictionary<string, ChatRoomState> — 主数据源
- `_agentToTeam`: ImmutableDictionary<string, string> — agent→team 映射
- `_bySessionId`: ImmutableDictionary<string, string> — sessionId→teamId **冗余索引**
- `_byTeamName`: ImmutableDictionary<string, string> — teamName→teamId **冗余索引**
- 分析: 注释自认"唯一数据源 _rooms + 2 个冗余查询索引",AddRoom/TryRemoveRoom 需手工同步 4 个字典
- 可合并: 是(改为按需计算或 MultiIndexRegistry 委托 _rooms)

### 2. SandboxManager — `lib\guard\security\sandbox\core\SandboxManager.cs:9-15`
- `_providers`: ImmutableDictionary<SandboxType, ISandboxProvider> — 主数据源
- `_activeExecutions`: ImmutableDictionary<string, SandboxActiveExecution> — 活跃执行
- `_sandboxToProvider`: ImmutableDictionary<string, ISandboxProvider> — **冗余索引**
- 分析: ResolveProviderBySandboxId 先查冗余索引,miss 时 fallback 线性遍历并补建
- 可合并: 是(按需遍历 _providers)

### 3. UsageStore — `kit\brain\cost_tracking\services\core\UsageStore.cs:9-13`
- `_usageRecords`: ImmutableList<TokenUsageRecord> — 主列表
- `_bySessionId`: ImmutableDictionary<string, ImmutableList<TokenUsageRecord>> — **冗余索引**
- `_byDate`: ImmutableDictionary<DateTime, ImmutableList<TokenUsageRecord>> — **冗余索引**
- `_costByDate`: ImmutableDictionary<DateTime, decimal> — **冗余汇总**
- 分析: Add 需 4 次 CAS 原子更新,Reset 需清空 4 个,全部可从 _usageRecords 派生
- 可合并: 是(单一 _usageRecords + 按需查询)

### 4. AnalyticsService — `kit\brain\cost_tracking\services\core\AnalyticsService.cs:7-11`
- `_events`: ImmutableList<AnalyticsEvent> — 主列表
- `_byType`: ImmutableDictionary<AnalyticsEventType, ImmutableList<AnalyticsEvent>> — **冗余索引**
- `_byDate`: ImmutableDictionary<DateTime, ImmutableList<AnalyticsEvent>> — **冗余索引**
- `_agentSpans`: ImmutableDictionary<string, ITelemetrySpan> — agent→span(语义不同)
- 分析: _byType/_byDate 可从 _events 派生,AddEventToIndices 需同步 3 个
- 可合并: _events/_byType/_byDate 可合并;_agentSpans 不可

### 5. ChatRoomState — `lib\abstractions\abs_core\models\models_agent\agent\chat_room\ChatRoomState.cs:11-33`
- `Members`: ImmutableHashSet<string> — 成员 ID
- `Messages`: ImmutableDictionary<string, TeamMessage> — MessageId→Message
- `MessagesByTime`: ImmutableList<TeamMessage> — 按时间排序 **双索引**
- `AllowedPaths` / `MemberDetails` — 语义不同
- 分析: Messages 和 MessagesByTime 是同一份消息的双索引,TryAddMessage/ReplaceMessage/CleanupOldMessages 需同步
- 可合并: Messages/MessagesByTime 可合并为 ImmutableSortedDictionary

### 6. ImmutablePrefix — `lib\abstractions\abs_ai\llm\chat\cache\ImmutablePrefix.cs:3-8`
- `_toolSpecs`: ImmutableDictionary<string, ToolSpec> — 工具规格字典
- `_toolSpecsOrder`: ImmutableList<string> — 工具名顺序 **双索引**
- `_fewShots`: ApiMessage[] — FewShot(语义不同)
- 分析: AddTool/RemoveTool 需同步两个并失效指纹缓存
- 可合并: 是(单一字典 + 排序键,或专用有序字典)

### 7. SessionScope — `lib\abstractions\abs_core\core_session_router\SessionScope.cs:8-11`
- `_entities`: ConcurrentDictionary<ObjectId, Entity> — 主存储
- `_typeIndex`: ConcurrentDictionary<ObjectType, HashSet<ObjectId>> — **冗余类型索引**
- 分析: _typeIndex 可从 _entities 派生,Register/Unregister 需同步两个
- 可合并: 是(单一 _entities + 按需分组)

### 8. MapRegistry — `lib\abstractions\abs_core\core_utils\core\registry\MapRegistry.cs:8-12`
- `_items`: ImmutableDictionary<TKey, TValue> — 主存储
- `_canonicalKeys`: ImmutableHashSet<TKey> — **冗余子集跟踪**
- `_indices`: ImmutableList<ISecondaryIndex> — 次级索引(扩展点)
- 分析: _canonicalKeys 是 _items 的子集,泛型基类设计
- 可合并: _canonicalKeys 可改为 _items 视图;_indices 不可(扩展点)

### 9. TransportFallbackMetrics — `lib\transport.impl\shared\TransportFallbackMetrics.cs:6-12`
- `_connectionAttempts`: int[] — 尝试次数
- `_connectionSuccesses`: int[] — 成功次数
- `_connectionFailures`: int[] — 失败次数
- 分析: 三个平行数组,同索引同生命周期,是分散的同一逻辑数据
- 可合并: 是(合并为 TransportStat[] 结构数组)

### 10. AutoModeClassifier — `lib\guard\security\services\classifiers\AutoModeClassifier.cs:75-96`
- `DangerousCommandPatterns`: string[] — 危险命令模式源
- `DangerousCommandRegexes`: Regex[] — **源+派生缓存**
- `ReadOperationMask`/`WriteOperationMask`: int (BitMask,独立常量)
- 分析: DangerousCommandRegexes = DangerousCommandPatterns.Select(p => new Regex(...))
- 可合并: 是(静态初始化时合并)

### 11. SecurityPatterns — `lib\abstractions\abs_guard\security\scanning\SecurityPatterns.cs:20`
- `SecretRegexPatterns`: string[] — 正则源
- `_compiledSecretRegexes`: Regex[] — **源+派生缓存**
- `SensitiveFilePatternsByCategory`/`SecretPrefixes`/`RuleIdLabels`/`SensitiveFileNames`/`SensitiveFileExtensions` — 语义不同但职责过载
- 分析: SecretRegexPatterns/_compiledSecretRegexes 冗余;7 个数据源塞同一 partial class
- 可合并: 正则源+派生可合并;其余应按职责拆分

---

## 二、部分违规类(2 个)

### 28. PathConstraintValidator — `lib\guard\security\services\path_validation\PathConstraintValidator.cs:8-130`
- `CommandOperationTypeMap`: FrozenDictionary<PathCommand, FileOperationType>
- `ActionVerbs`: FrozenDictionary<PathCommand, string>
- `DangerousRemovalPaths`: FrozenSet<string> + `DangerousRemovalPathsNormalized`: string[] — **源+派生**
- 分析: CommandOperationTypeMap 和 ActionVerbs 键相同(PathCommand)可合并;DangerousRemovalPaths 和 Normalized 是源+派生
- 可合并: 2 组可合并,其余语义不同

### 29. ReadOnlyCommandDetector — `lib\guard\security\services\readonly_detector\ReadOnlyCommandDetector.cs:8-108`
- 7 个 FrozenSet/FrozenDictionary(SimpleReadOnlyCommands/SafeGitSubcommands/DangerousGitSubcommands/SafeXargsTargets/CommandAllowlist/GitInternalPatterns/NonCreatingWriteCommands)
- 分析: 各数据源语义不同(不同检测路径),但数量多,职责过载
- 可合并: 否(语义不同),但应考虑拆分

---

## 三、例外类(33 个,语义不同,无法合并)

| # | 类名 | 路径(相对 w1) | 例外理由 |
|---|------|--------------|----------|
| 12 | AgentSummaryService | llm\agents\Services\Support | 键不同(executionId vs agentName)、值类型不同 |
| 13 | TeammateMailboxService | llm\agents\Coordinator\Team\core | 值类型不同(Actor vs 读游标) |
| 14 | SwarmPermissionCallbackService | llm\agents\Coordinator\Swarm | 值类型不同(回调 vs 请求) |
| 15 | AgentWorktreeManager | llm\agents\Coordinator\Core\Lifecycle | 值类型不同(会话 vs 生命周期守卫) |
| 16 | RemotePolicyService | lib\guard\policy | 值类型不同(计数 vs 窗口时间) |
| 17 | HookConfigurationManager | lib\guard\hooks\configuration | 语义不同(提供者 vs 缓存) |
| 18 | TelemetryService | lib\infrastructure\telemetry | 语义不同(指标 vs span) |
| 19 | PluginManager | lib\plugins.infrastructure\services | 已做过合并优化(3→1) |
| 20 | InMemoryFileSystem(test) | test\unit\testing.common | 语义不同(文件/目录/锁) |
| 21 | InMemoryFileSystem(lib) | lib\infrastructure\io\file_system | 语义不同(文件/目录) |
| 22 | TeamMemorySyncService | lib\vault\memdir\sync\core | 语义不同(本地 vs 远程) |
| 23 | ServiceMessageBus | lib\clock\hosting | 语义不同(订阅者 vs 历史) |
| 24 | AsyncLockedDictionary | lib\async_lock\lock | 语义不同(数据 vs per-key锁) |
| 25 | DebounceTracker | lib\infrastructure\utils\io | 语义不同(定时器 vs 时间戳) |
| 26 | ModelConfigLoader | lib\abstractions\abs_core\configuration\llm | 预构建索引(非冗余,查询入口不同) |
| 27 | PermissionChecker | lib\guard\permission\permission2 | 值类型不同(string vs CommandDangerLevel) |
| 30 | AssistantDailyLogExtensions | lib\vault\memdir\memdir2\operations | 正反映射对([EnumValue]标准模式) |
| 31 | AgentStateMachine | llm\agents\Coordinator\Core\services | 语义不同(转换表 vs 状态上下文) |
| 32 | McpTransportFallbackChain | kit\mcp\transports\shared | 类型职责不同(传输/健康检查/熔断器) |
| 33 | DialogueCompressor | kit\brain\context\compression\strategies | 5个正则语义不同 |
| 34 | PlanSlugGenerator | kit\brain\planning\planning2 | 三个词表语义不同(形/动/名) |
| 35 | ConfigChangeNotifier | lib\guard\configuration\configuration2 | 监控目标不同 |
| 36 | BuddyService | app\cli\services\user_experience | 装饰性数据语义不同 |
| 37 | StructuredTaskMarkdownReader | lib\scheduling\storage | 解析字段不同 |
| 38 | FileFilter | non_deliverables_tools\jcc_audit_ast_cli\core | 过滤层级不同 |
| 39 | ExternalRulesLoader | lib\guard\configuration\configuration2\rules | 项目级 vs 用户级 |
| 40 | ProjectRulesLoader | lib\guard\configuration\configuration2\rules | 规则文件/目录不同 |
| 41 | InvariantRegistry | lib\plugins.contracts\registry | 语义不同(注册表 vs 白名单 vs 黑名单) |
| 42 | NoOpPlanDetector | lib\abstractions\abs_core\models\models_agent\plan | 检测目的不同 |
| 43 | CommandInterceptionDispatcher | lib\guard\hooks\execution\interception\core | 守卫链 vs 拦截器链 |
| 45 | CodeContentCompressor | kit\brain\context\compression\strategies | 代码结构识别不同 |
| 46 | AhoCorasick | lib\infrastructure\utils\text | DFA 自动机结构组件不可分离 |

---

## 四、关键观察

1. **冗余索引模式普遍**: TeamRegistry/SandboxManager/UsageStore/AnalyticsService/ChatRoomState/SessionScope 都是"主数据源+冗余查询索引",大多注释自认。~~建议引入 `MultiIndexRegistry` 泛型基类统一管理。~~ **经实际改造验证: 这些冗余索引是有意的 O(1) 查询优化,同步逻辑已封装在 AddRoom/Register/TryAddMessage 等方法中,合并会导致 O(1)→O(n) 性能退化。标记为例外,不合并。**
2. **源+派生缓存模式**: AutoModeClassifier/SecurityPatterns/PathConstraintValidator 存在"源数组+预编译正则"冗余,建议静态初始化时合并或按需编译。**✅ 已完成(P2-⑨)**
3. **平行数组模式**: TransportFallbackMetrics 的 3 个 int[] 应合并为结构数组。**✅ 已完成(P2-⑩)**
4. **双索引模式**: ChatRoomState/ImmutablePrefix 的"字典+排序列表",~~建议引入专用有序字典结构。~~ **经实际改造验证: 合并会导致 O(1) 查找/排序退化为 O(n)/O(n log n)。标记为例外,不合并。**
5. **已优化案例**: PluginManager 已将 3 个 ConcurrentDictionary 合并为 1 个 _plugins,成功实践。

---

## 五、P2 改造结论

| 项 | 类 | 结论 | 原因 |
|----|-----|------|------|
| P2-⑩ | TransportFallbackMetrics | ✅ 已合并 | 平行数组合并为结构数组,访问仍 O(1) |
| P2-⑨ | AutoModeClassifier/SecurityPatterns/PathConstraintValidator | ✅ 已合并 | 消除中间源字段,不涉及查询性能 |
| P2-⑪ | ChatRoomState/ImmutablePrefix | ❌ 例外 | O(1) 索引降级为 O(n)/O(n log n) 不可接受 |
| P2-⑧ | TeamRegistry/SandboxManager/UsageStore/AnalyticsService/SessionScope | ❌ 例外 | O(1) 冗余索引降级为 O(n) 遍历不可接受,同步逻辑已封装 |
