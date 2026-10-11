# 面板统一+可停靠 计划

> 用户需求：①菜单栏元素不好 ②配置应从左边弹出(像目录树) ③从左边弹出的统一为可停靠面板(VSCode 拖拽停靠)

## 当前架构

| 面板 | 触发图标 | 弹出方式 | 位置 |
|------|---------|---------|------|
| 会话列表 | 💬 | SideBar (Column 1) | 左侧 |
| 目录树 | 📁 | SideBar (Column 1) | 左侧 |
| 编辑器 | 📝 | 主区切换 (Column 3) | 中央 |
| goal | 🎯 | **Popup** Placement=Right | 浮在右侧 |
| 拦截器 | 🛡 | **Popup** Placement=Right | 浮在右侧 |
| 聊天室 | 👥 | **Popup** Placement=Right | 浮在右侧 |
| 设置 | ⚙ | **Popup** Placement=Right | 浮在右侧 |
| SecondaryBar | ⇥ | Border (Column 5) | 右侧 |

**问题**：SideBar 只支持 Sessions/FileTree 两种，其他面板用 Popup 浮在右侧，不统一。

## 阶段1：统一面板系统（Popup → SideBar）

**目标**：所有面板从左侧 SideBar 弹出，移除 Popup。

### 1.1 扩展 SidePanelKind 枚举
- 文件：`app/gui/view_models/main_view_model/panels/MainViewModel.FileTree.cs`
- 改动：`SidePanelKind { None, Sessions, FileTree, Settings, Goal, Interceptor, ChatRoom }`
- 加 `IsSettingsPanelActive/IsGoalPanelActive/IsInterceptorPanelActive/IsChatRoomPanelActive` 属性

### 1.2 SideBarCol 加入新面板视图
- 文件：`app/gui/views/MainWindow.axaml` (line 196-199)
- 改动：Panel 里加入 `<local:SettingsView IsVisible="{Binding IsSettingsPanelActive}" />` 等
- 需新建：`SettingsView.axaml`（从 Popup 内容提取为 UserControl）

### 1.3 ActivityBar 图标统一走 ToggleSidePanelCommand
- 文件：`app/gui/views/MainWindow.axaml` (line 125-135, 145-147)
- 改动：🎯🛡👥⚙ 改为 `ToggleButton` + `Command="{Binding ToggleSidePanelCommand}"` + `CommandParameter="{x:Static vm:SidePanelKind.Settings}"` 等
- 移除 Popup（line 156-182）

### 1.4 移除 IsSettingsPanelOpen/IsGoalPanelOpen 等 Popup 状态
- 文件：`MainViewModel.cs`, `MainViewModel.Interceptors.cs`, `MainViewModel.Appearance.cs`
- 改动：删除 `IsSettingsPanelOpen`/`IsGoalPanelOpen`/`IsInterceptorPanelOpen`/`IsChatRoomPanelOpen` 及相关 Command
- `SettingsPanelWidth` 改为基于 `IsSettingsPanelActive`

### ⚠️ 验收点1（阶段1完成后）
- [ ] 点击⚙设置图标 → 设置面板从左侧弹出（不是 Popup 浮在右侧）
- [ ] 点击🎯🛡👥 → 同样从左侧弹出
- [ ] 再点同一图标 → 收起
- [ ] 点不同图标 → 切换面板内容（互斥）
- [ ] 拖拽 SideBar 右边缘 → 可调整宽度
- [ ] 编译通过 + 黑盒验收

---

## 阶段2：拖拽停靠（VSCode 风格）

**目标**：面板可停靠在左/右/底部，可拖拽移动。

### 2.1 设计 DockPosition 枚举
- `DockPosition { Left, Right, Bottom, Float }`
- 每个面板记录自己的 DockPosition

### 2.2 面板标题栏 + 拖拽手柄
- 每个面板顶部加标题栏（标题 + 关闭按钮 + 拖拽手柄）
- 拖拽手柄触发 DragDrop

### 2.3 停靠位置指示器
- 拖拽时显示上/下/左/右指示器
- 释放时根据指示器位置移动面板

### 2.4 布局重组
- 左侧 Dock 区：所有 DockPosition=Left 的面板
- 右侧 Dock 区：所有 DockPosition=Right 的面板
- 底部 Dock 区：所有 DockPosition=Bottom 的面板
- 浮动：DockPosition=Float 的面板用 Float 面板

### ⚠️ 验收点2（阶段2完成后）
- [ ] 拖拽面板标题栏 → 显示停靠指示器
- [ ] 拖到左侧 → 面板停靠在左
- [ ] 拖到右侧 → 面板停靠在右
- [ ] 拖到底部 → 面板停靠在底
- [ ] 拖出 → 面板浮动
- [ ] 编译通过 + 黑盒验收

---

## 阶段3：菜单栏改进

**目标**：改进顶部菜单栏（文件/编辑/视图/帮助）。

### 3.1 调研用户不满点
- 当前菜单栏在 TopBarView.axaml
- 需要用户明确"哪里不好"（太占空间？元素太多？样式不好？）

### ⚠️ 验收点3（阶段3完成后）
- [ ] 菜单栏样式/布局改进
- [ ] 编译通过 + 黑盒验收

---

## 执行顺序

1. **阶段1**（统一面板系统）→ 验收点1 → 用户确认
2. **阶段2**（拖拽停靠）→ 验收点2 → 用户确认
3. **阶段3**（菜单栏）→ 验收点3 → 用户确认

## 风险

- 阶段1 改动面大（枚举+XAML+VM），但逻辑清晰
- 阶段2 拖拽停靠是 Avalonia 高级功能，需要 DragDrop API，复杂度高
- 建议阶段1 完成后先验收，确认方向正确再做阶段2

## 文件清单（阶段1）

| 文件 | 改动 |
|------|------|
| `MainViewModel.FileTree.cs` | 扩展 SidePanelKind + IsXxxPanelActive 属性 |
| `MainWindow.axaml` | SideBarCol 加面板视图 + ActivityBar 图标改 ToggleButton + 移除 Popup |
| `MainViewModel.cs` | 移除 IsSettingsPanelOpen/IsChatRoomPanelOpen + ToggleXxxPanel |
| `MainViewModel.Interceptors.cs` | 移除 IsGoalPanelOpen/IsInterceptorPanelOpen + ToggleXxxPanel |
| `MainViewModel.Appearance.cs` | SettingsPanelWidth 改基于 IsSettingsPanelActive |
| `MainWindow.axaml.cs` | SyncActivityBarButtons 加新按钮同步 |
| **新建** `SettingsView.axaml` | 从 Popup 内容提取为 UserControl |
| **新建** `GoalView.axaml` | 从 Popup 内容提取 |
| **新建** `InterceptorView.axaml` | 从 InterceptorPopupContent 改名 |
| `ChatRoomView.axaml` | 已存在，改为 UserControl（去掉 Popup Border） |
