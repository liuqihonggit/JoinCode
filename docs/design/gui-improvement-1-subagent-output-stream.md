# 设计：子代理实时输出流

## 需求

后台代理面板每个卡片可展开看实时 token 流 + 工具调用历史，不用切到主对话框就能监控子代理进展。

## 现状分析

### 两套并行的子代理 GUI 模型

| 维度 | 管理面板（要改进） | 运行态面板（已有实时流） |
|------|------------|--------------|
| VM 类 | `BackgroundAgentItemVm` | `SubAgentRun` |
| 聚合器 | `BackgroundAgentsPanelViewModel` | `SubAgentRunTracker` |
| 数据源 | `IAgentService.GetRunningAgentsAsync`（pull 快照） | `ChatStreamEvent` 流（via `SubAgentEventChannel`） |
| 输出流字段 | **无** | `FinalOutput` / `LastActivityText` / `VisibleActivities` / `Transcript` |
| 刷新方式 | `RefreshAsync()` 手动拉取 | `Observe(evt)` 事件驱动 |

### 关键发现
- `SubAgentRunTracker` 已聚合子代理实时事件流到 `SubAgentRun`
- `SubAgentRun` 有 `VisibleActivities`（尾部 3 条活动）+ `Transcript`（完整时间线）+ `LastActivityText`
- `BackgroundAgentsPanelViewModel` 是 pull 模型快照，不订阅实时事件流
- `SubAgentRunTracker` 在 `ChatTurnProcessor` 中私有持有

## 设计方案

### 方案：复用 SubAgentRunTracker 数据

**核心思路**：`BackgroundAgentsPanelViewModel` 注入 `SubAgentRunTracker` 引用，展开卡片时从 tracker 按 AgentId 查找 `SubAgentRun`，显示其活动数据。

**为什么不直接订阅事件流**：
- `SubAgentRunTracker` 已做事件聚合 + 环形缓冲 + LRU 驱逐，重复实现是造轮子
- pull 模型快照 + 按需查 tracker 的混合模型更简单，不引入新的事件订阅链路
- tracker 中没有的 agent（如 fork 路径）优雅降级显示"无实时输出"

### 改动清单

#### 1. `BackgroundAgentItemVm` 加展开属性
```csharp
public bool IsExpanded { get; set; }  // 展开/收起状态
```

#### 2. `BackgroundAgentsPanelViewModel` 注入 tracker
```csharp
private readonly SubAgentRunTracker? _runTracker;

public BackgroundAgentsPanelViewModel(
    Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>> fetcher,
    Func<string, CancellationToken, Task<bool>> stopper,
    Func<string, CancellationToken, Task<bool>>? pauser = null,
    Func<string, CancellationToken, Task<bool>>? resumer = null,
    SubAgentRunTracker? runTracker = null)  // 新增可选注入
```

#### 3. 加展开/收起命令
```csharp
[RelayCommand]
public void ToggleExpand(string? agentId) {
    var item = Items.FirstOrDefault(i => i.AgentId == agentId);
    if (item is not null) item.IsExpanded = !item.IsExpanded;
}
```

#### 4. 加获取活动数据方法
```csharp
public IReadOnlyList<string> GetActivities(string agentId) {
    if (_runTracker is null) return [];
    var run = _runTracker.Runs.FirstOrDefault(r => r.AgentId == agentId);
    return run?.VisibleActivities ?? [];
}

public string? GetLastActivity(string agentId) {
    if (_runTracker is null) return null;
    var run = _runTracker.Runs.FirstOrDefault(r => r.AgentId == agentId);
    return run?.LastActivityText;
}
```

#### 5. XAML 卡片加展开按钮 + 展开区
- 卡片左侧加展开/收起按钮（▶/▼）
- 展开时显示 `GetActivities(AgentId)` 的活动列表
- 展开时显示 `GetLastActivity(AgentId)` 的当前活动

#### 6. `MainViewModel.Lifecycle.cs` 注入 tracker
- 把 `ChatTurnProcessor` 的 `_agentTracker` 暴露出来
- 传给 `BackgroundAgentsPanelViewModel` 构造函数

## TDD 测试

### 红测试
1. `ToggleExpand_ValidId_TogglesIsExpanded` — 展开/收起切换
2. `GetActivities_TrackerHasRun_ReturnsVisibleActivities` — 有 tracker 数据时返回活动
3. `GetActivities_TrackerNull_ReturnsEmpty` — 无 tracker 时返回空
4. `GetActivities_AgentNotInTracker_ReturnsEmpty` — agent 不在 tracker 时返回空

## 涉及文件

- `app/gui/view_models/a_to_c/BackgroundAgentsPanelViewModel.cs` — 加 tracker 注入 + 展开/活动方法
- `app/gui/views/MainWindow.axaml` — 卡片加展开按钮 + 展开区
- `app/gui/view_models/main_view_model/MainViewModel.Lifecycle.cs` — 注入 tracker
- `app/gui/view_models/a_to_c/ChatTurnProcessor.cs` — 暴露 tracker
- `test/unit/join_code_gui.tests/view_models/BackgroundAgentsPanelTests.cs` — 加展开测试
