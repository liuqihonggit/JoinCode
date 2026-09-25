# 判断条件顺序不当 — 修复任务清单

> 生成时间: 2026-09-25
> 扫描范围: `D:\project\w2` 全工程
> 排除: `*.tests/`、`non_deliverables_tools/`、`.xxx/`、`obj/`、`bin/`
> 总计: **46 处** (A类 36 + C类 3 + D类 7)

## 修复原则

- **A 类**: 翻转 `&&` 两侧顺序,简单/便宜条件前置,耗时条件后置,利用短路求值先过滤
- **C 类**: 翻转 `.Where()` 谓词内条件顺序,简单谓词前置
- **D 类**: 循环外缓存不变量,循环内复用;或提为 `static readonly` 字段
- 每处修复后编译 + 快速冒烟测试
- 按区域渐进式提交,每次一个区域

---

## 全部修改点位

### A 类: if/when 耗时条件在前 (36 处,含 4 弱)

| # | 路径 | 行 | 当前代码 (耗时 && 简单) | 建议改为 (简单 && 耗时) | 热度 |
|---|------|----|------------------------|------------------------|------|
| A01 | `lib\guard\security\power_shell\core\PsSecurityChecker.cs` | 448 | `DeriveSecurityFlags(parsed).HasAssignments && envVars.Count > 0` | 删除冗余 Count(第440行已早返回恒为true);或 `envVars.Count > 0 && DeriveSecurityFlags(parsed).HasAssignments` | ★★★ |
| A02 | `kit\hands\skills\skills2\core\VariableResolver.cs` | 42 | `result.Contains("{{") && iterations < maxIterations` | `iterations < maxIterations && result.Contains("{{")` | ★★ |
| A03 | `kit\hands\skills\skills2\core\VariableResolver.cs` | 245 | 同 A02 | 同 A02 | ★★ |
| A04 | `kit\brain\context\compression\strategies\CodeContentCompressor.cs` | 96 | `IsDocumentationComment(line) && options.PreserveDocumentation` | `options.PreserveDocumentation && IsDocumentationComment(line)` | ★★ |
| A05 | `kit\brain\context\compression\strategies\CodeContentCompressor.cs` | 101 | `IsComment(line) && options.PreserveComments` | `options.PreserveComments && IsComment(line)` | ★★ |
| A06 | `kit\brain\context\compression\strategies\CodeContentCompressor.cs` | 108 | `IsTypeDefinition(line) && options.PreserveTypeDefinitions` | `options.PreserveTypeDefinitions && IsTypeDefinition(line)` | ★★★ |
| A07 | `kit\brain\context\compression\strategies\CodeContentCompressor.cs` | 114 | `IsEnumDefinition(line) && options.PreserveEnums` | `options.PreserveEnums && IsEnumDefinition(line)` | ★★★ |
| A08 | `kit\brain\context\compression\strategies\CodeContentCompressor.cs` | 120 | `IsConstantDefinition(line) && options.PreserveConstants` | `options.PreserveConstants && IsConstantDefinition(line)` | ★★★ |
| A09 | `llm\core\Adapters\LLM\QueryServices\OpenAI\OpenAIQueryService.cs` | 35 | `firstContent.Contains("tool_description_request") && kernel != null` | `kernel != null && firstContent.Contains("tool_description_request")` | ★★★ |
| A10 | `llm\core\Adapters\LLM\QueryServices\Anthropic\AnthropicQueryService.cs` | 32 | 同 A09 | 同 A09 | ★★★ |
| A11 | `llm\agents\Coordinator\Backend\TmuxPaneBackend.cs` | 209 | `hex.StartsWith('#') && hex.Length == 7` | `hex.Length == 7 && hex.StartsWith('#')` | ★★ |
| A12 | `llm\reasoning\Cone\RoleCone.cs` | 72 | `\|\|`链: `Contains(trigger) \|\| trigger == "*"` | `trigger == "*" \|\| Contains(trigger)` | ★ (弱) |
| A13 | `llm\agents\Coordinator\Core\Lifecycle\AgentMcpServerManager.cs` | 186 | `!IsNullOrWhiteSpace(config.AuthName) && _authConfigProvider != null` | `_authConfigProvider != null && !IsNullOrWhiteSpace(config.AuthName)` | ★ (弱) |
| A14 | `lib\abstractions\abs_guard\security\shell\bash\BashSafeWrapperStripper.cs` | 62 | `Regex.IsMatch(arg) && i + 1 < a.Length` | `i + 1 < a.Length && Regex.IsMatch(arg)` | ★★★ |
| A15 | `lib\abstractions\abs_guard\security\shell\bash\BashSemanticChecker.cs` | 131 | `dangerFlags.Contains(arg) && i + 1 < a.Length && a[i+1].Contains('[')` | `i + 1 < a.Length && dangerFlags.Contains(arg) && a[i+1].Contains('[')` | ★★★ |
| A16 | `lib\abstractions\abs_guard\security\shell\bash\BashSemanticChecker.cs` | 137 | `arg.StartsWith(flag) && arg.Length > flag.Length && arg.Contains('[')` | `arg.Length > flag.Length && arg.StartsWith(flag) && arg.Contains('[')` | ★★ |
| A17 | `lib\abstractions\abs_guard\security\shell\bash\BashSecurityConstants.cs` | 67 | `a[i].StartsWith('-') && a[i].Length > 1` | `a[i].Length > 1 && a[i].StartsWith('-')` | ★★ |
| A18 | `lib\abstractions\abs_guard\security\shell\bash\BashSecurityConstants.cs` | 80 | `a[i].StartsWith('-') && a[i].Length > 1 && a[i][1] != '-'` | `a[i].Length > 1 && a[i].StartsWith('-') && a[i][1] != '-'` | ★★ |
| A19 | `lib\abstractions\abs_guard\security\shell\core\SedValidation.cs` | 248 | `token.StartsWith('-') && token.Length > 1 && !token.StartsWith("--")` | `token.Length > 1 && token.StartsWith('-') && !token.StartsWith("--")` | ★★ |
| A20 | `lib\abstractions\abs_guard\security\shell\core\SedValidation.cs` | 591 | `expr.StartsWith('s') && expr.Length > 1 && expr[^1] is...` | `expr.Length > 1 && expr.StartsWith('s') && expr[^1] is...` | ★★ |
| A21 | `lib\abstractions\abs_core\core_utils\core\path\PathConverter.cs` | 52 | `normalized.StartsWith("//") && normalized.Length > 2` | `normalized.Length > 2 && normalized.StartsWith("//")` | ★ |
| A22 | `lib\abstractions\abs_core\core_utils\core\path\PathConverter.cs` | 71 | `path.StartsWith("//") && path.Length > 2 && path[2] != '/'` | `path.Length > 2 && path.StartsWith("//") && path[2] != '/'` | ★★ |
| A23 | `lib\plugins.contracts\session\SessionEvent.cs` | 220 | `knownTypes is not null && !knownTypes.Contains(evt.Type) && !evt.Ignorable` | `knownTypes is not null && !evt.Ignorable && !knownTypes.Contains(evt.Type)` | ★★ |
| A24 | `lib\infrastructure\hot_spot\HotFileDetector.cs` | 130 | `fileName.StartsWith("I") && extension.Equals(".cs") && fileName.Length > 1 && char.IsUpper(fileName[1])` | `fileName.Length > 1 && extension.Equals(".cs") && fileName.StartsWith("I") && char.IsUpper(fileName[1])` | ★★ |
| A25 | `server\code_index\analytics\GraphAnalytics.cs` | 623 | `symbol.Name.StartsWith("On") && symbol.Kind == SymbolKind.Method` | `symbol.Kind == SymbolKind.Method && symbol.Name.StartsWith("On")` | ★★ |
| A26 | `app\cli\pipe\code_session\CodeSessionApiHandler.cs` | 165 | `p.StartsWith("/code-sessions/") && method == "GET"` | `method == "GET" && p.StartsWith("/code-sessions/")` | ★★★ |
| A27 | `app\cli\pipe\code_session\CodeSessionApiHandler.cs` | 169 | `p.StartsWith("/code-sessions/") && method == "DELETE"` | `method == "DELETE" && p.StartsWith("/code-sessions/")` | ★★★ |
| A28 | `server\bridge\session\main\core\BridgeRunOrchestrator.cs` | 374 | `string.Equals(userType,"ant",OrdinalIgnoreCase) && !IsNullOrEmpty(ingressOverride)` | `!IsNullOrEmpty(ingressOverride) && string.Equals(userType,"ant",OrdinalIgnoreCase)` | ★★ |
| A29 | `server\bridge\session\main\core\BridgeMain.Helpers.cs` | 182 | 同 A28 | 同 A28 | ★★ |
| A30 | `server\bridge\transport\shared\BridgeInit.cs` | 225 | 同 A28 | 同 A28 | ★★ |
| A31 | `server\eyes\lsp\server\LspServerInstance.cs` | 303 | `IsContentModifiedError(ex) && attempt < MaxRetries` | `attempt < MaxRetries && IsContentModifiedError(ex)` | ★★★ |
| A32 | `server\code_index\parsing\CSharpCallExtractor.cs` | 154 | `classNameSet.Contains(typeName) \|\| (typeName.Length > 0 && char.IsUpper(typeName[0]))` | `(typeName.Length > 0 && char.IsUpper(typeName[0])) \|\| classNameSet.Contains(typeName)` | ★★★ |
| A33 | `server\code_index\parsing\CSharpCallExtractor.cs` | 592 | 同 A32 | 同 A32 | ★★★ |
| A34 | `app\cli\core\commands\core\GhCommandResolver.cs` | 71 | `!token.Contains('=') && i + 1 < args.Length && !args[i+1].StartsWith("--")` | `i + 1 < args.Length && !token.Contains('=') && !args[i+1].StartsWith("--")` | ★ (弱) |
| A35 | `app\tui\core\tui\rendering\ColorMapper.cs` | 40 | `rgb.StartsWith('#') && rgb.Length >= 7` | `rgb.Length >= 7 && rgb.StartsWith('#')` | ★ (弱) |
| A36 | `gen\aot_safety.generator\rules\aot_safety\JsonSerializerAotRule.cs` | 171 | `IsJsonSerializerOptionsType(property.ContainingType) && property.Name == "TypeInfoResolver"` | `property.Name == "TypeInfoResolver" && IsJsonSerializerOptionsType(property.ContainingType)` | ★★ |

### C 类: 管道 Where/Filter 中耗时谓词在前 (3 处,含 1 弱)

| # | 路径 | 行 | 当前代码 | 建议改为 | 热度 |
|---|------|----|----------|----------|------|
| C01 | `lib\vault\memdir\sync\core\TeamMemorySyncService.cs` | 286 | `.Where(e => e.FilePath.StartsWith(teamPath,...) && e.Type == SyncEventType.ConflictDetected)` | `.Where(e => e.Type == SyncEventType.ConflictDetected && e.FilePath.StartsWith(teamPath,...))` | ★★ |
| C02 | `lib\vault\memdir\memdir2\core\MemoryStore.cs` | 212 | `.Where(m => m.IsExpired() && !m.IsArchived)` | `.Where(m => !m.IsArchived && m.IsExpired())` | ★★ |
| C03 | `llm\reasoning\Agents\ReasoningContext.cs` | 53 | `.Where(item => visibleSourceIds.Contains(item.Id) \|\| item.State is Assumption or PendingEvidence)` | `.Where(item => item.State is Assumption or PendingEvidence \|\| visibleSourceIds.Contains(item.Id))` | ★ (弱) |

### D 类: 循环内重复求值 (7 处)

| # | 路径 | 行 | 问题 | 建议修复 | 热度 |
|---|------|----|------|----------|------|
| D01 | `lib\guard\security\power_shell\core\PsSecurityChecker.cs` | 70,83,96,107,126,163,229,256,269,291,317,355,364,442,457,469,485 | `GetAllCommands(parsed)` 重复调用 **17 次**,每次 `new List + AddRange` 分配+拷贝 | 在 `CommandIsSafe` 入口缓存 `var allCommands = PsAstParser.GetAllCommands(parsed);` 一次,传入各检查器复用 | ★★★ |
| D02 | `lib\guard\security\power_shell\core\PsSecurityChecker.cs` | 351,382,391,400,409,418,448 | `DeriveSecurityFlags(parsed)` 重复调用 **7 次**,每次遍历 AST 全部 SecurityPatterns | 在 `CommandIsSafe` 入口缓存 `var flags = PsAstParser.DeriveSecurityFlags(parsed);` 一次,传入各检查器复用 | ★★★ |
| D03 | `kit\brain\context\compression\strategies\ReferenceIndexCompressor.cs` | 140 | `IsFilePathLine` 内 `var filePatterns = new[]{...}` 每次调用重建(循环内第113行调用) | 提为 `static readonly string[]` 字段 | ★★ |
| D04 | `kit\brain\context\compression\strategies\ReferenceIndexCompressor.cs` | 164 | `IsIdentifierLine` 内 `var identifierPatterns = new[]{...}` 同上(循环内第119行调用) | 提为 `static readonly string[]` 字段 | ★★ |
| D05 | `kit\brain\context\compression\strategies\ReferenceIndexCompressor.cs` | 186 | `IsReferenceLine` 内 `var referencePatterns = new[]{...}` 同上(循环内第121行调用) | 提为 `static readonly string[]` 字段 | ★★ |
| D06 | `lib\abstractions\abs_guard\security\shell\bash\BashSemanticChecker.cs` | 137 | 内层 `foreach(flag)` 中 `arg.Contains('[')` 不依赖循环变量 `flag`,每次迭代重复计算 | 内层循环外提取 `var hasBracket = arg.Contains('[');`,循环内改用 `hasBracket` | ★★ |
| D07 | `lib\abstractions\abs_guard\security\shell\bash\BashSemanticChecker.cs` | 145 | 内层 `foreach(flag)` 中 `i+1<a.Length` 和 `a[i+1].Contains('[')` 不依赖 `flag`,每次迭代重复计算 | 内层循环外提取 `var hasNext = i + 1 < a.Length;` 和 `var nextHasBracket = hasNext && a[i + 1].Contains('[');`,循环内改用 | ★★ |

---

## TOP 5 最值得改造 (按收益排序)

| 排名 | 编号 | 位置 | 类型 | 收益说明 |
|------|------|------|------|----------|
| 1 | D01 | `PsSecurityChecker.cs` — `GetAllCommands` ×17 | D | 每次 `new List + AddRange`,17 次冗余分配+拷贝。缓存一次消除 16 次,PowerShell 安全检查热路径 |
| 2 | A06-A08 | `CodeContentCompressor.cs:108,114,120` | A | 代码压缩 `for` 循环遍历每行跑正则;配置 `PreserveXxx=false` 时全浪费,翻转后零正则开销 |
| 3 | D02 | `PsSecurityChecker.cs` — `DeriveSecurityFlags` ×7 | D | 每次遍历 AST 全部 SecurityPatterns,7 次冗余。缓存一次消除 6 次遍历 |
| 4 | A31 | `LspServerInstance.cs:303` | A | LSP 重试热路径,方法调用(内含 `Data.Contains`+类型检查)在前,整数比较在后;最后一次重试白算 |
| 5 | A32-A33 | `CSharpCallExtractor.cs:154,592` | A | 代码索引遍历大量 AST 节点,HashSet 查找在前,单字符判断在后;camelCase 变量占多数可短路 |

---

## 建议修复顺序

按区域渐进式提交,每区域编译+冒烟后提交一次:

1. **lib\guard + lib 其余** (A01, A14-A24, C01-C02, D01-D02, D06-D07) — 18 处,安全守卫热点
2. **kit** (A02-A08, D03-D05) — 10 处,上下文压缩热点
3. **server + app** (A25-A35) — 11 处,代码索引/桥接/LSP
4. **llm** (A09-A13, C03) — 6 处,LLM 适配器
5. **gen** (A36) — 1 处,源码生成器

> 注: D01/D02 需要修改 `PsSecurityChecker.cs` 的方法签名(给检查器传入缓存的 `allCommands`/`flags`),影响面较大,建议单独一个 commit 并充分测试。
