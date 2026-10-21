# 0145. native_gui 顶层自治与 Core/Host 分层

- 状态：accepted（2026-10-21 B1-B4 落地：搬迁完成/编译零错误/253 测试绿/exe 真机启动全链路验证）
- 日期：2026-10-21
- 决策者：项目架构组（用户方向确认 + AI 调研）

## 背景

GUI（DSH WebView2 宿主，ADR 0143）当前位于 `modules/apps/gui/`，与 16 功能域就近隔离体系（ADR 0137）存在三重错位：

1. **语义错位**：GUI 是引擎的展示宿主而非引擎功能域，塞在 `modules/apps/` 与 native_tui（顶层自治）结构不对称。TUI 的 csproj 注释原文即"跨 slnx 直引引擎库（照抄 gui 先例）"——两者引擎接入同款，差异仅在目录自治级别与分层。
2. **平台传染**：WebView2/CsWinRT 要求 `net10.0-windows` TFM，单项目混装导致协议层（web_host 20 个 DshXxx 类，19 个零 UI）也被迫 windows TFM；测试项目随之 windows TFM → CI 需要 `apps-gui` windows-latest 专属 job（CsWinRT 投影工具链物理上只能 Windows 跑的根源）。
3. **分层缺失**：TUI 有 Core（零依赖渲染）+ Host（引擎装配）+ tests 同仓三段式；GUI 单项目混装协议层、表现层、引擎装配，跨层耦合点散落（`App.LogDiag` 静态定义在 Avalonia `App` 类上被协议层 34 处调用）。

调研结论（2026-10-21 全量扫描 50 个 .cs）：
- Core 直迁 31 文件 / Host 16 文件 / 需拆分 3 文件（GuiPalette、AppearanceCatalog、GlobalUsings）
- 测试 27/29 不依赖 Avalonia.Headless，可纯 net10.0（ubuntu）运行
- `App.LogDiag` 的实现（写 `.jcc/dumps/startup_timing.log`）本身零 UI，只是定义位置错误

## 决策

建立顶层 `native_gui/`，与 `native_tui/` 平级同构（apps/src/tests 三段）：

```
native_gui/
  apps/NativeGui.Host/        net10.0-windows TFM，Exe
      WebView2 + Avalonia 表现层 + JccChatSession 引擎装配 + GuiAppResources/GuiControlStyles
      webui/ 静态资源、app.manifest
      ProjectReference: NativeGui.Core + JoinCode.Engine + WebView2/CsWinRT/Avalonia 包
  src/NativeGui.Core/         net10.0 纯 TFM，Lib
      web_host/ 协议层（19 个 DshXxx，DshWebUiWindow 除外）
      hosting/ 契约（IJccChatSession/PermissionConfirmation/BackgroundAgentInfo/PlaceholderChatSession/WorkspaceReader/GuiPermissionConfirmationHandler）
      persistence/（8 文件全纯逻辑）
      theming/ 数据面（GuiPalette/AppearanceCatalog 拆 partial：Scheme 数据在 Core，Avalonia 渲染面在 Host）
      webview/ 协议面（BridgeProtocol/PermissionPromptBroker/WebChatFlow）
      GuiDiag 静态诊断日志（App.LogDiag 抽象迁入，Host 的 App 委托）
      dsh_web.zip 嵌入资源（LogicalName 不变）
      ProjectReference: Scheduling + BclBridge + EnumMetadata.Generator
  tests/NativeGui.Core.Tests/ 纯 net10.0（ubuntu 可跑，承接 web_host/webview/persistence/hosting 契约测试）
  tests/NativeGui.Host.Tests/ net10.0-windows（Headless 视觉测试 + JccChatSession 装配测试）
```

关键取舍：
1. **命名空间暂保持 `JoinCode.Gui.*` 不变**（RootNamespace 延续），迁移安全性优先、每批 diff 最小化；统一改命 `NativeGui.*` 另行独立批次（python 脚本批量替换）。
2. **`App.LogDiag` → `GuiDiag`**：静态实现迁 Core（零 UI），Host 的 `App.LogDiag` 委托 `GuiDiag.Log`，34 处协议层调用批量替换。
3. **IJccChatSession 随迁 Core**：接口与 DTO（318 行）全为 Abstractions 类型零 UI；`JccChatSession` 实现（EngineSessionFactory 装配）留 Host。
4. **启动速度非本 ADR 目标**：启动大头是 WebView2 冷启动（~6s，实测日志），搬迁不减；引擎组装已与 WebView2 并行且 #439 已优化。预热/首帧骨架另行立项。

## 替代方案（考虑过但放弃）

| 方案 | 放弃原因 |
|------|----------|
| 只搬目录不分层 | 收益仅目录平级；windows TFM 传染与 CI 平台限制原样保留 |
| 保持现状（modules/apps/gui） | ADR 0137 就近隔离语义对"非引擎域的展示宿主"不成立；CI 双平台拆分长期维护成本 |
| 原生渲染 GUI（抛弃 DSH WebView2） | 推翻 ADR 0143，TASK058 B1-B8 的 DSH 对齐工作失去消费方；启动快但重写量大 |
| 桥进程协议（bridge_serve） | 与 ADR 0143 阶段二结论一致拒绝：进程内直连零协议税 |

## 实施批次（每批 编译+测试+commit）

| 批次 | 内容 |
|------|------|
| B1 | 建 `NativeGui.Core`（纯 net10.0），git mv 迁 31 文件 + GuiDiag 抽象 + theming partial 拆分；旧 JoinCodeGui 引 Core，GUI 全量测试绿 |
| B2 | 建 `NativeGui.Host`（windows TFM），迁表现层 16 文件 + webui 资源；旧 `modules/apps/gui` 清空归档；Host exe 真机启动验证 |
| B3 | 测试拆分 `NativeGui.Core.Tests`（纯 net10.0）+ `NativeGui.Host.Tests`（windows）；删旧测试项目 |
| B4 | slnx（Gui.slnx 重指 / module.slnx 移除 gui）+ CI（apps-gui 拆 core-ubuntu + host-windows；gui-daily/aot-publish 路径更新） |
| B5（可选独立 PR） | 命名空间统一 `JoinCode.Gui.*` → `NativeGui.*`（python 批量替换） |

## 风险与对策

- **EmbeddedResource LogicalName 硬编码**（`JoinCode.Gui.dsh_web.zip` / `JoinCode.Gui.Appearance.json`）：LogicalName 显式声明与程序集名解耦，迁移时保持字符串不变即零影响。
- **InternalsVisibleTo**：Core 需 `InternalsVisibleTo("NativeGui.Host")` + `InternalsVisibleTo("JoinCode.Gui.Tests")`（测试程序集名 B1-B3 期间不变）。
- **跨层引用回归**：每批编译零警告容忍（TreatWarningsAsErrors）兜底；任何 Core 文件引入 Avalonia/WebView2 using 即编译失败。
- **CI 双平台矩阵**：B4 前旧 `apps-gui` job 保持不动，B4 一次切换。

## 关联

- 上游：ADR 0137（就近隔离）、ADR 0138（WebView2 四件套）、ADR 0143（DSH 前端托管）
- 同构参照：`native_tui/`（ADR 0140/0142 的 Core/Host 分层先例）
- 任务文档：`docs/proposed/task/TASK059-native_gui搬迁与CoreHost分层.md`
