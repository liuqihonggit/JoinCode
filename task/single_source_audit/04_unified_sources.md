# 统一数据源类清单与委托消费现状

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3`
**统一数据源类总数**: 21 个 | **违规重复硬编码**: 8 处

---

## 一、统一数据源类清单(21 个)

### 1. DangerousCommandCatalog(3 文件分部类)
- 路径: lib\guard\security\danger_classification\{DangerousCommandCatalog.cs, .Commands.cs, .Flags.cs}
- 持有: InterpreterCommands(FrozenSet)、Commands(字典)、Flags(字典)、Combinations(列表)、DangerousPaths(字典)、RiskToLevelMap
- 消费: 22 处引用(CommandDangerClassifier 11 处、DangerousCommandProtectionMiddleware 2 处等)
- 违规: 否

### 2. DangerCommandDefinitions
- 路径: lib\guard\security\danger_classification\DangerCommandDefinitions.cs
- 持有: ~150 个 const string 命令名常量,每个标注 [DangerCommand] 特性
- 消费: 3 处(自身 + 源码生成器扫描 + XML 注释)
- 违规: 否

### 3. RetainedDeviceNames
- 路径: lib\abstractions\abs_guard\security\scanning\RetainedDeviceNames.cs
- 持有: Pattern 常量 + Names(FrozenSet 22 个)
- 消费: 21 处引用
- 违规: 否

### 4. ToolHypergraphPresets
- 路径: kit\mcp_tool_dispatch\core\execution\ToolHypergraphPresets.cs
- 持有: 5 个预设工具链超边,工具名全部来自 XxxToolName.ToValue()
- 消费: 4 处
- 违规: 否(零硬编码字符串)

### 5. CliErrorCatalog
- 路径: app\cli\core\output\CliErrorCatalog.cs
- 持有: ~20 个错误工厂方法(AUTH_/CONFIG_/NET_/RESOURCE_/CONFLICT_/ARG_/TOOL_)
- 消费: 24 处引用
- 违规: 否

### 6. ModelCatalog / IModelCatalog — ✅ P1-⑦ 违规已修复
- 路径: kit\slash\ai\model\{ModelCatalog.cs, IModelCatalog.cs}
- 持有: 模型列表查询、别名解析、能力判定(委托 IProviderDefinitionRegistry)
- 消费: 29 处引用
- **违规: 是,2 处** — ModelCatalog.cs:75 `?? "gpt-4o"`、:84 `?? "gpt-4o-mini"`(数据源类自身硬编码回退值) — ✅ 已委托 DefaultModelCatalog

### 7. BashSecurityConstants
- 路径: lib\abstractions\abs_guard\security\shell\bash\BashSecurityConstants.cs
- 持有: 12 个 FrozenSet(EvalLikeBuiltins/ZshDangerousBuiltins/ShellKeywords/SafeEnvVars 等)
- 消费: 20 处引用
- 违规: 否

### 8. WorkflowConstants
- 路径: lib\abstractions\abs_core\configuration\app_data\WorkflowConstants.cs
- 持有: ~15 个嵌套静态类(Timeouts/Retry/CodeExecution/Bridge/Cache/Worktree 等数十个常量)
- 消费: 87 处引用
- 违规: 否

### 9. AppDataConstants
- 路径: lib\abstractions\abs_core\configuration\app_data\AppDataConstants.cs
- 持有: ~30 个目录名/文件名 + ~40 个完整路径(委托 AppDataPaths)
- 消费: 304 处引用
- 违规: 否

### 10. ClaudeCompatConstants
- 路径: lib\abstractions\abs_core\core_utils\constants\jcc_mcp\ClaudeCompatConstants.cs
- 持有: ~20 个 CLAUDE 兼容性常量
- 消费: 30 处引用
- 违规: 否

### 11. BrandConstants — ✅ P1-⑦ 违规已修复
- 路径: lib\abstractions\abs_core\core_utils\constants\cli_ansi\BrandConstants.cs
- 持有: ProductName = "JoinCode"、CliCommandName = "jcc"
- 消费: 29 处引用
- **违规: 是,5 处** — ReleaseNotesService.cs:55 "JoinCode"、TelemetryConfig.cs:43 "JoinCode"、BridgeMainCommand.cs:262 "jcc"、BridgeSubprocessManager.cs:322 "jcc"、XdgPathResolver.cs:17 "jcc" — ✅ 已委托 BrandConstants

### 12. McpConstants — ✅ P4-20 已归档
- 路径: ~~lib\abstractions\abs_core\core_utils\constants\jcc_mcp\McpConstants.cs~~ → .xxx/McpConstants.cs.20260926.del
- 持有: DefaultServerName/DefaultServerVersion/ErrorServerErrorStart 等
- 消费: 0 处(无消费方)
- 违规: 否,但**死代码**(所有常量无消费方,已迁移至 JsonRpcConstants) — ✅ 已归档到 .xxx/

### 13. JsonRpcConstants — ✅ P1-⑦ 违规已修复
- 路径: kit\mcp\constants\JsonRpcConstants.cs
- 持有: JSON-RPC 协议版本、MCP 协议版本(V2024_11_05/V2025_03_26/V2025_06_18/V2025_11_25 + Current)、错误码
- 消费: 6 处 McpProtocolVersion.Current 引用
- **违规: 是,1 处** — test\mock\mcp.mock_server\models\McpMockServerConfig.cs:14 `"2025-11-25"` 硬编码 — ✅ 已委托 McpProtocolVersion.Current

### 14. ContentReplacementConstants
- 路径: lib\abstractions\abs_ai\llm\chat\chat_core\ContentReplacementConstants.cs
- 持有: 持久化输出标记、截断前缀、工具级 maxResultSizeChars 映射
- 消费: 41 处引用
- 违规: 否

### 15. ModelPricingTable(2 处:定义+转发)
- 路径: lib\abstractions\abs_ai\llm\execution\pricing\ModelPricingTable.cs(实现) + kit\brain\cost_tracking\services\core\ModelPricingTable.cs(转发包装)
- 消费: 15 处引用
- 违规: 否(kit 中是转发包装,非重复)

### 16. NativeConstants
- 路径: kit\hands\desktop\native\NativeConstants.cs
- 持有: ~25 个 Win32 常量(SendInput/MOUSEEVENTF/KEYEVENTF/窗口消息 等)
- 消费: 51 处引用
- 违规: 否

### 17. AgentCoordinatorConstants
- 路径: llm\agents\Coordinator\Core\services\AgentCoordinatorConstants.cs
- 持有: 日志模板、Agent ID 格式、系统提示
- 消费: 19 处引用
- 违规: 否

### 18. UiResourceTable
- 路径: lib\plugins.contracts\resource\UiResourceTable.cs
- 持有: ConcurrentDictionary(插件 UI 资源登记,运行时)
- 消费: 7 处引用
- 违规: 否

### 19. UnmanagedResourceTable
- 路径: lib\plugins.contracts\resource\UnmanagedResourceTable.cs
- 持有: ConcurrentDictionary(非托管资源登记,运行时)
- 消费: 11 处引用
- 违规: 否

### 20. TaskTableGenerator / ITaskTableGenerator — ✅ P4-21 排查完成
- 路径: lib\abstractions\abs_agents\hot_spot\task_table\ITaskTableGenerator.cs + lib\infrastructure\hot_spot\TaskTableGenerator.cs
- 消费: 5 处(仅测试)
- 违规: 否,但**消费不足**(生产代码无调用方,可能待启用) — ✅ 排查完成,有测试覆盖,保留

### 21. ISlashCommandCatalog / ISlashCommandSchemaCatalog
- 路径: lib\abstractions\abs_hands\shell\{SlashCommandCatalog.cs, SlashCommandSchemaCatalog.cs}
- 持有: 斜杠命令元数据(源码生成器从 [ChatCommand]/[ChatCommandArg] 自动提取)
- 消费: 10 + 3 处引用
- 违规: 否(编译时生成,零硬编码)

---

## 二、违规重复硬编码汇总(8 处) — ✅ 全部修复(P1-⑦)

| # | 违规文件(相对 w1) | 行号 | 硬编码内容 | 应委托的统一数据源 | 状态 |
|---|----------|------|-----------|---------------------|------|
| 1 | kit\slash\ai\model\ModelCatalog.cs | 75 | `?? "gpt-4o"` | 从 IModelConfigLoader 读取 | ✅ 委托 DefaultModelCatalog |
| 2 | kit\slash\ai\model\ModelCatalog.cs | 84 | `?? "gpt-4o-mini"` | 同上 | ✅ 委托 DefaultModelCatalog |
| 3 | kit\hands\integration\core\ReleaseNotesService.cs | 55 | `"JoinCode"` | BrandConstants.ProductName | ✅ 委托 BrandConstants |
| 4 | lib\abstractions\abs_core\models\models_telemetry\telemetry\TelemetryConfig.cs | 43 | `"JoinCode"` | BrandConstants.ProductName | ✅ 委托 BrandConstants |
| 5 | kit\slash\transport\BridgeMainCommand.cs | 262 | `?? "jcc"` | BrandConstants.CliCommandName | ✅ 委托 BrandConstants |
| 6 | server\bridge\session\core\BridgeSubprocessManager.cs | 322 | `"jcc"` | BrandConstants.CliCommandName | ✅ 委托 BrandConstants |
| 7 | app\cli\core\output\XdgPathResolver.cs | 17 | `"jcc"` | BrandConstants.CliCommandName | ✅ 委托 BrandConstants |
| 8 | test\mock\mcp.mock_server\models\McpMockServerConfig.cs | 14 | `"2025-11-25"` | McpProtocolVersion.Current | ✅ 委托 McpProtocolVersion.Current |

---

## 三、其他发现

- **McpConstants 死代码**: DefaultServerName/DefaultServerVersion/ErrorServerErrorStart/ErrorUrlElicitationRequired 均无消费方,已迁移至 JsonRpcConstants — ✅ P4-20 已归档到 .xxx/
- **TaskTableGenerator 消费不足**: [Register] 已注册但生产代码无调用方,仅测试引用 — ✅ P4-21 排查完成,有测试覆盖,保留
- **测试文件硬编码**: "gpt-4o" 在测试出现 ~120 处、"CLAUDE.md" 在 ProjectRulesLoaderTests 出现 2 处,属测试桩数据,非违规
