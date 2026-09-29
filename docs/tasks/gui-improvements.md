# 界面改进任务

## 背景

PR #342 合并后，GUI 已有拦截器/快速提示词/goal/子代理控制。现需提升界面可用性和信息密度。

## 改进项（按价值排序）

### 1. 子代理实时输出流
- **当前**：后台代理面板每个卡片只显示状态+按钮（暂停/继续/终止）
- **改进**：卡片可展开看实时 token 流 + 工具调用历史
- **价值**：用户不用切到主对话框就能监控子代理进展
- **设计**：[gui-improvement-1-subagent-output-stream.md](../design/gui-improvement-1-subagent-output-stream.md)
- **状态**：✅ 设计完成

### 2. goal 进度可视化
- **当前**：TopBar goal 按钮只显示"进行中"
- **改进**：加进度环（阶段/总阶段）+ 当前执行节点名
- **价值**：一眼看出 goal 跑到哪了
- **设计**：[gui-improvement-2-goal-progress.md](../design/gui-improvement-2-goal-progress.md)
- **状态**：✅ 设计完成

### 3. 拦截器图标+分类色
- **当前**：7 个 toggle 开关纯文字
- **改进**：按类别（文件/Shell/网络/Agent）用不同颜色+图标
- **价值**：比纯文字开关更直观
- **设计**：[gui-improvement-3-interceptor-icons.md](../design/gui-improvement-3-interceptor-icons.md)
- **状态**：✅ 设计完成

### 4. 后台代理搜索过滤
- **当前**：后台代理面板列出所有运行中代理
- **改进**：加搜索框按名称/状态过滤
- **价值**：子代理多了以后（如 10+）快速定位
- **设计**：[gui-improvement-4-search-filter.md](../design/gui-improvement-4-search-filter.md)
- **状态**：✅ 设计完成

### 5. 快捷键绑定
- **当前**：拦截器/快速提示词/goal 按钮只能鼠标点击
- **改进**：加快捷键（如 Ctrl+1~7 切换拦截器）
- **价值**：键盘用户不用点鼠标
- **设计**：[gui-improvement-5-keybindings.md](../design/gui-improvement-5-keybindings.md)
- **状态**：✅ 设计完成

## ✅ 完成状态（2026-09-30）

| 改进 | Commit | 状态 |
|------|--------|------|
| 1. 子代理实时输出流 | `78018ae32` | ✅ |
| 2. goal 进度可视化 | `ca1b36110` | ✅ |
| 3. 拦截器图标+分类色 | `b00c8b31c` | ✅ |
| 4. 后台代理搜索过滤 | `1b94168f2` | ✅ |
| 5. 快捷键绑定 | 本次 | ✅ |

**全部 5 个改进已完成。**

## 涉及文件（预估）

### GUI 层
- `app/gui/views/MainWindow.axaml` — 后台代理面板
- `app/gui/views/TopBarView.axaml` — goal 按钮 + 拦截器
- `app/gui/views/InputBarView.axaml` — 快速提示词
- `app/gui/view_models/a_to_c/BackgroundAgentsPanelViewModel.cs` — 子代理面板 VM

### 设计文档
- `docs/design/` — 每个改进一个设计文档
