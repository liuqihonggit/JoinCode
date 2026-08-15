# 任务：Spawn 管道统一 — 主代理/子代理合并为一条创建管道

## 背景
当前 Agent 创建阶段有三条路径，注册的中间件各不相同：
- **主代理**：直接 `new AgentBase()` / `AgentFactory.Create()`，**不经过任何 Spawn 管道**（`CliSession.cs:141`、`GoalEngine.cs:1073`）
- **子代理路径 A**（`AgentServiceImpl`）：`AgentSpawnContext` 管道，9 个中间件（偏初始化：Definition/Prompt/Context/Worktree/Hook/Mcp/Metadata/Transcript）
- **子代理路径 B**（`AgentCoordinator`）：`AgentSpawnCoordContext` 管道，7 个中间件（偏协调：Lifecycle/Worktree/Message/Context/Permission/TeammatePane）

执行阶段已统一（主子代理共用 `QueryEngine` 查询管道，见提交 `1bf280805`）。本任务统一**创建阶段**。

## 已确认决策
| 决策点 | 选择 | 理由 |
|--------|------|------|
| 统一范围 | 全部合并成一条管道，主代理也走 | 彻底消除三套创建路径 |
| Worktree 失败策略 | 统一为**降级**（Continue + 日志警告） | 符合管道中间件不应硬中断原则；路径 B 的 `[AGT011]` 硬失败改为降级，调用方不依赖硬失败语义 |
| 实施节奏 | **分两阶段** | 阶段一：合并子代理两管道；阶段二：主代理走管道。每阶段独立编译+测试+提交 |
| 主代理 no-op 机制 | 显式 `IsMainAgent` 标志 | 比 `role==Coordinator` 推断可靠，避免子协调器误判 |
| Options 容纳方式 | 双 Options 字段（`SpawnOptions?` + `SubOptions?`） | 不改动 `AgentSpawnOptions`/`SubAgentOptions` 定义，中间件按哪个非 null 判断来源 |

## 统一上下文 UnifiedSpawnContext 设计
```
继承 PipelineContextBase（获得 Failed/ErrorMessage）

输入（init）:
  + string Task                      // B.Task / A.Options.Description
  + AgentSpawnOptions? SpawnOptions  // A 路径原始选项（工具层），主代理/B 路径为 null
  + SubAgentOptions? SubOptions      // B 路径运行层选项，A 路径/主代理为 null
  + CancellationToken CancellationToken
  + bool IsMainAgent                 // 显式主代理标志

中间产物（set）:
  + AgentDefinition? Definition      // A1 产出
  + string SystemPrompt              // A2 产出
  + ProgressTracker ProgressTracker  // A 路径用
  + CacheSafeParams? CacheSafeParams // A3 产出
  + SubAgentOptions? ResolvedSubOptions // A3 组装后的最终 SubOptions

结果（set）:
  + IAgent? Agent                    // 统一命名（合并 A.SubAgent / B.Agent）
  + string AgentId => Agent?.ObjectId.UniqueId ?? ""

协调层登记（set，来自 B）:
  + string? SessionId
  + bool WorktreeCreated
  + bool MessageRegistered
  + DateTime SpawnedAt
  + AgentExecutionContext? ExecutionContext
  + bool PermissionRoutingEnsured
  + bool PlanApprovalRoutingStarted
  + bool TeammatePaneCreated
```

## 统一中间件顺序（13 个）
```
1. DefinitionResolutionMiddleware      // A1: 解析定义（主代理 no-op）
2. PromptBuildingMiddleware            // A2: 构建提示词（主代理 no-op）
3. ContextSetupMiddleware              // A3: 组装 SubOptions（主代理 no-op；B 路径 SubOptions 已存在则跳过组装）
4. LifecycleSpawnMiddleware            // 合并 A3的Spawn调用 + B1: 实际创建 Agent（主代理 no-op）
5. WorktreeMiddleware                  // 合并 A4 + B2: 统一 Worktree 创建（降级策略）
6. RecordContextMiddleware             // B4: 记录 SpawnedAt + ExecutionContext
7. RegisterMessageMiddleware           // B3: 注册消息通道 + Teammate 钩子
8. HookSetupMiddleware                 // A5: 注册 Hooks
9. McpSetupMiddleware                  // A6: 初始化 MCP
10. PermissionRoutingMiddleware        // B5: 启动权限路由（主代理保留）
11. TeammatePaneMiddleware             // B6: 创建 Teammate Pane（主代理 no-op）
12. MetadataMiddleware                 // A7: 保存元数据到 Transcript
13. TranscriptMiddleware               // A8: 记录提示词到 Transcript（主代理 no-op）
```

## 主代理 no-op 清单
| 中间件 | 主代理处理 | 理由 |
|--------|-----------|------|
| DefinitionResolution | no-op | 主代理即 Coordinator，无需解析 |
| PromptBuilding | no-op | 主代理提示词由 CliSession/GoalEngine 设置 |
| ContextSetup | no-op | 主代理不组装 SubOptions |
| LifecycleSpawn | no-op | 主代理实例由调用方预创建放入上下文，不能递归 Spawn |
| Worktree | no-op | 主代理不隔离 |
| RecordContext | 保留 | 主代理可记录 ExecutionContext |
| RegisterMessage | 可选保留 | 主代理若参与消息通信需注册 |
| HookSetup | 保留 | 主代理可能需要全局 Hooks |
| McpSetup | no-op | 主代理 MCP 由 CliSession 启动时初始化 |
| PermissionRouting | 保留 | 主代理作为 Leader 需启动权限路由 |
| TeammatePane | no-op | 主代理不需要 Teammate Pane |
| Metadata | 保留 | 主代理可记录元数据 |
| Transcript | no-op | 主代理 Transcript 由 CliSession 管理 |

## 实施步骤

### 阶段一：合并子代理两条管道
| # | 步骤 | 验证 |
|---|------|------|
| 1.1 | 创建 `UnifiedSpawnContext`（聚合两上下文字段） | 编译 Core 层 |
| 1.2 | 合并 A3 Spawn 调用 + B1 → `LifecycleSpawnMiddleware` | 编译 |
| 1.3 | 合并 A4 + B2 → `WorktreeMiddleware`（降级策略） | 编译 |
| 1.4 | 其余中间件适配 `UnifiedSpawnContext`（加 IsMainAgent 分支，阶段一恒 false） | 编译 |
| 1.5 | `PipelineComposition` 注册统一管道，删除旧两条 | 编译 Composition 层 |
| 1.6 | 改 `AgentServiceImpl` / `AgentCoordinator` 调用方构造统一上下文 | 编译 |
| 1.7 | 更新 `TestPipelineRegistration` + 2 个直接 new 管道的测试 | 测试编译 |
| 1.8 | 全量测试回归 | 全绿 |
| 1.9 | 删除旧 `AgentSpawnContext`/`AgentSpawnCoordContext`（移到 .xxx/） | 编译 |

### 阶段二：主代理走统一管道
| # | 步骤 | 验证 |
|---|------|------|
| 2.1 | `CliSession.CreateMainAgent` 改为构造统一上下文（IsMainAgent=true，预创建 AgentBase 放入） | 编译 App 层 |
| 2.2 | `GoalEngine.RegisterMainAgent` 同上 | 编译 |
| 2.3 | 各中间件补 IsMainAgent no-op 分支 | 编译 |
| 2.4 | 全量测试回归 | 全绿 |

## 风险
1. **Spawn 递归**：主代理走管道时 LifecycleSpawn/ContextSetup 必须严格 no-op，`IsMainAgent` 是安全闸
2. **Options 类型不一致**：A 用 `AgentSpawnOptions`（工具层），B 用 `SubAgentOptions`（运行层），统一上下文双字段容纳
3. **`IAgentWorktreeService` vs `IAgentWorktreeManager`**：合并 Worktree 中间件前需确认两接口关系
4. **B 路径 Worktree 硬失败语义**：改为降级后，`AgentCoordinator.SpawnSubAgentAsync` 调用方若依赖 `[AGT011]` 需适配

## 调用方清单
| 调用方 | 文件:行号 | 改动 |
|--------|-----------|------|
| AgentServiceImpl | `core/ai/Agents/src/Services/Core/AgentServiceImpl.cs:84-101` | 构造 UnifiedSpawnContext（从 AgentSpawnOptions），调用统一管道 |
| AgentCoordinator | `core/ai/Agents/src/Coordinator/Core/AgentCoordinator.cs:75-98` | 构造 UnifiedSpawnContext（从 task+SubAgentOptions），调用统一管道 |
| CliSession | `app/JoinCode/Cli/Core/CliSession.cs:141-163` | 阶段二：构造统一上下文（IsMainAgent=true） |
| GoalEngine | `composition/Clock/src/Goal/Core/GoalEngine.cs:1073-1107` | 阶段二：同上 |
| PipelineComposition | `composition/Pipelines/src/PipelineComposition.cs:179-206` | 两 DI 注册合并为一个 |
| TestPipelineRegistration | `composition/Pipelines/src/TestPipelineRegistration.cs:128-142` | 同上 |
| AgentCoordinatorExtendedTests | `core/ai/Agents/tests/.../AgentCoordinatorExtendedTests.cs:21` | 更新管道构造 |
| ParallelExecutionEngineTests | `core/execution/Scheduling/tests/.../ParallelExecutionEngineTests.cs:27` | 更新管道构造 |

<!-- 🤖 Auto Decision: 2026-08-16 -->
<!-- 决策: 阶段一完成 — 子代理两条 Spawn 管道合并为 UnifiedSpawnContext 单管道 -->
<!-- 原因: 消除 AgentSpawnContext(9中间件) + AgentSpawnCoordContext(7中间件) 两套创建路径，统一为 13 中间件单管道 -->
<!-- 替代方案: 保留两条管道仅提取公共中间件（改动小但不彻底）-->
<!-- 验证: Debug 编译 0 错 0 警，全量测试退出码 0 全绿 ✅ -->
<!-- 关键合并点: LifecycleSpawnMiddleware(合并 A3 Spawn 调用 + B1), WorktreeSpawnMiddleware(合并 A4 + B2, 统一降级策略) -->
<!-- 旧文件处理: 21 个旧文件移到 .xxx/*.20260816.del, 两个 TelemetryHook 合并为 UnifiedSpawnTelemetryHook -->
