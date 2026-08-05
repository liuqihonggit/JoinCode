# Goal 断裂点修复计划

> 生成时间：2026-08-05
> 背景：排查 /goal 命令相关代码，发现断裂点并逐一修复清理。

---

## 〇、Goal 完整流程图

### 1. /goal 命令入口 → GoalEngine 启动

```
用户输入 /goal <目标>
        │
        ▼
┌─────────────────┐
│  GoalCommand    │
│  ExecuteAsync   │
└────────┬────────┘
         │
    ┌────┴────┬────────┬──────────┐
    ▼         ▼        ▼          ▼
 /goal     /goal     /goal      /goal <目标>
 pause     resume    clear      [--cron] [--constraint] [--budget]
    │         │        │          │
    ▼         ▼        ▼          ▼
 PauseAsync ResumeAsync ClearAsync  ┌─────────────────┐
                                     │  GoalEngine     │
                                     │  StartAsync     │
                                     └────────┬────────┘
                                              │
                                    ┌─────────▼─────────┐
                                    │ BuildDefaultGraph │
                                    │    IfAbsent       │
                                    └─────────┬─────────┘
                                              │
                                   ┌──────────┴──────────┐
                                   ▼                     ▼
                            模板匹配成功            无匹配模板
                                   │                     │
                                   ▼                     ▼
                          template.BuildGraph     默认 agent→reviewer 图
                                   │                     │
                                   └──────────┬──────────┘
                                              ▼
                                    ┌─────────────────┐
                                    │  RunGoalLoopAsync│
                                    │  (仅 Graph 模式) │
                                    └────────┬────────┘
                                             │
                                             ▼
                                    ┌──────────────────┐
                                    │ GoalGraphEngine  │
                                    │  ExecuteAsync    │
                                    │  (DAG 调度)      │
                                    └──────────────────┘
```

### 2. GoalGraphEngine DAG 调度

```
┌─────────────────────────────────────────────────┐
│  ExecuteAsync(graph, goalState, chatHistory, ct) │
└────────────────────┬────────────────────────────┘
                     │
                     ▼
              ReadyQueue.Enqueue(StartNode)
                     │
                     ▼
              ┌──────────────┐
              │ while 循环   │◀─────────────────┐
              │ TryDequeue   │                   │
              └──────┬───────┘                   │
                     │                           │
                     ▼                           │
           ┌──────────────────┐                  │
           │AreAllUpstreams   │──否──▶ 重新入队  │
           │  Completed?      │      +Delay(50)─┘
           └────────┬─────────┘
                    │是
                    ▼
           ┌──────────────────┐
           │ ExecuteNodeAsync │
           └────────┬─────────┘
                    │
           ┌────────┴────────┐
           ▼                 ▼
     Kind=Agent        Kind=Function     Kind=Join
           │                 │                 │
           ▼                 ▼                 ▼
   IAgentService      _functionRegistry   CollectUpstream
   .RunAgentStream    [nodeId]            CountSuccessful
   → 评估+合成        → fn(NodeContext)   → MinSuccessfulInputs
           │                 │                 │
           └────────┬────────┘                 │
                    ▼                          │
           ┌──────────────────┐                │
           │  结果处理        │                │
           │  成功→Completed  │                │
           │  失败→Failed     │                │
           └────────┬─────────┘                │
                    │                          │
                    ▼                          │
           ┌──────────────────┐                │
           │ GetNextNodeIds    │───────────────┘
           │ → Enqueue 下游    │
           └────────┬─────────┘
                    │
                    ▼
           ┌──────────────────┐
           │ 终止判定          │
           │ EndNode完成→Achieved│
           │ EndNode失败→Unmet │
           │ 预算耗尽→BudgetLimited│
           └──────────────────┘
```

### 3. cluster 模板（并行集群执行）

```
┌──────────────┐     ┌───────────────┐
│ cluster_     │     │ cluster_      │
│  analyze     │────▶│  expand       │
│ (Agent/      │     │ (Function)    │
│ Coordinator) │     │ 动态展开Worker │
│ 分析可分解性  │     │ +审批检查     │
└──────────────┘     └───────┬───────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌─────────┐   ┌─────────┐   ┌─────────┐
        │ worker_1│   │ worker_2│   │ worker_N│
        │(Executor│   │(Executor│   │(Executor│
        │ Worktree│   │ Worktree│   │ Worktree│
        │ 隔离)   │   │ 隔离)   │   │ 隔离)/   │
        └────┬────┘   └────┬────┘   └────┬────┘
             │             │             │
             └─────────────┼─────────────┘
                           ▼
                    ┌──────────────┐
                    │ cluster_     │
                    │  gather      │
                    │ (Join节点)   │
                    │ 等待所有Worker│
                    │ MinSuccessfulInputs│
                    └──────┬───────┘
                           ▼
                    ┌──────────────┐
                    │ cluster_     │
                    │  merge       │
                    │ (Agent/      │
                    │ Coordinator) │
                    │ 评估+合成    │ ← 管理者在完整上下文中
                    │ Worker质量   │   同时评估每个 Worker 的 0-1 分
                    │ +合并结果    │   +合成最终输出
                    └──────┬───────┘
                           ▼
                    ┌──────────────┐
                    │ cluster_     │
                    │  review      │
                    │ (Agent/      │
                    │ Coordinator) │
                    │ 独立审查     │
                    │ FreshContext │
                    └──────────────┘
```

### 4. 状态转换

```
                 StartAsync
                      │
                      ▼
                ┌──────────┐
                │ Pursuing │
                └────┬─────┘
                     │
          ┌──────────┼──────────┬────────────┐
          ▼          ▼          ▼            ▼
    ┌──────────┐ ┌────────┐ ┌──────────┐ ┌────────┐
    │ Achieved │ │ Unmet  │ │BudgetLimit│ │ Paused │
    └──────────┘ └────────┘ └──────────┘ └────────┘
     评估通过     EndNode    预算耗尽     /goal pause
     或所有       失败                    /goal resume
     EndNode      或超时                  → Pursuing
     完成
```

### 5. 其他模板

| 模板 | 流程 | 用途 |
|------|------|------|
| refactor | agent → reviewer | 代码重构 |
| bugfix | agent → reviewer | 修复 bug |
| research | agent → reviewer | 研究调查 |
| code_review | agent → reviewer | 代码审查 |
| test_gen | agent → reviewer | 生成测试 |
| negative_review_loop | agent → neg_review → fix_neg（循环）| 负向评价修复循环 |

---

## 一、已修复清单

### ✅ P0 #1：GoalGraphEngine._agentService 永远 null

- **commit**: `3552cebd3`
- **问题**: `GoalGraphEngine` 被 `GoalEngine` 手动 `new` 创建，不走 DI，`[Inject] IAgentService` 不生效 → 所有 `/goal` Agent 节点返回失败 "IAgentService 未注入"
- **修复**: 构造函数中加 `_agentService = serviceProvider.GetService<IAgentService>();`

### ✅ P1 #8：StartAsync(GoalGraph, GoalGraphEngine) 死代码

- **commit**: `ae7bbc05f`
- **问题**: 该重载无任何外部调用方，仅设置字段后转发到 `StartAsync(string)` 重载
- **修复**: 删除该重载（不在 IGoalEngine 接口中，不破坏契约）

### ✅ #2/#3/#7：cluster_merge 评估断裂 + 孤儿代码移走

- **commit**: `a65f0fdcc` + `22e13ee6f`
- **问题**: cluster_merge 是 LLM Agent 合并但不评估 Worker 质量；SubAgentGrader/LeadMergeOrchestrator 是孤儿代码
- **修复**: 采纳 Anthropic orchestrator-worker 模式，cluster_merge SystemPrompt 加评估+合成指令；SubAgentGrader/LeadMergeOrchestrator 移到 .xxx/

### ✅ #5/#6：辅助服务移走

- **commit**: `9b7b2aeba`
- **问题**: ClusterTelemetry/ClusterResultSummarizer 已实现但零消费方
- **修复**: 移到 .xxx/

### ✅ 单 Agent 运行模式清理

- **commit**: `1384febed`
- **问题**: RunGoalLoopAsync 有两种模式（Graph 模式 + 单 Agent while 循环），单 Agent 模式是冗余的 fallback
- **修复**: 删除 while 循环 + ExecuteAgentTurnAsync + GoalTurnResult，只保留 Graph 模式；BuildDefaultGraphIfAbsent 改为 ArgumentNullException.ThrowIfNull 确保总是构建 Graph

### ✅ #4：IGoalApprovalHandler 孤儿接口移走

- **commit**: `1384febed`
- **问题**: 接口定义存在但全项目无实现类、无消费方
- **修复**: 移到 .xxx/

---

## 二、设计选择（非 bug，不修改）

### #10：AreAllUpstreamsCompleted 把 FailedNodes 算"完成"

- 上游失败时下游仍可执行（容错推进设计），改了会导致 DAG 停滞

### #11：ExecuteAsync ReadyQueue 空时返回 Pursuing

- 条件路由未走到 EndNode 的合理行为

---

## 三、决策记录

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: P0 #1 修复方式选择从 serviceProvider 解析而非改用 DI 创建 -->
<!-- 原因: 与现有 _userInteraction/_loopObserver 模式一致，改动最小 -->
<!-- 替代方案: 让 GoalEngine 通过 DI 创建 GoalGraphEngine（改动大，需重构 GoalEngine）-->
<!-- 验证: 编译通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: cluster_merge 采纳 Anthropic orchestrator-worker 模式 -->
<!-- 原因: Anthropic 的多 Agent 系统中 Lead agent 在完整上下文中同时评估+合成，不做独立评分和程序化合并 -->
<!-- 替代方案: 程序化合并（LeadMergeOrchestrator）或独立评分（SubAgentGrader）-->
<!-- 验证: 编译通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: 删除单 Agent 运行模式 -->
<!-- 原因: BuildDefaultGraphIfAbsent 总是构建 Graph（模板匹配或默认 agent→reviewer），单 Agent while 循环是冗余 fallback -->
<!-- 替代方案: 保留单 Agent 模式作为 fallback（但增加维护负担）-->
<!-- 验证: 编译通过 ✅ -->
