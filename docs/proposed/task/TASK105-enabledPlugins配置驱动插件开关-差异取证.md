# TASK105 — enabledPlugins 配置驱动插件开关（TASK103 §2.3 #36 取证）

> 状态：**取证完成 — 待用户裁决** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#36 / TASK068-C2**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`（下文 TS 行号均指该树）
> 本仓库：`D:\project\w2`（下文 CS 行号均指该树）

---

## 0. 结论（先看这里）

TASK068 原文 :179 的判定「PluginContext 只有 IServiceCollection 无 IServiceProvider（容器未 build），无法在插件 LoadAsync 时读 WorkflowConfig 过滤 —— 需 WorkflowPluginHost 加载前过滤或 PluginContext 架构改造」——**前提本身过时，需纠正**：

1. **插件不需要在 LoadAsync 读配置**：本仓 `IWorkflowPlugin` 是三段生命周期，`LoadAsync(PluginContext)` → `InitializeAsync(IServiceProvider)`（`IWorkflowPlugin.cs:27/32`）。root 服务的正当入口是 `InitializeAsync`，`TelemetryPlugin.InitializeAsync` 已在实际用 `GetRequiredService<TelemetryService>()`（`TelemetryPlugin.cs:31`）。所以「ctx 无 IServiceProvider 导致无法读 WorkflowConfig」是伪命题 —— 那个阶段本就不该读。
2. **真正缺口是配置写了不生效**：`SettingsJson.EnabledPlugins`（`SettingsJson.cs:293`，`PluginSettings{enabled,config}` :658-669）能反序列化（测试 `SettingsJsonDeserializationTests.cs:108-109` 断言 `dream.enabled==true`），但全仓**零运行时消费点** —— 没有任何一处读它来决定加载/不加载插件。与 #35 sandbox 同型「安全假配置」。
3. **本仓已有一套插件开关且正在生效**：`McpInitModule.StartPluginLoads`（:83-104）用 `capabilities.IsEnabled(CapabilityNodes.Dream/CodeIndex)` 决定是否加载 dream / CSharpLanguage 插件（TASK100 S7 落地，profile 可裁剪）。这套机制与基准 `enabledPlugins` **语义重叠、数据源不同**：前者是编译期 catalog + profile（面向部署环境），后者是用户可改的 settings.json（面向最终用户）。

> 一句话：**不是「PluginContext 缺 IServiceProvider」的架构改造题，是「基准的用户级 settings 开关」与「本仓已有的 profile/capabilities 开关」两套机制如何共存的取舍题**。

---

## 1. 四问回答（调研前置纪律）

### 问1 专家隐性实践
配置驱动的插件开关，成熟实现会做三件事：①默认值明确（基准「用户设置 > 插件默认 > true」，缺设置即启用，`builtinPlugins.ts:71-77`）；②支持多来源合并（managed policy > project > user > local，`dependencyResolver.ts:276-282` 按 source 取）；③开关只决定「是否加载」，插件内部读服务走独立的初始化阶段 —— 本仓三段生命周期（Load→Initialize→Unload）恰好天然满足③，所以不需要把 provider 塞进 ctx。

### 问2 原始素材（一手，非转述）
- 基准：`QueryEngine.ts:534-549`（`loadAllPluginsCacheOnly()` 取 `enabled`，进 `buildSystemInitMessage.plugins`）；`plugins/builtinPlugins.ts:55-90`（`settings.enabledPlugins[pluginId]` 用户设置 > 默认 > true）；`utils/plugins/dependencyResolver.ts:276-282`（按 source 取 `enabledPlugins`）；`services/plugins/pluginOperations.ts:130/189`；`cli/handlers/plugins.ts:169/244/376`。
- 本仓：`SettingsJson.cs:288-293/658-669`；`SettingsJsonDeserializationTests.cs:108-109`；`ConfigKeyPathsTests.cs:143`（模板刻意不写该键）；`IWorkflowPlugin.cs:27/32`（三段生命周期）；`WorkflowPluginHost.cs:41/47/73/82`（已有 rootServiceProvider 且 Load 后 BuildServiceProvider）；`PluginContext.cs:9-24`（刻意只暴露注册侧）；`PluginConfig.cs:6-20`（本仓自造 `DisabledPlugins`/`AutoLoadExternalPlugins`）；`McpInitModule.cs:83-104`（capabilities 开关）；`WorkflowConfig.cs:72`（`PluginConfig Plugins`）。

### 问3 具体方法与工具
两侧源码 grep + 逐文件读；本仓用 `grep -rn "EnabledPlugins"` 区分「映射/校验/测试」与「运行时消费」，确认零消费点；对照 `IWorkflowPlugin` 生命周期判定「ctx 无 provider」是否真构成阻塞。

### 问4 独立验证来源
本仓已有测试 `SettingsJsonDeserializationTests.EnabledPlugins...` 证明配置能读进来；`ConfigKeyPathsTests` 证明模板不生成该键；`PluginContextBackgroundTaskTests`/`PluginContextWeakSubscribeTests` 证明 ctx 现有能力完整。基准侧消费链有多个独立调用点（QueryEngine / builtinPlugins / dependencyResolver / pluginOperations / cli handlers）互相印证，非单一转述。

---

## 2. 两侧差异表

| 维度 | 基准 Claude Code | 本仓库现状 | 判定 |
|---|---|---|---|
| 配置结构 | `enabledPlugins[pluginId] = boolean | array`（直接进入 settings） | `SettingsJson.EnabledPlugins: Dictionary<string, PluginSettings{enabled?,config}>`（:293/658） | 结构近似（对象 vs bool/array 有差异，但可映射） |
| 默认值 | 用户设置 > 插件默认 > true（`builtinPlugins.ts:71-77`） | 无消费即为「开关不存在」 | 真缺口 |
| 多来源 | managed/project/user/local 分源取（`dependencyResolver.ts:276-282`） | `SettingsMergeStrategy.DictionaryMerge` 已合并，但合完没人读 | 半通（合并有、消费无） |
| 加载过滤 | `loadAllPluginsCacheOnly()` → `enabled` → init message（`QueryEngine.ts:534-549`） | `capabilities.IsEnabled(Dream/CodeIndex)` 决定两个插件（`McpInitModule.cs:88-102`） | **两套机制并存** |
| 插件读服务 | ctx 即服务容器（Cordis 风格） | `LoadAsync(ctx)` 注册 / `InitializeAsync(IServiceProvider)` 解析（`IWorkflowPlugin.cs:27/32`） | 实现不同，但**不构成缺口**（③天然满足） |
| 报告加载状态 | `buildSystemInitMessage.plugins`（把 enabled 插件报给客户端） | 无对应物 | 无产生点（本仓无 init message 协议） |

---

## 3. 三类归因

- **真缺口**：`SettingsJson.EnabledPlugins` 零运行时消费 —— 用户写 `enabledPlugins` 不生效（与 #35 sandbox 同型）。
- **转述过时**：TASK068 :179「PluginContext 无 IServiceProvider 是架构限制」—— 三段生命周期已让该阶段不需要 provider；`WorkflowPluginHost` 还持有 `rootServiceProvider`（:41/47），压根不是「无」。
- **架构分歧（需裁决）**：本仓 `capabilities`/profile 开关与基准 `enabledPlugins` settings 开关语义重叠。这是唯一需要拍板的地方。

---

## 4. 待裁决选项

**问题**：`enabledPlugins` 的用户级开关要不要接？与本仓已有 `capabilities` 开关如何共存？

- **选项 A（推荐 — 最小且诚实）**：认定 `capabilities`/profile 是本仓插件开关的**唯一事实源**，`enabledPlugins` 判为「不适用」（对齐基准但本仓用 profile 表达，正如 #35 的 macOS 分支）。只写澄清文档 + 总账归类，零生产代码。理由：现有开关已按 TASK100 S7 生效（profile 可裁剪 dream/codeIndex），再加一套用户 settings 开关会造成「哪里关掉才是真的」的二义性。
- **选项 B（接通，双源合并）**：在 `McpInitModule.StartPluginLoads` 读 `WorkflowConfig.EnabledPlugins`（需先补映射），与 `capabilities` 取交集（「与」关系：任一关则关），按基准语义 `enabled!==false` 默认开。改动小（一个映射 + 一处加载判断 + 测试），但引入两套开关并存，需在文档明确优先级。
- **选项 C（架构改造）**：把 `enabledPlugins` 提升为唯一插件开关源，废弃 `capabilities` 里的 dream/codeIndex 节点。语义最纯，但属 TASK100 S7 的反向改动，牵动 profile/能力视图/门禁三方同源，风险最大。

> 未裁决前不改生产代码。
