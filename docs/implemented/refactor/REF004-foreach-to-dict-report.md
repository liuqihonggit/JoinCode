# foreach / FirstOrDefault 转字典优化报告（完整版）

> 📍 **导航**: [docs/](../README.md) › [refactor/](README.md) | **前置**: [adr/](../adr/README.md)
> 🔗 **上游索引**: [refactor/README.md](README.md) — 修改本文档后须同步更新此索引

> 扫描日期：2026-09-14
> 扫描范围：lib / kit / server / llm / app / gen（排除 .xxx/ 归档、生成代码、测试）
> 交付方式：先出报告再分批改
> 改造原则：单一数据源 + 数据源类封装 GetXxx 方法 + 不透传数据给消费方

---

## 一、改造原则（用户指定）

1. **单一数据源**：字典在数据源类内部构建一次，消费方不重复建字典
2. **封装 GetXxx 方法**：数据源类暴露 `GetById(id)` / `TryGetByName(name, out var x)` 等方法，消费方调方法而非拿到集合自己查找
3. **不透传数据给消费方**：消费方不接收整个集合再线性查找，而是向数据源要特定元素
4. **符合 AGENTS.md 规则5**：传接口/完整对象，不传属性；数据源类承担查找职责

### 改造模式示例

❌ 旧（消费方拿集合线性查找）：
```csharp
var definitions = await GetAgentDefinitionsAsync();
return definitions.FirstOrDefault(d => d.Role == role && d.Variant == variant);
```

✅ 新（数据源封装查找，消费方调方法）：
```csharp
// AgentDefinitionProvider 内部
private FrozenDictionary<(AgentRole, ExecutorVariant?), AgentDefinition> _definitionMap = ...;
public AgentDefinition? GetDefinition(AgentRole role, ExecutorVariant? variant)
    => _definitionMap.TryGetValue((role, variant), out var def) ? def : null;

// 消费方
return _provider.GetDefinition(role, variant);
```

---

## 二、总体统计

| 目录 | 文件数 | foreach | FirstOrDefault | 可转 | 高 | 中 | 低 |
|------|--------|---------|----------------|------|----|----|-----|
| lib  | 1962 | 838 | 40 | 7 | 0 | 3 | 4 |
| kit  | 876 | 807 | 54 | 5 | 4 | 1 | 0 |
| server | 218 | 259 | 23 | 6 | 6 | 0 | 0 |
| llm  | 311 | 272 | 17 | 2 | 0 | 2 | 0 |
| app  | 226 | 159 | 29 | 4 | 0 | 1 | 3 |
| gen  | 52 | 236 | 69 | 6 | 0 | 4 | 2 |
| **合计** | **3645** | **2571** | **232** | **30** | **10** | **11** | **9** |

---

## 三、可转位置完整清单（30 处）

### 批次1：高收益 10 处

#### 1-6. server\code_index\parsing\CSharpCallExtractor.cs（6 处，同一数据源）

**背景**：`ExtractCallsFromTree` 是代码索引核心入口，接收 `symbols` 参数后递归遍历 AST，每个调用节点都触发对 `symbols` 的线性查找。`symbols` 在整个递归过程中不变，但未建索引，导致 O(N×M)（N=调用点数，M=符号数）。已预建 `classNameSet`/`interfaceNameSet`/`extensionMethodMap` 等字典，但 `symbols` 本身未建索引。

| # | 行号 | 原始代码 | key |
|---|------|----------|-----|
| 1 | 271 | `var match = symbols.FirstOrDefault(s => s.FullyQualifiedName == candidateFqn);` | FullyQualifiedName |
| 2 | 285 | `var globalMatch = symbols.FirstOrDefault(s => s.Name == handlerName && s.Kind is SymbolKind.Method);` | Name (+ Kind 过滤) |
| 3 | 496 | `var match = symbols.FirstOrDefault(s => s.FullyQualifiedName == candidateFqn);` | FullyQualifiedName |
| 4 | 504 | `var callerMatch = symbols.FirstOrDefault(s => s.FullyQualifiedName == callerAsParentFqn);` | FullyQualifiedName |
| 5 | 510 | `var globalMatch = symbols.FirstOrDefault(s => s.Name == calleeName && s.Kind is SymbolKind.Method or SymbolKind.LocalFunction);` | Name (+ Kind 过滤) |
| 6 | 539 | `var symbol = symbols.FirstOrDefault(s => s.Name == name && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface);` | Name (+ Kind 过滤) |

**改造方案**（单一数据源）：
- 新建 `SymbolIndex` 类，在 `ExtractCallsFromTree` 入口一次性构建：
  - `Dictionary<string, SymbolInfo> _byFqn`（key=FullyQualifiedName，保留首个与 FirstOrDefault 语义一致）
  - `ILookup<string, SymbolInfo> _byName`（key=Name，支持同名多符号按 Kind 二次过滤）
- 暴露 `TryGetByFqn(fqn, out var s)` 和 `GetByName(name)` 方法
- `CollectCalls` / `ResolveCalleeFqn` / `FindSymbolFqn` / `TryExtractEventHandlerEdge` 接收 `SymbolIndex` 而非 `IReadOnlyList<SymbolInfo>`
- 收益：O(N×M) → O(N×K)，K 为同名符号数（通常 ≤3）

#### 7-8. kit\hands\skills（2 处同构代码）

| # | 行号 | 原始代码 |
|---|------|----------|
| 7 | kit\hands\skills\skills2\core\SkillExecutor.cs:73 | `var step = skill.Steps.FirstOrDefault(s => s.Id == currentStepId);` 在 while 循环内 |
| 8 | kit\hands\skills\services\skill\SkillExecutionMiddleware.cs:56 | 同上同构 |

**上下文**：
```csharp
var currentStepId = skill.Steps.FirstOrDefault()?.Id;
while (currentStepId != null && !cancellationToken.IsCancellationRequested) {
    ...
    var step = skill.Steps.FirstOrDefault(s => s.Id == currentStepId);   // 每次线性查找
```

**改造方案**：
- `Skill` 类（或 `SkillSteps` 容器）内部建 `FrozenDictionary<string, SkillStep>` by Id，暴露 `GetStep(id)` 方法
- while 循环内改 `skill.GetStep(currentStepId)`
- 两处同构，统一改造，可抽公共步骤查找辅助方法
- 收益：O(n²) → O(n)，n=步骤数

#### 9. kit\slash\hands\git\WorktreeCommand.cs:77

**原始代码**：
```csharp
foreach (var worktreePath in worktrees) {
    var session = sessions.FirstOrDefault(s => s.WorktreePath.Equals(worktreePath, StringComparison.OrdinalIgnoreCase));
```

**改造方案**：
- sessions 数据源类建 `FrozenDictionary<string, Session>` by WorktreePath（OrdinalIgnoreCase），暴露 `GetByWorktreePath(path)` 方法
- 收益：O(n×m) → O(n+m)

#### 10. kit\brain\context\compression\strategies\ReferenceIndexCompressor.cs:242

**原始代码**：
```csharp
var entries = new List<ReferenceEntry>();
foreach (Match match in matches) {
    var filePath = match.Value;
    var existingEntry = entries.FirstOrDefault(e => e.FilePath == filePath);   // 去重查找
    if (existingEntry == null) { entries.Add(new ReferenceEntry { FilePath = filePath }); }
}
```

**改造方案**：
- 用 `HashSet<string> seenPaths` 替代线性查找判断存在性
- `if (seenPaths.Add(filePath)) { entries.Add(...) }`
- 收益：O(n²) → O(n)，matches 数可能上千

---

### 批次2：中收益 11 处

#### 11-13. lib\scheduling Dag 边查找（3 处同一模式）

| # | 行号 | 原始代码 |
|---|------|----------|
| 11 | lib\scheduling\services\TaskService.cs:191 | `var existingEdge = _dag.Edges.Values.FirstOrDefault(e => e.FromId == dependsOnTaskId && e.ToId == taskId);` |
| 12 | lib\scheduling\services\TaskService.cs:222 | `var edgeToRemove = _dag.Edges.Values.FirstOrDefault(e => e.FromId == dependsOnTaskId && e.ToId == taskId);` |
| 13 | lib\scheduling\runtime\TaskRuntime.cs:210 | `var edgeToRemove = _dag.Edges.Values.FirstOrDefault(e => e.FromId == dependsOnTaskId && e.ToId == taskId);` |

**改造方案**（单一数据源）：
- `Dag<T>` 内部新增 `Dictionary<(string FromId, string ToId), DagEdge>` 复合索引，AddEdge/RemoveEdge 时维护
- 暴露 `GetEdge(fromId, toId)` / `TryGetEdge(fromId, toId, out var edge)` 方法
- 三处调用改 `_dag.GetEdge(dependsOnTaskId, taskId)`
- 收益：O(E) → O(1)；`Dag<T>` 内部已有 `_adjacency`/`_reverseAdjacency` 邻接表，缺 `(FromId,ToId)→Edge` 复合索引

#### 14. kit\hands\shell\services\EnvironmentProbeService.cs:52

**原始代码**（共 9 次查找同一 List）：
```csharp
var git = report.Components.FirstOrDefault(c => c.Id == "git");
var wsl = report.Components.FirstOrDefault(c => c.Id == "wsl");
var powershell = report.Components.FirstOrDefault(c => c.Id == "powershell");
var dotnet = report.Components.FirstOrDefault(c => c.Id == "dotnet");
var python = report.Components.FirstOrDefault(c => c.Id == "python");
var docker = report.Components.FirstOrDefault(c => c.Id == "docker");
// GetRecommendedShell 中再 3 次 (wsl/git/powershell)
```

**改造方案**：
- `EnvironmentReport` 或 `ComponentScoreCollection` 封装 `FrozenDictionary<string, ComponentScore>` by Id，暴露 `GetComponent(id)` 方法
- 收益：9 次 O(n) → 9 次 O(1)；n=7 固定小，模式典型但绝对收益有限

#### 15. llm\reasoning\Engine\ReasoningEngine.cs:580

**原始代码**：
```csharp
foreach (var result in results) {
    ...
    _dag.Nodes.Values.FirstOrDefault(n => n.Payload.SourceUrl == result.Url);
```

**改造方案**：
- 方法入口建 `Dictionary<string, DagNode<ReasoningPayload>>` by SourceUrl（仅非空 SourceUrl 节点）
- 或 Dag 暴露 `GetNodesByPayloadField` 泛型索引（通用性更好但复杂度高，暂用方法入口局部字典）
- 建字典时机：方法入口处（验证期间 DAG 不再增删节点，仅改 TrustLevel/Version，集合稳定）
- 收益：O(results × nodes) → O(results)

#### 16. llm\agents\Services\Support\AgentDefinitionProvider.cs:97

**原始代码**：
```csharp
var definitions = await GetAgentDefinitionsAsync(...);
return definitions.FirstOrDefault(d => d.Role == role && d.Variant == variant);
```

**改造方案**（单一数据源）：
- `AgentDefinitionProvider` 缓存构建时同步建 `FrozenDictionary<(AgentRole, ExecutorVariant?), AgentDefinition>`
- 暴露 `GetDefinition(role, variant)` 方法，消费方调方法
- ClearCache 时同步重建
- 收益：agent 启动热路径 O(n) → O(1)；定义数量约 10-20 个

#### 17. app\cli\core\commands\slash\SlashCommandExecutors.cs:183

**原始代码**：
```csharp
var catalog = new GeneratedSlashCommandSchemaCatalog();
var entry = catalog.AllSchemas.FirstOrDefault(e => string.Equals(e.CommandName, cmdName, StringComparison.OrdinalIgnoreCase));
```

**改造方案**（单一数据源）：
- `GeneratedSlashCommandSchemaCatalog` 内部建静态 `FrozenDictionary<string, SlashCommandSchemaEntry>` by CommandName
- 暴露 `TryGetSchema(name, out var schema)` 方法
- 需同步改 gen 侧生成器生成的 GetSchema 方法（生成器已生成 GetSchema 方法但内部也是 foreach 线性查找）
- 收益：静态数据 O(n) → O(1)；30-50 个命令

#### 18-19. gen\mcp_tool_dispatch.generator\CommandRegistrationGenerator.cs（2 处）

| # | 行号 | 原始代码 |
|---|------|----------|
| 18 | :55 | 同一 `attr.NamedArguments` 上 8 次 `FirstOrDefault(n => n.Key == "Name"/"Category"/"Description"/"Usage"/"ArgumentHint"/"IsHidden"/"IsEnabled"/"Aliases")` |
| 19 | :130 | 同一 `a.NamedArguments` 上 7 次 `FirstOrDefault(n => n.Key == "Type"/"Description"/"Required"/"Default"/"ItemsType"/"ItemsDescription"/"Enum")` |

**改造方案**：
- 提取 `BuildNamedArgsDictionary(attr)` 辅助方法，返回 `Dictionary<string, TypedConstant>`
- 后续 TryGetValue 查找
- 收益：8/7 次线性 → 1 次建字典 + 8/7 次 O(1)

#### 20-21. gen\cli_option.generator\CliOptionGenerator.cs（2 处）

| # | 行号 | 原始代码 |
|---|------|----------|
| 20 | :241 | `var positiveOpt = enumInfo.Options.FirstOrDefault(o => !o.IsNegation && o.LongName == "--" + opt.LongName.Substring(5));` 在 foreach 内 |
| 21 | :391 | `var targetOpt = enumInfo.Options.FirstOrDefault(o => o.LongName == opt.AliasOf);` 在 foreach 内 |

**改造方案**：
- `EnumInfo.Options` 封装 `FrozenDictionary<string, Option>` by LongName，暴露 `GetByLongName(name)` 方法
- 或循环前局部建字典
- 收益：O(N²) → O(N)；CLI 枚举选项数通常几十个

---

### 批次3：低收益 9 处

#### 22-25. lib\guard\security\power_shell\core\PsPermissions.cs（4 处）

| # | 行号 | 规则列表 | 原始代码模式 |
|---|------|----------|--------------|
| 22 | :289 | foreach rules | `foreach (var rule in rules) { var ruleLower = rule.Trim().ToLowerInvariant(); if (ruleLower == cmdLower \|\| ruleLower == firstWord) { return rule; } ... }` |
| 23 | :366 | foreach denyRules | `foreach (var rule in denyRules) { var ruleLower = rule.Trim().ToLowerInvariant(); if (cmdName == ruleLower \|\| canonical == ruleLower) { ... return; } }` |
| 24 | :377 | foreach askRules | 同上 |
| 25 | :388 | foreach allowRules | 同上 |

**改造方案**：
- 配置加载时预建 `FrozenDictionary<string, string>` by ruleLower（及 canonical）
- static 方法需改 API 传入预建索引对象
- 收益：规则列表小（几十条），绝对收益有限；每个 PowerShell 命令的每个子命令都遍历三个规则列表

#### 26-28. app\gui\view_models\MainViewModel.cs（3 处 UI 小集合）

| # | 行号 | 集合 | 查找数 | 原始代码 |
|---|------|------|--------|----------|
| 26 | :581 等 | _connectionOptions | 5 处 | `SelectedConnection = _connectionOptions.FirstOrDefault(c => c.Id == session.CurrentVendor) ?? _connectionOptions.FirstOrDefault();` |
| 27 | :579 等 | ModelOptions | 8 处 | `_selectedModelOption = ModelOptions.FirstOrDefault(m => m.Id == _session.CurrentModelId);` |
| 28 | :125 | HotkeyItems | 1 处 | `foreach (var h in HotkeyItems) if (h.ActionKey == actionKey) return h.Gesture;` |

**改造方案**：
- ObservableCollection 重建时同步维护 Dictionary
- 收益：UI 层小集合（<10 到 <50），收益低，维护同步字典增加复杂度；集合在供应商切换时频繁重建

#### 29-30. gen（2 处 NamedArguments 少次查找）

| # | 行号 | 查找次数 | 原始代码 |
|---|------|----------|----------|
| 29 | gen\enum_metadata.generator\EnumMetadataGenerator.cs:96 | 4 次 | `var example = subCmdAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Example").Value.Value as string;` 等 4 次 |
| 30 | gen\mcp_tool_dispatch.generator\ServiceRegistrationGenerator.cs:658 | 2 次 | `var pathArg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "ConfigurationPath").Value;` 等 2 次 |

**改造方案**：同 18-19，提取 BuildNamedArgsDictionary 辅助方法

---

## 四、不可转位置分析汇总

### lib 目录（871 处不可转）

| 原因 | 占比 | 说明 |
|------|------|------|
| 聚合/转换/副作用/构建新对象 | ~45% | foreach 做 Sum/Count/累加、Select/构建列表、写入字典/文件/日志 |
| 模糊匹配 | ~15% | StartsWith/Contains/glob/正则/前缀匹配，字典 key 无法表达 |
| 集合每次重新构造 | ~15% | LINQ 结果、文件读取、AST 解析、`_interfaceProvider()` 动态获取，无法稳定预建 |
| AST/树/图结构遍历 | ~10% | 遍历子节点/边/邻接表做结构处理，遍历本身就是目的 |
| 无参数 FirstOrDefault | ~5% | `.FirstOrDefault()` 取第一个元素，非按 key 查找 |
| 复杂多字段/复杂条件 | ~5% | 双字段 AND、bool 属性过滤、`!IsNullOrEmpty` 条件、别名解析 |
| test/mock/fixture 代码 | ~3% | 非生产路径（ClockPromptTemplatesTests、StateServiceTests 等） |
| 已用字典/配置写入 | ~2% | 已用 TryGetValue，或正在构建字典（`dict[key]=value`） |

**重点**：838 个 foreach 中仅 56 个进入候选（foreach 内含 `if ==` + `return/break`），其中 52 个因聚合/模糊匹配/AST遍历/集合临时构造等排除。40 个 FirstOrDefault 中 37 个因无参数/复杂条件/动态集合/每次重新构造/test 代码排除。

### kit 目录（49 个 FirstOrDefault 不可转 + ~802 个 foreach 不可转）

FirstOrDefault 不可转原因分布：

| 原因 | 数量 | 典型示例 |
|------|------|----------|
| 无条件取第一个 `FirstOrDefault()` | 15 | `results.FirstOrDefault()?.Content`、`s.Tags.FirstOrDefault()`、`sessionIds.FirstOrDefault()` |
| 单次查找且集合每次重新构造 | 18 | `DetectInstalledIdes().FirstOrDefault(i => i.Type == ideType)`、`GetModelsForProvider(provider).FirstOrDefault(m => m.Id == ...)` |
| 复杂多字段/条件匹配 | 6 | `templates.FirstOrDefault(t => t.Id == x \|\| t.ToolName == x)`、`defenses.FirstOrDefault(d => d.Safety.Rejection is not null)` |
| 按状态/标志查找（非唯一键） | 5 | `_plans.Values.FirstOrDefault(p => p.IsInPlanMode)`、`_entries.Values.FirstOrDefault(e => e.Status == Building)` |
| 集合动态变化（Add/Remove 频繁） | 3 | `data.AuthConfigs.FirstOrDefault(x => x.AuthName == ...)` 后紧跟 Remove/Add |
| 集合元素极少（1-2 个） | 2 | `result?.Content?.FirstOrDefault(c => c.Type == ToolContentType.Text)` |

foreach 不可转原因分布：

| 原因 | 占比 | 说明 |
|------|------|------|
| 遍历输出/渲染（副作用） | ~45% | `foreach (var day in data.DailyUsage) WriteLine(...)` |
| 遍历聚合/转换 | ~30% | `foreach (var param in parameters) _variables[param.Key] = param.Value` |
| 遍历键值对执行操作 | ~15% | `foreach (var (clientId, client) in clients) await client.ConnectAsync()` |
| 已用字典/已优化 | ~8% | `ChatCommandRegistry` 已用 `CategorizedRegistry` 字典查找 |
| 可转字典（本次发现） | ~2% | 上述 5 处 |

### server 目录（276 处不可转）

| 原因 | 数量 | 典型位置 |
|------|------|----------|
| AST 子节点遍历递归（`node.NamedChildren`/`node.Children`） | ~85 | CSharpSymbolExtractor/CSharpDependencyExtractor/BashAstParser 全部 foreach |
| 构建/更新/删除字典（`dict[key]=value`/`dict.Remove`） | ~45 | SymbolIndex.cs InsertSymbolsInternal/RemoveFileInternal |
| 聚合/转换/截断（Sum/Count/Select/TokenBudget） | ~40 | CodeIndexer.cs:336/365/378、GraphAnalytics 全部 |
| 副作用（注册/打印/释放/排序） | ~35 | LspPassiveFeedback:44、TreeCache:127/142、DreamCommand:175 |
| BFS/DFS 图遍历（队列/栈驱动） | ~20 | CallGraph.cs:138/169、DependencyGraph.cs:119 |
| 全量搜索遍历（模糊/正则匹配，遍历即目的） | ~15 | SymbolSearcher.cs:41/125/198 |
| 文件/目录枚举遍历 | ~12 | CodeIndexer.cs:479/493/506、ISessionScanner:64 |
| 范围匹配（line range，字典 key 无法表达） | 1 | CSharpCallExtractor.cs:586 FindCallerFqn |
| 浮点近似匹配 + 固定小集合 | 1 | MeasurementToolHandlers.cs:183 commonRatios |
| FirstOrDefault 无谓词（取第一个） | 5 | LspFileSync:104、ChatCompletionClient:29 |
| FirstOrDefault 按动态状态查找 + 小集合 | 4 | LspService.cs:272/273/364/390 |
| FirstOrDefault AST 子节点极小集合 | 4 | CSharpDependencyExtractor:315/533/550、CSharpSymbolExtractor:583 |
| FirstOrDefault 启动路径/单次查找 | 3 | ServiceRegistration:13、CsprojParser:67 |
| FirstOrDefault 小集合单次查找 | 1 | MessageHandlers:297 |

### llm 目录（287 处不可转）

| 原因 | 数量 | 典型位置 |
|------|------|----------|
| 副作用（写入字典/清理/Cancel/Dispose/注册/发送消息） | ~130 | JudgeAgent、ReasoningAgent 多处 |
| 转换/构建新对象（JSON 解析/格式化/构建任务列表/克隆） | ~80 | |
| 流式消费（`await foreach` 消费 channel/stream） | ~20 | |
| 遍历枚举/固定小数组/字符序列 | ~15 | `Enum.GetValues`、`params string[] keys` fallback |
| 聚合/累加（Sum/Count/累加计数器） | ~15 | |
| 已用字典查找（循环内 `TryGetValue`） | ~12 | AgentRoleProfileRegistry、PluginAgentLoader |
| 测试代码（状态机遍历/断言/枚举排列） | 37 | |
| 集合每次重新构造 | 3 | API 返回/LINQ 结果 |
| 集合动态变化 | 2 | JudgeAgent `action.Verdicts` 循环内 Add |
| 无条件取首个 | 7 | |
| 模糊匹配 | 1 | `p.Content.Contains(claimContent)` |
| 反向查找 | 1 | TeamManager 按 Value 查 Key |

**结论**：`llm` 目录整体字典化程度较高（`AgentRoleProfileRegistry`、`PluginAgentLoader`、`TeamManager`、`PatientProcessManager` 等核心查找已用 `TryGetValue`）。

### app 目录（183 处不可转）

| 原因 | 数量 | 典型位置 |
|------|------|----------|
| 转换/构建新对象（遍历填充集合） | ~60 | JccChatSession.cs:660, SessionTree.cs:136, GuiSessionStore.cs:79 |
| 副作用（打印/注册/写入/事件触发） | ~40 | McpCommand.cs:72, TuiModeRunner.cs:103, CliSession.cs:217 |
| 聚合（构建字符串/计数/找最值） | ~20 | RgSubCommand.cs:399, MainViewModel.cs:281 |
| 字符遍历（字符串处理） | ~15 | InputSanitizer.cs:52, SessionIdGenerator.cs:53 |
| 无参数 FirstOrDefault()（取第一个） | 9 | MainViewModel.cs 多处 |
| 流式处理 await foreach | 8 | SessionController.cs:98, ReplLoopStep.cs:199 |
| 建字典本身（foreach 填充 Dictionary） | 5 | GhCommandResolver.cs:190, McpCommand.cs:311 |
| 条件/前缀/模式匹配 | 5 | ApiKeyRedLine.cs:42, RgEngine.cs:148 |
| bool 标志查找 | 2 | MainViewModel.cs:1242(IsSelected), PipeRegistry.cs:18(IsMain) |
| JSON 枚举每次重建 | 2 | DotEnvConfig.cs:59,90 |
| UI 树查找 | 1 | SlashPaletteView.axaml.cs:141 |

**关键发现**：app 目录整体字典化程度已较高（GhCommandResolver、ColorMapper、SlashCommandTrie 已主动建字典）。

### gen 目录（282 处不可转）

| 原因 | 数量 | 说明 |
|------|------|------|
| Roslyn 符号遍历（GetMembers/GetAttributes/NamedArguments 单次查找） | ~150 | 源码生成器标准模式，集合每次不同无法预建 |
| 代码生成输出（sb.AppendLine + foreach 构建字符串） | ~60 | foreach 目的是生成 C# 源码文本 |
| 聚合/转换/副作用（List.Add、赋值、累加） | ~40 | foreach 做构建新对象或收集结果 |
| 单次 FirstOrDefault 查找（不值得建字典） | ~35 | 在 GetAttributes() 上查找特定 AttributeClass |
| 测试代码 | ~12 | aot_safety.tests / fsm.generator.tests |
| 取第一个元素 | ~5 | ConstructorArguments.FirstOrDefault()、Locations.FirstOrDefault() |
| 字符串字面量中的 foreach | ~5 | sb.AppendLine("foreach ...") 生成代码文本 |

**关键观察**：gen 目录绝大多数 foreach 是 Roslyn 语法树遍历，属于编译时分析的标准模式，集合每次调用都不同且无法稳定预建字典。

---

## 五、补充发现（.Any() 线性查找，不在本次扫描范围但值得关注）

```
[值得关注] llm\reasoning\Engine\ReasoningEngine.cs:85
模式: foreach (var item in assumptions) { if (_dag.Nodes.Values.Any(n => n.Payload.Content == item.Content && n.Payload.State != DataState.Rejected)) throw ...; }
说明: 在 foreach item 循环内用 .Any() 按 Content 线性扫描全部 DAG 节点做去重检查，O(assumptions × nodes)。
      可预建 HashSet<string> by Content（非 Rejected 状态），但条件含动态 State（节点状态会变 Rejected），
      需在状态变更时同步维护索引，复杂度较高。暂列仅供参考。
```

---

## 六、批次建议与进度跟踪

### 批次建议

- **批次1（高收益 10 处）**：server CSharpCallExtractor 6 处（O(N×M)→O(N×K) 递归热路径）+ kit 4 处（O(n²)→O(n) 循环内查找）
- **批次2（中收益 11 处）**：lib Dag 边索引 3 处 + kit EnvironmentProbe + llm 2 处 + app SlashCatalog + gen 4 处
- **批次3（低收益 9 处）**：lib PsPermissions 4 处 + app UI 3 处 + gen 2 处

### 进度跟踪

| 批次 | 总数 | 已完成 | 状态 |
|------|------|--------|------|
| 批次1 高收益 | 10 | 10 | ✅ 完成 |
| 批次2 中收益 | 11 | 10 | ✅ 完成（#17 已是字典，无需改） |
| 批次3 低收益 | 9 | 8 | ✅ 完成（#28 HotkeyItems 极小集合 foreach 最优保留） |
| **合计** | **30** | **28** | **28 处已改，2 处评估后跳过** |

### 跳过原因

| 位置 | 原因 |
|------|------|
| #17 SlashCommandExecutors.cs:183 | 已是字典查找（生成器已生成 `_entryByName` FrozenDictionary + `GetEntry` O(1)） |
| #28 MainViewModel.cs HotkeyItems | 极小集合（~6 项），foreach 是最优解，改字典反优化 |

### 改造记录

（每完成一处在此追加一行：日期 + 位置 + 改造内容 + commit hash）

| 日期 | 位置 | 改造内容 | commit |
|------|------|----------|--------|
| 2026-09-14 | CSharpCallExtractor.cs 6 处 | 新建 SymbolIndex 类，封装 TryGetByFqn/GetByName，改造 CollectCallsOptions/ResolveCalleeOptions 签名 | a387b6762 |
| 2026-09-14 | ReferenceIndexCompressor.cs | HashSet 替代去重 O(n²)→O(n) | 14434e7ec |
| 2026-09-14 | WorktreeCommand.cs | ToLookup 替代嵌套查找 O(n×m)→O(n+m) | 14434e7ec |
| 2026-09-14 | SkillDefinition.cs + SkillExecutor.cs + SkillExecutionMiddleware.cs | BuildStepIndex 封装步骤索引，while 循环 O(n²)→O(n) | 14434e7ec |
| 2026-09-14 | Dag.cs + ConcurrentDag.cs + TaskService.cs + TaskRuntime.cs | Dag 加 (FromId,ToId) 复合索引 TryGetEdge O(E)→O(1) | 1cdf5dc57 |
| 2026-09-14 | EnvironmentProbeService.cs | ToLookup by Id 替代 9 次线性查找 | 66c24d443 |
| 2026-09-14 | CommandRegistrationGenerator.cs | 提取 GetNamedArg/GetNamedArgString 辅助方法，15 次 NamedArguments FirstOrDefault 转字典 | 66c24d443 |
| 2026-09-14 | CliOptionGenerator.cs | ToLookup by LongName 替代 foreach 内嵌套查找 O(N²)→O(N) | 66c24d443 |
| 2026-09-14 | ReasoningEngine.cs | ToLookup by SourceUrl 替代 foreach 内全节点扫描 | 2da0dd2c2 |
| 2026-09-14 | AgentDefinitionProvider.cs | 缓存 _cachedDefinitionMap ILookup，GetAgentDefinitionAsync O(1) 查找 | 2da0dd2c2 |
| 2026-09-14 | EnumMetadataGenerator.cs | 4 次 NamedArguments FirstOrDefault 转局部字典 | 2c2e4ea4f |
| 2026-09-14 | ServiceRegistrationGenerator.cs | 2 次 NamedArguments FirstOrDefault 转局部字典 | 2c2e4ea4f |
| 2026-09-14 | PsPermissions.cs 4 处 | MatchExactRule 预建 byRuleLower/byRuleCanonical 字典；CheckSubCommandRules 用 BuildRuleLowerMap 辅助方法 | c9f4718f2 |
| 2026-09-14 | MainViewModel.cs 2 处 | _connectionById/_modelById ILookup 字段 + GetConnectionById/GetModelById 辅助方法，RebuildConnectionOptions/RefreshModelOptions 末尾建索引 | c9f4718f2 |

---

## 七、附录：各目录子代理扫描原始报告

### 附录 A：lib 目录扫描原始报告

- 扫描文件数：1962（排除 .xxx/ 归档、.g.cs/.generated.cs 生成代码）
- foreach 总数：838
- FirstOrDefault 总数：40
- 可转字典数：7（高收益 0 / 中收益 3 / 低收益 4）
  - 中收益 3 处：TaskService.cs:191/222 + TaskRuntime.cs:210（Dag 边按 FromId+ToId 复合 key 查找，同一模式 3 处重复，统一在 Dag 内建索引）
  - 低收益 4 处：PsPermissions.cs:289/366/377/388（PowerShell 规则列表线性查找，规则列表小且需改 static 方法 API）
- 不可转数：871（foreach 834 + FirstOrDefault 37）

**最高价值改造**：Dag 边查找（3 处中收益）：`D:\project\w2\lib\structura\dag\Dag.cs` 内部已有 `_adjacency`/`_reverseAdjacency` 邻接表，但缺 `(FromId,ToId)→Edge` 复合索引，新增一个字典即可让三处调用从 O(E) 降至 O(1)。

### 附录 B：kit 目录扫描原始报告

- 扫描文件数：876
- foreach 总数：807
- FirstOrDefault 总数：54（生产代码，已排除测试 8 个）
- 可转字典数：5（高收益 4 / 中收益 1 / 低收益 0）
- 不可转数：49 个 FirstOrDefault + ~802 个 foreach

**优化优先级建议**：
1. 最高优先级：`ReferenceIndexCompressor.cs:242`（O(n²)→O(n)，正则匹配数可能上千）和 `SkillExecutor.cs:73`/`SkillExecutionMiddleware.cs:56`（while 循环内 O(n²)→O(n)，技能执行热路径）
2. 高优先级：`WorktreeCommand.cs:77`（嵌套 O(n×m)→O(n+m)）
3. 中优先级：`EnvironmentProbeService.cs`（9 次查找，n=7 固定）

注：`SkillExecutor.cs` 与 `SkillExecutionMiddleware.cs` 是**同构代码**（相同 while+FirstOrDefault 模式），建议统一改造为共用步骤字典构建辅助方法，避免重复。

### 附录 C：server 目录扫描原始报告

- 扫描文件数：218 个 .cs 文件
- foreach 总数：259
- FirstOrDefault 总数：23
- 可转字典数：6（高收益 6 / 中收益 0 / 低收益 0）
- 不可转数：276（foreach 259 + FirstOrDefault 17）

$0

**核心结论**：唯一值得优化的是 `CSharpCallExtractor.cs` 中 6 处对 `symbols` 列表的 `FirstOrDefault` 线性查找。它们全部位于 `ExtractCallsFromTree` → `CollectCalls`（递归）→ `ResolveCalleeFqn`/`TryExtractEventHandlerEdge`/`FindSymbolFqn` 的调用链上，`symbols` 在整个递归过程中不变。预建两个结构（`Dictionary<string, SymbolInfo>` by Fqn + `ILookup<string, SymbolInfo>` by Name）可将 O(N×M) 降为 O(N×K)（K 为同名符号数，通常 K≤3）。

### 附录 D：llm 目录扫描原始报告

- 扫描文件数：311（其中生产代码 198，测试代码 113）
- foreach 总数：272（生产 235，测试 37）
- FirstOrDefault 总数：17（生产 16，测试 1）
- 可转字典数：2（高收益 0 / 中收益 2 / 低收益 0）
  - 中收益：`ReasoningEngine.cs:580`（证据 URL 验证热路径）、`AgentDefinitionProvider.cs:97`（agent 启动热路径）
- 不可转数：287（foreach 272 + FirstOrDefault 15）

**结论**：`llm` 目录整体字典化程度较高（`AgentRoleProfileRegistry`、`PluginAgentLoader`、`TeamManager`、`PatientProcessManager` 等核心查找已用 `TryGetValue`）。仅发现 2 处 `FirstOrDefault` 线性查找可转字典，均属中收益。未发现 `foreach { if (x.Id == target) return x; }` 典型线性查找模式——生产代码中的 foreach 绝大多数是副作用、转换或流式消费。

### 附录 E：app 目录扫描原始报告

- 扫描文件数：226
- foreach 总数：159
- FirstOrDefault 总数：29
- 可转字典数：4（高收益 0 / 中收益 1 / 低收益 3）
  - 中收益：`GeneratedSlashCommandSchemaCatalog.AllSchemas`（静态集合，需配合静态字典缓存）
  - 低收益：`_connectionOptions`(5 处)、`ModelOptions`(8 处)、`HotkeyItems`(1 处) — 均 UI 层小集合
- 不可转数：183（foreach 158 + FirstOrDefault 15）

**关键发现**：
1. app 目录整体字典化程度已较高：多处本可线性查找的代码已主动建字典（如 GhCommandResolver.cs:190 的 `byName`、ColorMapper.cs 的 `_colorCache`、SlashCommandTrie 的 `_items`），说明开发者已有字典优化意识。
2. 唯一中等收益点在生成代码侧：`GeneratedSlashCommandSchemaCatalog` 的 `AllSchemas` 是编译时静态数据，但生成器生成的 `GetSchema` 方法内部仍用 foreach 线性查找，且 app 侧 SlashCommandExecutors.cs:183 绕过该方法直接 FirstOrDefault。建议在生成器中改为静态字典（此修改在 gen 目录，非 app 目录）。
3. UI 层 ObservableCollection 不建议盲目转字典：`_connectionOptions`/`ModelOptions`/`HotkeyItems` 虽多处查找，但集合极小（<10 到 <50）且与 UI 双向绑定频繁重建，维护同步字典的复杂度与收益不成正比。

### 附录 F：gen 目录扫描原始报告

- 扫描文件数：52 个 .cs 文件
- foreach 总数：236
- FirstOrDefault 总数：69
- 可转字典数：6（高收益 0 / 中收益 4 / 低收益 2）
- 不可转数：282（foreach 236 全部不可转 + FirstOrDefault 46 不可转）

**关键观察**：gen 目录是源码生成器代码，绝大多数 foreach 是 Roslyn 语法树遍历（`namespaceSymbol.GetMembers()`、`typeSymbol.GetAttributes()`、`attr.NamedArguments` 等），属于编译时分析的标准模式，集合每次调用都不同且无法稳定预建字典。真正可受益的只有两类：(1) 同一 `NamedArguments` 集合上多次 FirstOrDefault 查找不同 key（4 处），(2) foreach 循环内对同一 `Options` 集合反复 FirstOrDefault 查找 `LongName`（2 处，N²→N 改善最显著）。
