# 0149. 工具全量验证两层架构

> 📍 **导航**: [docs/](../../README.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adr/README.md](../README.md) — 修改本文档后须同步更新此索引

- 状态：proposed
- 日期：2026-10-25
- 决策者：项目架构组

## 背景

### 用户诉求

用户对现有工具验证提出 6 点诉求：

1. **不信任单元测试** — 单元测试无真实链路，必须启动 exe 验证
2. **所有 MCP 和内部工具都要验证** — 现有方案跳过 `gh_`/`git_`/`desktop` 等副作用工具，违背全覆盖诉求
3. **去 CI 上验证** — 非本地测试，CI 环境才可信
4. **卡死机制已有** — 5 层防御体系已完善（AsyncLock + LockRegistry / Actor AskAwait / ShellProcessWatchdog / ADR 0106 子代理四层 / TimeoutHelper）
5. **日志不全** — 中间件链路流转不可追踪
6. **如何设计** — 反射所有 MCP？每参数默认值？缺失参数？超时限制？

### 现状评估

项目已有 3 层工具验证：

| 层 | 位置 | 模式 | 缺口 |
|----|------|------|------|
| DI 批量调用 | `test/integration/integration.tests/mcp_tools/McpToolSmokeTests.cs` | DI 容器内遍历调用，`BuildMinimalArguments` 单组参数 | 仅最小参数，无缺失参数场景；副作用工具跳过 |
| 反射枚举 | `test/integration/integration.tests/mcp_tools/McpToolRegistrationValidationTests.cs` | 反射 `[McpToolDispatch]` 验证注册 | AOT 下反射被裁剪；仅验证注册，不验证调用 |
| 进程级巡检 | `non_deliverables_tools/scripts/Invoke-AllCommandTests.ps1` + `e2e_tool_patrol.py` | `mcp_list` 枚举 → `mcp_schema` 获取参数 → `mcp_call` 真实调用 | 参数填充策略/断言/报告不够精细 |

CI 现有 `daily-command-test.yml` 每 2 天 7 点巡检，3 分片并行。

### 日志薄弱环节

| 环节 | 现状 | 影响 |
|------|------|------|
| CrashSnapshotMiddleware | 无 ILogger | 崩溃快照只写 ICrashSnapshotStore，无日志输出 |
| 中间件入/出口 | 无 Trace 级日志 | 无法追踪请求在 13 层管道中的流转 |
| 工具参数/结果内容 | 不记录 | 调试时难以定位参数问题（安全考虑） |
| 中间件链路顺序 | 无日志 | 实际执行顺序、跳过的中间件不可见 |

## 决策

### 决策 1：两层结合架构（DI 快速反馈 + exe 真实链路）

| 层 | 职责 | 枚举方式 | 执行方式 | 反馈速度 |
|----|------|---------|---------|---------|
| **DI 层** | 逻辑正确性（参数验证/缺失参数/默认值） | `IMcpToolRegistry.GetAllToolsAsync`（AOT 友好） | DI 容器内直接调用 | 秒级 |
| **exe 层** | 真实链路验证 | `mcp_list --json` | `jcc mcp_call --debuglog` | 分钟级 |

**理由**：DI 层验证逻辑正确性（参数校验、缺失参数错误处理），exe 层验证真实链路（DI 卡死、JSON 宽容、进程启动）。两层互补，符合"不信任单元测试但保留快速反馈"诉求。

**枚举方式选择**：`IMcpToolRegistry.GetAllToolsAsync` 而非反射，因为 NativeAOT 强制约束（`TrimMode=full`）下反射被裁剪。运行时枚举能发现注册失败的工具，且 AOT 友好。

### 决策 2：参数生成策略（默认值 + 逐个缺失）

每个工具生成 2 组参数：

1. **默认值参数**：Required 用 schema 默认值，Optional 用 null → 验证正常路径
2. **逐个缺失**：对每个 Required 参数移除后调用 → 验证返回明确错误（非静默失败）

**用例数** ≈ 工具数 × (1 + Required 参数数) ≈ 2000-3000 用例。

**缺失参数断言**：必须返回结构化错误（`isError: true` + 明确错误消息），禁止静默失败（空输出或 `isError: false` 但无结果）。

### 决策 3：副作用工具分类策略

| 分类 | 工具示例 | 策略 | 理由 |
|------|---------|------|------|
| **读类** | `gh_pr_view`/`read`/`grep`/`glob` | 真实执行 | 无副作用，可安全调用 |
| **写类** | `gh_pr_comment`/`git_commit`/`write`/`edit` | MockServer 模拟 API | 避免真实副作用，验证调用链路 |
| **危险类** | `delete`/`desktop_click`/`desktop_type` | 仅验证参数校验 + 权限拒绝 | 真实执行有破坏性，验证拒绝逻辑即可 |

**分类实现**：插件式策略接口 `IToolValidationStrategy`，每工具一个策略，按 `ToolCategory` + 工具名前缀路由。符合"万物皆插件"原则，好扩展。

### 决策 4：按分类超时

| 分类 | 超时 | 分类 | 超时 |
|------|------|------|------|
| `file`/`read` | 5s | `github`(`gh_`) | 30s |
| `search` | 10s | `code_index` | 60s |
| `desktop` | 10s | 其他 | 15s |

**实现**：`ToolTimeoutPolicy` 字典配置，复用现有 `CancellationTokenSource(TimeSpan)` + `TimeoutHelper.WithTimeoutAsync`。超时记入巡检报告，P99 耗时超阈值的工具标注。

### 决策 5：增强中间件链路日志（Trace 级）

在 13 层中间件入/出口加 `LogTrace`：

```csharp
public async Task<ToolResult> InvokeAsync(ToolExecutionContext context, Func<Task<ToolResult>> next) {
    _logger.LogTrace("[MW:{Order}] {MiddlewareName} ENTER tool={ToolName}", Order, Name, context.ToolName);
    try {
        var result = await next().ConfigureAwait(false);
        _logger.LogTrace("[MW:{Order}] {MiddlewareName} EXIT tool={ToolName} ok=true", Order, Name, context.ToolName);
        return result;
    } catch (Exception ex) {
        _logger.LogTrace("[MW:{Order}] {MiddlewareName} EXIT tool={ToolName} ok=false err={Err}", Order, Name, context.ToolName, ex.Message);
        throw;
    }
}
```

**零性能影响**：生产默认 `Warning` 级别，Trace 不输出。测试时用 `Debug` 级别可见，追踪管道流转，定位卡在哪层。

**补全 CrashSnapshotMiddleware 的 ILogger**：通过 DI 注入 `ILogger<CrashSnapshotMiddleware>`。

### 决策 6：CI 每日定时全量验证

`daily-command-test.yml` 从每 2 天改为每天触发：

```yaml
on:
  schedule:
    - cron: '0 7 * * *'    # 每天7点运行（原 '0 7 */2 * *'）
  workflow_dispatch:
```

**全量验证**：所有 400+ 工具，含写类 MockServer，3 分片并行，30 分钟内完成。

## 替代方案

### 决策 1 替代：仅增强 exe 巡检

放弃 DI 测试，只扩展 `Invoke-AllCommandTests.ps1`。

**放弃原因**：最真实但 CI 反馈慢（每次启动 exe 开销大），且无法快速定位参数逻辑错误。DI 层秒级反馈对开发效率至关重要。

### 决策 1 替代：新建统一 E2E 项目

新建 `ToolValidation.E2E.Tests` 项目，整合 3 层为系统性测试。

**放弃原因**：插件式架构面向未来好扩展，但开发量大，且现有 3 层已基本覆盖，增强比重建性价比更高。

### 决策 2 替代：3 组参数全覆盖

默认值 + 逐个缺失 + 全参数填充。

**放弃原因**：覆盖最全但用例数 ≈ 4000-6000，CI 耗时可能超 10 分钟。默认值 + 缺失已覆盖正常路径 + 错误处理，全参数填充边际价值低。

### 决策 3 替代：全部真实执行 + 隔离环境

在临时隔离环境执行所有工具。

**放弃原因**：最真实但复杂，CI 环境可能无 GitHub 凭证，且 desktop 类工具在 CI 无图形界面。

### 决策 5 替代：仅增强测试层日志

不改生产代码，在测试层捕获所有输出。

**放弃原因**：安全但无法追踪中间件内部流转，无法定位卡在哪层。Trace 级日志零性能影响，增强生产代码是值得的。

## 影响面

### 涉及文件

| 模块 | 文件 | 改动 |
|------|------|------|
| 测试 | `test/integration/integration.tests/mcp_tools/McpToolSmokeTests.cs` | 参数化增强：默认值 + 逐个缺失 |
| 测试 | `test/integration/integration.tests/mcp_tools/ToolValidationStrategy.cs`（新增） | 插件式策略接口 + 读/写/危险分类实现 |
| 测试 | `test/integration/integration.tests/mcp_tools/ToolTimeoutPolicy.cs`（新增） | 按分类超时配置 |
| 测试 | `test/integration/integration.tests/mcp_tools/ArgumentGenerator.cs`（新增） | 默认值参数 + 缺失参数生成 |
| 巡检 | `non_deliverables_tools/scripts/Invoke-AllCommandTests.ps1` | 增强参数策略 + 报告 |
| 生产 | `modules/mcp/mcp_tool_dispatch/core/middleware/*.cs`（13 个） | 入/出口加 `LogTrace` |
| 生产 | `modules/mcp/mcp_tool_dispatch/core/middleware/CrashSnapshotMiddleware.cs` | 补全 ILogger |
| CI | `.github/workflows/daily-command-test.yml` | 每 2 天改为每天 |

### 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `IToolValidationStrategy` 插件式策略（读/写/危险分类） | `McpToolSmokeTests` 遍历调用 | ❌ | ❌ |
| `ToolTimeoutPolicy` 按分类超时配置 | `McpToolSmokeTests` + `Invoke-AllCommandTests.ps1` | ❌ | ❌ |
| `ArgumentGenerator` 默认值 + 缺失参数生成 | `McpToolSmokeTests` 参数化用例 | ❌ | ❌ |
| 中间件入/出口 `LogTrace`（13 层） | 测试时 Debug 级别追踪管道流转 | ❌ | ❌ |
| `CrashSnapshotMiddleware` ILogger 补全 | 崩溃快照日志可见 | ❌ | ❌ |
| `daily-command-test.yml` 每天触发 | CI 每日全量验证 | ❌ | ❌ |

> `已验收`列需走 [ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) 手动 exe 验收方式（真实运行非 mock）。

## 实现步骤

1. **增强 DI 层测试**：`McpToolSmokeTests` 参数化 + `ArgumentGenerator` + `ToolTimeoutPolicy` + `IToolValidationStrategy`
2. **增强 exe 层巡检**：`Invoke-AllCommandTests.ps1` 参数策略 + 报告增强
3. **增强中间件日志**：13 层入/出口 `LogTrace` + `CrashSnapshotMiddleware` ILogger
4. **更新 CI**：`daily-command-test.yml` 每天触发
5. **验证**：DI 测试通过 + exe 巡检通过 + CI 每日运行
6. **ADR 改 accepted**：验证通过后状态改为 accepted

## 关联

- [ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) — 手动 exe 测试指南
- [ADR 0083](../../implemented/adr/engineering_ci_test/0083-e2e-script-mode-spec.md) — E2E 测试脚本模式规范
- [ADR 0106](../../implemented/adr/data_core_selection/0106-subagent-stall-defense-in-depth.md) — 子代理卡死四层防御
- [ADR 0137](../../implemented/adr/architecture/0137-colocation-module-isolation.md) — 16 功能域就近隔离
