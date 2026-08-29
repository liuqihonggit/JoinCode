# GUI 优化任务清单（11 项需求）

> 调研时间：2026-08-26
> GUI 项目：`app/JoinCodeGui/`（Avalonia + CommunityToolkit.Mvvm）
> 最近 commit 已完成：走马灯主区替代绿点、双击 ESC 终止、Ctrl+Enter 默认键位、子代理 worktree 右键

## 状态总览

| # | 需求 | 状态 | 工作量 | 关键文件 |
|---|------|------|--------|----------|
| 1 | 走马灯替代侧栏绿点 | ✅ 完成 | 小 | `Views/SidebarView.axaml` |
| 2 | 双击 ESC 终止对话网络，遥测不终止 | ✅ 已验证 | 小 | `MainWindow.axaml.cs:69` 注释 |
| 3 | 快捷键面板（可自定义键位） | ✅ 完成 | 中 | `HotkeyItemVm.cs`, `SettingsPanelView.axaml` |
| 4 | 子代理对话传递 | ✅ 已验证 | 验证 | `MainViewModel.cs:1348-1393` |
| 5 | git worktree 右键打开资源管理器 | ✅ 已有 | — | `MainWindow.axaml:167` |
| 6 | 设计时数据 + commit 截图美观分析 | ✅ 完成 | 中 | `Design/DesignData.cs`, `--design` 模式 |
| 7 | 本地/VPN 网络切换（GUI+底层） | ✅ 完成 | 大 | `MainViewModel.Network.cs`, `SettingsPanelView` |
| 8 | mem/sessions 路径转物理路径 | ✅ 已有 | — | `IFileSystem` 抽象 |
| 9 | 走马灯停止时弹窗提醒 | ✅ 完成 | 小 | `GlobalRunStatusViewModel.cs` MarqueeStopped |
| 10 | 卡片式系统提示词注入展示（默认折叠） | ✅ 完成 | 中 | `ChatUiMessageKind.SystemPromptInjection` |
| 11 | 子会话树形展示 + 右键打开文件夹 | ✅ 完成 | 大 | `SessionItem.cs`, `SidebarView.axaml` |

## 已完成（无需改动）

- **5** git worktree 右键直达资源管理器 — `MainWindow.axaml:167`
- **8** 路径转换 — `IFileSystem` 抽象统一处理，生产用 `~/.jcc/sessions/`，`mem/sessions` 仅测试用

## 未完成明细

### 1. 走马灯替代侧栏绿点（小）
- **现状**：主区已用走马灯（`MainWindow.axaml:317`），但侧栏底部仍保留 `●` 绿点 + StatusText（`SidebarView.axaml:92-102`）
- **方案**：删除侧栏绿点，侧栏底部改用走马灯（或直接移除侧栏状态栏，统一由主区走马灯承载）
- **决策点**：侧栏底部是改走马灯还是直接移除？

### 2. 双击 ESC 终止对话网络，遥测不终止（小-验证）
- **现状**：双击 ESC → `StopGeneratingCommand`（`MainWindow.axaml.cs:71-88`）
- **需验证**：`StopGeneratingCommand` 是否只终止对话网络（HttpClient/chat 流），遥测网络（TelemetryClient）是否保持
- **方案**：检查 `StopGeneratingAsync` 实现，确认遥测通道独立

### 3. 快捷键面板（中）
- **现状**：仅 `EnterSends`/`DoubleEscStop` 2 个布尔开关（`SettingsPanelView.axaml:92-113`）
- **需求**：完整快捷键面板，用户可自由更换键位
- **方案**：
  - 新增 `HotkeyConfig.cs`（键位→动作映射，持久化到 `gui-preferences.json`）
  - 新增 `HotkeyPanelView.axaml`（键位录制控件：按下任意键组合捕获）
  - 可配置项：发送、换行、终止、新建会话、清空、打开设置、聚焦输入、历史上下导航
- **决策点**：键位录制控件实现方式（Avalonia KeyBinding vs 自定义捕获）

### 4. 子代理对话传递（验证）
- **现状**：`SubAgentRunTracker` 消费 `ChatStreamEvent`，`@子代理名` 可直发
- **需确认**：对话框（InputBar）发送时，是否传递当前选中的子代理上下文给引擎
- **方案**：检查 `SendCommand` → `JccChatSession.SendAsync` 是否携带 `TargetAgentId`

### 6. 设计时数据 + commit 截图美观分析（中）
- **现状**：无 `DesignTimeData`，XAML 设计器无法预览；`PlaceholderChatSession` 是运行时降级非设计时数据
- **需求**：commit 截图 GUI 做美观分析，需设计时数据填充模拟，避免截图空白的分析失败
- **方案**：
  - 新增 `Design/DesignData.cs` + `Design/SampleMainViewModel.cs`（填充示例会话、消息、子代理、走马灯文本）
  - 各 axaml 添加 `xmlns:d` + `d:DataContext="{x:Static design:DesignData.MainViewModel}"`
  - commit 截图流程：启动 GUI → 加载设计时数据 → 截图 → analyzeImage 美观分析

### 7. 本地/VPN 网络切换（大）
- **现状**：引擎层有 `INetworkConnectivityService`（检测 VPN/代理路由），GUI 无切换 UI
- **需求**：本地网络和 VPN 都能跑，用户自选切换，GUI 和底层都要做
- **方案**：
  - 底层：`INetworkConnectivityService` 新增 `SwitchNetworkAsync(NetworkProfile)` 切换方法
  - GUI：`SettingsPanelView.axaml` 新增"网络"卡片（当前网络状态 + 可用网络列表 + 切换按钮）
  - 网络配置持久化到 `gui-preferences.json`
- **决策点**：网络切换是切换代理路由还是切换物理接口？VPN 检测已有，切换语义需明确

### 9. 走马灯停止时弹窗（小）
- **现状**：走马灯停止（空闲/断线）时无提醒
- **需求**：走马灯停止时弹窗，避免用户不知情
- **方案**：`GlobalRunStatusViewModel` 监听状态从 Running→Idle/Error 转换时触发弹窗（Avalonia `MessageBox` 或自定义弹窗）
- **决策点**：弹窗是模态阻塞还是非模态 toast？模态会打断用户输入

### 10. 卡片式系统提示词注入展示（中）
- **现状**：系统提示词仅在设置面板编辑（`SettingsPanelView.axaml:115-122`），不在消息区展示
- **需求**：每个系统提示词注入都展示卡片，标题"系统提示词注入"，内容默认折叠
- **方案**：
  - `ChatUiMessage` 新增 `Kind = SystemPromptInjection`，含 `Title`/`Content`/`IsExpanded`
  - `MainWindow.axaml` 消息区新增 DataTemplate（Expander 卡片，默认 Collapsed）
  - 引擎注入系统提示词时，向 UI 推送一条 `SystemPromptInjection` 消息

### 11. 子会话树形展示 + 右键打开文件夹（大）
- **现状**：侧栏 `Sessions` 平铺 `ObservableCollection<SessionItem>`，子代理在消息区/状态栏展示
- **需求**：主会话展开后才是子会话，右键子会话可打开文件夹（worktree）
- **方案**：
  - `SessionItem` 新增 `ParentId`/`Children`/`IsExpanded`/`HasWorktree`/`WorktreePath`
  - `SidebarView.axaml` 改用 `TreeView` + `HierarchicalDataTemplate`
  - 子会话右键菜单：打开 worktree 文件夹、打开回放
  - 引擎层 `ParentSessionId` 已有，GUI 层桥接填充 `Children`
- **决策点**：TreeView vs Expander 嵌套；子会话是实时从引擎拉取还是缓存？

## 建议优先级

1. **小工作量先做**（快速见效）：1（侧栏绿点）、9（走马灯弹窗）、2（验证遥测）
2. **中工作量**：10（系统提示词卡片）、3（快捷键面板）、6（设计时数据）
3. **大工作量**：11（会话树）、7（网络切换）
4. **验证项**：4（子代理传递）

<!-- 🤖 Auto Decision: 2026-08-26 -->
<!-- 决策: 用户确认优先级与架构方向 -->
<!-- 优先级: 小→中→大（1/9/2/4 → 10/3/6 → 11/7） -->
<!-- 需求1: 侧栏改用走马灯（双走马灯） -->
<!-- 需求9: 走马灯停止用模态对话框（用户选模态，非toast） -->
<!-- 需求7: 网络切换=切换代理路由（HTTP_PROXY），不断物理连接 -->
<!-- 需求11: 子会话用 TreeView + HierarchicalDataTemplate -->
