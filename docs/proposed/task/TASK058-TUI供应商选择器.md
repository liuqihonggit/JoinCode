# TASK058 — TUI 供应商选择器（/vendor 模态）

> 状态：in_progress（2026-10-21 开工）
> 前置：TASK057 完结；Bug1/Bug2 验收通过后用户新需求："怎么切换供应商，你好像没有做"
> 用户决策：模态选择器方案（对齐 /model 交互；引擎切换逻辑现成，只做 TUI 交互层）

## 调研结论

| 资产 | 现状 |
|------|------|
| 引擎 `/vendor` 命令 | ✅ 完整（VendorCommand：10 供应商校验 + 默认模型跟随 + profile 持久化；`/vendor 空格` 参数补全已随 Bug1 元数据管道自动可用） |
| GUI 切换链 | ✅ JccChatSession.SetVendorAsync — `config.Provider.Vendor = vendor` 原地改（ProviderConfig 的 Vendor/ModelId 是 `{ get; set; }` 可变属性，DI 单例引用不变全消费方可见） |
| TUI 模态先例 | ✅ ModelPicker 三件套（Engine 装饰器 + Coordinator 状态机 + Dialog 渲染），vendor 交互完全同构 |
| WorkflowConfig 获取 | ✅ EngineSessionFactory.Result.Config 直接暴露 + DI 单例 AddSingleton(config) |
| banner | ❌ SessionInfo 事件无 Vendor 字段（需加，末尾可选参数零破坏） |

## 方案（复用优先，禁止重复造轮子）

1. **Core**：`BridgeEvent.SessionInfo` 加 `Vendor = null` 可选参数；`BannerItem` 加 Vendor 属性渲染 `{vendor}/{model} · billing`（空退化）；`ReplBridgeDriver` 透传
2. **Core**：`ModelPickerDialog` 构造参数化 Title/Subtitle/Shortcuts（默认现文案）——vendor 复用整条对话框渲染链
3. **Core**：新增 `VendorPickerEngine`（对齐 ModelPickerEngine：拦截 `/vendor` 无参弹选择器；带参透传引擎直达命令）
4. **Host**：`VendorPickerAssembly` — LoadItems（VendorKind 枚举 → ModelPickerItem，DisplayName 用 IModelCatalog.GetProviderDisplayName，Description 显默认模型，IsCurrent 标记）+ ApplyVendor（config.Provider 原地改 + SetPrimaryModel + profile 持久化，对齐 GUI SetVendorAsync 语义）
5. **Host**：`EngineSession.BuildSessionInfoEvent` 加 Vendor（`_engine.Config.Provider.Vendor`）；`ModelPickerAssembly.CurrentProvider` 改读 WorkflowConfig 单例（vendor 切换后 /model 清单跟随，环境变量兜底保留）
6. **Program**：VendorPickerEngine 包 ModelPickerEngine 外层（装饰链）+ applyVendor 委托组合 ApplyVendor+RefreshSessionInfo；Demo 双入口接线

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| VendorPickerEngine（Core） | Program 装饰链 + Demo | ✅ | ✅ ADR 0080 真机（/vendor 弹框 10 项 + 切换 + banner vendor/model 同步） |
| BannerItem.Vendor 渲染 | ReplBridgeDriver.ApplySessionInfo | ✅ | ✅ |
| VendorPickerAssembly（Host） | Program applyVendor 委托 | ✅ | ✅ |
| SessionInfo.Vendor 字段 | EngineSession/Demo 种子事件 | ✅ | ✅ |
