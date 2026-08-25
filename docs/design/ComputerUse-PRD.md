# Computer Use 能力建设 PRD

> **版本**：v0.1（基线）
> **日期**：2026-08-25
> **状态**：草案 / 待评审
> **作者**：差距分析 derived from 能力调查 + 用户需求清单
> **关联**：本 PRD 基于对 JoinCode 七层架构的全面能力调查，对照用户提供的「Computer Use 五层渴望 + 撤销元意识」需求清单构造。

---

## 0. 背景与动机

JoinCode 当前是一个**面向代码工程的 CLI/TUI Agent**，其「Hands」操作的是文件系统 + Shell 命令 + HTTP + LLM 对话，**不是桌面像素**。用户期望将能力边界从「代码工程」扩展到「桌面操作」，使 Agent 具备完整的「认知-执行-反馈」闭环。

用户需求清单定义了 **5 个核心层级 + 1 项隐藏安全能力**：

| 层级 | 渴望 |
|------|------|
| ① 视觉理解层 | 像素级语义理解，看懂 UI 布局/颜色含义/进度隐喻，而非死记坐标 |
| ② 多模态操作链 | 跨应用拖拽、右键菜单链、悬停弹出等复合操作 |
| ③ 环境感知与容错 | 弹窗劫持、异步等待（感知沙漏/进度条）、意外处理 |
| ④ 系统级元操作 | 任务管理器级干预、快捷键宏录制、批量加速 |
| ⑤ 观察学习 | 演示模式：录制人类操作轨迹 → 抽象逻辑 → 回放/优化 |
| 隐藏·撤销元意识 | 不可逆操作前 0.5s 元认知 + 自动备份/确认 |

---

## 1. 目标与非目标

### 1.1 目标

- 在 JoinCode 七层架构中**渐进式补齐**桌面控制能力，使 Agent 能操作第三方 GUI 应用。
- **最大化复用**现有资产：浏览器截图链路、死循环干预状态机、安全护栏（Guard/Vault）、SystemActuator 执行框架。
- 保持 **NativeAOT 兼容**：所有新增能力禁止 `dynamic`/反射 emit，Win32 互操作用 P/Invoke + 源码生成器。
- 保持**七层架构隔离**：新能力按依赖链落入对应层，不破坏编译顺序。

### 1.2 非目标

- **不**替换现有 CLI/TUI 交互模式，Computer Use 作为**可选能力包**按需启用。
- **不**在本期实现完整的 RPA 平台，聚焦「让 Agent 能看懂屏幕 + 操作桌面 + 处理意外」。
- **不**引入不支持 NativeAOT 的微软 AI SDK（如 WinUI 依赖）。
- **不**做移动端（iOS/Android）桌面控制，仅 Windows 桌面（后续可扩展）。

---

## 2. 现状基线（差距分析结论）

### 2.1 能力现状矩阵

| 渴望层级 | 项目现状 | 评估 | 关键证据 |
|---------|---------|------|----------|
| ① 视觉理解 | `services/Eyes` = 代码索引+LSP（"看代码的眼"）；无 `analyzeImage` 工具；OCR 仅作 web 降级建议；截图塞 base64 给外部多模态 LLM | 🔴 完全空白 | `ModalityValidationMiddleware.cs:8` |
| ② 多模态操作链 | `Hands` = 文件/Shell 隐喻；`SystemActuator` 只跑命令行；`DesktopHandoffService` 仅打日志；无 Mouse/Keyboard/Window/Drag 类 | 🔴 完全空白 | `SystemActuatorBase.cs:150` |
| ③ 环境感知与容错 | LLM 维度强：4 层循环检测 + 3 级干预状态机 + 错误/超时恢复；GUI 维度零：无弹窗/沙漏/杀毒感知 | 🟡 半具备 | `InformationEntropyGuardian.cs:17`、`LoopInterventionMiddleware.cs:8` |
| ④ 系统级元操作 | Shell 执行 + 长任务 kill+重启 + 构建队列 cancel + 防休眠；无宏录制/批量加速/进程现场恢复 | 🟡 半具备 | `LongRunningTaskRegistry.cs:7`、`PreventSleepService.cs:22` |
| ⑤ 观察学习 | `services/Dream` = 会话记忆整合（非录制）；`VoiceService` 录音是 stub；无 Observe/Demonstrate/Teach 类 | 🔴 完全空白 | `DreamFeature.cs:10`、`VoiceService.cs:235` |
| 隐藏·撤销元意识 | 有 `core/safety/Guard`（命令扫描/沙箱/危险命令检测）+ `Vault`，但是命令文本审计，非 GUI 操作前元认知 | 🟡 CLI 维度具备 | `DestructiveCommandDetector.cs:76` |

### 2.2 可复用资产

| 资产 | 位置 | 复用方向 |
|------|------|----------|
| 浏览器截图链路 | `PuppeteerBrowserAutomationService.cs:51` | ① 视觉理解的图像采集起点 |
| 死循环干预状态机 | `LoopInterventionMiddleware.cs`（4 检测 + 3 级 `InterventionLevel`） | ③ GUI 环境意外的干预框架直接迁移 |
| 安全护栏 | `core/safety/Guard` + `Vault` + `DestructiveCommandDetector` | 隐藏·撤销元意识的命令级基础 |
| SystemActuator 执行框架 | `SystemActuatorBase.cs:150`（沙箱/超时/环境变量） | ④ 进程管理的执行底座 |
| 防休眠 P/Invoke | `PreventSleepService.cs:22`（kernel32） | 证明 Win32 P/Invoke + NativeAOT 路径已通 |
| 多模态 LLM 适配 | `WebBrowserToolHandlers.cs:92` `WithImage(base64Png)` | ① 视觉理解的语义理解后端 |

### 2.3 架构上完全缺失的三层

1. **图像语义分析层** — 截图 + UI 元素检测 + 多模态 LLM 理解
2. **桌面输入模拟层** — Win32 SendInput / UIAutomation / SendMessage
3. **环境感知层** — 窗口枚举 / 弹窗检测 / 光标状态 / 进程窗口关联

---

## 3. 需求范围（EARS 格式）

### 3.1 ① 视觉理解层

| ID | 需求 | EARS |
|----|------|------|
| V-01 | 截图采集 | **When** Agent 需要理解当前屏幕内容，**the system shall** 捕获指定窗口/区域/全屏的 PNG 截图并返回 base64 |
| V-02 | UI 元素检测 | **When** 获得截图后，**the system shall** 识别其中的 UI 元素（按钮/输入框/菜单/对话框/进度条）及其坐标、状态（可用/灰显/选中） |
| V-03 | 语义理解 | **When** 获得截图后，**the system shall** 调用多模态 LLM 输出结构化语义描述（布局逻辑关系、颜色含义、进度百分比隐喻），而非仅 OCR 文字 |
| V-04 | 元素定位查询 | **While** Agent 执行桌面操作，**the system shall** 支持按语义描述（如"红色的停止按钮"）定位到具体坐标 |
| V-05 | 增量截图 | **While** 监控区域未变化，**the system shall** 复用上次截图避免重复采集 |

### 3.2 ② 多模态操作链

| ID | 需求 | EARS |
|----|------|------|
| M-01 | 鼠标操作 | **The system shall** 提供鼠标移动/单击/双击/右键/拖拽能力，支持绝对坐标与语义元素定位两种寻址方式 |
| M-02 | 键盘操作 | **The system shall** 提供按键/组合键/文本输入能力，支持 Unicode 输入与剪贴板粘贴 |
| M-03 | 窗口管理 | **The system shall** 提供窗口枚举/激活/移动/调整大小/最小化/关闭能力 |
| M-04 | 跨应用拖拽 | **When** 执行拖拽复合操作，**the system shall** 支持按下→移动→悬停等待弹出→松开的完整序列 |
| M-05 | 右键菜单链 | **When** 调用右键上下文菜单，**the system shall** 支持右键唤起→等待菜单渲染→点击菜单项→处理子菜单的链式操作 |
| M-06 | 操作原子化 | **The system shall** 将每个底层操作封装为可回放、可撤销的原子单元 |

### 3.3 ③ 环境感知与容错

| ID | 需求 | EARS |
|----|------|------|
| E-01 | 弹窗检测 | **When** 操作后出现非预期弹窗（杀毒警告/保存覆盖/网络超时/系统通知），**the system shall** 识别弹窗类型并分类（可关闭/需保留/可重试） |
| E-02 | 弹窗处理 | **If** 弹窗分类为可关闭，**then the system shall** 自主关闭；**If** 需用户决策，**then the system shall** 暂停并请求用户确认 |
| E-03 | 异步等待 | **When** 点击触发异步任务（光标变沙漏/进度条转动），**the system shall** 感知完成信号（光标恢复/进度条消失/目标元素出现）后再继续，而非固定等待 |
| E-04 | GUI 循环干预 | **While** GUI 操作陷入死循环（反复点同一坐标无进展），**the system shall** 触发与 LLM 循环同构的 3 级干预（Soft 提示/Hard 撤回/Compact 重置） |
| E-05 | 现场恢复 | **If** 目标应用崩溃，**then the system shall** 重启应用并尝试恢复到崩溃前现场（基于操作日志回放） |

### 3.4 ④ 系统级元操作

| ID | 需求 | EARS |
|----|------|------|
| S-01 | 进程干预 | **The system shall** 提供任务管理器级能力：枚举进程/结束卡死进程/重启应用 |
| S-02 | 宏录制 | **When** 用户触发录制模式，**the system shall** 捕获鼠标键盘事件序列并存储为可回放宏 |
| S-03 | 宏回放加速 | **When** 执行批量重复操作，**the system shall** 支持宏回放并加速执行（参数化循环） |
| S-04 | 进程窗口关联 | **The system shall** 维护进程 ↔ 主窗口的映射，支持按进程操作其窗口 |

### 3.5 ⑤ 观察学习

| ID | 需求 | EARS |
|----|------|------|
| L-01 | 演示模式 | **When** 用户进入演示模式并亲手操作，**the system shall** 录制鼠标轨迹 + 键盘输入 + 上下文截图 |
| L-02 | 操作抽象 | **When** 演示结束，**the system shall** 将原始轨迹抽象为参数化操作逻辑（识别循环/条件/参数提取） |
| L-03 | 回放复现 | **When** 用户请求复现已学习任务，**the system shall** 基于抽象逻辑在新环境下执行 |
| L-04 | 步骤优化 | **When** 回放前，**the system shall** 分析抽象逻辑并提出优化建议（合并冗余步骤/加速等待） |

### 3.6 隐藏·撤销元意识

| ID | 需求 | EARS |
|----|------|------|
| U-01 | 不可逆操作识别 | **The system shall** 维护不可逆操作分类（删除/发送/覆盖/格式化/进程终止） |
| U-02 | 操作前元认知 | **If** 即将执行的操作属于不可逆分类，**then the system shall** 在执行前自动备份现场/弹出确认 |
| U-03 | 撤销栈 | **While** 操作可逆，**the system shall** 维护撤销栈支持回退最近 N 步 |
| U-04 | 危险坐标护栏 | **If** 鼠标点击坐标命中危险区域（如"确定删除"按钮），**then the system shall** 二次确认 |

---

## 4. 优先级与里程碑

| 优先级 | 能力域 | 需求 ID | 里程碑 | 理由 |
|--------|--------|---------|--------|------|
| **P0** | ② 桌面输入模拟底座 | M-01, M-02, M-03, M-06 | M1 | 没有「手」，①③⑤ 无从落地 |
| **P1** | ① 视觉理解 | V-01, V-02, V-03, V-04 | M2 | 有了手还要有眼；复用现有截图链路 |
| **P2** | ③ 环境感知与容错 | E-01, E-02, E-03, E-04 | M3 | 复用现有循环干预状态机，改造成本最低 |
| **P2** | 隐藏·撤销元意识 | U-01, U-02, U-03, U-04 | M3 | 安全护栏与容错同期建设 |
| **P3** | ② 复合操作链 | M-04, M-05 | M4 | 依赖 P0/P1 就绪 |
| **P3** | ④ 系统级元操作 | S-01, S-04, E-05 | M4 | 依赖 P0 落地 |
| **P4** | ④ 宏录制 | S-02, S-03 | M5 | 依赖 P0-P2 |
| **P5** | ⑤ 观察学习 | L-01, L-02, L-03, L-04 | M6 | 难度最高，依赖 P0-P2 全部就绪 |

---

## 5. 技术方案概要（架构落点）

### 5.1 七层架构落点

| 新增模块 | 所属层 | 目录 | 依赖下层 |
|---------|--------|------|----------|
| 桌面输入模拟抽象 | Foundation | `foundation/Abstractions/07-desktop/` | — |
| Win32 输入模拟实现 | Core | `core/execution/Hands/src/Desktop/` | Abstractions |
| 图像语义分析 | Core | `core/perception/Vision/`（新增） | Abstractions + 多模态 LLM |
| 环境感知（弹窗/光标/窗口） | Core | `core/perception/Environment/`（新增） | Abstractions |
| GUI 循环干预（复用） | Core | 复用 `core/execution/Brain/.../Loop/` | — |
| Computer Use 工具 Handlers | Core | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/` | Hands + perception |
| 宏录制/回放 | Services | `services/Macro/`（新增） | Core |
| 观察学习 | Services | `services/Apprentice/`（新增） | Core + Services |

### 5.2 关键技术选型

| 能力 | 选型 | NativeAOT 兼容 | 备注 |
|------|------|---------------|------|
| 鼠标键盘模拟 | **Win32 `SendInput` P/Invoke** | ✅ | 已有 `PreventSleepService` 验证 P/Invoke 路径 |
| UI 元素检测 | **UIAutomation (UIAutomationClient)** | ⚠️ 需验证 | 备选：纯截图 + 多模态 LLM 检测 |
| 窗口枚举 | **Win32 `EnumWindows` P/Invoke** | ✅ | — |
| 截图采集 | **GDI+ `BitBlt` P/Invoke** | ✅ | 复用现有 `PuppeteerBrowserAutomationService` 浏览器截图，桌面截图新增 |
| 图像语义理解 | **多模态 LLM**（复用现有适配） | ✅ | `WithImage(base64Png)` 链路已通 |
| 弹窗检测 | **UIAutomation + 窗口类名匹配** | ⚠️ 需验证 | 备选：截图 + LLM 分类 |
| 宏存储 | **JsonContext + 源码生成器** | ✅ | 禁止 dynamic |

### 5.3 与现有模块的关系

| 现有模块 | 关系 | 说明 |
|---------|------|------|
| `Hands/SystemActuator` | **并存** | SystemActuator 跑命令行；Desktop 输入模拟跑 GUI 事件，两者同属 Hands 但互不替代 |
| `core/safety/Guard` | **扩展** | 危险命令检测扩展为「危险操作检测」（含危险坐标/不可逆 GUI 操作） |
| `LoopInterventionMiddleware` | **泛化** | 现有 4 检测器针对 LLM 输出；新增 GUI 操作循环检测器，复用 3 级 `InterventionLevel` 状态机 |
| `IBrowserAutomationService` | **保留** | 浏览器自动化保持现状，桌面控制走独立路径 |
| `DesktopHandoffService` | **替换** | 现有"打日志返回 true"的占位实现替换为真实桌面控制委派 |

---

## 6. 验收标准

### 6.1 M1（P0 桌面输入模拟底座）

- [ ] 能在指定坐标执行鼠标单击/双击/右键/拖拽
- [ ] 能输入文本与组合键（含 Unicode）
- [ ] 能枚举窗口、激活、移动、关闭
- [ ] 每个操作封装为可回放原子单元
- [ ] NativeAOT 编译通过，零警告
- [ ] 单元测试覆盖 Win32 P/Invoke 边界

### 6.2 M2（P1 视觉理解）

- [ ] 能采集全屏/窗口/区域截图
- [ ] 能识别截图中的 UI 元素及其坐标/状态
- [ ] 能输出结构化语义描述（布局关系/颜色含义）
- [ ] 能按语义描述定位坐标
- [ ] 端到端：截图 → 理解 → 定位 → 操作 闭环跑通

### 6.3 M3（P2 环境感知 + 撤销元意识）

- [ ] 能检测并分类非预期弹窗
- [ ] 能感知异步任务完成信号（非固定等待）
- [ ] GUI 操作死循环触发 3 级干预
- [ ] 不可逆操作前自动备份/确认
- [ ] 维护撤销栈支持回退

### 6.4 后续里程碑

- M4（P3 复合操作链 + 系统级元操作）：跨应用拖拽、右键菜单链、进程干预
- M5（P4 宏录制）：录制 → 存储 → 回放加速
- M6（P5 观察学习）：演示 → 抽象 → 复现 → 优化

---

## 7. 风险与依赖

| 风险 | 等级 | 缓解 |
|------|------|------|
| UIAutomation 的 NativeAOT 兼容性未验证 | 🔴 高 | M1 前做卫星项目验证；备选纯截图+LLM 方案 |
| 桌面操作的安全风险（误删/误发） | 🔴 高 | U-01~U-04 撤销元意识与 P0 同步设计，不滞后 |
| 多模态 LLM 视觉理解准确率不足 | 🟡 中 | V-02 元素检测用确定性算法（UIAutomation）兜底，LLM 仅做语义增强 |
| Win32 P/Invoke 跨平台兼容 | 🟡 中 | 本期仅 Windows；Linux 用 AT-SPI、macOS 用 Accessibility API，后续扩展 |
| 操作录制隐私（含敏感截图） | 🟡 中 | L-01 录制支持脱敏模式，敏感区域打码 |
| 性能：高频截图 + LLM 调用延迟 | 🟡 中 | V-05 增量截图 + 元素缓存 |

---

## 8. 附录：能力现状详细证据

### 8.1 视觉理解（完全不具备）

- `services/Eyes/src` 仅含 `CodeIndexAdapter/` + `Lsp/`，是代码索引 + LSP 集成
- 无 `analyzeImage`/`OCR`/`Vision`/`ImageAnalyze` 工具
- `ModalityValidationMiddleware.cs:8` 注释"降级策略：纯文本验证 → web 工具找 OCR(≤5次) → 请求用户接管"
- 截图仅 base64 PNG 塞给外部多模态 LLM（`WebBrowserToolHandlers.cs:92-96`）

### 8.2 桌面 GUI 操作（完全不具备）

- `Hands/src` 子目录无 Mouse/Keyboard/Window/Desktop/InputSimulate
- `SystemActuatorBase.cs:150` `ExecuteAsync` 是 `Process.Start` 跑命令行
- `DesktopHandoffService.cs:42` `HandoffToDesktopAsync` 仅打日志返回 true
- `Mouse`/`Keyboard`/`Window` 类全在 `libs/Terminal.Gui/`（TUI 框架内部，非控制第三方应用）
- 唯一真·系统级能力：`PreventSleepService.cs:22`（kernel32 P/Invoke 防休眠）

### 8.3 浏览器自动化（部分具备，极弱）

- `IBrowserAutomationService.cs:7` 仅 `ScreenshotAsync` + `EvaluateAsync` 两方法
- 默认 `NoOpBrowserAutomationService.cs:8` `IsAvailable => false`
- Headless 模式，无 Click/Type/Hover/WaitForSelector/Drag

### 8.4 环境感知（LLM 维度强，GUI 维度零）

- 4 层检测器：`InformationEntropyGuardian.cs:17`（OutputLoop/LogicFingerprint/ToolCallSequence/ShannonEntropy）
- 3 级干预：`LoopInterventionMiddleware.cs:8` `InterventionLevel { None, Soft, Hard, Compact }`
- 错误恢复：`ErrorRecoveryToolHandlers.cs:9`、`TimeoutRecoveryToolHandlers.cs:9`
- 无弹窗/沙漏/杀毒警告/保存覆盖感知

### 8.5 系统级元操作（部分具备）

- Shell 执行：`SystemActuatorBase.cs:150`（Bash/Cmd/PowerShell/Python）
- 长任务 kill+重启：`LongRunningTaskRegistry.cs:7`（非续等，MaxRetries=5）
- 构建队列 cancel：`BuildOutputToolHandlers.cs:131`
- 无宏录制/批量加速/进程现场恢复

### 8.6 观察学习（完全不具备）

- `services/Dream` = 会话记忆整合（`DreamFeature.cs:10`"做梦功能 - 记忆整合"）
- `VoiceService.cs:235` `GenerateSilenceBuffer` 填静音字节，录音是 stub
- 无 Observe/Demonstrate/Teach/Learn 类

---

<!-- 🤖 Auto Decision: 2026-08-25 -->
<!-- 决策: 将 Computer Use 能力建设拆为 P0-P5 六个优先级、M1-M6 六个里程碑 -->
<!-- 原因: ②桌面输入模拟是所有 GUI 能力的底座（无手则眼/容错/学习皆无从落地），故 P0；①视觉理解复用现有截图+多模态LLM链路成本最低，P1；③环境感知可复用现有循环干预状态机框架，P2 -->
<!-- 替代方案: 优先做①视觉理解（先看懂再操作）— 否决，因无操作能力则看懂也无法执行，闭环不成立 -->
<!-- 验证: 差距分析基于对七层架构的全面代码调查，证据含 file:line 引用 ✅ -->
