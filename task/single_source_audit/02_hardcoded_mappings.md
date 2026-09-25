# 硬编码映射表违规审查报告

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3`
**审查范围**: lib/、kit/、server/、gen/、app/、llm、tool/(排除 test/、tests/)
**总命中**: 87 处 | **高违规**: 0 | **中违规**: 19 | **低违规**: 68

> 本工程已大规模采用 [EnumValue] + 源码生成器,大部分静态字典已委托枚举常量。无"明显应委托却硬编码"的高违规项。

---

## 一、中违规清单(19 处,可能应委托)

> **改造状态**: ✅ 全部完成(P3-13~19) | 详见 00_summary.md

### 命令参数 Enum 字面量(5 处) — ✅ P3-13 完成
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| kit\slash\agents\agent\MemoryCommand.cs:7 | `Enum = new[] { "edit", "open", "add", "search", "db", "stats", "health", "cleanup" }` | 提取 MemoryActionEnum + [EnumValue] | ✅ 委托 MemorySubCommandEnumConstants |
| kit\slash\guard\config\ConfigCommand.cs:8 | `Enum = new[] { "get", "set", "list", "remove" }` | 委托 CrudActionEnumConstants(同文件第50行已用,双套定义) | ✅ 新建 ConfigAction 枚举委托 |
| kit\slash\hands\code\DiffCommand.cs:7 | `Enum = new[] { "files", "cached" }` | 提取 DiffScopeEnum | ✅ 委托 DiffModeEnumConstants |
| server\vision\tool_handlers\QuadtreeToolHandlers.cs:185 | `EnumValues = new[] { "N","S","W","E","NW","NE","SW","SE" }` | 提取 CompassDirectionEnum | ✅ 委托 CardinalDirectionEnumConstants |
| app\cli\core\commands\core\Program.cs:249-250 | `validPermissionModes = new[] { "plan","auto","ask","bypass" }` / `validFormats = new[] { "text","json","ndjson" }` | 委托 PermissionMode 枚举 / 新建 OutputFormatEnum | ✅ 委托 PermissionModeEnumConstants + 新建 OutputFormat 枚举 |

### 文件扩展名/语言映射(3 处) — ✅ P3-14 完成(2处,LspFileSync语义不同保留)
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| lib\vault\memdir\services\SessionScanner.cs:14 | ExtensionToLanguage 字典(20 项 .cs→C# 等) | 提取 LanguageMapCatalog | ✅ 委托 LanguageMapCatalog.ExtensionToLanguage |
| kit\brain\context\resolution\ReferenceResolver.cs:16 | DirectoryAliases 字典(17 项 工具→[tools,tool,...]) | 提取 DirectoryAliasCatalog | ⚠️ 语义不同(目录别名),保留 |
| kit\brain\context\resolution\ReferenceResolver.cs:37 | ExtensionPatterns 字典(13 项 .cs→[*.cs]) | 与 SessionScanner 合并到 LanguageMapCatalog | ✅ 委托 LanguageMapCatalog.ExtensionToGlobs |

### 路径/目录黑名单(4 处,重复定义) — ✅ P3-15 + P3-16 完成
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| server\code_index\incremental\FileWatcherIntegration.cs:17 | `DefaultExcludedDirs = new[] { "bin","obj",".git",".x" }` | 统一 CodeIndexExcludedDirCatalog | ✅ 委托 CodeIndexExcludedDirCatalog |
| server\code_index\incremental\IncrementalUpdater.cs:11 | `ExcludedDirs = new(...) { "bin","obj",".git",".x" }` | 同上(重复) | ✅ 委托 CodeIndexExcludedDirCatalog |
| server\code_index\indexing\CodeIndexer.cs:419 | `new[] { "bin","obj",".git",".x" }` 内联 | 同上(第三处重复) | ✅ 委托 CodeIndexExcludedDirCatalog |
| non_deliverables_tools\jcc_audit_ast_cli\audit\BomStripper.cs:16,21 | ExcludedDirectories/ExcludedFilePatterns | 委托 FileFilter.s_commonExcludedDirs(双套) | ✅ P3-16 委托 FileFilter |

### 路径逃逸/危险路径(2 处) — 🟢 合理例外(安全规则,非有限集合标识)
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| lib\guard\security\services\path_validation\PathValidator.cs:9 | PathEscapePatterns 字典(13 项 ..→描述) | 提取 PathEscapeCatalog | 🟢 安全规则,键为路径模式非枚举值 |
| lib\guard\security\services\path_validation\PathValidator.cs:26 | `DangerousPathPrefixes = { "/", @"C:\", @"D:\" }` | 与上合并 | 🟢 路径前缀,非典型枚举值 |

### 安全规则/密钥标签(1 处) — 🟢 合理例外(外部 gitleaks 规则ID,随版本变化)
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| lib\abstractions\abs_guard\security\scanning\SecurityPatterns.cs:195 | RuleIdLabels 字典(27 项 aws→AWS 等) | 委托 gitleaks 规则数据源 | 🟢 外部规则ID,非项目可控枚举 |

### 参数别名/修复映射(1 处) — ✅ P3-17 完成
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| lib\abstractions\abs_core\core_utils\core\json_repair\ParameterNameRepairer.cs:8 | ParameterAliases 字典(40+ 项 file_path→filePath) | 提取 ParameterAliasCatalog | ✅ 删除KV相同恒等映射 |

### AppState 键映射(1 处) — ✅ P3-19 完成
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| lib\vault\state\store\AppStateSettingSyncService.cs:18 | s_appStateKeyMappers 字典(3 项 DebugLog→...) | 委托 ConfigKey 枚举 | ✅ 键大小写bug修复+OrdinalIgnoreCase |

### 证据权重(1 处) — ✅ 已改造
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| llm\reasoning\Weight\Calculator\EvidenceWeightCalculator.cs:8 | SourceCredibilityMap 字典(7 项 政府机构→0.95) | 提取 EvidenceSourceCategory 枚举 | ✅ 委托 EvidenceSourceCategory+FromValue() |

### 记忆类型字面量(1 处) — ✅ P3-18 完成
| 路径:行号 | 摘要 | 可委托目标 | 状态 |
|---|---|---|---|
| kit\prompts\templates\memory\ExtractMemoriesPromptTemplate.cs:10 | `MemoryTypes = new[] { MessageRoleEnumConstants.User, "feedback", "project", "reference" }` | 委托 MemoryTypeEnumConstants(混用枚举与字面量) | ✅ 统一硬编码+注释(kit/prompts不引用lib/vault) |

---

## 二、低违规清单(68 处,合理常量或已委托枚举)

### 已委托枚举常量(合理)
- 命令别名(ClearCommand/ResumeCommand/SimpleCommand 等 6 处):已用 ChatCommandNameEnumConstants
- 路径命令映射(PathConstraintValidator CommandOperationTypeMap/ActionVerbs):已用 PathCommand 枚举为键
- 敏感文件分类(SecurityPatterns SensitiveFilePatternsByCategory):已用 SensitiveFilePattern 枚举
- cron 模式(CronToolHandlers CommonCronPatterns/CronHumanMap):已用 CronPresetEnumConstants
- Todo/Task 图标(TodoIcons 5 处):已用 TodoPriority/TodoStatus/TaskExecutionStatus 枚举
- 分析类型提示(PromptTemplates AnalysisTypePrompts):已用 AnalysisType 枚举
- 工具结果大小限制(ContentReplacementConstants ToolMaxResultSizeChars):已用各 ToolName 枚举
- Tree-sitter 节点类型(CSharpSymbolExtractor NodeTypeToKind):由语法决定
- PowerShell 常量(PsAstParser TransientErrorIds/AltParamPrefixes/PsExecutableNames):系统常量
- 后台命令黑名单(SystemActuatorTypes DisallowedAutoBackgroundCommands):已用 SystemToolName.Sleep.ToValue()
- SSRF 私有地址(PrivateNetworkGuard 5 处):RFC 1918 标准常量
- Bridge 控制命令(MessageHandlers CommandHandlers):已用方法引用
- 枚举反向映射(MemoryType/AssistantDailyLog __reverseMap):[EnumValue] 源码生成器模式
- 本地语言检测(LocalLanguageDetector LCID):Windows API 常量
- 配置默认值/预设实例(BudgetConfig/ReasoningOptions/PluginUnloadOptions/RunMode 等 15 处):预设实例
- JSON/序列化选项(WorktreeJsonFormatting/StructuredOutputToolHandler/JwtUtils 等 5 处):序列化配置
- Buddy 精灵/Slug 生成/文件单位(BuddyService/PlanSlugGenerator/FileSizeFormatter 7 处):装饰性数据
- 开发工具检测/回调前缀/TFM(EnvironmentSnapshot/AotSafetyHelpers/FastProjectLoader 3 处):工具常量
- 表达式方法名(NumericMethods/StringMethods 15 处):方法注册
- 沙箱参数(Bubblewrap/Docker 2 处):CLI 参数
- LSP/Git 工具参数(LspToolHandlers/ReplService 2 处):工具参数

---

## 三、优先修复建议(按收益排序) — ✅ 全部完成

1. **统一 bin/obj/.git/.x 排除目录**到 CodeIndexExcludedDirCatalog(消除 3 处重复) — ✅ P3-15
2. **BomStripper 委托 FileFilter**(消除双套) — ✅ P3-16
3. **ConfigCommand/MemoryCommand/DiffCommand/QuadtreeToolHandlers 的 Enum 特性**委托对应枚举 — ✅ P3-13
4. **ExtractMemoriesPromptTemplate 改用 MemoryTypeEnumConstants** — ✅ P3-18
5. **AppStateSettingSyncService 改用 ConfigKey.ToValue()** — ✅ P3-19
6. **SessionScanner + ReferenceResolver 扩展名映射**合并到 LanguageMapCatalog — ✅ P3-14
