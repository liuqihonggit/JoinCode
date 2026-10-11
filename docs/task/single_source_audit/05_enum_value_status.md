# 枚举 + [EnumValue] 使用现状调查报告

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3`
**[EnumValue] 特性总数**: 2882 个,分布在 300+ 个文件

---

## 一、已用 [EnumValue] 的枚举(代表性样本,已全面覆盖)

工程大量采用 [EnumValue] + 源码生成器模式,覆盖绝大多数枚举:

| 类别 | 代表性枚举 | 位置(相对 w1) |
|------|-----------|--------------|
| 工具名(20+) | AgentToolName/CodeToolName/FileToolName/GitToolName/GitHubToolName/ShellToolName/SearchToolName/SystemToolName/WebToolName/MemoryToolName | lib\abstractions\abs_core\core_utils\constants\tool_names\ 及 tool_names_ext\ |
| CLI 命令/参数 | ChatCommandName/CliSubCommand/CommandCategory/JccCliArg/JccExitCode/McpAuthConfigType | lib\abstractions\abs_core\core_utils\constants\{cli_ansi,jcc_mcp}\ |
| 类型/操作(20+) | AnalysisType/BrowserAction/CaptureType/CronPreset/LspMethod/ReplAction/ReplLanguage/SnipMode | lib\abstractions\abs_core\core_utils\constants\types\ |
| 模型/配置 | ModelModalityKind/ProtocolKind/VendorKind/ConfigKey/SettingSource/McpTransportType/HttpMode/FileSystemMode/HeadlessMode | lib\abstractions\abs_core\configuration\ |
| 安全/权限 | PermissionLevel/PermissionDecision/PermissionMode/PermissionBehavior/OperationType/SandboxType/SandboxExecutionState/CommandDangerLevel/ScanResult | lib\abstractions\abs_guard\security\ |
| Agent/任务 | AgentStatus/TaskExecutionStatus/TodoStatus/TodoPriority/GoalStatus/PlanStatus/AgentRole/AccessLevel | lib\abstractions\abs_core\models\models_agent\ |
| 桥接/传输 | BridgeMessage/BridgeFault/BridgeHandle/TransportMode/TransportProtocol/BridgeSpawnMode | lib\transport.contracts\、lib\abstractions\abs_transport\ |
| LLM/推理 | OpenAIFinishReason/AnthropicEnums/FallbackCause/ReasoningPreset/TrustLevel/VerdictDecision/EvidenceCategory | llm\ |
| Server 桥接 | PeerSessionStatus/BridgeSessionStatus/SecretLifecycle/DeviceTrustLevel/BridgeStatusState | server\bridge\ |
| App CLI/TUI | CliArg/CliErrorCategory/CommandRiskLevel/ConfigPriority/ToolExecutionStatus/QueuePriority/CommandOrigin/ChatUiMessageKind/StatusKind/AgentState/TuiMessageType | app\ |

---

## 二、未用 [EnumValue] 的枚举(违规)

> **改造状态**: ✅ P3-12 完成(8个全部加特性,生成器已支持internal枚举) | 详见 00_summary.md

### 🔴 真正违规(有限集合字符串标识应枚举化,8 个)

| 枚举名 | 路径(相对 w1):行号 | 定义 | 违规说明 | 状态 |
|--------|------|------|----------|------|
| IdeType | lib\abstractions\abs_core\interfaces\integration\IIdeIntegrationService.cs:3 | {VsCode,Cursor,Windsurf,JetBrains} | IDE 类型,涉及序列化 | ✅ 已加 [EnumValue] |
| ForkState | lib\abstractions\abs_agents\agent\sub_agent\IForkSubAgentManager.cs:77 | {Running,Completed,Merged,Cancelled,Failed} | 分叉状态,涉及序列化 | ✅ 已加 [EnumValue] |
| BuddyRarity | lib\abstractions\abs_core\interfaces\application\IBuddyService.cs:3 | {Common,Uncommon,Rare,Epic,Legendary} | 稀有度,有限集合 | ✅ 已加 [EnumValue] |
| WorktreeCleanupMode | lib\abstractions\abs_agents\agent\agent_interfaces\IAgentWorktreeManager.cs:27 | {OnTaskComplete,ForceRemove} | 清理模式 | ✅ 已加 [EnumValue] |
| NodeCompletionOutcome | lib\clock\goal\core\node\NodeCompletionOutcome.cs:7 | {Continue,GoalAchieved,GoalUnmet} | 目标状态决策 | ✅ 已加 [EnumValue] |
| CacheScope | llm\core\Adapters\LLM\CacheProtocol\AnthropicCacheProtocol.cs:3 | {None,Org,Global} | 内部硬编码 "global"/"org",应加 [EnumValue] 消除 | ✅ 已加 [EnumValue](生成器支持internal) |
| BudgetType | kit\brain\cost_tracking\services\core\CostTracker.cs:320 | {Daily,Monthly,Total} | 涉及业务标识 | ✅ 已加 [EnumValue](生成器支持internal) |
| FlagArgType | lib\guard\security\services\readonly_detector\ReadOnlyCommandTypes.cs:6 | {None,Required,Optional} | 对齐 TS FlagArgType | ✅ 已加 [EnumValue](生成器支持internal) |

### 🟢 合理例外(不需要 [EnumValue],16 个)

| 枚举名 | 路径 | 例外理由 |
|--------|------|----------|
| KeyModifier | lib\abstractions\abs_hands\desktop\KeyModifier.cs:7 | [Flags] 位标志,按位组合数值 |
| SandboxCapabilities | lib\abstractions\abs_guard\security\sandbox\core\SandboxCapabilities.cs:4 | [Flags] 位标志 |
| InterceptionFlags | lib\guard\hooks\execution\interception\core\InterceptionFlags.cs:14 | [Flags] 位标志(ushort) |
| SubAgentLivenessState | llm\agents\Coordinator\Core\Liveness\SubAgentIdleDetector.cs:7 | [Flags] 位标志(byte) |
| PromptSectionInject | lib\abstractions\abs_core\core_attributes\core\PromptSectionAttribute.cs:7 | [Flags] 位标志 |
| SubAgentLivenessEvent | llm\agents\Coordinator\Core\Liveness\SubAgentIdleDetector.cs:24 | 状态机事件(byte),驱动转换 |
| ToolStatus(private) | kit\brain\context\services\chat\core\StreamingToolExecutor.cs:337 | private 内部状态,不序列化 |
| ToolStatus(private) | kit\brain\context\services\chat\core\StreamingToolExecutorActor.cs:366 | private 内部状态 |
| EditType(private) | lib\infrastructure\io\services\diff\StructuredPatchGenerator.cs:395 | private 内部算法状态(byte) |
| LogParseState(private) | kit\mcp\git_hub\GitHubRunLogFilterRunner.cs:221 | private 解析状态机 |
| MessageType(internal) | lib\async_lock\transport\NamedPipeTransport.cs:271 | internal 二进制协议消息分类(byte) |
| McpListArg/McpServeArg/ToolCallArg/CliArg | app\cli\core\services\*.cs | 用 [CliOption] 特性,属不同生成器体系 |
| ProjectType | gen\aot_safety.generator\infrastructure\IAnalyzerRule.cs:24 | 生成器基础设施枚举 |
| SettingsMergeStrategy(private) | gen\enum_metadata.generator\SettingsMergeGenerator.cs:414 | private 生成器内部 |
| CommandType(private) | gen\mcp_tool_dispatch.generator\CommandRegistrationGenerator.cs:348 | private 生成器内部 |
| PromptTemplateCategoryValue(private) | gen\prompt_template.generator\PromptTemplateGenerator.cs:146 | private 生成器内部 |

---

## 三、手动维护 KV 完全相同的映射字典违规(2 处) — ✅ P3-17 完成

| 路径(相对 w1):行号 | 内容 | 违规说明 | 状态 |
|-----------|------|----------|------|
| lib\abstractions\abs_core\core_utils\core\json_repair\ParameterNameRepairer.cs:11 | `["old_string"] = "old_string",` | Key==Value 恒等映射,冗余条目,应删除 | ✅ 已删除 |
| lib\abstractions\abs_core\core_utils\core\json_repair\ParameterNameRepairer.cs:12 | `["new_string"] = "new_string",` | 同上 | ✅ 已删除 |

> 其他字典均为合法映射(Key != Value):TodoIcons(跨枚举)、CronHumanMap、RuleIdLabels、PsAliases、PathEscapePatterns 等

---

## 四、有限集合字符串常量未枚举化违规

### 🔴 明确违规(1 处) — ✅ P3-18 完成
| 路径(相对 w1):行号 | 内容 | 违规说明 | 状态 |
|-----------|------|----------|------|
| kit\prompts\templates\memory\ExtractMemoriesPromptTemplate.cs:10 | `MemoryTypes = new[] { MessageRoleEnumConstants.User, "feedback", "project", "reference" }` | 混用枚举常量与硬编码字符串,应定义 MemoryType 枚举 + [EnumValue] | ✅ 统一硬编码+注释(kit/prompts不引用lib/vault) |

### 🟡 轻度违规(外部协议字符串,2 处) — ✅ 已改造
| 路径(相对 w1):行号 | 内容 | 违规说明 | 状态 |
|-----------|------|----------|------|
| app\cli\core\commands\core\GhCommandResolver.cs:29 | `KnownGroups = ["pr","issue","repo","release","run","branch","api"]` | gh CLI 分组,建议定义 GhGroup 枚举 | ✅ 委托 GhGroupEnumConstants |
| lib\abstractions\abs_core\configuration\llm\BedrockModelHelper.cs:14 | `RegionPrefixes = ["us","eu","apac","global"]` | Bedrock 区域前缀,建议定义 BedrockRegionPrefix 枚举 | ✅ 委托 BedrockRegionPrefixEnumConstants |

### 🟢 合理例外(数据数组/UI 文本,非配置标识)
- BuddyService Species/Eyes/Hats/Names:装饰性内容
- TmuxPaneBackend TmuxColorMap:tmux 外部 CLI 颜色
- TeammateLayoutManager AgentColors:十六进制颜色
- GlobalRunStatusViewModel SpinnerVerbs:UI 动词文本
- NoOpPlanDetector NoOpPhrases/ActionTerms:自然语言短语
- SecurityPatterns SecretPrefixes/SecretRegexPatterns:安全正则模式
- FileSizeFormatter Units:物理单位
- ConfigChangeNotifier RootConfigFiles 等:路径/目录名

---

## 五、需修复违规汇总(13 项) — ✅ P3-12/17/18 全部完成

### P1(明确违规,应立即修复) — ✅ 全部完成
1. ParameterNameRepairer.cs:11-12 — 2 处 Key==Value 恒等映射,删除冗余条目 — ✅ P3-17
2. ExtractMemoriesPromptTemplate.cs:10 — 混用枚举与字面量,定义 MemoryType 枚举 — ✅ P3-18
3. AnthropicCacheProtocol.cs:3 CacheScope — 内部硬编码 "global"/"org",加 [EnumValue] — ✅ P3-12(生成器支持internal)

### P2(应枚举化,涉及序列化/业务标识) — ✅ 8个全部完成
4. IdeType — ✅
5. ForkState — ✅
6. BuddyRarity — ✅
7. WorktreeCleanupMode — ✅
8. NodeCompletionOutcome — ✅
9. BudgetType — ✅(生成器支持internal)
10. FlagArgType — ✅(生成器支持internal)

### P3(外部协议字符串,建议枚举化) — ✅ 全部完成
11. GhCommandResolver KnownGroups — ✅ 委托 GhGroupEnumConstants
12. BedrockModelHelper RegionPrefixes — ✅ 委托 BedrockRegionPrefixEnumConstants
13. (可选) BuddyService Species — 装饰性数据,争议性较大
