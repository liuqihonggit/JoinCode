# 例外/不舒服点清单

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3`
本清单记录:某类必须持有多个数据源/某硬编码无法委托/某枚举无法 [EnumValue] 化的合理例外。

---

## 一、必须持有多个数据源的类(语义不同无法合并,33 个)

### 1.1 键/值类型不同(12 个)
| 类名 | 路径(相对 w1) | 数据源 | 例外理由 |
|------|--------------|--------|----------|
| AgentSummaryService | llm\agents\Services\Support | _executions(executionId→摘要) + _metrics(agentName→累加器) | 键不同、值类型不同 |
| TeammateMailboxService | llm\agents\Coordinator\Team\core | _actors(agentId→Actor) + _cursors(agentId→游标) | 值类型不同(写串行化 vs 读位置) |
| SwarmPermissionCallbackService | llm\agents\Coordinator\Swarm | _pendingCallbacks + _pendingRequests | 值类型不同、生命周期不同 |
| AgentWorktreeManager | llm\agents\Coordinator\Core\Lifecycle | s_worktreeSessions + _lifecycleGuards | 值类型不同(会话 vs 守卫) |
| RemotePolicyService | lib\guard\policy | _usageCounters + _windowStartTimes | 值类型不同(计数 vs 时间) |
| HookConfigurationManager | lib\guard\hooks\configuration | _providers(注册) + _cache(加载) | 语义不同 |
| TelemetryService | lib\infrastructure\telemetry | _metrics + _activeSpans | 语义不同(指标 vs span) |
| PermissionChecker | lib\guard\permission\permission2 | _autoApprovedTools + _autoRejectedTools + _approvedLevels | 值类型不同(string vs CommandDangerLevel) |
| AgentStateMachine | llm\agents\Coordinator\Core\services | Transitions(静态表) + _states(上下文) | 语义不同 |
| InvariantRegistry | lib\plugins.contracts\registry | _registrations + _allowlist + _blocklist | 语义不同(注册表 vs 白名单 vs 黑名单) |
| CommandInterceptionDispatcher | lib\guard\hooks\execution\interception\core | _guards + _interceptors | 语义不同(守卫链 vs 拦截器链) |
| ModelConfigLoader | lib\abstractions\abs_core\configuration\llm | _modelById + _aliasToModelId + _modelsByProvider | 预构建索引,查询入口不同(非冗余) |

### 1.2 语义域不同(13 个)
| 类名 | 路径(相对 w1) | 数据源 | 例外理由 |
|------|--------------|--------|----------|
| PluginManager | lib\plugins.infrastructure\services | _plugins + _pluginResourceIds + _blacklistedPlugins + _diagnostics | 已做过合并优化(3→1),剩余语义不同 |
| InMemoryFileSystem(test) | test\unit\testing.common | _files + _directories + _editLocks | 文件/目录/锁语义不同 |
| InMemoryFileSystem(lib) | lib\infrastructure\io\file_system | _files + _directories | 文件/目录语义不同 |
| TeamMemorySyncService | lib\vault\memdir\sync\core | _localEntries + _remoteEntries | 本地 vs 远程(同步对比) |
| ServiceMessageBus | lib\clock\hosting | _subscribers + _messageHistory | 订阅者 vs 历史 |
| AsyncLockedDictionary | lib\async_lock\lock | _dict + _keyLocks | 数据 vs per-key 锁 |
| DebounceTracker | lib\infrastructure\utils\io | _timers + _internalWriteTimestamps | 定时器 vs 时间戳 |
| ReferenceResolver | kit\brain\context\resolution | DirectoryAliases + ExtensionPatterns | 目录别名 vs 扩展名模式 |
| ConfigChangeNotifier | lib\guard\configuration\configuration2 | RootConfigFiles + RulesSubDirs + CommandsSubDirs + AppDataConfigFiles | 监控目标不同 |
| ExternalRulesLoader | lib\guard\configuration\configuration2\rules | ProjectRulesDirs + UserRulesDirs | 项目级 vs 用户级 |
| ProjectRulesLoader | lib\guard\configuration\configuration2\rules | 4 个规则路径 | 规则文件/目录不同 |
| NoOpPlanDetector | lib\abstractions\abs_core\models\models_agent\plan | NoOpPhrases + ActionTerms | 检测目的不同 |
| StructuredTaskMarkdownReader | lib\scheduling\storage | StatusPrefixes + ResultPrefixes + ExclusionReasonPrefixes | 解析字段不同 |

### 1.3 装饰性/词库数据(3 个)
| 类名 | 路径(相对 w1) | 数据源 | 例外理由 |
|------|--------------|--------|----------|
| BuddyService | app\cli\services\user_experience | Species + Eyes + Hats + Names + _cache | 装饰性外观数据,语义不同 |
| PlanSlugGenerator | kit\brain\planning\planning2 | Adjectives + Verbs + Nouns + SlugCache | 三个词表语义不同(形/动/名) |
| FileFilter | non_deliverables_tools\jcc_audit_ast_cli\core | 4 个排除集合 | 过滤层级不同 |

### 1.4 算法/协议结构(5 个)
| 类名 | 路径(相对 w1) | 数据源 | 例外理由 |
|------|--------------|--------|----------|
| AhoCorasick | lib\infrastructure\utils\text | _transitions + _failures + _outputs | DFA 自动机结构组件不可分离 |
| DialogueCompressor | kit\brain\context\compression\strategies | _supportedTypes + 5 个 Regex[] | 5 个正则识别不同消息格式 |
| CodeContentCompressor | kit\brain\context\compression\strategies | _supportedTypes + 4 个正则 | 识别不同代码结构 |
| McpTransportFallbackChain | kit\mcp\transports\shared | _transports + _healthChecks + _circuitBreakers | 类型职责不同(传输/健康检查/熔断器) |
| ReadOnlyCommandDetector | lib\guard\security\services\readonly_detector | 7 个 FrozenSet/Dictionary | 各检测路径不同(数量多但语义不同,职责过载可考虑拆分) |

### 1.5 正反映射对(1 个)
| 类名 | 路径(相对 w1) | 数据源 | 例外理由 |
|------|--------------|--------|----------|
| AssistantDailyLogExtensions | lib\vault\memdir\memdir2\operations | __reverseMap + CategoryLabels | 正反映射对,[EnumValue] 源码生成器标准模式 |

---

## 二、无法委托的硬编码(合理常量)

| 路径(相对 w1) | 内容 | 例外理由 |
|------|------|----------|
| kit\hands\desktop\native\NativeConstants.cs | Win32 常量(SendInput/MOUSEEVENTF 等) | 系统魔数,集中定义即可 |
| kit\hands\web\services\PrivateNetworkGuard.cs:8-12 | RFC 1918 私有地址前缀 | RFC 标准常量 |
| lib\infrastructure\utils\text\FileSizeFormatter.cs:13 | Units = {"B","KB","MB","GB","TB","PB"} | 物理单位,通用常量 |
| server\code_index\parsing\CSharpSymbolExtractor.cs:8 | NodeTypeToKind(16 项) | Tree-sitter 节点类型由语法决定 |
| lib\guard\security\power_shell\ast\PsAstParser.cs:285 | AltParamPrefixes = { '/','–','—','―' } | Unicode 常量 |
| lib\abstractions\abs_core\core_utils\core\misc\LocalLanguageDetector.cs:110 | LCID→语言代码(20 项) | Windows API 常量 |
| app\cli\services\user_experience\BuddyService.cs:8-11 | Species/Eyes/Hats/Names(各 18 项) | 装饰性内容数据 |
| kit\brain\planning\planning2\PlanSlugGenerator.cs:13,27,41 | Adjectives/Verbs/Nouns(各 76 项) | 词库数据 |
| lib\guard\security\sandbox\providers\BubblewrapSandboxProvider.cs:85-108 | bubblewrap 参数 | CLI 参数 |
| lib\guard\security\sandbox\providers\DockerSandboxProvider.cs:133,165 | docker rm/exec 参数 | CLI 参数 |

---

## 三、无法 [EnumValue] 化的枚举(16 个)

### 3.1 [Flags] 位标志枚举(5 个) — 按位组合数值,非字符串标识
- KeyModifier、SandboxCapabilities、InterceptionFlags、SubAgentLivenessState、PromptSectionInject

### 3.2 private/internal 内部状态(6 个) — 不涉及序列化
- ToolStatus(private,2 处)、EditType(private)、LogParseState(private)、MessageType(internal)

### 3.3 状态机事件(1 个) — 驱动状态转换,非序列化标识
- SubAgentLivenessEvent

### 3.4 不同生成器体系(4 个) — 用 [CliOption] 特性
- McpListArg、McpServeArg、ToolCallArg、CliArg(app\cli\core\services\)

### 3.5 生成器基础设施(4 个) — private 生成器内部
- ProjectType、SettingsMergeStrategy、CommandType、PromptTemplateCategoryValue

---

## 四、不舒服点(非违规但值得关注)

> **改造状态**: ✅ P4 全部完成(含排查保留) | 详见 00_summary.md

1. **ReadOnlyCommandDetector 持有 7 个数据源**: 各语义不同(例外),但数量多、职责过载,建议按检测路径拆分到多个专注类 — ✅ P4-23 排查完成,当前合理暂不拆分
2. **SecurityPatterns 持有 7 个数据源**: 源+派生冗余(违规)外,其余 6 个语义不同但塞同一 partial class,建议按职责拆分 — ✅ P2-⑨ 源+派生已合并;P4-23 拆分暂不执行
3. **PathConstraintValidator 持有 7 个数据源**: 2 组可合并(违规),其余语义不同,但数量多 — ✅ P2-⑨ 源+派生已合并
4. **MapRegistry 作为泛型基类**: _canonicalKeys 冗余(违规),但作为泛型注册器基类,次级索引是扩展点,设计意图合理 — ⚠️ 例外(P2-⑧)
5. **ModelConfigLoader 3 个预构建索引**: 非冗余(查询入口不同),但可考虑合并为单一 ModelConfigRoot + 按需查询 — ⚠️ 例外(P2-⑧)
6. **McpConstants 死代码**: 所有常量无消费方,已迁移至 JsonRpcConstants,应清理 — ✅ P4-20 已归档到 .xxx/
7. **TaskTableGenerator 消费不足**: [Register] 已注册但生产代码无调用方,可能待启用功能 — ✅ P4-21 排查完成,有测试覆盖,保留
8. **VendorCommand 自相矛盾**: 第8行特性 Enum 硬编码供应商列表,第28行却用 Enum.GetValues<VendorKind>(),同一文件两套定义 — ✅ P4-22 已修复,委托 VendorKindEnumConstants
