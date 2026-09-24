# REF009 冗余拷贝消除任务

> 来源报告: `D:\Users\54076\Desktop\2冗余拷贝.txt`
> 改造方向: 无锁化 + 不可变类型 + 无锁编程 + 唯一数据源
> 检索效率准则: 并行检索安全(不可变快照) + O(1) > 二分 > 线性，缺排序条件要主动创造条件

## 已完成 ✅

| # | 文件 | commit | 改造内容 |
|---|------|--------|----------|
| 1 | ChatRoomState.cs | a80cc5026 | record + 不可变集合 + with表达式 |
| 2 | ChatRoomState.cs | e89650ff0 | MessagesByTime排序列表，GetMessages O(limit)，LastMessageAt O(1) |
| 3 | AnalyticsService.cs | bcc2d0be5 | _byType+_byDate冗余索引 |
| 4 | AnalyticsService.cs | f5e89184c | ConcurrentQueue/Dictionary → ImmutableList/Dictionary+Interlocked |
| 5 | AnalyticsService.cs | d603d7829 | 三索引按Timestamp排序，GetEventHistory O(limit) |
| 6 | HookEventBroadcaster.cs | c04a84978 | ConcurrentBag/Queue重建 → ImmutableArray+Interlocked |
| 7 | MessageDedupTracker.cs | b9630989c | 嵌套ConcurrentDictionary → ImmutableDictionary+ImmutableHashSet |
| 8 | SessionHookManager.cs | 9919d496a | GetHooks/GetAllHooks ToList → IReadOnlyList |
| 9 | UsageStore.cs | 5c90d6103 | 查询方法ToList → IReadOnlyList，CostTracker移除多余lock |
| 10 | TeamRegistry.cs | 7228110d1 | SnapshotRooms ToDictionary → IReadOnlyDictionary |
| 11 | TeamRegistry.cs | a80cc5026 | UpdateRoom方法 + null检查修复 |
| 12 | ConfigurationService.cs | a89d360fe | ConcurrentDictionary → ImmutableDictionary+CAS |
| 13 | DockerSandboxProvider.cs | a89d360fe | ConcurrentDictionary → ImmutableDictionary+CAS |
| 14 | lib/guard 3静态缓存 | 81cd565c3 | PsPermissions/HookConditionEvaluator/PsAstParser |
| 15 | kit/brain A类扩散 | fc3f3cabc | PlanSlugGenerator/ModelPricing/QueryStopHookManager/PlanModeManager/ChatContextManager |
| 16 | McpClientToolHandlers.cs | 22ccfa0ba | _connectionConfigs无锁化 |
| 17 | McpSessionRegistry等 | cdcc53f55 | McpSessionRegistry/McpServer/McpChannelNotificationHandler |
| 18 | lib/guard剩余13处 | 7441aeda8 | TokenRefreshScheduler/PermissionManager/HookConfigurationManager/Sandbox*/ToolPermissionFilter/MtpPerturbationNode |
| 19 | ActiveSandboxes | 130910fcb | 消除ToArray + sandboxId→provider冗余O(1)索引 |
| 20 | WorktreeIncludePatternMatcher | 02fa59622 | 静态缓存ImmutableDictionary+CAS |

### w3分支已完成(本表commit为w4的,w3独立完成)

| # | 文件 | w3 commit | 改造内容 |
|---|------|-----------|----------|
| w3-1 | AgentServiceImpl.cs | a27cc7170+856421803 | 3字典→单一ImmutableDictionary<string,AgentRuntimeState>聚合record+CAS |
| w3-2 | AgentLifecycleManager.cs | dc52e0cf5 | 双字典→单一ImmutableDictionary<string,AgentEntry>聚合 |
| w3-3 | UsageStore.cs | a8a5274ee+d3e74b4e7 | ImmutableList无锁+3冗余O(1)索引(_bySessionId/_byDate/_costByDate) |
| w3-4 | SessionHookStore | 563020111 | ImmutableDictionary<HookEvent,ImmutableList>无锁化 |
| w3-5 | TeamRegistry.cs | 54fb97b1f+b160d8320 | 2个ImmutableDictionary+2冗余O(1)索引(_bySessionId/_byTeamName) |
| w3-6 | RemoteClientRegistry等3个 | b71b3350c | 各自无锁化(保留3类关注点分离) |
| w3-7 | RemotePolicyService.cs | 1064d75b3 | 双字典各自无锁化(保留2字典因key空间不同) |
| w3-8 | llm/agents 22处 | 895504ced | 全部ConcurrentDictionary→ImmutableDictionary+CAS(含Task 11-14范围) |
| w3-9 | PathConstraintValidator | 7a19f8fb1 | IsDangerousRemovalPath stackalloc Span预归一化(含Task 8) |
| w3-10 | ToolSearchEngine | 6524b6abd | ComputeScore预缓存nameParts+ReadOnlySpan(含Task 10) |
| w3-11 | ReferenceResolver | f151c1893 | ExtractKeywords Span+Levenshtein Span(含Task 9) |
| w3-12 | ReadOnlyCommandDetector.GitFlags | d0f21a83b | 4处ToList→Any/foreach零分配 |
| w3-13 | P4-B类18处 | fdffc5cde | 消除冗余ToList/ToArray(llm/agents 6处+kit/brain 6处+kit/mcp 1处+kit/mcp_tool_dispatch 5处) |

## 待完成 — 高优先级（A+D类，同时消除锁+可变类型+重复数据源）

### Task 1: AgentServiceImpl — 3字典合并为单一状态对象 ✅w3已完成(a27cc7170+856421803)
- **文件**: `llm/agents/Services/Core/AgentServiceImpl.cs:37-40`
- **问题**: 3个ConcurrentDictionary(_completionSources/backgroundCts/progressTrackers)以agentId为key，同一agent状态分散
- **方案**: 改为 `volatile ImmutableDictionary<string, AgentRuntimeState>`，AgentRuntimeState聚合 Tcs + Cts + Tracker
- **收益**: 消除三字典分散查找 + 一致性维护

### Task 2: AgentLifecycleManager — 2字典合并为单一状态对象 ✅w3已完成(dc52e0cf5)
- **文件**: `llm/agents/Coordinator/Core/Lifecycle/AgentLifecycleManager.cs:12-13`
- **问题**: _subAgents + _results 两个ConcurrentDictionary以agentId为key
- **方案**: 改为 `volatile ImmutableDictionary<string, AgentEntry>`，AgentEntry聚合 Agent + Result?
- **关联C类**: line 87,90,93,245,260,290 的 ToList/ToDictionary 快照拷贝一并消除

### Task 3: RemoteClient三字典合并为单一状态对象 ✅分析后不改造(bee5f4166后)
- **文件**: `kit/mcp/remote/core/RemoteClientRegistry.cs:8` + `RemoteReconnectCtsRegistry.cs:8` + `RemoteToolSpecCache.cs:8`
- **问题**: 三个独立ConcurrentDictionary都以clientId为key，同一client状态(连接/重连CTS/工具规格)分散
- **方案**: 改为 `volatile ImmutableDictionary<string, RemoteClientState>`，RemoteClientState聚合 Client + ReconnectCts + ToolSpecs
- **结论**: 不改造。三个字典已用无锁ImmutableDictionary+CAS(A类已解决)，持有不同维度状态非重复数据(不符D类定义)，更新频率不同合并后高频更新触发更多CAS重试降低性能，职责清晰合并违反单一职责

## 待完成 — 中优先级B类（直接转换属性 → 消费者自己处理）

### Task 4: AgentWorktreeService 属性直接返回ToList ⬜w3未改(P4-B跳过,Actor串行无需锁)
- **文件**: `llm/agents/Services/Support/AgentWorktreeService.cs:218`
- **问题**: `=> _sessions.Values.ToList()` 属性直接返回转换
- **方案**: 返回 IEnumerable 或 IReadOnlyCollection，让消费者自己 ToList

### Task 5: AgentPermissionMode ToList快照 ⬜w3评估保留(合理拷贝)
- **文件**: `lib/guard/permission/utils/AgentPermissionMode.cs:168,241`
- **问题**: `var snapshot = _rules.Values.ToList()` / `return _rules.Values.OrderByDescending(...).ToList()`
- **方案**: 配合A类改造，返回IReadOnlyList引用 + 消费者自己Order
- **w3结论**: line168序列化前必须ToList(RelaxedJsonSerializer.Serialize需要List)、line241排序后必须materialize(返回IReadOnlyList,延迟求值会导致每次枚举重新排序) → 保留

## 待完成 — 中优先级C类（热路径防御性拷贝 → 不可变引用/Span）

### Task 6: TeamManager DTO构造拷贝 ⬜w3未改(C类,DTO序列化需List)
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.cs:95,106,107,152,186,282,499,500,539,560,602`
- **问题**: 大量DTO构造时ToList/ToDictionary拷贝Members/MemberDetails/AllowedPaths
- **方案**: TeamInfo/TeamDetail DTO持有IReadOnlyCollection引用(配合A类不可变快照)
- **w3结论**: DTO目标是可变List用于序列化,改为IReadOnlyCollection需序列化器适配,收益低

### Task 7: TeamManager.Persistence 持久化拷贝 ⬜w3未改(C类,序列化必须materialize)
- **文件**: `llm/agents/Coordinator/Team/core/TeamManager.Persistence.cs:52,85,86,87,88`
- **问题**: 持久化时4次ToDictionary/ToList拷贝整个roomsSnapshot
- **方案**: 序列化器直接接受IReadOnlyDictionary/IEnumerable枚举
- **w3结论**: 序列化前必须materialize为可变集合,改需序列化器适配

### Task 8: PathConstraintValidator 循环内Skip+ToList ✅完成(eb9bd438b)
- **文件**: `lib/guard/security/services/path_validation/PathConstraintValidator.cs:646,655,660,670,692,704,716`
- **问题**: 路径校验热路径循环内 `currentArgs = currentArgs.Skip(1).ToList()` 重复拷贝
- **方案**: 改为 ReadOnlySpan<string> 或索引遍历

### Task 9: ReferenceResolver 12次拷贝 ✅完成(bee5f4166)
- **文件**: `kit/brain/context/resolution/ReferenceResolver.cs:167,193,301,331,342,343,393,428,489,566,625,666`
- **问题**: 引用解析器热路径12次拷贝
- **方案**: ReadOnlySpan切片 + 延迟求值，仅最终返回时ToList

### Task 10: ToolSearchEngine 9次拷贝 ✅完成(de057cb0f)
- **文件**: `kit/mcp/skill/ToolSearchEngine.cs:60,68,77,84,94,109,125,133,150`
- **问题**: 工具搜索热路径9次ToList/ToArray
- **方案**: IEnumerable链式延迟求值，仅最终结果返回时ToList

## 待完成 — 低优先级（报告中其他命中，数量多但热度低）

### Task 11: AgentWorktreeManager static ConcurrentDictionary ✅w3已完成(895504ced)
- **文件**: `llm/agents/Coordinator/Core/Lifecycle/AgentWorktreeManager.cs:13-14,53`
- **问题**: static ConcurrentDictionary s_worktreeSessions + ConcurrentDictionary _lifecycleGuards

### Task 12: Coordinator核心状态 ✅w3已完成(895504ced)
- **文件**: `AgentCoordinator.cs:21,73` + `AgentStateMachine.cs:13,27` + `ExecutionStatisticsCalculator.cs:32` + `SecretaryRegistry.cs:7`

### Task 13: Messaging 8个文件 ✅w3已完成(895504ced)
- **文件**: `llm/agents/Coordinator/Core/Messaging/` 下 MailboxPoller/NamedPipeMailbox/MailboxHub/AgentChannelRegistry/AgentInputForwardQueue/AgentNameIndex/AgentOutputChannelManager

### Task 14: Swarm权限同步 ✅w3已完成(895504ced)
- **文件**: `SwarmPermissionBridge.cs:129,157` + `SwarmPermissionCallbackService.cs:76-77,92-93`

### Task 15: kit/mcp_tool_dispatch 其他 ⬜w3 A类已改(895504ced),C类未改(90%+合理拷贝)
- **文件**: ToolHealthMonitor.cs:47,335 + ToolCacheManager.cs:75,111 + ToolTemplateService.cs:64,67,150,221 + ToolHypergraphScorer.cs:40,41,47
- **w3状态**: A类(ConcurrentDictionary→ImmutableDictionary)已改;C类(热路径ToList)未改,分析为LINQ materialize/锁内快照等合理拷贝

### Task 16: kit/brain 其他热路径 ⬜w3 A类已改(fc3f3cabc),C类未改(90%+合理拷贝)
- **文件**: CacheBreakDetector.cs:87,157,179 + StreamTokenDetector.cs:76 + AwaySummaryService.cs:192,202,207,277 + StreamingToolExecutor.cs:101,118,157
- **w3状态**: A类已改;C类未改,分析为锁内materialize锁外使用(StreamingToolExecutor)/LINQ查询materialize等合理拷贝

### Task 17: lib/guard 其他 ✅完成(eb53b1894+51249d88f+4d738b8ce+a524cc3f5+7bed71b17+4ae5544e2)
- **文件**: AsyncHookRegistry.cs:240,336,362,388 + HookMatcher.cs:29,39,149,161 + PermissionManager.cs:183,205 + RemotePolicyService.cs:46,60,89,102
- **核心改造**: MapRegistry基类ConcurrentDictionary+脏标记缓存→ImmutableDictionary+CAS,影响20个继承类,AsyncHookRegistry消除2处GetAll().ToList()快照
- **次级索引基础设施**: MapRegistry新增CreateIndex(声明)+Reindex(可变属性更新)+自动同步,SecondaryIndex+ISecondaryIndex+SecondaryIndexBox
- **次级索引子类**: TaskRegistry GetByType/GetByAssignee O(1),ToolExecutionEntityRegistry GetByToolName/GetActive/GetCompleted O(1)+TransitionLifecycle,GoalRegistry GetByStatus/GetPursuing O(1)+TransitionStatus,PlanEntityRegistry GetByStatus O(1)+TransitionStatus,BuildEntityRegistry/ShellTaskEntityRegistry/PermissionRequestEntityRegistry GetByStatus/GetPending O(1)+TransitionStatus,NotificationEntityRegistry GetUnread O(1)+TransitionIsRead,McpServerEntityRegistry GetByStatus O(1)+TransitionStatus,WorktreeEntityRegistry GetActive/GetStale O(1)+TransitionStatus
- **调用点改造**: PermissionAwareToolExecutor 4处LifecycleState直接赋值→TransitionLifecycle调用
- **保留**: HookMatcher(构造防御性拷贝+最终物化) + PermissionManager(遍历时修改集合需快照) + RemotePolicyService(JSON解析+排序物化)

### Task 18: kit/mcp 其他 ⬜w3 A类已改(cdcc53f55+22ccfa0ba),C类未改(90%+合理拷贝)
- **文件**: RemoteClientManager.cs:312,346,380,391,454,520 + RemoteToolRegistrationMiddleware等
- **w3状态**: A类已改;C类未改,分析为RemoteClientManager返回结果materialize等合理拷贝

### Task 19: AgentBase 公共可变属性 ⬜w3未改
- **文件**: `llm/agents/Coordinator/Fork/AgentBase.cs:103`
- **问题**: `public ConcurrentQueue<string> ContractChangeNotifications { get; set; } = new()` 公共可变属性暴露

### Task 20: D类剩余 — RemotePolicyService双字典 ✅w3已完成(1064d75b3)
- **文件**: `lib/guard/policy/RemotePolicyService.cs:12-13`
- **问题**: _usageCounters + _windowStartTimes 两个ConcurrentDictionary以ruleId为key
- **w3方案**: 各自无锁化(保留2字典因key空间不同:复合key后缀不同)

## 注意事项

- **不要修改** `SandboxProviderBase.cs` 的 ActiveSandboxes `.ToArray()`（用户撤销）
- 编译命令: `dotnet build <csproj> --no-restore`
- 解决方案: `build/sln/JoinCode.slnx`
- 渐进式: 每次改一个功能，编译通过，git提交

---

## w3分支进度总结(2026-09-25更新)

| w4 Task | w3状态 | 说明 |
|---------|--------|------|
| Task 1 AgentServiceImpl | ✅已完成 | commit a27cc7170+856421803 |
| Task 2 AgentLifecycleManager | ✅已完成 | commit dc52e0cf5 |
| Task 3 RemoteClient | ✅不改造(一致) | 各自无锁化,保留关注点分离 |
| Task 4 AgentWorktreeService | ⬜未改 | P4-B跳过,Actor串行无需锁 |
| Task 5 AgentPermissionMode | ⬜保留 | 序列化/排序必须materialize |
| Task 6 TeamManager DTO | ⬜未改 | C类,DTO序列化需List |
| Task 7 TeamManager.Persistence | ⬜未改 | C类,序列化必须materialize |
| Task 8 PathConstraintValidator | ✅已完成 | commit 7a19f8fb1 |
| Task 9 ReferenceResolver | ✅已完成 | commit f151c1893 |
| Task 10 ToolSearchEngine | ✅已完成 | commit 6524b6abd |
| Task 11-14 Coordinator/Messaging/Swarm | ✅已完成 | commit 895504ced |
| Task 15 mcp_tool_dispatch C类 | ⬜A类已改,C类未改 | 90%+合理拷贝 |
| Task 16 kit/brain C类 | ⬜A类已改,C类未改 | 90%+合理拷贝 |
| Task 17 lib/guard其他 | ✅已完成 | commit eb53b1894+51249d88f+4d738b8ce+a524cc3f5+7bed71b17+4ae5544e2 |
| Task 18 kit/mcp C类 | ⬜A类已改,C类未改 | 90%+合理拷贝 |
| Task 19 AgentBase | ⬜未改 | 公共可变属性 |
| Task 20 RemotePolicyService | ✅已完成 | commit 1064d75b3 |

**w3总计**: 26个commit,Task 1/2/3/8-14/17/20已完成,Task 4/5/6/7/15/16/18/19未改(C类90%+合理拷贝或需序列化器适配)
