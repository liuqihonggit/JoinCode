# 重复定义违规清单(最严重违规)

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3`
**发现**: 13 类重复定义违规,涉及约 50 处硬编码位置

---

## 一、统一数据源类清单(12 个)

| 类名 | 路径(相对 w1) | 职责 |
|------|------|------|
| DangerousCommandCatalog | lib\guard\security\danger_classification\ | 危险命令/参数/组合/路径统一目录(3 文件分部类) |
| DangerCommandDefinitions | lib\guard\security\danger_classification\ | [DangerCommand] 特性标注的命令定义 |
| RetainedDeviceNames | lib\abstractions\abs_guard\security\scanning\ | Windows 保留设备名唯一数据源 |
| BashSecurityConstants | lib\abstractions\abs_guard\security\shell\bash\ | Bash 安全常量 |
| PsDangerousCmdlets | lib\guard\security\power_shell\validation\ | PowerShell 危险 cmdlet |
| PsAliases | lib\guard\security\power_shell\ast\ | PowerShell 别名→规范名 |
| VendorKind | lib\abstractions\abs_core\configuration\providers\ | LLM 供应商枚举 |
| ProviderEnvVar | lib\abstractions\abs_core\configuration\app_data\JccEnvVar.cs | Provider API Key 环境变量名枚举 |
| JccEnvVar | lib\abstractions\abs_core\configuration\app_data\JccEnvVar.cs | JCC 专属环境变量名枚举 |
| ClaudeCompatConstants | lib\abstractions\abs_core\core_utils\constants\jcc_mcp\ | Claude 兼容性常量 |
| ShellToolName | lib\abstractions\abs_core\core_utils\constants\tool_names_ext\ | Shell 工具名称枚举 |
| ModelPricingTable | lib\abstractions\abs_ai\llm\execution\pricing\ | 模型定价表 |

---

## 二、重复定义清单(按严重程度排序)

> **改造状态**: P0(严重级1-4) ✅ 全部完成 | P1(严重级5-6 + 中等级) ✅ 全部完成 | 详见 00_summary.md

### 🔴 严重级(不一致风险高)

#### 1. 危险命令名 — 在 9 处定义 — ✅ P0-① 完成
- **唯一数据源**: DangerCommandDefinitions
- **重复硬编码**:
  1. lib\guard\security\danger_classification\PathCaseSensitiveGuard.cs:20 — `"rm","del","erase","Remove-Item","rmdir","rd"`
  2. lib\guard\security\danger_classification\CommandDangerClassifier.cs:341-344 — `"Remove-Item","rm","del","erase"`
  3. lib\guard\security\power_shell\validation\PsModeValidation.cs:43-56 — `"rm","del","rd","rmdir","erase","sc","cp","copy","mv","move"`
  4. lib\guard\security\power_shell\ast\PsAliases.cs:58-76 — `"del","rd","rmdir","rm","erase","mv","move","cp","copy","kill"`
  5. lib\guard\security\power_shell\validation\PsDangerousCmdlets.cs:92 — `"del","rm","rd","rmdir","erase","clear-item","cli"`
  6. lib\guard\security\power_shell\core\PsSecurityChecker.cs:275,319,323 — `"schtasks","start-process","saps","start","runas"`
  7. lib\guard\security\services\bash_validation\BashPermissionChecker.cs:37 — `"sudo","doas","pkexec"`
  8. lib\guard\hooks\execution\interception\guards\RobocopyMirrorGuard.cs:30,33,34,71,74,75 — `"robocopy","/mir","/purge"`
  9. llm\agents\Coordinator\Swarm\SwarmPermissionRequestProcessor.cs:163-165 — `"file_delete","rm","delete","format_disk","shutdown"`
- **风险**: DangerCommandDefinitions 新增/删除命令,9 处不同步,安全策略不一致
- **修复**: 8处改造,新增Doas/Pkexec/Saps/Start常量,PsSecurityChecker `is not`模式匹配重构

#### 2. 危险命令组合模式 — 在 4 处定义 — ✅ P0-② 完成(2处架构层级限制保留)
- **唯一数据源**: DangerousCommandCatalog.Flags.cs
- **重复硬编码**:
  1. lib\guard\security\services\classifiers\AutoModeClassifier.cs:76-84 — 13 个模式(rm -rf /、format、dd if=、fork 炸弹等)
  2. lib\guard\permission\configuration\PermissionConfig.cs:123-138 — 同上 13 个模式
  3. lib\abstractions\abs_core\configuration\execution\ShellExecutionConfig.cs:69-76 — 5 个模式
  4. lib\infrastructure\utils\validation\DestructiveCommandAnalyzer.cs:124-204 — 大量正则模式
- **风险**: 4 套危险命令列表各自维护,分级标准不同(DangerLevel vs CommandDangerLevel)
- **修复**: 新增 `DangerousPatternEntry` 记录 + `BuildDangerousCommandPatterns` 方法(14个模式),2处改造,2处因架构层级限制保留

#### 3. 保留设备名 "NUL" — 在 2 处定义 — ✅ P0-③ 完成
- **唯一数据源**: RetainedDeviceNames
- **重复硬编码**:
  1. lib\guard\security\services\path_validation\PathConstraintValidator.cs:571 — `redirect.Target.Equals("NUL", OrdinalIgnoreCase)`
  2. lib\abstractions\abs_guard\security\scanning\RetainedDeviceNames.cs:107 — 正则 `nul|con|prn|aux|com[1-9]|lpt[1-9]` 与第24行 Pattern 常量重复(注释说"必须保持一致"但无编译期保证)
- **风险**: RetainedDeviceNames.Names 新增设备名,PathConstraintValidator 和正则不同步
- **修复**: 2处改造,扩展到全部22个保留设备名,新增33个测试

#### 4. 供应商名 — 在 5 处定义 — ✅ P1-⑤ 完成
- **唯一数据源**: VendorKind 枚举
- **重复硬编码**:
  1. kit\slash\ai\model\VendorCommand.cs:8 — `Enum = new[] { "openai","anthropic","deepseek","azure","agnes","sensenova","bedrock","list" }`(同文件第28行却用 Enum.GetValues<VendorKind>(),自相矛盾)
  2. kit\slash\transport\BridgeMainCommand.cs:11 — `private const string TokenProviderAnthropic = "anthropic"`
  3. app\cli\entry\runners\DotEnvConfig.cs:48 — `config.Vendor = "anthropic"`
  4. app\cli\entry\runners\StartupWorkflow.cs:106,236 — `ProviderPicker.Show("openai",...)` 和 `GetDefaultModelId("deepseek")`
  5. lib\guard\configuration\configuration2\core\loading\core\SettingsLoader.cs:63-125 — 默认 settings.json 骨架硬编码所有供应商名
- **风险**: VendorCommand 特性列表与枚举不同步(特性缺 zhipu/jev,枚举有)
- **修复**: 5处改造,统一委托 VendorKind.ToValue()

#### 5. 默认模型名 — 在 3 处定义 — ✅ P1-⑥ 完成
- **重复硬编码**:
  1. llm\core\Adapters\Chat\services\PipeQueryService.cs:132 — `?? "gpt-4" : "gpt-4"`
  2. app\cli\entry\runners\StartupWorkflow.cs:236 — `?? "deepseek-chat"`
  3. lib\guard\configuration\configuration2\core\loading\core\SettingsLoader.cs:66,75,84,93,102,111,120 — `"sensenova-6.8-flash-lite","gpt-5.6-sol","claude-opus-5","deepseek-v4-flash","agnes-2.0-flash","glm-5.3"`
- **风险**: 无统一数据源,模型 ID 升级时需全工程搜索替换
- **修复**: 新建 `DefaultModelCatalog.cs`,3处改造

#### 6. API Key 环境变量名 — 在 3 处定义 — ✅ P0-④ 完成
- **唯一数据源**: ProviderEnvVar 枚举
- **重复硬编码**:
  1. lib\guard\configuration\configuration2\core\loading\core\SettingsLoader.cs:68,77,86,95,104,113,122 — `"SENSENOVA_API_KEY","OPENAI_API_KEY","ANTHROPIC_API_KEY","DEEPSEEK_API_KEY","AGNES_API_KEY","ZHIPUAI_API_KEY"`
  2. kit\hands\shell\services\SubprocessEnvCleaner.cs:20-44 — 硬编码 25 个敏感环境变量名
  3. app\cli\entry\runners\DotEnvConfig.cs:47,71,85 — `"ANTHROPIC_AUTH_TOKEN","ANTHROPIC_BASE_URL","ANTHROPIC_DEFAULT_SONNET_MODEL"`
- **风险**: SubprocessEnvCleaner 漏掉 DEEPSEEK/SENSENOVA/ZHIPUAI 等新供应商密钥,CI 环境可能泄露
- **修复**: 3处改造,SubprocessEnvCleaner补齐全部8个供应商密钥

### 🟡 中等级(语义略有差异)

#### 7. Shell 名/解释器命令 — 在 2 处定义
- **唯一数据源**: DangerousCommandCatalog.InterpreterCommands
- **重复**: BashPermissionChecker.cs:34-35 多了 fish/csh/tcsh;PsAstParser.cs:325,339 有 pwsh 列表
- **风险**: 列表不一致,新增解释器需改 2 处

#### 8. PowerShell 别名映射 — 在 2 处定义
- **唯一数据源**: PsAliases
- **重复**: PsModeValidation.cs:41-64 CommonAliases 字典硬编码 21 个别名
- **风险**: 与 PsAliases.AliasMap 不同步

#### 9. PowerShell 危险 cmdlet — 在 2 处定义
- **唯一数据源**: PsDangerousCmdlets
- **重复**: PsSecurityChecker.cs:114 硬编码 `invoke-expression`/`iex`
- **风险**: 若 PsDangerousCmdlets 移除 invoke-expression,PsSecurityChecker 仍拦截

#### 10. Git 子命令列表 — 在 2 处定义
- **重复**: ReadOnlyCommandDetector.cs:46-65(Safe/Dangerous) + CommandDangerClassifier.cs:229-239(只读白名单)
- **风险**: 分类标准不同,新增 git 子命令需改 2 处

#### 11. Claude 兼容性常量 — 在 2 处定义
- **唯一数据源**: ClaudeCompatConstants
- **重复**: InstallGitHubAppCommand.cs:210,211,214,465,490 — `"claude","claude-review"` 工作流名
- **风险**: 改名时易遗漏

### 🟢 低等级(技术性重复)

#### 12. Bash AST 节点类型 — 在 5 处定义
- BashAstParser.cs:89,91,103 / BashAstSecurityWalker.WalkCommand.cs:100 / WalkStatements.cs:29 / CollectCommands.cs:10,64 / WalkArgument.cs:142-150
- **风险**: AST 节点类型由 TreeSitter 定义,属外部契约,风险较低

#### 13. Bash 内置命令 — 在 2 处定义
- **唯一数据源**: BashSecurityConstants.EvalLikeBuiltins
- **重复**: BashRegexCheckRegistry.cs:98,723 — `token is "command" or "builtin" or "noglob" or "nocorrect"`
- **风险**: noglob/nocorrect 不在 EvalLikeBuiltins 中,不一致

---

## 三、优先修复顺序 — ✅ 全部完成(P0+P1)

1. **危险命令名统一委托 DangerCommandDefinitions**(影响 9 处) — ✅ P0-①
2. **危险命令组合统一委托 DangerousCommandCatalog.Combinations**(影响 4 处) — ✅ P0-②
3. **保留设备名统一委托 RetainedDeviceNames.IsMatch**(影响 2 处) — ✅ P0-③
4. **供应商名统一用 VendorKind.ToValue() / Enum.GetValues<VendorKind>()**(影响 5 处) — ✅ P1-⑤
5. **API Key 环境变量名统一用 ProviderEnvVar.ToValue()**(影响 3 处) — ✅ P0-④
6. **默认模型名提取统一 DefaultModelCatalog**(影响 3 处) — ✅ P1-⑥
