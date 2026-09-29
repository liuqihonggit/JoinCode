# TASK030 — 基建确定性测试补充任务清单

> **目标**:给所有基建模块补充确定性测试(不依赖时序),通过拆分长方法的纯计算中间部分为 `internal` 方法,使其可独立单测。
>
> **检测方式**:并行 8 个 explore 子代理检测 7 个基建模块 + ActorBase 专项。
>
> **创建时间**:2026-09-29
>
> **状态**:待执行

---

## 一、检测汇总(7 模块 + ActorBase 专项)

| 模块 | 路径 | P0 纯逻辑零测试 | 需拆 internal 长方法 | 测试项目 |
|------|------|----------------|---------------------|---------|
| structura | `lib/structura/` | `Dag<T>`/`ConcurrentDag<T>` 全无 | SwissTable SIMD 18份重复、`RemoveNodeFromState`、`GetAffectedSubgraph` | `lib/structura.tests/` |
| async_lock | `lib/async_lock/` | `LockRegistry`图算法、`BinaryProtocol` | ActorBase 纯计算片段、`DecideRole` | `lib/async_lock.tests/` |
| guard | `lib/guard/` | `BashAstSecurityWalker`(8文件~1000行)、`CommandDangerClassifier`内部 | 守卫纯逻辑、`IsGitReadOnlySubcommand` | `lib/guard.{config,hooks,security}.tests/` |
| transport.impl | `lib/transport.impl/` | `SseStreamParser`/`HttpRequestSerializer` | `DrainAsync`/`HandleConnectionError`/`ExecuteWithOAuthRetryAsync` | InternalsVisibleTo 落空 |
| abstractions | `lib/abstractions/` | `JsonRepairPipeline.StripOuterQuotes`(377行)、`JsonLenientCoercer`、`SedValidation` | `StripOuterQuotes`拆状态机、`AhoCorasick.Build` | `test/unit/abs.tests/`(覆盖率2.8%) |
| plugins.infrastructure | `lib/plugins.infrastructure/` | `PluginManager`(872行)、`PluginLifecycleTracker` | `LoadWorkflowPluginCoreAsync`(95行) | `lib/plugins.tests/`(仅2文件) |
| vault | `lib/vault/` | `MemoryPaths`/`TeamMemoryPaths`、`SyncFileHash` | `SessionScanner.ExtractSessionMetaAsync`(129行) | `lib/vault.{memdir,other}.tests/` |

**最大风险点**:
1. `Dag<T>`/`ConcurrentDag<T>` 完全无测试(核心算法,与 ImmutableDag 同构但零覆盖)
2. `BashAstSecurityWalker` 8文件~1000行纯AST遍历零测试
3. `JsonRepairPipeline.StripOuterQuotes` 377行未拆分未测试
4. `PluginManager` 872行零单元测试
5. `LockRegistry`死锁检测图算法 private 无直接测试(CI历史漏检bug)

---

## 二、推进策略(分阶段,渐进式)

### 阶段 1:P0 纯逻辑零测试(不拆方法,只补已有可测方法的测试)

**原则**:优先补"已是 public/internal 但零测试"的纯逻辑方法,零重构风险,收益最大。

#### 1.1 structura — `Dag<T>` + `ConcurrentDag<T>` 对拍 ImmutableDag
- [ ] 新建 `lib/structura.tests/DagTests.cs`
  - [ ] `TopologicalSort`:链/钻石/菱形/空图/单节点/孤立多节点 → 断言层级
  - [ ] `HasCycle`:无环/自环/2节点环/3+节点环/多独立环
  - [ ] `FindAllCycles`:多环→断言环路径集合;嵌套环
  - [ ] `WouldCreateCycle`:实例+静态,跨长链可达性
  - [ ] `GetAffectedSubgraph`:钻石依赖图
  - [ ] `GetAncestors`/`GetDescendants`
  - [ ] 对拍:同输入下 `Dag` 与 `ImmutableDag` 结果一致
- [ ] 新建 `lib/structura.tests/ConcurrentDagTests.cs`
  - [ ] 锁超时返回空数组路径
  - [ ] 同步+异步 API 一致性
  - [ ] `WithLock`/`Dispose`
- [ ] 补 `ImmutableDag` 缺失:深拷贝快照隔离语义、`FindAllCycles`多环、`GetAffectedSubgraph`钻石图
- [ ] 补 `ImmutableHamTStringStringConverter` 往返测试(完全无测试)

#### 1.2 async_lock — 死锁检测图算法 + 二进制协议
- [ ] `LockRegistry.BuildWaitEdges` private→internal + 测试:空快照/全空闲/1等待/阈值过滤
- [ ] `LockRegistry.FindCycleFrom` private→internal + 测试:自环/2节点/3+节点/无环/断链
- [ ] `LockRegistry.EmitDeadlockReport` private→internal + 测试:1/2/3节点环格式
- [ ] `LockRegistry.DetectDeadlockFromCurrentFlow` internal + 直接测试
- [ ] `BinaryProtocol.Encode`/`ReadAsync` 往返测试:各消息类型/空payload/最大长度/边界
- [ ] `BinaryProtocol.WriteInt32BigEndian`/`ReadInt32BigEndianAsync` private→internal + 大端测试
- [ ] `HostElectionService.DecideRole` private→internal + 决策表:null→Host/myPid大→Host/myPid小→Slave/非数字→Slave
- [ ] 零覆盖类补测:`OrderedLockManager`/`AsyncLockedDictionary`/`AsyncLazy`/`BackpressureChannel`/`AsyncFlowIdentity`

#### 1.3 guard — BashAstSecurityWalker + CommandDangerClassifier 内部
- [ ] `BashAstSecurityWalker` 全部 `Walk*`/`Validate*`/`Mask*` private static→internal static + 逐方法测试(8文件~1000行)
  - [ ] `WalkCommand`/`WalkRedirectedStatement`
  - [ ] `WalkArgument`/`WalkString`/`ValidateArithmeticNode`
  - [ ] `WalkVariableAssignment`/`WalkDeclarationCommand`/`ResolveSimpleExpansion`
  - [ ] `WalkFileRedirect`/`WalkHeredocRedirect`/`WalkHerestringRedirect`
  - [ ] `CollectCommands`/`WalkStructuralNode`
  - [ ] `WalkForStatement`/`WalkConditionalStatement`/`WalkSubshell`
  - [ ] `PreChecks`:RunPreChecks/MaskBracesInQuotedContexts/HasErrorNode/StripRawString
- [ ] `CommandDangerClassifier` 内部纯函数 private→internal + 测试
  - [ ] `IsGitReadOnlySubcommand`(53行白名单)
  - [ ] `IsGitIrreversibleSubcommand`
  - [ ] `ClassifyByCombinations`/`IsCombinationHit`
  - [ ] `ClassifyPath`/`CheckRecurseForceCombination`
  - [ ] `ClassifyGitPipeRedirect`/`GetPipeTargetCommands`
- [ ] `PermissionCheckContext` 静态方法:`IsSensitivePath`/`IsDangerousCommand`/`MatchesPattern`/`ContainsOrdinalIgnoreCase`
- [ ] 守卫纯逻辑:`HeredocGuard.Evaluate`/`EscapeForDoubleQuotedString`、`VpnRouteGuard.Evaluate`、`GhPrBodyGuard.EscapeBody`/`GenerateDefaultBody`
- [ ] `DangerousCommandCatalog` 数据源完整性:`BuildCommands`/`BuildFlags`/`BuildCombinations`

#### 1.4 transport.impl — 协议编解码
- [ ] **先创建 `JoinCode.Infra.Transport.Tests` 测试项目**(InternalsVisibleTo 落空)
- [ ] `SseStreamParser.ParseAsync`:MemoryStream喂入SSE帧文本,断言event/data/id拼接、空行分隔、多行data
- [ ] `HttpRequestSerializer.SerializeAsync`/`Deserialize`:构造HttpRequestMessage序列化后字符串断言;反序列化状态行/头/体
- [ ] `HttpRequestSerializer.ParseStatusLine`/`ParseHeaderLine`/`IsContentHeader` private→internal static + 测试
- [ ] `BridgeTokenRefreshScheduler.DecodeJwtExpiry` private→internal + 测试:含/不含exp的JWT,毫秒转换
- [ ] `V1ReplBridgeTransport.IsStreamEvent` private→internal + 测试
- [ ] `ConnectionManager.CalculateReconnectDelay` private→internal + 测试:Min(base*2^(n-1), max)
- [ ] `SerialBatchEventUploader.ComputeRetryDelay`/`TakeBatch` private→internal + 测试
- [ ] `BoundedUUIDSet.Add`/`ToListAsync` 专项测试:FIFO淘汰+去重

#### 1.5 abstractions — JSON修复 + 安全验证
- [ ] `JsonRepairPipeline.StripOuterQuotes`(377行)拆 internal 状态机片段 + 逐片段测试(单引号/双引号/反引号/转义)
- [ ] `JsonRepairPipeline.FixUnquotedValues`/`FixUnquotedKeys`/`RepairJson` internal + 测试
- [ ] `JsonLenientCoercer.CoerceToNumber`/`CoerceToBool` private→internal + 测试:数字格式探测/布尔词表
- [ ] `SedValidation.ExtractSedExpressions`(122行)/`ContainsDangerousOperations` private→internal + 测试
- [ ] `BashSafeWrapperStripper.StripSafeWrappers` + 测试
- [ ] `ToolListDriftClassifier.Classify`(105行) + 测试:新增/移除/重命名推断
- [ ] `SettingsEditValidator.ValidateSettingsContent`(已internal) + 测试
- [ ] `PreapprovedDomains.CreateHostSet` + 测试
- [ ] `AppDataPaths` 静态构造 + 路径常量测试

#### 1.6 plugins.infrastructure — 生命周期 + 依赖图
- [ ] `PluginLifecycleTracker.ExecuteUndoChain`/`ExecuteAsyncUndoChainAsync` + 测试:逆序执行+异常隔离
- [ ] `PluginDependencyGraph`(contracts)补环场景:A→B→A截断行为断言、`DeclarePluginDependency`/`GetDependents`零测试
- [ ] `PluginCommandRegistry.RegisterCommandAsync` 别名展开 + 测试
- [ ] `ResourceReferenceGraph` 全部方法 + 测试
- [ ] `PluginResourceScanner.ScanPluginResources` + 测试
- [ ] `PluginManager` internal测试钩子已暴露(`AddToBlacklistForTest`/`IsBlacklistedForTest`)+ 补测试使用

#### 1.7 vault — 路径计算 + 哈希 + 评分
- [ ] `MemoryPaths` 全部方法 + 测试:纯Path.Combine拼接
- [ ] `TeamMemoryPaths` 全部方法 + 测试
- [ ] `SyncFileHash.ComputeAsync` + 确定性测试:固定输入→固定哈希值
- [ ] `MemoryRelevanceScorer.CalculateAdvancedRelevanceScore`/`GetMatchReason` + 测试:6加权因子
- [ ] `MemoryRelevanceSelector.ScoreMemory` private→internal + 测试
- [ ] `SessionScanner.CategorizeToolError`/`ExtractLanguageAndFileStats` private→internal static + 测试
- [ ] `MemorySearchHistory.IsQueryRelated` private→internal static + 测试:30%重叠度阈值
- [ ] `MemoryAgeInfo.CalculateHealthScore` private→internal + 测试
- [ ] `AppStateConverter.ToDocument`/`FromDocument` 往返一致性:FromDocument(ToDocument(s))==s
- [ ] `TranscriptFileWriter.ValidateId` + 测试:字符白名单
- [ ] `FacetCacheService.IsValidFacets`/`GetFacetFilePath` private→internal + 测试
- [ ] `MemoryTruncator.TruncateByBytes` private→internal static + UTF-8续字节边界测试

---

### 阶段 2:拆分长方法为 internal(重构导向,为后续测试铺路)

**原则**:先转为统一写法,再拆分纯计算片段为 internal,然后补测试。

#### 2.1 structura — SwissTable SIMD 去重
- [ ] `SwissTableHelper.FindForInsert.*` 三分派提取 `ProbeCore<TKey>` internal 泛型(消除18份重复)
- [ ] `SwissTableHelper.FindBucketOfDictionary.*` 同上
- [ ] `SwissTable.ICollection.CopyTo` 拆 `CopyToKvp`/`CopyToDictEntry`/`CopyToObjectBox` internal
- [ ] `SwissTable.TryInsert` 拆 `TryReplaceExisting`/`InsertNewBucket` internal
- [ ] `ImmutableDag.RemoveNodeFromState` 拆 `RemoveOneIncidentEdge` internal static
- [ ] `Dag.GetAffectedSubgraph`/`ImmutableDag.GetAffectedSubgraph` 拆 `ComputeSubgraphInDegrees`/`KahnTraverseSubgraph` internal(合并重复)
- [ ] `ImmutableHamT.BitmapNode.Add` 拆 `MaybeUpgradeToFullArrayNode` internal static

#### 2.2 async_lock — ActorBase 纯计算片段
- [ ] `ActorBase.CheckInputWatermark` 拆 `internal static WatermarkLevel ComputeWatermarkLevel(int count, int high, int critical)` + 表驱动测试
- [ ] `ActorBase.ConsumeBackpressureDelay` 拆 `internal static TimeSpan SumDelays(IEnumerable<TimeSpan>)` + 测试
- [ ] `ActorBase.RetrySendAsync` 退避公式拆 `internal static TimeSpan ComputeBackoff(int retry)` + 边界测试(0/10/11/16)
- [ ] `ActorBase.AskWithRetryAsync` 退避公式拆 `internal static int ComputeAskBackoffMs(int attempt)` + 边界测试(20/21/30)
- [ ] `ActorBase.EnterWaitGraph` internal + 直接测试:加边成功/环抛异常/callerId==Id跳过/callerId==null跳过
- [ ] `ActorBase.CreateInputChannel`/`CreateOutputChannel` internal static + 测试:capacity=0/null/正数
- [ ] `ActorBase.ConsumeLoopAsync` 幂等去重路径直接测试:重复IRequestCommand→Handle仅调一次
- [ ] `GatewayActor.CheckBreakerOpen` private→internal + 测试:Closed/Open/HalfOpen转换
- [ ] `SupervisedActor.TryRecordRestart` private→internal + 测试:重启次数+时间窗口
- [ ] `BackpressureChannel.CalculateCriticalDelay` private→internal + 测试
- [ ] `PriorityMailbox.TryReadByPriority` private→internal + 测试:High→Normal→Low
- [ ] `HostContextSyncService.SerializeSnapshot`/`DeserializeSnapshot`/`ExtractJsonField` private→internal + 测试

#### 2.3 guard — 守卫纯逻辑 + 防御节点
- [ ] `GitCommitGuard.ExtractFirstToken`/`IsGitCommitSubCommand` private→internal static + 测试
- [ ] `CmdIndirectCallGuard.TryExtractCmdInner`/`TryExtractPwshInner`/`ExtractQuotedOrRaw` private→internal static + 测试
- [ ] `HeredocGuard.EscapeForDoubleQuotedString` private→internal static + 测试
- [ ] `GhPrBodyGuard.HasBodyParameter`/`EscapeBody` private→internal static + 测试
- [ ] `RedirectWhitelistNode` 各 private static→internal + 测试
- [ ] `MtpPerturbationNode` 各 private static→internal + 测试
- [ ] `HookConditionEvaluator` 各 private→internal + 测试
- [ ] `RemotePolicyService.EvaluateRule`/`EvaluateUsageLimit`/`EvaluateCostLimit`/`EvaluateRateLimit`/`EvaluateToolRestriction`/`EvaluateTimeRestriction` private→internal + 测试
- [ ] 统一 `SelectPrimaryRisk`(CommandDangerClassifier与DangerousCommandProtectionMiddleware重复,优先级数组不一致)

#### 2.4 transport.impl — 重试状态机拆分
- [ ] `BridgeOAuthRetry.ExecuteWithOAuthRetryAsync`(69行)拆纯计算分支为 internal
- [ ] `V1ReplBridgeTransport.HandleConnectionError`(63行)拆休眠检测+预算计算为 internal
- [ ] `V2ReplBridgeTransport.RunSseReadLoopAsync` 拆重连逻辑
- [ ] `SerialBatchEventUploader.DrainAsync`(48行)拆批次排空+失败重试
- [ ] `HttpRequestSerializer.SerializeAsync`(46行)拆序列化步骤

#### 2.5 abstractions — JsonRepairPipeline 拆状态机
- [x] `JsonRepairPipeline.StripOuterQuotes`(**实际14行非377行,已internal static+8测试,无需拆分**) — 阶段2.5确认
- [x] `AhoCorasick.Build`(**在lib/infrastructure/非abstractions,跳过留给阶段2.9**) — 阶段2.5确认
- [x] `JsonRepairPipeline.FixUnquotedValues`(100行)拆出 `AppendQuotedValue`+`ShouldQuoteValueStart` 两个 internal static + 28确定性测试 — 阶段2.5完成
- [x] `JsonRepairPipeline.FixUnquotedKeys`(56行逻辑线性,拆分收益小,保留) — 阶段2.5评估
- [ ] `FileEditor.EditFileAsync`(127行)拆编辑策略选择/上下文匹配/替换应用 internal
- [ ] `PhysicalProcessService.ExecuteAsync`(65行)拆stdout/stderr读取/编码探测/退出码处理
- [ ] `GitHubApiClient.ReadLogStreamLinesAsync`(69行)拆流式行分割

#### 2.6 plugins.infrastructure — PluginManager 拆分
- [ ] `PluginManager.LoadWorkflowPluginCoreAsync`(95行)拆7职责为 internal 子方法
- [ ] `PluginManager.UnloadPluginCoreAsync`(59行)拆 `InternalUnloadExternal`/`InternalUnloadNative`/`InternalUnloadWorkflow`
- [ ] `PluginManager.UnloadAllPluginsCoreAsync` 拆 `OrderUnloadSequence` internal static
- [ ] `PluginManager.ScanAfterUnload` 拆 `ComputeLeakReport` internal
- [ ] `PluginHotReloader.HandleAsyncImpl`(48行)拆 HandleStartWatching/HandleStopWatching/HandleReload
- [ ] `PluginCommandRegistry.RegisterCommandAsync` 拆 `ExpandAliases` internal static
- [ ] `PluginLifecycleTracker.ExecuteUndoChain` 拆 `ExecuteUndoChainPure` internal static

#### 2.7 vault — SessionScanner 拆分
- [ ] `SessionScanner.ExtractSessionMetaAsync`(129行)拆6个 internal static:统计用户消息/统计助手消息/统计工具/提取语言文件/检测中断/构建结果
- [ ] `TodoService.WriteTodosAsync`(88行)拆
- [ ] `MemorySearchHistory.BuildSearchingPastContextSectionAsync`(79行)拆
- [ ] `MemoryManagementService.ScanMemoriesCoreAsync`(75行)拆
- [ ] `CompleteStepToolHandlers.CompleteStepAsync`(68行)拆
- [ ] `MemoryTruncator.SmartTruncate`(65行)拆
- [ ] `AppStateConverter.ToDocument`/`FromDocument` 拆

#### 2.8 clock — GoalGraphTemplates 拆分
- [ ] `GoalGraphTemplates` 各 Build* 方法拆 internal static 子方法 + 图构造单测
- [ ] `GoalGraphTemplates.ClusterExpandFunction`(112行)拆
- [ ] `DecomposabilityAnalyzer.BuildAnalyzerPrompt`(80行)拆
- [ ] `GraphExecutionContext.GetNextNodeIds` internal + 拓扑下一节点计算单测
- [ ] `ContinuationPromptBuilder.BuildContinuationPrompt` internal + 测试

#### 2.9 infrastructure — AhoCorasick + FileEditor
- [ ] `AhoCorasick.Build` 拆 goto/failure 两个 internal static + 算法单测
- [ ] `FileEditor.EditFileAsync`/`EditByLineRangeAsync` 拆 internal 纯函数
- [ ] `PhysicalProcessService.ExecuteAsync` 拆
- [ ] `GitHubApiClient.ReadLogStreamLinesAsync` 拆流式行分割

---

### 阶段 3:补测有IO/异步但可mock的方法

- [ ] `PermissionChecker.CheckPermissionAsync` 管道执行
- [ ] `PermissionCheckingInterceptor.OnBeforeToolInvokeAsync`
- [ ] `HookConfigurationManager.LoadAllHooksAsync` 缓存+锁
- [ ] `SessionHookManager` CAS操作
- [ ] `InteractiveHandler.Handle`/`ExecuteHooksAsync`/`ExecuteClassifierAsync`
- [ ] `SwarmWorkerHandler.ForwardToLeaderAsync`
- [ ] `PluginManager` 加载/卸载全流程(mock依赖)
- [ ] vault 团队同步子系统(`SyncFileScanner`/`SyncFileTransfer`/`SyncConflictResolver`/6中间件)

---

## 三、验收标准

每个任务完成后必须满足:
1. **编译通过**:`dotnet build` 对应 csproj Debug 模式
2. **测试通过**:新增测试全部绿
3. **确定性**:测试不依赖时序/线程调度/IO,给定输入→断言输出
4. **InternalsVisibleTo**:internal 方法所在项目已对测试项目暴露
5. **git 提交**:每个子任务独立提交,消息格式 `test: 补充 {模块} {方法} 确定性测试`
6. **无警告**:TreatWarningsAsErrors 已启用

---

## 四、执行顺序建议

**推荐顺序**(收益/风险比降序):
1. 阶段1.1 structura Dag/ConcurrentDag(核心算法零测试,最高风险)
2. 阶段1.2 async_lock LockRegistry图算法(死锁检测安全网)
3. 阶段1.4 transport.impl 协议编解码(先建测试项目)
4. 阶段1.3 guard BashAstSecurityWalker(1000行零测试)
5. 阶段1.5 abstractions JsonRepairPipeline(377行)
6. 阶段1.6 plugins.infrastructure PluginLifecycleTracker
7. 阶段1.7 vault 路径计算+哈希
8. 阶段2.* 拆分长方法(按模块逐个)
9. 阶段3.* mock测试

---

## 五、注意事项

1. **禁止删除文件**:遇到需移除的测试/代码,移到 `.xxx/` 目录(AGENTS.md 红线)
2. **InternalsVisibleTo 落空**:transport.impl 需先创建 `JoinCode.Infra.Transport.Tests` 测试项目
3. **NativeAOT 兼容**:禁止 dynamic/反射emit,测试项目也需 IsAotCompatible
4. **GlobalUsings**:.cs 文件内禁止写 using,统一放 GlobalUsings.cs
5. **TDD 循环**:🔴红→🟢绿→🔵重构,每个子任务独立循环
6. **并行子代理禁止 git commit**:由主代理统一提交
7. **已知 flaky**:`RingBufferMultiWriterTests`/`WatermarkReached` 有预先存在竞态,失败时先重跑确认勿误归因

---

<!-- 🤖 Auto Decision: 2026-09-29 -->
<!-- 决策: 分三阶段推进,P0纯逻辑零测试优先(零重构风险),阶段2拆internal,阶段3补mock测试 -->
<!-- 原因: 全部一次性推进上下文爆炸,渐进式收益/风险比最优 -->
<!-- 替代方案: 按模块逐个全做(深度优先,跨模块进度慢) -->
<!-- 验证: 8个explore子代理检测完成,任务清单已整合 ✅ -->
