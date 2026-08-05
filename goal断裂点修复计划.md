# Goal 断裂点修复计划

> 生成时间：2026-08-05
> 背景：排查 /goal 命令相关代码，发现 16 个断裂点，已修复 2 个，剩余 5 个需架构决策。

---

## 一、已修复（2 个）

### ✅ P0 #1：GoalGraphEngine._agentService 永远 null

- **commit**: `3552cebd3`
- **问题**: `GoalGraphEngine` 被 `GoalEngine` 手动 `new` 创建，不走 DI，`[Inject] IAgentService` 不生效 → 所有 `/goal` Agent 节点返回失败 "IAgentService 未注入"
- **修复**: 构造函数中加 `_agentService = serviceProvider.GetService<IAgentService>();`（与已有的 `_userInteraction`/`_loopObserver` 模式一致）
- **文件**: `composition/Clock/src/Goal/Core/GoalGraphEngine.cs`

### ✅ P1 #8：StartAsync(GoalGraph, GoalGraphEngine) 死代码

- **commit**: `ae7bbc05f`
- **问题**: 该重载无任何外部调用方，仅设置字段后转发到 `StartAsync(string)` 重载
- **修复**: 删除该重载（不在 IGoalEngine 接口中，不破坏契约）
- **文件**: `composition/Clock/src/Goal/Core/GoalEngine.cs`

---

## 二、剩余断裂点（5 个，需架构决策）

### #2/#3/#7：LeadMergeOrchestrator + cluster_merge + SubAgentGrader（逻辑冲突）

**现状**:
- `LeadMergeOrchestrator` 已实现（拓扑排序+git merge+build验证+冲突预检），标注 `[Register]`，但**零消费方**
- `cluster_merge` 节点是 `Kind=Agent`（LLM Agent 合并），不调用 `LeadMergeOrchestrator`
- `SubAgentGrader` 已实现（规则+LLM 评分），标注 `[Register]`，但**零消费方**
- `LeadMergeOrchestrator` 第32行用 `GradingScore >= 0.6` 过滤 Worker

**逻辑冲突**: SubAgentGrader 从未被调用 → `GradingScore` 永远是默认值 0 → LeadMergeOrchestrator 的 `>= 0.6` 过滤淘汰所有 Worker → 即使集成 LeadMergeOrchestrator 也会返回"没有可合并的 Worker"

**融合方案**:
1. `ClusterExpandFunction` 中保存 `ClusterPlan` 到 `GraphExecutionContext`（当前用完即丢）
2. Worker 节点完成后调用 `SubAgentGrader.GradeAsync` 评分，写入 `WorkerCompletion.GradingScore`
3. `cluster_merge` 从 `Kind=Agent` 改为 `Kind=Function`，构造 `LeadMergeContext` 调用 `LeadMergeOrchestrator.MergeCompletedWorkersAsync`
4. 程序化合并（拓扑排序+git merge+build验证）替代 LLM Agent 合并

**影响面**:
- `GoalGraphTemplates.cs`：cluster_merge 节点定义改为 Function + 新增 cluster_merge Function handler
- `GoalGraphEngine.cs`：Worker 完成后触发评分（ExecuteAgentNodeAsync 或 ExecuteNodeAsync 后置）
- `GraphExecutionContext.cs`：可能需要加字段存 ClusterPlan 和 WorkerCompletion 列表

**风险**: 中。改变 cluster 合并策略（LLM → 程序化），但 LeadMergeOrchestrator 已有完整实现和单元测试

**替代方案**: 保留 cluster_merge 为 Agent，但在 Agent 的 SystemPrompt 中注入 Worker 评分信息（由 SubAgentGrader 预先评分）。不调用 LeadMergeOrchestrator，仅接入 SubAgentGrader。

---

### #5/#6：ClusterTelemetry + ClusterResultSummarizer（孤儿辅助服务）

**现状**:
- `ClusterTelemetry`：记录 cluster 各阶段耗时/成功失败，`RecordPhase` 零调用
- `ClusterResultSummarizer`：用 LLM 汇总 cluster 执行结果，`SummarizeAsync` 零调用

**融合方案**:
- 在 `GoalGraphEngine.ExecuteNodeAsync` 完成后，对 cluster 相关节点调用 `ClusterTelemetry.RecordPhase`
- 图执行完成后，如果包含 cluster 节点，调用 `ClusterResultSummarizer.SummarizeAsync` 生成汇总

**影响面**: `GoalGraphEngine.cs` 加几行调用

**风险**: 低。纯辅助功能，不影响核心逻辑，也不修复 #2/#3/#7 的冲突

---

### #9：TeamManager 与 Goal 体系脱节

**现状**:
- `TeamManager` 实现 `ITeamManager`，管理 Team/Teammate 体系（LeadAgentId、消息通信、资源共享）
- `composition/Clock/src/Goal/` 目录下**完全无引用**
- Goal 的并行执行通过 DAG 调度（GoalGraphEngine），不使用 Team 体系

**融合方案**: 需要大改。要么在 Goal 中引入 Team 体系（DAG 节点 → Team Worker），要么废弃 Team 体系。这是架构级决策。

**风险**: 高。两套并行机制孤立，融合需要重新设计并行执行架构

**建议**: 暂不处理，留到后续架构重构

---

### #4：IGoalApprovalHandler 无实现无消费方

**现状**: 接口定义存在（`ApproveTaskAsync`），但全项目无实现类、无消费方。`cluster_expand` 中 `approvalHook` 有 null 检查，不会崩溃。

**建议**: 完全孤儿接口，零影响，暂不处理

---

### #10/#11：DAG 调度逻辑（设计选择，非 bug）

- **#10**: `AreAllUpstreamsCompleted` 把 FailedNodes 也算"完成" → 上游失败时下游仍可执行（容错推进设计，改了会导致 DAG 停滞）
- **#11**: `ExecuteAsync` ReadyQueue 空时返回 `Pursuing` → 条件路由未走到 EndNode 的合理行为

**建议**: 不修改，是设计选择

---

## 三、需求 c/d 设计思路（待确认）

### 需求 c：Lead 后台低频率扫描冲突

**架构现状**:
- `GoalEngine` 用 `Task.Run` 启动后台循环，`GoalGraphEngine` 用 `Queue + Task.Delay(50)` 调度
- 项目有 `PeriodicBackgroundServiceBase`（IHostedService）可用于周期性后台服务
- `IGitCommandRunner` 已有 `DetectMergeConflictAsync` + `DetectStaleConflictMarkersAsync`（已实现）

**设计思路**:
- 创建 `MergeConflictMonitorService`（继承 `PeriodicBackgroundServiceBase`）
- goal 启动时激活，goal 结束时停止
- 定期扫描所有活跃 worktree 的冲突标记 + merge-tree 预检
- 发现冲突 → 写标记文件 + 通过消息注入通知 Lead

### 需求 d：自动合并标记 + 提示

**设计思路**:
- 发现冲突 → 在主 worktree 写 `.merge-conflict-detected` 标记文件
- 通过 `GoalEngine` 消息注入机制通知 Lead Agent
- Lead 决定是否调用 `fix_merge_conflict` 工具修复（已实现）
- 不自动合并，由 Lead 决策

---

## 四、推荐执行顺序

| 步骤 | 内容 | 风险 | 依赖 |
|------|------|------|------|
| 1 | 融合 #5/#6 辅助服务（低风险验证接入模式） | 低 | 无 |
| 2 | 融合 #2/#3/#7 LeadMergeOrchestrator + SubAgentGrader | 中 | 步骤1验证接入模式 |
| 3 | 需求 c：后台冲突扫描服务 | 中 | #2/#3/#7 融合后 LeadMergeOrchestrator 已激活 |
| 4 | 需求 d：自动合并标记 + 提示 | 低 | 需求 c |

---

## 五、决策记录

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: P0 #1 修复方式选择从 serviceProvider 解析而非改用 DI 创建 -->
<!-- 原因: 与现有 _userInteraction/_loopObserver 模式一致，改动最小 -->
<!-- 替代方案: 让 GoalEngine 通过 DI 创建 GoalGraphEngine（改动大，需重构 GoalEngine）-->
<!-- 验证: 编译通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: #10/#11 判定为设计选择不修改 -->
<!-- 原因: #10 容错推进设计（上游失败下游仍可处理），#11 条件路由未走到 EndNode 的合理行为 -->
<!-- 替代方案: 可改为失败传播+图未完成报错，但会导致 DAG 停滞 -->
<!-- 验证: 代码分析确认，未修改 -->
