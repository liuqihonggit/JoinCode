# 单数据源审查总纲(现状摘要 + 改造建议)

**调查工程**: `D:\project\w1` | **报告产出**: `D:\project\w3\.xxx\single_source_audit\`
**调查时间**: 2026-09-25 | **调查方法**: 5 个 explore 子代理并行检索 + 主代理综合分析

---

## 一、现状摘要

工程 `D:\project\w1` 对"单数据源"原则的遵守现状**整体良好**,已建立较完善的统一数据源体系:

| 维度 | 现状 | 评价 |
|------|------|------|
| 统一数据源类 | 21 个(DangerousCommandCatalog/RetainedDeviceNames/BrandConstants 等) | ✅ 完善 |
| [EnumValue] 特性 | 2882 个,300+ 文件 | ✅ 全面 |
| 多数据源类 | 46 个(11 真违规 + 2 部分 + 33 例外) | 🟡 有改进空间 |
| 硬编码映射表 | 87 处(0 高 + 19 中 + 68 低) | ✅ 良好 |
| 重复定义 | 13 类,约 50 处 | 🔴 最严重 |
| 枚举未 [EnumValue] | 8 个违规 + 16 个合理例外 | 🟡 少量 |
| 统一数据源违规消费 | 8 处 | 🟡 少量 |

**核心结论**: 最严重的违规是**重复定义**(13 类约 50 处),尤其是危险命令名(9 处)和危险命令组合模式(4 处)各自维护,导致安全策略不一致风险。其次是**多数据源类的冗余索引模式**(11 个真违规)。

---

## 二、分项统计

### 2.1 多数据源类(详见 01_multi_source_classes.md)
- **真违规 11 个**: TeamRegistry/SandboxManager/UsageStore/AnalyticsService/ChatRoomState/ImmutablePrefix/SessionScope/MapRegistry/TransportFallbackMetrics/AutoModeClassifier/SecurityPatterns
- **部分违规 2 个**: PathConstraintValidator/ReadOnlyCommandDetector
- **例外 33 个**: 语义不同无法合并
- **主要模式**: ①冗余查询索引(6 个) ②源+派生缓存(3 个) ③平行数组(1 个) ④双索引(2 个)

### 2.2 硬编码映射表(详见 02_hardcoded_mappings.md)
- **中违规 19 处**: 命令参数 Enum 字面量(5)、文件扩展名映射(3)、路径黑名单重复(4)、路径逃逸(2)、安全规则标签(1)、参数别名(1)、AppState 键(1)、证据权重(1)、记忆类型(1)
- **低违规 68 处**: 合理常量或已委托枚举

### 2.3 重复定义(详见 03_duplicate_definitions.md)
- **严重级 6 类**: 危险命令名(9 处)、危险命令组合(4 处)、保留设备名(2 处)、供应商名(5 处)、默认模型名(3 处)、API Key 环境变量(3 处)
- **中等级 5 类**: Shell 名(2 处)、PS 别名(2 处)、PS 危险 cmdlet(2 处)、Git 子命令(2 处)、Claude 兼容常量(2 处)
- **低等级 2 类**: Bash AST 节点类型(5 处)、Bash 内置命令(2 处)

### 2.4 统一数据源类(详见 04_unified_sources.md)
- **21 个统一数据源类**,消费引用总计 800+ 处
- **8 处违规重复硬编码**: BrandConstants(5 处 "JoinCode"/"jcc") + ModelCatalog(2 处 "gpt-4o") + McpMockServerConfig(1 处协议版本)
- **死代码**: McpConstants(已迁移至 JsonRpcConstants)
- **消费不足**: TaskTableGenerator(仅测试引用)

### 2.5 枚举 [EnumValue](详见 05_enum_value_status.md)
- **已用 [EnumValue]**: 2882 个特性,覆盖工具名/CLI 命令/类型/配置/安全/Agent/桥接/LLM 等核心领域
- **未用 [EnumValue] 违规 8 个**: IdeType/ForkState/BuddyRarity/WorktreeCleanupMode/NodeCompletionOutcome/CacheScope/BudgetType/FlagArgType
- **手动 KV 相同映射 2 处**: ParameterNameRepairer old_string/new_string 恒等映射
- **有限集合未枚举化 1 处**: ExtractMemoriesPromptTemplate MemoryTypes 混用

### 2.6 例外(详见 06_exceptions.md)
- **33 个多数据源例外类**: 键/值类型不同(12)、语义域不同(13)、装饰性数据(3)、算法结构(5)、正反映射对(1)
- **16 个无法 [EnumValue] 枚举**: [Flags] 位标志(5)、private/internal(6)、状态机事件(1)、不同生成器(4)
- **10 类无法委托硬编码**: Win32 常量/RFC 标准地址/物理单位/Tree-sitter 节点类型/Unicode 常量/装饰性数据/词库/CLI 参数

---

## 三、改造建议(按优先级排序)

> **改造完成时间**: 2026-09-26 | **PR**: #289(P0-P2 已合并) + #290(P3-P4 已合并)

### 🔴 P0 — 安全相关重复定义(最严重,不一致风险高)

| # | 改造项 | 影响范围 | 建议方案 | 状态 |
|---|--------|----------|----------|------|
| 1 | 危险命令名统一委托 DangerCommandDefinitions | 9 处硬编码 | 所有消费方改为引用 DangerCommandDefinitions 常量或 DangerousCommandCatalog.Commands | ✅ 完成 |
| 2 | 危险命令组合统一委托 DangerousCommandCatalog.Combinations | 4 处硬编码 | AutoModeClassifier/PermissionConfig/ShellExecutionConfig/DestructiveCommandAnalyzer 改为引用 Combinations | ✅ 完成(2处架构层级限制保留) |
| 3 | 保留设备名统一委托 RetainedDeviceNames.IsMatch | 2 处硬编码 | PathConstraintValidator 改用 RetainedDeviceNames 查询;RetainedDeviceNames 内部正则与 Pattern 常量用源码生成器保证一致 | ✅ 完成 |
| 4 | API Key 环境变量名统一用 ProviderEnvVar.ToValue() | 3 处硬编码 | SettingsLoader/SubprocessEnvCleaner/DotEnvConfig 改用枚举;SubprocessEnvCleaner 补齐新供应商密钥 | ✅ 完成 |

### 🟠 P1 — 配置相关重复定义

| # | 改造项 | 影响范围 | 建议方案 | 状态 |
|---|--------|----------|----------|------|
| 5 | 供应商名统一用 VendorKind.ToValue() | 5 处硬编码 | VendorCommand 特性改为引用枚举;BridgeMainCommand/DotEnvConfig/StartupWorkflow/SettingsLoader 改用枚举 | ✅ 完成 |
| 6 | 默认模型名提取统一 DefaultModelCatalog | 3 处硬编码 | 新建 DefaultModelCatalog 统一默认模型 ID;PipeQueryService/StartupWorkflow/SettingsLoader 委托 | ✅ 完成 |
| 7 | 统一数据源违规消费修复 | 8 处 | BrandConstants(5 处 "JoinCode"/"jcc") + ModelCatalog(2 处 "gpt-4o") + McpMockServerConfig(1 处协议版本) 改为引用常量 | ✅ 完成 |

### 🟡 P2 — 多数据源类冗余索引

| # | 改造项 | 影响范围 | 建议方案 | 状态 |
|---|--------|----------|----------|------|
| 8 | 引入 MultiIndexRegistry 泛型基类 | 6 个类 | 统一管理"主数据源+冗余查询索引",消除手工同步(TeamRegistry/SandboxManager/UsageStore/AnalyticsService/ChatRoomState/SessionScope) | ⚠️ 例外(O(1)索引不应降级为O(n)遍历) |
| 9 | 源+派生缓存合并 | 3 个类 | AutoModeClassifier/SecurityPatterns/PathConstraintValidator 静态初始化时合并源与派生正则 | ✅ 完成 |
| 10 | 平行数组合并 | 1 个类 | TransportFallbackMetrics 3 个 int[] 合并为 TransportStat[] 结构数组 | ✅ 完成 |
| 11 | 双索引合并 | 2 个类 | ChatRoomState/ImmutablePrefix 引入专用有序字典结构 | ⚠️ 例外(O(1)索引不应降级为O(n)遍历) |

### 🟢 P3 — 枚举化与硬编码委托

| # | 改造项 | 影响范围 | 建议方案 | 状态 |
|---|--------|----------|----------|------|
| 12 | 8 个枚举加 [EnumValue] | 8 个枚举 | IdeType/ForkState/BuddyRarity/WorktreeCleanupMode/NodeCompletionOutcome/CacheScope/BudgetType/FlagArgType | ✅ 8个全部完成(生成器已支持internal枚举,可访问性跟随源枚举) |
| 13 | 命令参数 Enum 特性委托枚举 | 5 处 | MemoryCommand/ConfigCommand/DiffCommand/QuadtreeToolHandlers/Program 改为引用对应枚举常量 | ✅ 完成(新建ConfigAction+OutputFormat枚举) |
| 14 | 扩展名映射统一 LanguageMapCatalog | 3 处 | SessionScanner/ReferenceResolver 合并 | ✅ 2处完成(LspFileSync语义不同保持独立) |
| 15 | 排除目录统一 CodeIndexExcludedDirCatalog | 3 处重复 | FileWatcherIntegration/IncrementalUpdater/CodeIndexer 委托 | ✅ 完成 |
| 16 | BomStripper 委托 FileFilter | 1 处双套 | ExcludedDirectories 改为 => FileFilter.s_commonExcludedDirs | ✅ 完成 |
| 17 | ParameterNameRepairer 删除 KV 相同恒等映射 | 2 处 | old_string/new_string 冗余条目 | ✅ 完成 |
| 18 | ExtractMemoriesPromptTemplate 改用 MemoryTypeEnumConstants | 1 处 | 消除混用 | ✅ 完成(kit/prompts不引用lib/vault,统一硬编码+注释) |
| 19 | AppStateSettingSyncService 改用 ConfigKey.ToValue() | 1 处 | 字符串键委托枚举 | ✅ 完成(键大小写bug修复+OrdinalIgnoreCase) |

### ⚪ P4 — 清理(非违规但值得关注)

| # | 改造项 | 说明 | 状态 |
|---|--------|------|------|
| 20 | McpConstants 死代码清理 | 已迁移至 JsonRpcConstants | ✅ 完成(归档到.xxx/) |
| 21 | TaskTableGenerator 消费不足排查 | 确认是否待启用功能 | ✅ 排查完成(无消费方,有测试,保留) |
| 22 | VendorCommand 自相矛盾修复 | 第8行特性与第28行 Enum.GetValues 统一 | ✅ 完成(委托VendorKindEnumConstants) |
| 23 | ReadOnlyCommandDetector/SecurityPatterns 职责拆分 | 数据源数量多,按检测路径/职责拆分到多个专注类 | ✅ 排查完成(当前合理,暂不拆分) |

---

## 四、改造原则

1. **唯一数据源 + 委托消费**: 每个概念只在一处定义,消费方通过常量/枚举/方法引用获取,禁止双向维护
2. **枚举 + [EnumValue] + 源码生成器**: 有限集合字符串标识必须枚举化,用 ToValue()/FromValue() 消费,禁止手动维护 KV 映射
3. **MultiIndexRegistry 统一冗余索引**: 主数据源 + 派生索引由泛型基类统一管理,消除手工同步
4. **源+派生合并**: 源数组与预编译正则/归一化版本在静态初始化时合并,禁止分离维护
5. **渐进式改造**: 每次改一个概念,编译+测试+提交,禁止一次性大规模重构

---

## 五、报告文件清单

| 文件 | 内容 |
|------|------|
| 00_summary.md | 本总纲(现状摘要 + 改造建议) |
| 01_multi_source_classes.md | 多数据源类清单(46 个) |
| 02_hardcoded_mappings.md | 硬编码映射表清单(87 处) |
| 03_duplicate_definitions.md | 重复定义清单(13 类约 50 处) |
| 04_unified_sources.md | 统一数据源类清单(21 个) + 违规消费(8 处) |
| 05_enum_value_status.md | 枚举 [EnumValue] 现状(2882 特性 + 8 违规) |
| 06_exceptions.md | 例外/不舒服点清单(33 类 + 16 枚举 + 10 硬编码) |
