# DSG041 — UI 全面对齐 CLI 与精美界面规划（学习 DSH）

> 状态: proposed
> 日期: 2026-10-17
> 前置: [DSG032](DSG032-vscode-style-gui-master-plan.md)(VSCode风格GUI)、[GUI001](../task/gui/GUI001-任务-GUI全面美化.md)(全面美化)、[TASK011](../task/TASK011-TuiGui双端对齐任务清单.md)(TuiGui双端对齐)
> 学习对象: DSH (DeepSeek Harness) Web UI — 双进程宿主/客户端、slot 体系、四条交互缝
> 关联: ADR [0098](../../../../docs/implemented/adr/architecture/0098-plugin-system-fusion-actor-effectscope.md)(插件系统)、PLG006(万物皆插件)、ADR [0005](../../../../docs/implemented/adr/architecture/0005-file-driven-ui.md)(文件驱动界面)、DSG039(启动参数)

---

## 一、目标与判定标准

| 目标 | 判定标准 |
|------|---------|
| **功能对齐**: CLI 全部功能在 UI 可达 | 以 `ISlashCommandCatalog`(98 命令) 为唯一枚举的"三端功能矩阵测试"全绿；每个交互缝回调有接线测试 |
| **精美界面**: 统一设计语言 + DSH 级交互细节 | 双主题截图矩阵人工核对通过；过程折叠/宽度调节/贡献点等细节落地 |
| **架构最美**: UI 是可替换的渲染器 | 功能一律先落在共享命令层/交互缝；UI 贡献点（slot）体系让面板/菜单/状态栏可插件注册 |

**核心原则（继承既定模式，本轮贯彻到底）**：
CLI 命令层 = 唯一功能源 → 三端共享执行器 `SlashCommandRunner` → UI 只做两件事：① 渲染 `CommandResult`/事件流 ② 注入交互缝回调（Confirm/Prompt/ReadPassword/Select）。**禁止 UI 端私建功能路径**。

---

## 二、现状盘点（2026-10-17 调研）

### 2.1 已达成（本轮不再是问题）

- 斜杠命令执行链三端统一：CLI `CliSession` / TUI `TuiModeRunner` / GUI `JccChatSession` 均走共享 `SlashCommandRunner`（TASK011 G1）
- Confirm 缝三端已通：TUI `PermissionDialogView`、GUI `ConfirmDialogWindow`（TASK011 T9）
- AskUserQuestion、会话持久化(transcript 中间件)、token 用量、Markdown/Diff 渲染、Docking 布局(TASK035/DSG032)已落地
- GUI 已有设计语言体系：GuiPalette token + GuiControlStyles(GUI001)

### 2.2 缺口矩阵

#### A. 交互缝缺口（最高优先级）

`ChatCommandContext` 已有 `Confirm/Prompt/ReadPassword/ClearScreen` 四个回调，但三端接入不齐：

| 缝 | CLI | TUI | GUI | 受影响命令 |
|----|-----|-----|-----|-----------|
| Confirm | Y | Y | Y | /exit /commit /worktree |
| **Prompt** | Y(ReadLine) | **N(恒 null)** | **N(恒 null)** | **/commit 自定义消息、/login endpoint/OAuth、/resume 无参** |
| **ReadPassword** | Y(掩码逐键) | **N(恒空串)** | **N(恒空串)** | **/login（直接不可用）** |
| Select/选择器 | Y(Terminal.Gui Selector) | Y(同左) | **N(无控制台可见性)** | /resume 无参交互选择 |
| ClearScreen | Y | Y | N(无意义) | /clear 等 |

证据：`CommitCommand.cs:127`、`LoginCommand.cs:86,97,142`、`ResumeCommand.cs:217`；GUI 仅注入 `SlashConfirmHandler`（`JccChatSession.cs:497-505`）。

#### B. 功能入口缺口

| # | 缺口 | 现状证据 |
|---|------|---------|
| B1 | **自定义 .md 命令仅 CLI 加载** | `CustomCommandLoader` 唯一实例化点 `CliSession.cs:201-206` |
| B2 | **CLI 启动参数仅 CLI 生效** | GUI 仅认 `--design`(`App.axaml.cs:47`)、TUI 仅认 `--await`(`Program.cs:52-58`)；--model/--vendor/--trust/--permission-mode/--continue/--resume/--no-confirm 等 26 项全缺 |
| B3 | **TUI 缺 `!`/`!!` 前缀命令** | GUI 有(`MainViewModel.Send.cs:120-157`)，TUI 零处理 |
| B4 | **子代理实时面板断裂** | TUI `AgentPanesView` 挂载但无生产者(死代码)、`/switch` 无效果；GUI 无对应视图 |
| B5 | 主题变体 daltonized/ansi 仅 CLI | GUI `ThemeConverter.cs:7` 明确不支持 |
| B6 | `/passes` `/output-style` `/keybindings` 枚举有值无实现 | `ChatCommandName.cs` 有无 `[ChatCommand]` 类，三端均报未知命令 |

#### C. 死代码 / 不一致

| # | 项 | 处置建议 |
|---|----|---------|
| C1 | GUI `StatusBarView.axaml` 孤儿视图(零引用) | 归档 `.xxx/` 或接线复活 |
| C2 | GUI 命令面板 2 死项(`file.openFiles/openManagement`) | 接线 `ExecuteCommand` |
| C3 | GUI `SlashCommandItem` 硬编码 16 条兜底命令 | 引擎就绪后删除兜底（IsEngineReady 门控） |
| C4 | GUI `DockPosition.Float` 未实现 | 决策：实现 or 删除枚举值 |
| C5 | 别名冲突：/usage 与 /rate-limit-options 共用 `rate-limit`；/fork 别名 `branch` 与 /branch 重名 | 按注册表冲突校验修复 |

#### D. 插件 UI 贡献零消费（DSH 学习点最大缺口）

`UiResourceKind`(Icon/MenuItem/ToolbarButton/Panel/StatusBar) + `UiResourceTable` + `UiResourceChangedEvent` 基建已存在，
且 `DreamPlugin` 已注册 `menu.dream` 等资源——**但 GUI/TUI 零消费**（grep 全仓零命中）。
DSH 的 slot 体系正是这块：插件注册 UI 贡献 → 宿主 UI 动态呈现。

---

## 三、DSH 学习总结与映射

来源：[Web UI 架构](https://deepseekdocs.com/docs/learn/core/web-ui)、[交互缝](https://deepseekdocs.com/docs/learn/core/interaction-seams)、[客户端资源](https://deepseekdocs.com/docs/learn/core/client-resources)

| DSH 机制 | 做法 | 本项目现状 | 采纳方式 |
|----------|------|-----------|---------|
| **四条交互缝独立** | approval(四值闭集 fail-closed)、permissionPresets、commands、userQuestions 各自独立 | 已有雏形：ChatCommandContext 四回调 + AskUser | Phase A：补齐缝 + **fail-closed 契约**（缺接线 → 明确失败，绝不静默替答） |
| **命令注册表不进模型面** | CommandResult 由 UI 直接渲染，不烧 token | 已对齐（TUI011 G1 斜杠命令不进聊天流） | 保持，测试钉死 |
| **双进程：宿主=能力，UI=订阅** | 浏览器不持真实能力，只订阅 event 流 | 已对齐：IJccChatSession 事件门面 | 保持；方向 = UI 越来越薄 |
| **slot 体系** | `settings.section` 等命名 slot，插件注册 UI 不硬编码 | UiResourceTable 存在但零消费 | **Phase C：UI 贡献点消费层**（最大架构增量） |
| **过程默认折叠 + 用量账目完整才展示** | chat 折叠过程/系统提示行；部分统计不冒充完整 | GUI 思考可折叠；工具卡片未折叠；Token 显示已有 | Phase D |
| **正文宽度可调** | 对话列宽用户可调 | 无 | Phase D |
| **权限预设（preset 写穿沙箱+审批两旋钮）** | 用户可见命名预设 + custom 派生态 | 三档确认 + GUI Shift+Tab 循环权限 | 已有近似物，不新增（避免加法思维） |
| **资源模型 dsh-resource:// + provider** | 统一资源地址 + 预览 | 文件编辑/差异已有 | 暂不采纳（GUI 已有 EditorPanel；YAGNI） |
| **工作区注册表** | workspace 记录 + 会话索引分组 | 会话列表已有、无工作区分组 | 可选增强，不进主路径 |
| **Dock（split tree + tabbed panes）** | ui-dockkit | 已实现(TASK035 + libs/Avalonia Dock) | 保持 |

**明确不采纳**：DSH 式 Web 双进程前端（浏览器 React 栈）不引入——本项目 GUI=Avalonia 桌面、TUI=Terminal.Gui 是既有技术栈，重写将沉没 TASK035/DSG032/GUI001 全部投资。学习其**架构思想**（slot、交互缝、薄 UI），不学其**技术栈**。若未来需要远程访问，GUI 的 VM/事件层可复用，再议。

---

## 四、架构方案

### 4.1 交互缝统一契约（Phase A 核心）

```
UI(CLI/TUI/GUI) ──注入──> ChatCommandContext { Confirm, Prompt, ReadPassword, Select, ClearScreen, ExitRequested }
                                      │
                                      ▼
                     命令层消费（CommitCommand/LoginCommand/ResumeCommand…）
```

- **补 `Select` 缝**：结构化选择（/resume 会话选择器），签名 `Select(string title, string[] options) → int?`（null=取消）
- **fail-closed 语义（学 DSH `unavailable`）**：缝未接线时 → 命令显式报错"当前界面不支持 X 输入"，**禁止**返回 null 继续执行或静默替答
- 三端实现：
  - CLI：`ConsoleOutput` 既有实现（保持）
  - TUI：`painter.Invoke` 切主循环 + 输入对话框（复用 AskUserDialogView 模式）
  - GUI：`Dispatcher.UIThread.InvokeAsync` + 新 `TextInputDialog`(含 PasswordBox 模式) + 选择列表对话框
- 异步封送注意：缝回调是同步签名，GUI 侧用 `Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()` 模式已存在于 T9，沿用

### 4.2 自定义命令下沉共享层（B1）

`CustomCommandLoader` 从 `CliSession` 下沉到共享命令层（`modules/composition/slash`），三端经 `SlashCommandRunner` 免费获得；加载失败提示按"错误提示诱导"规则给修正指引。

### 4.3 启动参数对齐（B2）

复用既有 `CliArg` 体系（TASK017/TASK052 已重构完成），GUI/TUI 入口解析同一参数子集：
`--model --vendor --trust --permission-mode --no-confirm --continue --resume --system-prompt --await`。
优先级：CLI 参数 > settings.json > 默认值（对齐 CLI `ConfigPriority` 语义）。

### 4.4 UI 贡献点（slot）体系（Phase C 核心，DSH 学习落地）

```
插件 WorkflowPluginBase.UiResources.Register("panel.xxx", new UiResourceEntry(...))
        │ UiResourceChangedEvent（已有）
        ▼
UiContributionRegistry（新建，宿主侧）
        │ 按 Kind + Priority 排序、按 Slot 分组
        ▼
GUI: 菜单/工具栏/状态栏/侧栏面板/设置分区 动态注册
TUI: 工具栏按钮/FooterTab 页签
```

- `UiResourceKind` 扩展：`SettingsSection`、`CommandContribution`（插件贡献斜杠命令入口）
- 消费层遵循"文件驱动界面"ADR 0005：数据源=注册表，UI 只绑定
- 试点：`DreamPlugin` 的 `menu.dream` 端到端打通（已有注册方，零新增插件）
- 好处：新增 UI 能力=写插件（新增面板/设置卡不改 UI 源码）；可替换性达成
- 代价：GUI 菜单/状态栏需从静态 XAML 改为 VM 驱动的 ItemsSource（一次性改造）

### 4.5 功能矩阵测试（防回归钉子）

- **命令可达矩阵**：`ISlashCommandCatalog` 全量 × {Cli, Tui, Gui}，断言"命令可解析可达 + 所需缝已接线 + 未接线时 fail-closed"
- **快照测试**：注册命令清单快照（新增/删除命令必须显式确认）
- 形式：E2E 红 → 单元绿（遵守双层 TDD）；UI 部分经 ViewModel 层可测（headless）

---

## 五、阶段任务清单

> 每项独立：红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交。标 ✅ 项为开工前需先决策。

### Phase A — 交互缝补齐（P0，最先做）

- [ ] A1 E2E 红测试：`/login` `/commit` `/resume` 在 GUI/TUI 的当前失败行为钉死
- [ ] A2 ChatCommandContext 补 `Select` 缝 + fail-closed 契约（未接线→显式错误）+ 契约单测
- [ ] A3 GUI：TextInputDialog（普通/密码两模式）接线 Prompt/ReadPassword
- [ ] A4 TUI：输入对话框接线 Prompt/ReadPassword
- [ ] A5 /resume 选择器三端一致（GUI/TUI 用 Select 缝渲染列表；CLI 保持）
- [ ] A6 自定义命令加载下沉共享层（B1），三端可达测试

### Phase B — 功能入口对齐

- [ ] B1 TUI `!`/`!!` 前缀命令（对齐 GUI `PrefixCommands` 行为）
- [ ] B2 GUI/TUI 接受 CLI 启动参数子集（4.3）
- [ ] B3 子代理实时面板：TUI `AgentPanesView` 接线（事件流生产者）+ GUI 对齐；`/switch` 验证
- [ ] B4 ✅ 决策：`/passes` `/output-style` `/keybindings` 实现 or 从枚举移除
- [ ] B5 GUI 主题变体 daltonized/ansi

### Phase C — UI 贡献点（slot）体系

- [ ] C1 `UiResourceKind` 扩展 + `UiContributionRegistry` 契约 + 单测
- [ ] C2 GUI 消费层：菜单/工具栏/状态栏/面板/设置分区（VM 驱动 ItemsSource）
- [ ] C3 TUI 消费层：工具栏/FooterTab
- [ ] C4 DreamPlugin 端到端试点 + 手动 exe 验收

### Phase D — 精美度增强（DSH 细节）

- [ ] D1 过程内容默认折叠（工具卡片折叠，对标 DSH 折叠过程行）
- [ ] D2 对话正文宽度可调 + 用量"完整账目才展示"规则（部分统计加标记）
- [ ] D3 设置面板分区重构（与 C2 的 SettingsSection 结合）
- [ ] D4 空态/微交互续扫（GUI001 遗留项）

### Phase E — 清理与验收

- [ ] E1 死代码：StatusBarView 归档/复活决策、CommandPalette 死项接线、SlashCommandItem 兜底门控
- [ ] E2 别名冲突修复（rate-limit / branch）+ 注册表冲突校验
- [ ] E3 ✅ 决策：DockPosition.Float 实现 or 移除
- [ ] E4 功能矩阵测试全绿 + ADR 0080 手动 exe 验收 + 双主题截图矩阵

---

## 六、风险与边界

| 风险 | 缓解 |
|------|------|
| GUI 同步等待异步对话框死锁 | 沿用 T9 已验证的 `Dispatcher.UIThread.InvokeAsync + GetAwaiter().GetResult()` 模式；headless 测试覆盖 |
| NativeAOT：动态 XAML/反射 | UI 贡献点用 VM 驱动 ItemsSource + 编译型模板，禁运行时 XAML 加载（GUI001 已踩坑） |
| C 阶段改造面大（菜单/状态栏静态→动态） | 渐进式：每次一个区域，独立编译+提交；先 registry+单测，再逐个消费点 |
| TUI 主循环重入（painter.Invoke 嵌套对话框） | 复用 AskUserDialogView/T2 已验证模式 |
| 与 TASK011 遗留项重叠 | 本规划覆盖 TASK011 未清的 G5/C1/C2 与 T2-T5 之外的缺口，不重复已完成项 |

---

## 七、验收表（四列，随实现逐项填写）

> `已验收` 列必须走 [ADR 0080](../../../../docs/implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) 手动 exe 验收（真实运行非 mock）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| ChatCommandContext.Select 缝 + fail-closed | GUI/TUI/CLI SlashCommandRunner | ❌ | ❌ |
| CustomCommandLoader 共享层 | CliSession / TuiModeRunner / JccChatSession | ❌ | ❌ |
| CliArg 子集解析 | GUI Program / TUI Program | ❌ | ❌ |
| UiContributionRegistry | GUI菜单/工具栏/状态栏/设置分区 + TUI 工具栏/FooterTab | ❌ | ❌ |
| 功能矩阵测试 | CI unit-tests | ❌ | ❌ |

---

## 八、证据索引

- 命令枚举：`modules/abstractions/abstractions/abs_core/core_utils/constants/cli_ansi/ChatCommandName.cs`（101 值 / 98 实现）
- 共享执行器：`modules/apps/cli/core/commands/core/SlashCommandRunner.cs`
- 缝消费：`CommitCommand.cs:127`、`LoginCommand.cs:86,97,142`、`ResumeCommand.cs:217`
- GUI 会话门面：`modules/apps/gui/hosting/JccChatSession.cs:497-505`
- 插件 UI 资源：`modules/plugins/plugins.contracts/resource/`（UiResourceTable/UiResourceKind）
- DSH 参考：deepseekdocs.com `/docs/learn/core/web-ui`、`/docs/learn/core/interaction-seams`
