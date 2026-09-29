# 子代理视图 + 暂停/继续控制任务

## 需求

1. 添加子代理视图 — 子代理运行期间的上下文,保留用户直接操作对话框的功能
2. 子代理视图中提供 `暂停` `继续` 两个按钮（每个子代理独立）
3. 子代理运行期间,外部有 `暂停所有子代理` `恢复所有子代理` 按钮
4. 每个按钮都可以让主代理自由操控 — 通过 MCP 工具

## 设计决策

### 架构方向（用户确认）
- **子代理上下文位置**:右侧可折叠侧边栏面板,每个子代理一个卡片（状态+暂停/继续+上下文流）,主对话框不受影响
- **MCP 工具粒度**:1个统一工具 `subagent_control(action, id?)` 覆盖所有操作
- **统一控制点**:GUI 按钮和 MCP 工具都委托到同一个 `ISubAgentController`,不重复逻辑

### 现有基础设施
- `AgentStatus.Paused = 3` 已定义（`AppState.cs:192`）但未使用
- `InterruptSubAgentAsync` 已有"中断进 idle"语义（teammate 路径）,是事实上的暂停
- `ForkSubAgentManagerActor` 是 Actor,可加 Pause/Resume 命令
- `SubAgentEventChannel`（AsyncLocal）管输出流,`SubAgentRunTracker` 聚合到 GUI
- `SubAgentRunTracker.Observe(evt)` 已按 AgentId 路由归约为每 agent 一行的运行记录

### 实现层次

```
┌─ GUI 层 ──────────────────────────────────────────────┐
│  SubAgentView.axaml (右侧侧边栏)                       │
│  ├─ 每个子代理卡片:状态+暂停/继续按钮+上下文流         │
│  └─ TopBar:暂停所有/恢复所有按钮                       │
│  MainViewModel.SubAgents.cs — 子代理视图 ViewModel     │
└──────────────────────────────────────────────────────┘
          │
          ▼
┌─ 接口层 ──────────────────────────────────────────────┐
│  IJccChatSession 扩展:                                 │
│  ├─ PauseSubAgentAsync(id) / ResumeSubAgentAsync(id)   │
│  ├─ PauseAllSubAgentsAsync() / ResumeAllSubAgentsAsync()│
│  └─ GetSubAgentStatesAsync() → SubAgentState[]         │
└──────────────────────────────────────────────────────┘
          │
          ▼
┌─ 引擎层 ──────────────────────────────────────────────┐
│  ISubAgentController (统一控制点)                       │
│  ├─ PauseAsync(id) / ResumeAsync(id)                   │
│  ├─ PauseAllAsync() / ResumeAllAsync()                 │
│  └─ GetStates() → SubAgentState[]                     │
│  ForkSubAgentManagerActor 加 PauseForkCmd/ResumeForkCmd│
└──────────────────────────────────────────────────────┘
          │
          ▼
┌─ MCP 工具层 ──────────────────────────────────────────┐
│  [McpTool("subagent_control", ...)]                    │
│  action: list/pause/resume/pause_all/resume_all        │
│  id: 指定子代理（pause/resume 必填）                   │
└──────────────────────────────────────────────────────┘
```

## 执行顺序

```
1. 引擎层: ISubAgentController 接口 + ForkSubAgentManagerActor Pause/Resume 命令
2. 接口层: IJccChatSession 扩展 + JccChatSession 实现
3. MCP 工具: subagent_control 统一工具
4. GUI 层: SubAgentView 侧边栏 + MainViewModel.SubAgents.cs
5. TopBar: 暂停所有/恢复所有按钮
6. 编译验证 + 测试 + git 提交
```

## 涉及文件

### 引擎层（新增/修改）
- `lib/abstractions/abs_agents/agent/sub_agent/ISubAgentController.cs` — 新增接口
- `llm/agents/Coordinator/Fork/ForkSubAgentManagerActor.cs` — 加 PauseForkCmd/ResumeForkCmd
- `llm/agents/Coordinator/Core/services/AgentCoordinator.cs` — 实现 ISubAgentController

### 接口层（修改）
- `app/gui/hosting/IJccChatSession.cs` — 加 Pause/Resume/GetStates 方法
- `app/gui/hosting/JccChatSession.cs` — 实现
- `app/gui/hosting/PlaceholderChatSession.cs` — 空实现

### MCP 工具层（新增）
- `kit/hands/tool_handlers/system_tools/agent/SubAgentControlToolHandlers.cs` — subagent_control 工具

### GUI 层（新增/修改）
- `app/gui/views/SubAgentView.axaml` — 子代理侧边栏面板
- `app/gui/view_models/main_view_model/MainViewModel.SubAgents.cs` — 子代理 ViewModel
- `app/gui/views/TopBarView.axaml` — 暂停所有/恢复所有按钮
- `app/gui/views/MainWindow.axaml` — 集成 SubAgentView 侧边栏
