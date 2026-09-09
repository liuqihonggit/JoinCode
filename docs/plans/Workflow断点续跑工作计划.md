# Workflow 断点续跑工作计划

> 来源规格：`D:\Users\54076\Desktop\1并行工具调用设计 - 副本.txt`（并行工具调用 + DAG依赖 + 断点续跑）
> 探索报告：w3 已实现规格 90% 能力，唯一硬缺口是 **Workflow 级断点续跑**
> 关联 ADR：[0013](../adr/0013-hypergraph-vs-dag-separation.md)（DAG/超图分离）、[0068](../adr/0068-unified-persistence-pipeline-actor.md)（统一持久化管道）、[0074](../adr/0074-actor-supervisor-tree.md)（Actor 监督树）
> 待新建 ADR：0094（Workflow 级断点续跑持久化策略）

---

## 一、背景

规格要求"并行工具调用 + DAG依赖 + 断点续跑"，探索发现 w3 已有：

| 能力 | 现有实现 | 位置 |
|------|----------|------|
| DAG 算法 | 分层拓扑+增量重算+静态环检测+线程安全 | `foundation/Structura/Dag/Dag.cs` |
| 并行执行 | Actor 模型 + Channel<T>（非裸 Parallel.ForEachAsync） | `core/execution/Scheduling/src/Execution/ParallelExecutionEngine.cs` |
| 任务级断点续跑 | PersistAsync/RecoverTasksAsync/QuarantineCorruptFileAsync | `core/execution/Scheduling/src/Runtime/TaskRuntime.cs:281` |
| 重试 | 指数退避+抖动+三档预设 | `core/execution/Hands/src/Api/Core/RetryPolicy.cs:129` |
| 工具注册 | IToolRegistry + 源码生成器 [McpTool] | `core/execution/Hands/src/ToolHandlers/Handlers/Core/Registry/LocalToolRegistry.cs` |

**三个硬缺口**：
1. **Workflow 级断点续跑缺失**：`WorkflowTaskExecutor._activeWorkflows`（`WorkflowTask.cs:84`）是内存 `ConcurrentDictionary`，进程崩溃全丢；`WorkflowDefinition` 不持久化 CompletedSteps/FailedSteps/SkipReasons
2. **TaskRuntime.PersistAsync 非原子写**：`TaskRuntime.cs:298` 直接 `WriteFileAsync` 覆盖写，崩溃中途损坏；应复用 `TaskFileWriter.WriteAtomicAsync` 模式（temp+move）
3. **统一 workflow 进度流缺失**：各层事件分散（TaskStatusChanged/OnDependencyMet/OnProgress），无聚合 sink

---

## 二、目标

补齐 Workflow 级断点续跑，使 `WorkflowTaskExecutor` 在 DAG 模式下：
- 每完成一层步骤后**原子化保存**快照到 `workflow_{id}.state.json`
- 启动时**加载快照**跳过已完成步骤（幂等恢复）
- 依赖失败传播时记录 **SkipReasons**
- 进程崩溃后重启传入相同 `workflowId` → 自动恢复继续执行

**不做的事**（超出本计划范围，待后续确认需求）：
- Redis / RabbitMQ / 跨进程分布式持久化
- Web API 进度查询端点
- 统一 workflow 进度流（方向 2，单独计划）
- Sequential/Parallel 模式的断点续跑（先做 DAG 模式，其他模式后续复用）

---

## 三、架构约束（不可违背）

| 约束 | 说明 |
|------|------|
| 不回退到 Parallel.ForEachAsync | w3 用 Actor 模型 + Channel<T>（ADR 0074/0091），背压+监督树+全双工更优 |
| 不改循环依赖为抛异常 | w3 用 OperationResult.Fail + 本地化消息（Rust 风格 Result 模式，ADR 0044） |
| 原子写用 temp+move | 复用 `TaskFileWriter.WriteAtomicAsync` 模式，不重复造轮子 |
| 损坏文件隔离不删除 | 复用 `QuarantineCorruptFileAsync` 模式（ADR 0008 归档不删除） |
| AOT 兼容 | JsonContext + RelaxedJsonSerializer，禁止 dynamic/反射 emit |
| using var 释放资源 | 遵循 ADR 0093 资源管理规范 |

---

## 四、分阶段任务（从最小开始）

### 阶段 0：修复 TaskRuntime.PersistAsync 非原子写（现有 bug，前置依赖）

**为什么先做**：风险最低、是阶段 1 的前置依赖（Workflow 快照也需要原子写）、修复现有 bug 不引入新功能。

**改动**：
- `TaskFileWriter` 新增 `WriteAtomicAsync(string filePath, string json, CancellationToken)` 重载（接受任意 JSON 字符串，复用 temp+move 逻辑）
- `TaskRuntime.PersistAsync`（`TaskRuntime.cs:298`）改调 `WriteAtomicAsync` 而非 `WriteFileAsync`

**TDD 循环**：
- 🔴 单元红：写测试"PersistAsync 写入中途模拟崩溃 → 不应残留半写文件"（用故障注入 IFileOperationService 在 MoveFileAsync 前抛异常，验证原文件未损坏）
- 🟢 单元绿：实现原子写
- 🔵 重构：提取公共原子写逻辑

**验收**：
- ✅ TaskRuntime.PersistAsync 用 temp+move 原子写
- ✅ 现有 TaskRuntimeTests 全部通过
- ✅ 新增"崩溃不留半文件"测试通过

**涉及文件**：
- `core/execution/Scheduling/src/Storage/TaskFileWriter.cs`（加 string 重载）
- `core/execution/Scheduling/src/Runtime/TaskRuntime.cs:298`（改调用）
- `core/execution/Scheduling/tests/Unit/Scheduling/TaskRuntimeTests.cs`（加测试）
- `core/execution/Scheduling/src/Storage/ITaskFileWriter.cs`（接口加方法）

---

### 阶段 1：新增 WorkflowSnapshot 数据结构 + WorkflowStateStore

**为什么做**：Workflow 级断点续跑的核心数据结构 + 持久化服务，不接入执行器，独立可测。

**改动**：
- 新增 `WorkflowSnapshot`（含 WorkflowId/CompletedSteps/FailedSteps/SkippedSteps/StepResults/SkipReasons/LastUpdated）
- 新增 `IWorkflowStateStore` 接口（SaveSnapshotAsync/LoadSnapshotAsync/QuarantineCorruptAsync）
- 新增 `WorkflowStateStore` 实现（原子写 `workflow_{id}.state.json` + 损坏隔离）
- JsonContext 注册 WorkflowSnapshot

**TDD 循环**：
- 🔴 单元红：Save→Load round-trip 测试 / 损坏文件隔离测试 / 不存在文件返回空测试
- 🟢 单元绿：实现 WorkflowStateStore
- 🔵 重构：复用 TaskFileWriter 的原子写 + QuarantineCorruptFileAsync 模式

**验收**：
- ✅ Save→Load round-trip 数据一致
- ✅ 损坏文件被隔离到 `.corrupt` 后缀，Load 返回空
- ✅ 不存在文件 Load 返回空 snapshot
- ✅ AOT 兼容（JsonContext 注册）

**涉及文件**（新建）：
- `core/execution/Scheduling/src/Storage/WorkflowSnapshot.cs`
- `core/execution/Scheduling/src/Storage/IWorkflowStateStore.cs`
- `core/execution/Scheduling/src/Storage/WorkflowStateStore.cs`
- `core/execution/Scheduling/tests/Unit/Scheduling/Storage/WorkflowStateStoreTests.cs`
- `core/execution/Scheduling/src/SchedulingTasksJsonContext.cs`（注册新类型）

---

### 阶段 2：WorkflowTaskExecutor DAG 模式接入快照保存

**为什么做**：让执行器在 DAG 执行过程中持久化进度，这是断点续跑的"写"半边。

**改动**：
- `WorkflowTaskExecutor` 构造函数注入 `IWorkflowStateStore`（可选，null 时不持久化保持兼容）
- `ExecuteDagAsync` 每完成一层后调 `_stateStore?.SaveSnapshotAsync`
- `WorkflowRunState` 增加 ToSnapshot() 转换方法

**TDD 循环**：
- 🔴 单元红：执行 DAG workflow → 验证 state.json 文件存在且含已完成步骤
- 🟢 单元绿：接入 SaveSnapshotAsync
- 🔵 重构：抽离快照转换逻辑

**验收**：
- ✅ DAG 模式执行后 `workflow_{id}.state.json` 存在
- ✅ 快照含所有已完成步骤的 StepResults
- ✅ IWorkflowStateStore 为 null 时不报错（兼容）

**涉及文件**：
- `core/execution/Scheduling/src/Tasks/WorkflowTask.cs`（ExecuteDagAsync + 构造函数）
- `core/execution/Scheduling/tests/Unit/Scheduling/Tasks/WorkflowTaskExecutorTests.cs`

---

### 阶段 3：ExecuteWorkflowAsync 启动时加载快照恢复

**为什么做**：断点续跑的"读"半边，启动时跳过已完成步骤。

**改动**：
- `ExecuteWorkflowAsync` 开头调 `_stateStore?.LoadSnapshotAsync`
- 若快照存在 → 跳过 CompletedSteps，只执行 pending
- 依赖失败传播：FailedSteps 的下游标记 Skipped + 记录 SkipReasons

**TDD 循环**：
- 🔴 单元红：预置快照含已完成步骤 A → 执行时 A 不重复执行，直接从 B 开始
- 🟢 单元绿：实现恢复逻辑
- 🔵 重构：统一"从快照恢复"和"从头执行"路径

**验收**：
- ✅ 预置快照 → 跳过已完成步骤
- ✅ FailedSteps 下游自动 Skipped + SkipReasons 记录原因
- ✅ 无快照时正常从头执行

**涉及文件**：
- `core/execution/Scheduling/src/Tasks/WorkflowTask.cs`（ExecuteWorkflowAsync）
- `core/execution/Scheduling/tests/Unit/Scheduling/Tasks/WorkflowTaskExecutorTests.cs`

---

### 阶段 4：ADR 0094 + E2E 真杀进程测试

**为什么做**：架构决策记录 + 端到端验证。

**改动**：
- 新建 ADR 0094：Workflow 级断点续跑持久化策略（状态：proposed → accepted）
- E2E 测试：启动 workflow → 杀进程 → 重启同 workflowId → 验证跳过已完成步骤继续执行
- AGENTS.md 反向引用标注

**TDD 循环**：
- 🔴 E2E 红：真杀进程重启测试（用 exe 进程级交互模拟）
- 🟢 E2E 绿：阶段 0-3 实现应使 E2E 通过

**验收**：
- ✅ ADR 0094 状态 accepted
- ✅ E2E 真杀进程重启测试通过
- ✅ AGENTS.md 规则1处标注 `> ADR: [0094](docs/adr/0094-xxx.md)`

**涉及文件**（新建）：
- `docs/adr/0094-workflow-checkpoint-resume.md`
- `tests/Integration/...` 或 `tests/E2E/...`（E2E 测试脚本）
- `AGENTS.md`（反向引用）

---

## 五、风险与缓解

| 风险 | 缓解 |
|------|------|
| WorkflowRunState 现有结构不支持 ToSnapshot | 阶段 2 时检查，必要时扩展字段 |
| 多 workflow 并发持久化文件冲突 | 每 workflow 独立 `workflow_{id}.state.json`，无冲突 |
| 快照写入频率过高影响性能 | 只在每层完成后写（非每步），DAG 层数通常远少于步数 |
| 恢复时 DAG 结构与快照不一致 | 快照含 StepId 集合，恢复时校验 definition.Steps 包含这些 ID，不一致则丢弃快照从头执行 |
| IWorkflowStateStore 注入破坏现有测试 | 构造函数参数可选（= null），现有测试不传则不持久化 |

---

## 六、验收标准（整体）

对应规格第九章测试用例：
1. ✅ 无依赖任务全部并行执行 — **已有**（ParallelTaskSchedulerTests）
2. ✅ A→B→C 串行执行 — **已有**（GetExecutableTasks_ShouldRespectDependencies）
3. ✅ A、B 并行 → C 依赖 A+B — **阶段 2/3 补显式测试**
4. ✅ 中途杀死进程 → 重启跳过已完成 — **阶段 4 E2E**
5. ✅ 任务 A 失败 → 依赖 A 的 B 自动跳过 — **阶段 3 SkipReasons**
6. ✅ 网络超时 → 重试 3 次后标记失败 — **已有**（RetryPolicy）

---

## 七、自主决策记录

<!-- 🤖 Auto Decision: 2026-09-09 -->
<!-- 决策: 从阶段 0（TaskRuntime 非原子写修复）开始，而非直接做阶段 1 -->
<!-- 原因: 阶段 0 是现有 bug、风险最低、是阶段 1 的前置依赖（Workflow 快照也需原子写），符合"从最小开始做" -->
<!-- 替代方案: 直接做阶段 1（跳过 bug 修复），但会导致 Workflow 快照也用非原子写，崩溃仍会损坏 -->
<!-- 验证: 待阶段 0 完成后编译+测试通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-09-09 -->
<!-- 决策: IWorkflowStateStore 构造函数参数可选（= null），不破坏现有测试 -->
<!-- 原因: 现有 WorkflowTaskExecutorTests 不传 IWorkflowStateStore，强制注入会破坏数十个测试 -->
<!-- 替代方案: 强制注入 + 批量改测试，但改动面大、违反渐进式原则 -->
<!-- 验证: 待阶段 2 完成后现有测试仍通过 ✅ -->
