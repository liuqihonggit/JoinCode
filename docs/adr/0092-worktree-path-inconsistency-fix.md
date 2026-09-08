# 0092. Worktree 路径不一致修复 — 中间件幂等 + Guard 路径锁定

- 状态：accepted
- 日期：2026-09-08
- 决策者：AI 助手
- 关联 ADR：[0080](0080-manual-exe-testing-guide.md)、[0091](0091-actor-duplex-inplace-upgrade.md)

## 背景

手动测试 jcc.exe MCP 工具时发现 worktree 静默残留 bug：agent 创建的 worktree 在 agent 退出后不被清理，磁盘上积累孤立 worktree 目录和分支。

根因链：
1. `WorktreeGitRootMiddleware.FindGitRootAsync` 在 worktree 内（`D:\project\w1`）解析 `.git` 文件，返回主仓库根（`D:\project\JoinCode`）而非当前 worktree 根
2. 中间件执行顺序由源码生成器决定（非显式排序），`WorktreeCreateMiddleware` 可能在 `WorktreeGitRootMiddleware` 之前执行
3. `WorktreeCreateMiddleware` 用 `OriginalCwd` 兜底设置 `context.GitRoot = D:\project\w1`（正确），但后续 `WorktreeGitRootMiddleware` 覆盖为 `D:\project\JoinCode`（错误）
4. `WorktreeRecoveryMiddleware` 用被覆盖的 `context.GitRoot` 重新计算 `context.WorktreePath`，覆盖了 `WorktreeCreateMiddleware` 已设置的正确路径
5. session 存储错误路径，清理时 `git worktree remove` 找不到 worktree，exitCode=128

此外，per-agent worktree 创建绕过 `AgentWorktreeManager`：
- `WorktreeSpawnMiddleware.CreatePerAgentWorktreeAsync` 直接调 `_worktreeService.CreateAgentWorktreeAsync`，不注册 guard/session
- `AgentForkMiddleware.ExecuteTeammatePathAsync` 创建 `InProcessTeammateDefinition` 时丢弃 `isolation` 参数
- `InProcessTeammateTask` 直接调 `_worktreeService`，不走 manager
- `CleanupWorktreeAsync` 和 `DisposeWorktreeCleanupMiddleware` 检查全局 `_enableWorktreeIsolation` 开关，per-agent 隔离时返回 `NotIsolated` 跳过清理

## 决策

### 决策1：中间件幂等检查 — 已设置则跳过

**选择**：
```csharp
// WorktreeGitRootMiddleware
if (!string.IsNullOrEmpty(context.GitRoot))
{
    await next(context, ct).ConfigureAwait(false);
    return;
}

// WorktreeRecoveryMiddleware
if (string.IsNullOrEmpty(context.WorktreePath))
{
    context.WorktreePath = worktreePath;
}
```

**理由**：
- 消除中间件顺序依赖 — 无论谁先执行，已设置的值不被覆盖
- 幂等可重复执行，符合替换方法论
- 替代方案：显式排序中间件（需改源码生成器，侵入性大）

### 决策2：WorktreeLifecycleGuard — 路径锁定 + IAsyncDisposable

**选择**：新建 `WorktreeLifecycleGuard`，构造时锁定 worktree 路径为 readonly 字段，`DisposeAsync` 用同一路径执行 `git worktree remove`。

**理由**：
- 从根上消除"创建用当前目录、清理用主仓库"的路径不一致 — 没有第二次计算路径的机会
- `IAsyncDisposable` 对齐项目统一模式（分析器 JCC9102 禁止 IDisposable + IAsyncDisposable 双实现）
- 无终结器：worktree 删除需 git 命令（托管对象），终结器中不安全

### 决策3：WorktreeRemovalDiagnoser — 状态机诊断根因

**选择**：新建 `WorktreeRemovalDiagnoser`，删除失败时诊断根因（PathNotFound/ProcessOccupied/PermissionDenied/GitError），不盲目兜底。

**理由**：
- 对齐 AGENTS.md 规则8：显式状态枚举 + switch 表达式
- 把根因暴露给调用方决策（如提示用户关闭编辑器），而非 `.Wait(超时)` 恶意兜底
- 替代方案：重试机制（掩盖根因，不可取）

### 决策4：CreateWorktreeForAgentAsync — per-agent 不依赖全局开关

**选择**：`IAgentWorktreeManager` 新增 `CreateWorktreeForAgentAsync`，不检查 `_enableWorktreeIsolation`，显式请求时直接创建。

**理由**：
- per-agent 隔离（`isolation: "worktree"`）和全局隔离（`EnableWorktreeIsolation`）是不同概念
- per-agent 应该总是创建 worktree（用户显式请求），全局开关只影响自动行为
- 统一所有 worktree 创建走 `AgentWorktreeManager`，注册 guard + session

### 决策5：CleanupWorktreeAsync 按 session 存在性清理

**选择**：移除 `CleanupWorktreeAsync` 和 `DisposeWorktreeCleanupMiddleware` 中的 `_enableWorktreeIsolation` 检查，改为按 session 字典存在性判断。

**理由**：
- 全局开关关闭时 per-agent worktree 仍需清理
- session 字典查找 O(1)，无性能影响
- `NoSession` 返回值已覆盖"无需清理"语义

## 影响

- `WorktreeLifecycleGuard`：新建，路径锁定 + 异步释放 + 诊断器集成
- `WorktreeRemovalDiagnoser`：新建，状态机诊断删除失败根因
- `IAgentWorktreeManager`：新增 `CreateWorktreeForAgentAsync` 方法
- `AgentWorktreeManager`：guard 字典 + `DisposeAsync` 遍历释放 + `RemoveWorktreeViaGuardAsync` 统一删除入口
- `WorktreeSpawnMiddleware`：per-agent 路径走 `CreateWorktreeForAgentAsync`
- `ForkSpawnMiddleware`：per-agent 隔离走 `CreateWorktreeForAgentAsync`
- `AgentForkMiddleware`：传递 `IsolationMode` 到 `InProcessTeammateDefinition`
- `InProcessTeammateTask`：worktree 创建走 manager
- `WorktreeGitRootMiddleware`：幂等检查
- `WorktreeRecoveryMiddleware`：幂等检查
- `DisposeWorktreeCleanupMiddleware`：移除全局开关检查
- `AgentServiceImpl`：`FireAgentCompleted` 修复 ObjectDisposedException + `OnDispose` 补充 worktree 清理

## 验证

- `worktree_create` + 自动清理：路径 `D:\project\w1\.jcc\worktrees\...`，exitCode=0 ✅
- `agent` + `isolation:worktree`：worktree 在正确路径创建 ✅
- 558 Agents 单元测试通过 ✅
- 262 Scheduling 单元测试通过 ✅
