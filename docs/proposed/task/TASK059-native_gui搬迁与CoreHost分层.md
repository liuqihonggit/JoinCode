# TASK059 — native_gui 搬迁与 Core/Host 分层

> ADR：[0145](../adr/0145-native-gui-top-level-autonomy-core-host-split.md)（proposed）
> 目标：GUI 从 `modules/apps/gui/` 迁至顶层 `native_gui/`，与 `native_tui/` 平级同构（apps/src/tests 三段）；协议层拆纯 net10.0 Core，测试 ubuntu 可跑，CI windows job 收窄到表现层。
> 执行方式：每批「编译 → 测试 → commit」，命名空间暂保持 `JoinCode.Gui.*`（统一改命另立批次 B5）。

## 一、切分清单（2026-10-21 全量调研结论）

- Core 直迁 31 文件：web_host 19（除 DshWebUiWindow）+ hosting 8（IJccChatSession/PermissionConfirmation/BackgroundAgentInfo/PlaceholderChatSession/WorkspaceReader/WorkspaceFile/GuiPermissionConfirmationHandler + WorkspaceReader 配套）+ persistence 8 + webview 3（BridgeProtocol/PermissionPromptBroker/WebChatFlow）
- Host 16 文件：DshWebUiWindow/WebUiWindow/WebView2Host/App/Program/JccChatSession/GuiInteractionModule/AvaloniaInteractiveService/GuiWindowShakeService/ShakeAnimationHelper/GuiAppResources/GuiControlStyles + theming 渲染面
- 需拆分 3 文件：GuiPalette（数据面 Core / 渲染面 Host partial）、AppearanceCatalog（同）、GlobalUsings（两项目各自重建）
- 跨层障碍 2 个：`App.LogDiag`（协议层 34 处调用 → 抽 `GuiDiag` 静态进 Core，App 委托）、`IJccChatSession`（318 行零 UI 契约 → 随迁 Core，JccChatSession 实现留 Host）
- 资源硬编码：`JoinCode.Gui.dsh_web.zip` / `JoinCode.Gui.Appearance.json` LogicalName 保持不变（与程序集名解耦）
- 测试：27/29 纯 net10.0 可跑；Headless 仅 GuiPaletteContrastTests + VisualTestApp

## 二、批次计划

| 批次 | 内容 | 状态 |
|------|------|------|
| B1 | NativeGui.Core 建立 + 31 文件 git mv + GuiDiag + theming partial + 旧项目引 Core | ✅ 8de8686eb（编译 0 错误 + 253 绿） |
| B2 | NativeGui.Host（AssemblyName=NativeGui.Host）+ 表现层 23 文件 git mv + 旧 csproj 归档 .xxx | ✅ 真机启动全链路（listening/目录 6 组/引擎 attach/前端 RPC） |
| B3 | 测试拆分 NativeGui.Core.Tests（纯 net10.0，236 测试 ubuntu 可跑）+ NativeGui.Host.Tests（windows TFM，34 测试） | ✅ 2026-10-22 拆分落地（CI matrix gui-core=ubuntu/gui-host=windows + 分支保护 required 同步） |
| B4 | Gui.slnx 三段登记/module.slnx 删 gui/CI 三 workflow 路径更新（python yaml 校验过） | ✅ CI 合入后验收 |
| B5 | （可选独立 PR）命名空间统一 NativeGui.* | ⬜ |

## 三、四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| B1 NativeGui.Core（纯 net10.0 协议层） | Host 项目引用 + Core.Tests | ⬜ | ⬜ |
| B2 NativeGui.Host（windows TFM 表现层+引擎装配） | exe 真机启动（DSH 前端加载/引擎 attach/RPC 通） | ⬜ | ⬜ |
| B3 Core.Tests ubuntu 可跑 | CI matrix | ⬜ | ⬜ |
| B4 CI 双平台收窄 | apps-gui job 拆分 | ⬜ | ⬜ |

## 四、风险

- InternalsVisibleTo 双向补齐（Core→Host/Tests）
- CI B4 前旧 apps-gui job 不动，一次切换
- 启动速度不在本任务范围（WebView2 冷启动 ~6s 另行立项）
