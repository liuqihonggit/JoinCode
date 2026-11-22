# TASK105 — 公开元组清零收尾与工作交接

> 状态：已完成。交付代码批已提交 `8f12a7d6b`；非交付工具 `jcc_audit_ast_cli` 残留 11 处已在后续提交清零，`JCC10011` 升 `warning`（详见第 8 节）。

## 1. 背景与结论

JCC10011（公开 API 禁止元组，规则实现 `modules/generators/aot_safety.generator/rules/code_organization/PublicTupleApiRule.cs`）在 2026-11-21 全仓 Rebuild 采集到 79 处存量，按域分五批清理：

| 批次 | 提交 | 覆盖域 |
|---|---|---|
| 首批 | `a7bbfc59b` | mcp / guard / apps |
| 第二批 | `3341efa00` | composition / services / llm / native_gui |
| 第三批 | `74da2d5af` | storage |
| 第四批 | `fb219f4f0` | perception |
| 第五批（本批） | `8f12a7d6b` | abstractions 全域 + cognition + tools + native_gui 消费适配 + 测试适配 |

**结论**：2026-11-22 以 `.editorconfig` 临时提升 `JCC10011.severity = warning` 做全仓 `-t:Rebuild` 重采：

- `JoinCode.slnx`（218 项目）：仅 11 处命中，**全部位于 `non_deliverables_tools/jcc_audit_ast_cli`（非交付工具）**；
- `native_tui/native_tui.slnx`：0 处；
- `native_gui` core/host 测试项目：0 处。

即**交付代码的 79 处存量已清零**。采集后 `.editorconfig` 已还原为 `suggestion`（`git diff` 无内容差异，仅 mtime 变化）。

## 2. 本批改动范围

### 2.1 按域统计

| 域 | 文件数 | 主要内容 |
|---|---|---|
| abstractions（核心接口 + 实现 + DTO） | 24 | IFileSystem / IKvStore / LineRangeReader / SubAgentMentionParser / IHotFileWatchdog / ApiMessageExtensions / ModelPricingTable / IContentReplacementService / SecurityPatterns / INotebookService / LineSpanIndexer / SearchRegexCompiler / ToolTransitionModel / EditFileCommands / InMemoryFileSystem / PhysicalFileSystem / StructuredPatchGenerator / FileEncodingDetector / ThrottledFileService / KvStore 四实现 / BoundedObjectPool / StringBuilderPool / NetworkRetryOptions / HotFileWatchdog |
| cognition | 14 | ContentReplacementService / ITokenCostTracker.GetUsage / BudgetConfig.TryBuild / ModelPricingTable / BuildQueueBase / NotebookService / MethodNameCache / ApplyPatchLogic / FileEditLogic / AgentTypeSpecParser / ShellSedInterceptMiddleware / ShellImageOutputDetector / SessionToolMap / SystemPromptBuilder / SessionMemoryPromptTemplate |
| guard | 3 | ConfigLoader / SettingsSourceMigration / HookConfigurationManager |
| tools | 4 | RgEngine（补漏）/ CacheGitHubApiClient / GitHubToolHandlers / GhCacheServiceTests |
| composition / services / native_gui | 5 | LoginCommand / LogoutCommand / McpServerConfigStore / ConcurrentSession / JccChatSession |
| test（消费适配） | 7 | SubAgentMentionParserTests / EditFileAsyncTests / KvStore 三套测试 / HotFileWatchdogTests / ToolTransitionTest / testing.common InMemoryFileSystem |

新增文件 2 个：

- `modules/abstractions/abstractions.primitives.interfaces/src/EditFileTransformResult.cs`
- `modules/abstractions/agents/hot_spot/hot_file/HotFileChange.cs`

新增具名 DTO 共 **28 个**（清单见附录 A）。所有公开元组均替换为 `public sealed record`，私有方法内部的临时元组（如并行分块 `(int Start, int End)`）保留。

### 2.2 本批补漏（全仓编译暴露）

| 位置 | 问题 | 修复 |
|---|---|---|
| `modules/tools/rg/RgEngine.cs:111` | `CompileRegex` 仍返回 `(Regex?, string?)`，与 `SearchRegexCompiler.Compile` 新签名冲突（CS0029） | 改返回 `RegexCompileResult` |
| `modules/tools/rg/RgEngine.cs:342/375/400` | 三个匹配收集方法参数仍为 `List<(int Start, int Length)>`，与 `LineSpanIndexer.BuildLineRanges` 的 `List<LineCharRange>` 冲突（CS1503） | 参数改 `List<LineCharRange>` |
| `test/unit/mcp_tool_dispatch.tests/execution/ToolTransitionTest.cs:153-154` | 断言仍用元组 `(Role, ToolName)` | 改 `new ToolRecommendation(...)` |

## 3. 验证证据（2026-11-22）

| 检查 | 命令 | 结果 |
|---|---|---|
| 全仓编译 JoinCode.slnx（218 项目） | `dotnet build build/sln/JoinCode.slnx -c Debug -v q -m` | ✅ 0 警告 0 错误 |
| native_tui | `dotnet build native_tui/native_tui.slnx -c Debug -v q -m` | ✅ 0 警告 0 错误 |
| native_gui core tests | `dotnet build native_gui/tests/native_gui.core.tests/NativeGui.Core.Tests.csproj` | ✅ 0 警告 0 错误 |
| native_gui host tests | `dotnet build native_gui/tests/native_gui.host.tests/NativeGui.Host.Tests.csproj` | ✅ 0 警告 0 错误 |
| JCC10011 重采 | 临时 `warning` + 四个 target `-t:Rebuild -p:TreatWarningsAsErrors=false` | ✅ 交付代码 0 命中（仅非交付工具 11 处） |
| 单元测试（相关 10 个项目） | `dotnet test <csproj> --filter "Category!=Integration&Category!=Benchmark"` | ✅ 3969 通过 / 0 失败 |

测试项目明细：

| 项目 | 通过数 |
|---|---|
| Infra.IO.Tests | 323 |
| Infra.Utils.Tests | 561 |
| Infra.Services.Tests | 426 |
| Abs.Ai.Tests | 104 |
| Gh.Tests | 794 |
| Rg.Tests | 154 |
| Hands.Shell.Tests | 540 |
| McpToolDispatch.UniqueTests | 228 |
| Brain.Prompts.Tests | 229 |
| Hands.ToolHandlers.Tests | 610 |

> 未跑全仓所有单元测试；按项目纪律由接任者在 CI 或本地全量执行。此次接口契约变更（`IFileSystem.EditFileAsync` / `IKvStore.ScanAsync` / `LineRangeReader.Slice` 等）已通过“全仓编译 + 消费方测试”覆盖。

## 4. 需求落地验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `EditFileTransformResult<T>`（`IFileSystem.EditFileAsync` transform 回调结果） | `PhysicalFileSystem` / `InMemoryFileSystem` / `ThrottledFileService` / `FileEditLogic` / `ApplyPatchLogic` / `ConfigLoader` / `ShellSedInterceptMiddleware` / `JccChatSession` / testing.common InMemoryFileSystem | ✅ | ✅ 编译 + Infra.IO/Hands.Shell/Hands.ToolHandlers 测试 |
| `KvStoreEntry`（`IKvStore.ScanAsync` 条目） | `InMemoryKvStore` / `LeasedKvStoreAdapter` / `PithosKvStore` / `PithosKvStoreLease` / `CacheGitHubApiClient` / Gh.Tests / Infra.IO.Tests | ✅ | ✅ 编译 + KvStore/Gh 测试 |
| `LineCharRange` / `LineRangeSlice<T>` | `LineSpanIndexer` / `LineRangeReader` / `RgEngine` / `GitHubToolHandlers` | ✅ | ✅ 编译 + Rg/Gh 测试 |
| `SubAgentMention` / `HotFileChange` | `SubAgentMentionParser` / `HotFileWatchdog` / `IHotFileWatchdog` / Abs.Ai.Tests / Infra.Utils.Tests | ✅ | ✅ 编译 + 对应测试 |
| `ToolRecommendation` / `ToolCallReference` / `ModelPricingEntry` | `ToolTransitionModel` / `ApiMessageExtensions` / 两套 ModelPricingTable / McpToolDispatch.UniqueTests | ✅ | ✅ 编译 + 对应测试 |
| `RegexCompileResult` / `LineChangeCount` / `DecodedFileContent` | `SearchRegexCompiler` / `StructuredPatchGenerator` / `FileEncodingDetector` / `RgEngine` | ✅ | ✅ 编译 + Rg/Infra.IO 测试 |
| `PoolStats` / `ReconnectParams` / `MethodNameCacheStats` / `BudgetBuildResult` / `TokenCostUsage` | 对象池、重连参数、方法名缓存、预算构建、Token 统计 | ✅ | ✅ 编译 + Infra/Hands 测试 |
| `SystemPromptPartition` / `SessionMemoryTruncation` / `ShellImageData` / `SessionToolTimeouts` / `AgentTypeSpec` / `PatchHunkOutcome` / `NotebookCellSummary` / `BuildQueueEntryHandle` / `GitleaksRule` / `CompiledGitleaksRule` | 提示词分区、记忆截断、Shell 图片、工具超时、Agent 类型、补丁结果、Notebook、编译队列、安全规则 | ✅ | ✅ 编译 + Brain.Prompts/Hands.Shell/Hands.ToolHandlers 测试 |

## 5. 剩余工作（交接给下一任）

| 优先级 | 事项 | 说明 |
|---|---|---|
| ✅ 已完成 | `non_deliverables_tools/jcc_audit_ast_cli` 11 处元组 | 2026-11-22 清零（10 个 record），工具测试 61/61；见第 8 节 |
| ✅ 已完成 | 提升 JCC10011 严重级别 | `.editorconfig` 由 `suggestion` 升 `warning`（交付代码全仓 0 命中）；统一升 `error` 留 P5 |
| P3 | 全仓单元测试 | 本批只跑了相关项目；完整 CI 由 PR 流水线覆盖 |
| P3 | 编译脚本改进 | 见第 6 节，可选 |

### 如何重采 JCC10011 存量

1. `.editorconfig` 中把 `dotnet_diagnostic.JCC10011.severity = suggestion` 临时改为 `warning`；
2. 对四个 target 执行 `-t:Rebuild`（增量编译采不全），并关闭警告即错误：
   ```bash
   dotnet build build/sln/JoinCode.slnx -c Debug -v q -m -t:Rebuild -p:TreatWarningsAsErrors=false
   dotnet build native_tui/native_tui.slnx -c Debug -v q -m -t:Rebuild -p:TreatWarningsAsErrors=false
   dotnet build native_gui/tests/native_gui.core.tests/NativeGui.Core.Tests.csproj -c Debug -v q -m -t:Rebuild -p:TreatWarningsAsErrors=false
   dotnet build native_gui/tests/native_gui.host.tests/NativeGui.Host.Tests.csproj -c Debug -v q -m -t:Rebuild -p:TreatWarningsAsErrors=false
   ```
3. 过滤 `JCC10011`；采集完务必还原为 `suggestion`。

### 下一批修复方法（可复用本批模式）

1. 在抽象层接口处先改返回类型为具名 `record`（位置式 record 自带 `Deconstruct`，`var (a, b) = ...` 消费点可继续工作）；
2. `dotnet build build/sln/JoinCode.slnx -c Debug -v q -m` 全仓编译，让遗漏消费方以 CS0029/CS1503 暴露；
3. 逐项修复后跑对应测试项目；
4. 更新本文件第 2、3、4 节并提交（提交信息带 `TASK105` 或新编号）。

## 6. 编译脚本诊断（`non_deliverables_tools/scripts/Build-AllDomains.ps1`）

用户疑问：为什么编译时机器没有满负荷、是否存在并行限制和重复编译？结论：

1. **没有显式并行限制**：脚本未传 `-m:N` / `MaxCpuCount`；`dotnet build` 默认按 CPU 核数并行（本机 16 核）。
2. **不轰鸣的直接原因**：
   - `-Changed` 模式用 `foreach` **串行逐个 csproj** 调 `dotnet build`，每个时刻只有一个项目图在编译，内部并行度很低；
   - 输出被 `-v q` + `Tee-Object | Out-Null` 完全静默，只在失败时打印 error；
   - 依赖项目已是最新（增量 up-to-date 检查），实际只重编译改动项目，CPU 自然上不去。
3. **重叠情况（实测）**：
   - `JoinCode.slnx` 有 218 个项目，16 个 `module.slnx` 合计 217 个项目，其中 **199 个与 JoinCode.slnx 交集**；但脚本“全量模式”只编 `JoinCode.slnx`，**不编** `module.slnx`，因此无实际重复编译；
   - `module.slnx` 之间有 **4 个项目重复**：`CodeFixes.csproj`、`AotSafety.Generator.csproj`、`EnumMetadata.Generator.csproj`（abstractions 与 generators 同时列出）、`Structura.csproj`（abstractions 与 collections 同时列出）；只有 `-Domains` 模式会重复构建它们（增量会跳过，仅多一次 MSBuild 评估）；
   - `native_tui` 不在 `JoinCode.slnx` 中，`native_gui` core/host 测试项目也不在，全量模式的 4 个 target 无重叠。
4. **`-Changed` 模式的真实低效**：63 个改动文件映射到几十个 csproj，逐个 `dotnet build` 会把共享依赖图重复评估 N 次；大改动时建议直接用全量 `JoinCode.slnx` 一次编译（本批实测 2 分钟）
5. **改进建议（P3，可选）**：
   - `-Changed` 模式若项目数 > 10 或跨域 > 3，自动回退到 `JoinCode.slnx` 一次编译；
   - 增加 `-MaxCpuCount` 参数；
   - 增加 `-Verbose` 开关（`-v m`）以观察进度；
   - 清理 `module.slnx` 间 4 个重复项目归属。
6. **实测缺陷（2026-11-22）**：`git` / `dotnet` 不在 PATH 时，脚本内调用失败却仍打印“无源码改动，跳过编译”或“按需编译全部通过”——未检查 `$LASTEXITCODE`。建议每个外部命令后校验退出码并以非零退出，避免假绿。

## 7. ⚠️ 环境阻塞：git 无法写入主仓库 `.git`

本批代码与文档已完成，但**当前运行环境拒绝 git 写操作**，无法由 AI 完成提交：

```
fatal: Unable to create 'D:/project/JoinCode/.git/worktrees/w4/index.lock': Permission denied
error: insufficient permission for adding an object to repository database D:/project/JoinCode/.git/objects
```

排查记录：

- `git add` / 系统 Git / PowerShell 启动 Git 均失败（`index.lock` 创建被拒、`objects` 写入被拒）；
- `powershell`/`pwsh` 对 `D:\project\JoinCode\.git` 下任意文件写入被拒；
- 但 bash 的 `touch` 可写 `.git` 根；`GIT_INDEX_FILE` 指向工作区内时可写；
- 结论：这是**平台/环境级写权限限制**，不是仓库损坏，AI 不应绕过。

**请在工作区外（或无沙箱限制的终端）执行以下命令完成提交**，提交信息已带文档编号：

```bash
cd D:/project/w4
git add -A
git commit -m "refactor: 公开元组签名改具名 record（abstractions 收尾批）| 文档: TASK105 | 决策: 交付代码 JCC10011 存量清零，非交付工具 11 处留待后续"
```

提交后可用 `git log -1 --format=%s` 确认包含 `TASK105`，交接时以该编号检索本文件。

> 后续已在无限制终端完成提交：`8f12a7d6b`（`git log -1 --format=%s` 包含 `TASK105`）。

## 8. 收尾（2026-11-22）

| 事项 | 结果 |
|---|---|
| 非交付工具 11 处 | `SyncAsyncFixer` / `GetAwaiterFixer` / `AsyncMethodFixer` / `AsyncIssueFixer` / `AnalyzerDrivenFixer` / `BuildErrorFixer` / `CascadeAsyncFixer` / `DiCycleInfo.Edges`+构造参数 / `DiRegistrationExtractor.Extract` / `FastProjectLoader.LoadCompilationsAsync`+`LoadCompilationsToChannelAsync` 全部改具名 record（`Models.cs` 新增 10 个） |
| 工具测试 | `JccAuditCli.Tests` 61/61 通过 |
| JCC10011 重采 | `JoinCode.slnx` 218 项目 `-t:Rebuild -m:1` + `warning`：0 命中 / 0 错误（日志 `.xxx/logs/jcc10011/full-slnx-20261122-serial.log`） |
| 严重级别 | `.editorconfig`: `JCC10011.severity` `suggestion` → `warning`（P5 统一升 `error`） |
| 工程事实 | solution 级 `-t:Rebuild -m`（并行）会因先清掉依赖项目 ref 程序集而报大量 CS0006；全量采集必须 `-m:1` |

## 附录 A — 本批新增具名 record（28 个）

| # | 类型 | 所在文件 |
|---|---|---|
| 1 | `EditFileTransformResult<T>(byte[]? NewContent, T Result)` | abstractions.primitives.interfaces/src/EditFileTransformResult.cs（新增文件） |
| 2 | `HotFileChange(string FilePath, string ChangerId)` | agents/hot_spot/hot_file/HotFileChange.cs（新增文件） |
| 3 | `KvStoreEntry(byte[] Key, byte[] Value)` | abstractions.primitives.interfaces/src/IKvStore.cs |
| 4 | `LineRangeSlice<T>(IReadOnlyList<T> Range, bool HasMore, int NextSkip)` | abstractions.utils/core/misc/LineRangeReader.cs |
| 5 | `SubAgentMention(string AgentName, string Message)` | agents/agent/sub_agent/SubAgentMentionParser.cs |
| 6 | `ToolCallReference(string Id, string Name)` | ai/llm/chat/chat_core/ApiMessageExtensions.cs |
| 7 | `ModelPricingEntry(string Keyword, decimal PromptCost, decimal CompletionCost)` | ai/llm/execution/pricing/ModelPricingTable.cs |
| 8 | `ContentReplacementOutcome(...)` | brain/context/content_replacement/IContentReplacementService.cs |
| 9 | `GitleaksRule(string Id, string Pattern, string? Flags)` | guard/security/scanning/SecurityPatterns.cs |
| 10 | `CompiledGitleaksRule(string Id, Regex Re)` | guard/security/scanning/SecurityPatterns.cs |
| 11 | `NotebookCellSummary(int Index, NotebookCellType Type, string Preview)` | hands/code/INotebookService.cs |
| 12 | `LineCharRange(int Start, int Length)` | hands/code/LineSpanIndexer.cs |
| 13 | `RegexCompileResult(Regex? Regex, string? Error)` | hands/code/SearchRegexCompiler.cs |
| 14 | `ToolRecommendation(ToolTransitionRole Role, string ToolName)` | hands/tools/models/ToolTransitionModel.cs |
| 15 | `LineChangeCount(int Added, int Removed)` | infrastructure/io/services/diff/StructuredPatchGenerator.cs |
| 16 | `DecodedFileContent(string Content, Encoding Encoding)` | infrastructure/io/services/file_ops/FileEncodingDetector.cs |
| 17 | `PoolStats(int Count, int MaxSize)` | infrastructure/utils/pooling/BoundedObjectPool.cs |
| 18 | `ReconnectParams(TimeSpan BaseDelay, TimeSpan MaxDelay)` | infrastructure_configuration/NetworkRetryOptions.cs |
| 19 | `TokenCostUsage(int InputTokens, int OutputTokens, decimal Cost)` | cognition/brain.query/query2/token_budget/core/QueryEngine.cs |
| 20 | `BudgetBuildResult(BudgetConfig? Config, string? Error)` | cognition/brain_cost_tracking/cost_tracking/services/core/BudgetConfig.cs |
| 21 | `BuildQueueEntryHandle(BuildQueueEntry Entry, TaskCompletionSource<BuildQueueResult> Tcs)` | cognition/hands/build/BuildQueueBase.cs |
| 22 | `MethodNameCacheStats(int Count, int ApproximateSize)` | cognition/hands_core/skills/utils/MethodNameCache.cs |
| 23 | `PatchHunkOutcome(bool Failed, int Count)` | cognition/hands_core/tool_handlers/core/logic/ApplyPatchLogic.cs |
| 24 | `AgentTypeSpec(string PrimaryType, IReadOnlyList<string>? AllowedTypes)` | cognition/hands_core/tool_handlers/system_tools/agent/AgentTypeSpecParser.cs |
| 25 | `ShellImageData(string MediaType, string Base64Data)` | cognition/hands_shell/shell/services/ShellImageOutputDetector.cs |
| 26 | `SessionToolTimeouts(List<ToolLifecycleInfo> ToWarn, List<ToolLifecycleInfo> ToKill)` | cognition/hands_shell/system_actuator/abstractions/SessionToolMap.cs |
| 27 | `SystemPromptPartition(string StaticPrefix, string DynamicSuffix)` | cognition/prompts/core/SystemPromptBuilder.cs |
| 28 | `SessionMemoryTruncation(string TruncatedContent, bool WasTruncated)` | cognition/prompts/templates/memory/SessionMemoryPromptTemplate.cs |