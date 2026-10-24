# TASK071 — 模型目录引擎单源统一（GUI/TUI 不再各自实现）

> 状态：in-progress（批 1 完成；批 2-4 待实施）
> 来源：用户指出"引擎的模型列表方式已改，GUI 和 TUI 都需从引擎获取，而不是各自实现一套"
> 决策：用户选择 **A 全链统一** — 目录下沉到配置域、选择/持久化单链、发现收口、两端变薄
> 角色划分：Abstractions 提供契约与目录服务实现，Guard 提供配置注册表/发现器，GUI/TUI 只做投影与交互

## 一、问题实证（统一前）

| 断点 | GUI 现状 | TUI 现状 | 引擎权威 |
|------|----------|----------|----------|
| 目录服务归属 | `IModelCatalog` 位于 `modules/composition/slash`，`NativeGui.Core` 只引用 Guard → 无法消费，转而在 `DshModelCatalogSource` 重读 settings | 直接消费 `IModelCatalog`，但供应商列表来自 `Enum.GetValues<VendorKind>()` | `ProviderDefinitionRegistry.GetRegisteredProviders()`（settings.json vendor 节点） |
| 模型元数据 | `DshModelItem { Id, Name = Id }`，丢失 DisplayName/ContextWindow | 使用 `ModelEntry`（元数据完整） | `ModelEntry`（id/displayName/contextWindow/description） |
| 切换链 | `JccChatSession.SetModelAsync` 只写 WorkflowConfig + flat "model" + 嵌套 vendor[profile].model；无 fast 跟随/effort 降级/历史 | `ModelPickerAssembly.ApplyModel` 写 FastModeService + flat "model"；不动 WorkflowConfig | `ModelCommand.ApplyModelSwitchAsync`：fast 跟随 + flat "model" + fast 自动关 + effort max→high + modelHistory |
| 发现 | `DshModelDiscoveryService` 自带 HttpClient + JSON 解析（与引擎重复） | 复用引擎 `IModelCatalog` | `ModelListFetcher` + `SettingsJsonModelWriter`（启动后台回写 models.json） |

## 二、批次划分

### 批 1 — 目录下沉（已完成）

| 项 | 内容 | 落点 |
|----|------|------|
| 契约迁移 | `IModelCatalog` / `ModelCatalog` 从 `modules/composition/slash/ai/model/` 迁入 Abstractions 配置域，`[Register]` 注册随之生效 | `modules/abstractions/abstractions/abs_core/configuration/llm/IModelCatalog.cs`、`ModelCatalog.cs` |
| 唯一读取形状 | 新增 `GetProviders()`（注册表顺序 + 显示名 + 默认模型 + 清单）与 `GetSnapshot(currentProvider, currentModelId)`（当前模型补全 / 未知供应商占位） | `ModelCatalogSnapshot.cs`（`ModelProviderCatalog` / `ModelCatalogSnapshot`） |
| 测试 | 8 项单测锁定顺序/元数据/白名单/自定义模型/补全/占位/大小写 | `test/unit/abs.tests/llm/chat/ModelCatalogSnapshotTests.cs` |

### 批 2 — 选择服务（待实施）

| 项 | 内容 | 落点 |
|----|------|------|
| 单一写入口 | `IModelSelectionService`（Guard）：切换模型/供应商、fast 跟随、flat "model" + 嵌套 `vendor[profile].model`、`profile` 持久化、fast 自动关、effort max→high 降级、modelHistory | 新增 Guard 服务 |
| 命令委托 | `ModelCommand` / `VendorCommand` 的 `ApplyModelSwitchAsync` / 供应商切换改为调用选择服务（行为不变） | `modules/composition/slash/ai/model/` |
| 两端委托 | TUI `ModelPickerAssembly` / `VendorPickerAssembly`、GUI `JccChatSession.SetModelAsync/SetVendorAsync` 改调同一服务 | `native_tui/apps/NativeTui.Host/`、`native_gui/apps/NativeGui.Host/hosting/` |

### 批 3 — 发现收口（待实施）

| 项 | 内容 | 落点 |
|----|------|------|
| 显式拉取 | `ModelListFetcher` 支持按 (vendor, endpoint, apiKey) 显式拉取并返回失败原因 | `modules/guard/guard/configuration/configuration2/core/model_fetch/` |
| GUI 复用 | `llm/discoverModels` 改走引擎发现器，可选回写 models.json（与启动后台同一写手） | `native_gui/src/NativeGui.Core/web_host/` |
| 删除重复实现 | `DshModelDiscoveryService` 的自带 HTTP/解析逻辑归档到 `.xxx/`（不删除） | `native_gui/src/NativeGui.Core/web_host/` |

### 批 4 — 两端变薄（待实施）

| 项 | 内容 | 落点 |
|----|------|------|
| GUI 投影 | `DshModelCatalogSource` 直接投影 `ModelCatalogSnapshot`（含 displayName/contextWindow），预挂载阶段用同一 Guard 组件构建，挂载后用引擎实例 | `native_gui/src/NativeGui.Core/web_host/DshModelCatalogSource.cs` |
| 引擎门面 | `IJccChatSession` 暴露 catalog/snapshot + 目录刷新方法（模型页写入后刷新） | `native_gui/src/NativeGui.Core/hosting/IJccChatSession.cs` |
| 注册表热刷新 | `ProviderDefinitionRegistry` 支持 reload（GUI 模型页新增 vendor 后可见） | `modules/guard/guard/configuration/configuration2/core/providers/` |
| TUI 供应商 | 供应商列表改 `catalog.GetProviders()`，不再枚举 `VendorKind` | `native_tui/apps/NativeTui.Host/VendorPickerAssembly.cs` |

## 三、验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 批 1：`ModelCatalog.GetProviders()` / `GetSnapshot()`（Abstractions） | 单测 8 项；后续批 4 GUI/TUI | ✅ | ✅ 单测全绿（710/710 abs.tests）；ADR 0080 手动 exe 验收留待批 4 端到端 |
| 批 2：`IModelSelectionService` 统一切换链 | ModelCommand/VendorCommand/TUI picker/GUI session | ❌ | ❌ |
| 批 3：`ModelListFetcher` 显式拉取 + GUI discover 复用 | GUI 模型页"拉取模型列表" | ❌ | ❌ |
| 批 4：`ModelCatalogSnapshot` → DSH DTO；TUI 供应商来自注册表 | GUI 模型选择器/选择后下一次请求；TUI `/vendor` | ❌ | ❌ |

## 四、批 1 验证记录

- 编译：`Abstractions` / `Slash` / `NativeTui.Host` Debug 增量编译 0 警告 0 错误
- 单测：`abs.tests` 710 通过 0 失败（含新增 `ModelCatalogSnapshotTests` 8 项）
- 回归：composition 命令测试（Model/Vendor/Status/Context/Effort）全绿

<!-- 🤖 Auto Decision: 2026-10-25 -->
<!-- 决策: IModelCatalog/ModelCatalog 下沉到 Abstractions 配置域，而不是在 NativeGui.Core 新建副本 -->
<!-- 原因: 目录服务依赖（IProviderDefinitionRegistry/IModelConfigLoader/WorkflowConfig/ModelEntry）全部位于 Abstractions；GUI 仅引用 Guard 才拿不到 -->
<!-- 替代方案: 让 NativeGui.Core 引用 Slash（引入整个命令层，违背分层）；GUI 自读 settings（当前重复实现，正是要消除的）-->
<!-- 验证: Abstractions/Slash/NativeTui.Host 编译通过；abs.tests 710 全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-25 -->
<!-- 决策: UI 读取采用 ModelCatalogSnapshot（当前选择 + 供应商分组 + 当前模型补全）单一 DTO，而非让两端各自拼装 -->
<!-- 原因: GUI 现有 VendorModelMap 只有 id，丢失显示名/上下文窗口；TUI 逐项查目录导致两端形状漂移 -->
<!-- 替代方案: 保留 VendorModelMap 并追加元数据字典（两套结构仍需同步，批 4 仍需重构）-->
<!-- 验证: 快照补全/占位/白名单/自定义模型 8 项单测通过 ✅ -->
