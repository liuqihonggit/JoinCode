# 设计：goal 进度可视化

## 需求

TopBar goal 按钮加进度环（阶段/总阶段）+ 当前执行节点名，一眼看出 goal 跑到哪了。

## 现状分析

- `GoalEngine.CurrentState` 暴露 `GoalState?`（有 `TokensUsed`/`TurnsCompleted`/`TokenBudget`/`TurnBudget`）
- `GoalEngine._goalGraph` 是私有的 `GoalGraph?`，`GoalGraph.Dag.Nodes` 有所有节点
- `GoalNodePayload.Status` 有 Pending/Running/Completed/Failed
- `GraphExecutionContext` 有 `CompletedCount`/`FailedCount`，但是内部的
- `MainViewModel` 只有 `IsGoalRunning` 布尔值，没有进度信息

## 设计方案

### 方案：GoalEngine 加进度查询方法

**核心思路**：`GoalEngine` 加 `GetGoalProgress()` 方法，从 `_goalGraph.Dag.Nodes` 计算节点级进度，返回 `(completedCount, totalCount, currentNodeName)`。

### 改动清单

#### 1. GoalEngine 加进度查询方法
```csharp
public GoalProgress? GetGoalProgress() {
    if (_goalGraph is null) return null;
    var nodes = _goalGraph.Dag.Nodes.Values.ToList();
    var completed = nodes.Count(n => n.Payload.Status == GoalNodeStatus.Completed);
    var running = nodes.FirstOrDefault(n => n.Payload.Status == GoalNodeStatus.Running);
    return new GoalProgress {
        CompletedNodes = completed,
        TotalNodes = nodes.Count,
        CurrentNodeName = running?.Payload.Name
    };
}
```

#### 2. 加 GoalProgress record
```csharp
public sealed record GoalProgress(int CompletedNodes, int TotalNodes, string? CurrentNodeName);
```

#### 3. IJccChatSession 加查询方法
```csharp
Task<GoalProgress?> GetGoalProgressAsync(CancellationToken ct = default) => Task.FromResult<GoalProgress?>(null);
```

#### 4. JccChatSession 实现 — 委托 GoalEngine
#### 5. MainViewModel 加 GoalProgressText 属性
```csharp
[ObservableProperty] private string _goalProgressText = string.Empty;
// 定时刷新或事件驱动更新
```

#### 6. TopBar goal 按钮显示进度
```xml
<ContentControl Content="{Binding GoalProgressText}" />
```

## 涉及文件

- `lib/clock/goal/core/goal_engine/GoalEngine.cs` — 加 GetGoalProgress 方法
- `lib/abstractions/abs_core/models/models_agent/goal/GoalProgress.cs` — 新增 record
- `app/gui/hosting/IJccChatSession.cs` — 加 GetGoalProgressAsync
- `app/gui/hosting/JccChatSession.cs` — 实现
- `app/gui/view_models/main_view_model/MainViewModel.Interceptors.cs` — 加 GoalProgressText
- `app/gui/views/TopBarView.axaml` — goal 按钮显示进度
