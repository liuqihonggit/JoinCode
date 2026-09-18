# Worktree 合并权限系统 — 设计疑问记录

> **状态**: 待决策(未实现)
> **创建时间**: 2026-09-19
> **背景**: 子代理通过 git worktree 隔离工作完成后,工作成果合并回主仓库的权限控制缺口分析

---

## 1. 当前实现现状

### 1.1 Worktree 隔离与合并流程

```
子代理启动
  → git worktree add 创建隔离环境(分支: worktree-{agentId})
  → 子代理在 worktree 中独立工作
  → 子代理停止
      ├─ 无变更 → 自动删除 worktree + 分支
      ├─ 有变更 → 保留 worktree(不自动合并) ⚠️
      └─ 自动 rebase(反向同步: 主干→worktree,非合并回主干)
  → 合并需 LLM 显式调用 worktree_merge 工具(手动触发)
```

### 1.2 核心代码位置

| 组件 | 位置 | 职责 |
|------|------|------|
| Worktree 管理 | `llm/agents/Coordinator/Core/Lifecycle/AgentWorktreeManager.cs` | 创建/清理 worktree |
| 合并服务 | `llm/agents/Services/Support/WorktreeMergeService.cs:31` | `MergeToTargetAsync` 双策略合并 |
| 合并工具入口 | `kit/mcp/workflow/WorktreeToolHandlers.cs:319` | `worktree_merge` MCP 工具 |
| 自动 Rebase | `lib/infrastructure/hot_spot/AutoRebaseService.cs:31` | 反向同步(主干→worktree) |
| 清理决策 | `llm/agents/Coordinator/Core/Lifecycle/AgentWorktreeManager.cs:224-255` | 有变更保留,无变更删除 |
| 人工接手 | `app/gui/view_models/MainViewModel.SessionTree.cs:14` | 空闲超时主代理接手 |

### 1.3 合并策略(现有)

`WorktreeMergeService.MergeToTargetAsync` 双路径:
- **无冲突** → `git apply patch`(行 49)
- **有冲突** → `git merge` + 策略(行 55):
  - `Fail` → `git merge --abort`
  - `Ours` → `git checkout --ours`
  - `Theirs` → `git checkout --theirs`
  - `AutoMerge` → 提交自动合并部分

---

## 2. 现有权限机制(可复用评估)

| 机制 | 位置 | 覆盖范围 | 可复用于合并权限? |
|------|------|---------|-----------------|
| 工具权限控制 | `lib/guard/permission/utils/AgentToolRestrictions.cs:62` | `PermissionMode`(Auto/Plan/Ask/Bypass)控制工具可用性 | ✅ 可控制 `worktree_merge` 工具本身 |
| 路径权限检查 | `lib/guard/permission/permission2/core/PathPermissionChecker.cs:141` | 文件读写路径白名单 | ✅ 可扩展为合并前文件白名单检查 |
| Git 安全拦截 | `GitSecurityInterceptor` | 拦截 `git_commit`/`git_add` 敏感文件 | ✅ 可在合并前调用 |
| 危险命令分类 | `lib/guard/security/danger_classification/CommandDangerClassifier.cs:307` | `git push` 标记不可撤回 | ⚠️ 合并非 push,需新增分类 |
| 纵深防御架构 | `lib/guard/` 按层按名称组织 | 前缀树→结构化解析→专项预处理→五色灯→确认→执行→审计 | ✅ 合并权限可作为新拦截层插入 |

---

## 3. 权限缺口分析

当前 `worktree_merge` 工具仅受通用 `PermissionMode` 控制,存在以下缺口:

| 缺口 | 风险 | 严重度 |
|------|------|--------|
| **无文件白名单** | 子代理可改任意文件,合并时无越权检查 | 🔴 高 |
| **无合并前代码审查** | 未审查代码直接进入主干 | 🔴 高 |
| **无合并前安全审查** | 安全漏洞(secrets/敏感信息)可能进入主干 | 🔴 高 |
| **无文件级变更范围限制** | 子代理可改超出任务范围的文件 | 🟡 中 |
| **无人工确认环节** | 高风险变更无 human-in-the-loop | 🟡 中 |
| **无合并审计日志** | 谁合并了什么、何时合并无追踪 | 🟡 中 |
| **无冲突解决审查** | `Ours`/`Theirs` 策略可能静默覆盖重要代码 | 🟡 中 |

---

## 4. 待决策疑问

### 疑问 1: 控制粒度选择

**选项 A: 文件白名单权限系统**
- 子代理只能改白名单内的文件
- 合并前检查是否有越权改动,有则拒绝合并
- 适合:需要严格控制子代理修改范围的场景
- 实现成本:中(复用 `PathPermissionChecker` 扩展)
- 缺点:白名单维护成本高,可能阻碍合法工作

**选项 B: 合并前审查门禁**
- 合并前强制走代码审查 + 安全审查(复用现有 `security_audit`)
- 审查通过才允许合并
- 适合:需要保证代码质量的场景
- 实现成本:低(复用现有审查管道)
- 缺点:不控制文件范围,只审查内容

**选项 C: 分层权限(文件 + 审查 + 人工确认)**
- 三层拦截:文件白名单 → 自动安全审查 → 人工确认(高风险变更)
- 最严格但实现复杂度最高
- 适合:高安全要求场景
- 实现成本:高
- 缺点:可能过度工程化,降低效率

**选项 D: 不构造,保持现状**
- 当前手动合并 + 通用权限控制已够用
- LLM/用户自行判断
- 适合:信任子代理输出质量的场景
- 实现成本:零
- 缺点:无防护,依赖人工把关

**选项 E: 先看现有安全拦截架构再决定**
- 先深入了解现有 `GitSecurityInterceptor`/`PathPermissionChecker`/纵深防御架构
- 评估能否复用扩展再决策
- 适合:谨慎决策场景
- 实现成本:零(仅调研)
- 缺点:延迟决策

### 疑问 2: 拦截层位置

若构造权限系统,应插入到合并流程的哪个位置?

- **位置 A**: `worktree_merge` 工具入口前(MCP 工具权限中间件)
- **位置 B**: `WorktreeMergeService.MergeToTargetAsync` 方法内(合并前检查)
- **位置 C**: 新增中间件层(遵循纵深防御架构,声明层名和优先级)

### 疑问 3: 冲突解决策略权限

当前 `Ours`/`Theirs`/`AutoMerge`/`Fail` 策略可由调用方任意选择:
- 是否需要限制子代理只能用 `Fail`(冲突必须人工处理)?
- 是否需要限制 `Theirs`(子代理覆盖主干)的策略使用?

### 疑问 4: 审计日志

- 合并操作是否需要审计日志(谁、何时、合并了什么)?
- 审计日志存放位置(独立审计文件?git commit message?数据库?)

### 疑问 5: 与现有 ADR 的关系

- 是否需要新建 ADR 记录此架构决策?
- 与现有 ADR 的关联:
  - [ADR 0020](../adr/0020-encapsulation-requirements.md) 封装要求
  - [ADR 0084](../adr/0084-platform-windows-env-rules.md) 平台专属操作禁令
  - 纵深防御按层按名称组织(AGENTS.md 第6条)

---

## 5. 建议的下一步

1. **先调研现有纵深防御架构** — 评估 `lib/guard/` 下各拦截层的组织方式,确定新权限层应插入的位置
2. **明确控制目标** — 是控制"能改哪些文件"还是"合并前必须审查"还是两者都要
3. **写 ADR** — 若决定构造,先在 `docs/adr/` 写 ADR(状态:proposed)再实现
4. **渐进式实现** — 从最关键的缺口(合并前安全审查)开始,逐步补充

---

## 6. 相关代码引用

- 合并服务实现: `llm/agents/Services/Support/WorktreeMergeService.cs:31`
- 合并工具入口: `kit/mcp/workflow/WorktreeToolHandlers.cs:319`
- Worktree 清理决策: `llm/agents/Coordinator/Core/Lifecycle/AgentWorktreeManager.cs:224-255`
- 路径权限检查: `lib/guard/permission/permission2/core/PathPermissionChecker.cs:141`
- 工具权限限制: `lib/guard/permission/utils/AgentToolRestrictions.cs:62`
- 危险命令分类: `lib/guard/security/danger_classification/CommandDangerClassifier.cs:307`
- 人工接手流程: `app/gui/view_models/MainViewModel.SessionTree.cs:14`

---

---

## 7. ClaudeCode ts 对比与缺陷风险分析(2026-09-19 补充)

### 7.1 ClaudeCode ts 的设计(对比基准)

| 机制 | ClaudeCode ts | 本项目 C# |
|------|--------------|----------|
| 子代理 worktree 创建 | 需显式 `isolation: "worktree"` | 需显式 `IsolationMode.Worktree` |
| 子代理 cwd 隔离 | `runWithCwdOverride`(AsyncLocalStorage)✅ | `SubAgentContext.CwdOverride`(AsyncLocal)**但是死代码** ❌ |
| 合并回主仓库 | **无自动合并**,手动 git merge | **无自动合并**,手动 `worktree_merge` 工具 |
| Agent Teams 隔离 | **不隔离**,teammate 共享 leader cwd | **不隔离**,默认 `IsolationMode.None` |
| ExitWorktree 选项 | keep/remove(无 merge) | keep/remove(无 merge) |

**结论**: 两者设计哲学一致(保留而非自动合并),但**本项目 C# 的 cwd 隔离实现有缺陷**。

### 7.2 三个缺陷在本项目的风险

#### 🔴 缺陷 1: 跨 worktree 的 cwd 污染 — **存在风险**

**根因**: `SubAgentContext.GetEffectiveCwd()` 是**死代码**(无调用方),shell 链路用进程级 `Directory.GetCurrentDirectory()`:

- `kit/hands/shell/handlers/ShellToolHandlers.cs:65` → `_fs.GetCurrentDirectory()`(进程级)
- `kit/hands/system_actuator/abstractions/CwdTracker.cs:33` → `_fs.SetCurrentDirectory(newCwd)` → `Directory.SetCurrentDirectory()`(进程级全局)
- `lib/infrastructure/io/file_system/PhysicalFileSystem.cs:281` → `Directory.SetCurrentDirectory(path)`

**触发链**:
1. 子代理 A 执行 `cd /worktreeA`(或任何改变目录的命令)
2. `CwdTracker.TryUpdateCwdFromTrackingFile()` 读取 cwd 文件 → `Directory.SetCurrentDirectory(newCwd)` 修改**进程级全局 cwd**
3. 子代理 B(并行或后续)执行 shell 命令时**未传 `working_directory` 参数**
4. `ShellToolHandlers` 使用 `_fs.GetCurrentDirectory()` = 被子代理 A 污染的 cwd
5. 命令在错误的目录(子代理 A 的 worktree)中执行

**对比 ClaudeCode ts**: ts 用 `runWithCwdOverride`(AsyncLocalStorage)正确隔离,**不污染父代理**(`AgentTool.tsx:641`)。本项目有 AsyncLocal 机制但**未接入 shell 链路**。

**证据代码**:
- `lib/abstractions/abs_agents/agent/SubAgentContext.cs:56-65` — `GetEffectiveCwd()` 定义存在但无调用方
- `llm/agents/Coordinator/Fork/AgentBase.cs:230` — `EnterScopeWithCwd(Options.WorktreePath)` 设置了 CwdOverride 但无人读取
- `kit/hands/system_actuator/abstractions/SystemActuatorBase.cs:251-255` — `ResolveWorkingDirectory` 用 `_fs.GetCurrentDirectory()` 而非 `GetEffectiveCwd()`

#### 🟡 缺陷 2: 顺序执行的 stale write — **部分风险**

**根因**: 顺序执行 + 无 worktree 隔离 + 并行 agent 修改主检出的组合场景。无文件读写冲突检测机制。

**证据**:
- `llm/agents/Coordinator/Core/services/AgentExecutionEngine.cs:67-95` — 顺序执行,前一个 Output 作为上下文注入下一个
- `llm/agents/Services/Spawn/Unified/WorktreeSpawnMiddleware.cs:39-57` — `IsolationMode != Worktree` 时 fallback 到主工作目录
- 无文件锁或读写冲突检测机制(仅有 `IFileWriteListener` 写入通知,非冲突检测)

**触发条件**(理论):
1. 顺序执行的子代理**未设置 `IsolationMode.Worktree`**(默认 `None`)
2. 同时有并行执行的 agent 也在修改主检出文件
3. 顺序 agent A 读文件 → 并行 agent B 改文件 → 顺序 agent A 写回旧内容

**风险较低**: 顺序执行通常用于依赖关系,并行执行通常设置 worktree 隔离,但两者都不设置隔离且共享主检出时理论上存在风险。

#### 🔴 缺陷 3: Agent Teams 不做 worktree 隔离 — **存在风险**

**根因**:
- `lib/scheduling/tasks/core/InProcessTeammateTask.cs:145` → `IsolationMode = AgentIsolationMode.None`(默认)
- `lib/infrastructure/hot_spot/WorktreeDecisionPolicy.cs:58-67` — `Decide()` 实现了正确逻辑(Teammate 应开 worktree),但**未被集成到生产 spawn 链路**(仅测试调用)
- `llm/agents/Coordinator/Team/core/TeammateStatusBuilder.cs:102` → `WorktreePath = null`(硬编码)
- `kit/hands/tool_handlers/system_tools/agent/AgentForkMiddleware.cs:67-76` — 从 `context.Isolation` 解析,用户未传则默认 None

**触发条件**:
1. 用户使用 Agent Teams 功能 spawn teammate(通过 `agent` 工具,`subagent_type` 为空)
2. **未显式传 `isolation=worktree` 参数**
3. 多个 teammate 同时修改主检出的同一文件
4. 由于无 worktree 隔离,teammate 之间共享工作目录,存在文件读写冲突

**ADR 0092** 曾修复 `AgentForkMiddleware` 丢弃 `isolation` 参数的 bug,但**默认值仍为 `AgentIsolationMode.None`**。

### 7.3 核心架构问题总结

本项目存在**两套 cwd 机制**(AsyncLocal `CwdOverride` vs 进程级 `Directory.GetCurrentDirectory`),但 shell 执行链路只读进程级,AsyncLocal 隔离**形同虚设**。

`WorktreeDecisionPolicy` 虽然实现了正确的决策逻辑(Teammate 应开 worktree),但**未被集成到 spawn 链路**,导致 Teammate 默认不隔离。

### 7.4 修复方向(待决策)

| 缺陷 | 修复方案 | 影响范围 |
|------|---------|---------|
| **cwd 污染** | 让 shell 链路读 `SubAgentContext.GetEffectiveCwd()` 而非 `_fs.GetCurrentDirectory()` | `ShellToolHandlers` / `SystemActuatorBase` / `CwdTracker` |
| **stale write** | 顺序执行强制 worktree 隔离;或加文件读写冲突检测 | `AgentExecutionEngine` / `WorktreeSpawnMiddleware` |
| **Teammate 不隔离** | 把 `WorktreeDecisionPolicy.Decide()` 集成到 spawn 链路;或改默认值为 `Worktree` | `InProcessTeammateTask` / `AgentForkMiddleware` |

---

<!-- 🤖 Auto Decision: 2026-09-19 -->
<!-- 决策: 将 worktree 合并权限系统疑问记录为设计文档,未实现任何代码 -->
<!-- 原因: 用户要求先记录疑问再决策,避免过早实现 -->
<!-- 替代方案: 直接构造权限系统(被否决,需先明确控制粒度) -->
<!-- 验证: 文档已创建,未修改任何生产代码 ✅ -->

<!-- 🤖 Auto Decision: 2026-09-19 -->
<!-- 决策: 补充 ClaudeCode ts 对比与三个缺陷风险分析到疑问文档 -->
<!-- 原因: 用户指出 ClaudeCode ts 存在三个已知缺陷,需评估本项目是否同样存在 -->
<!-- 发现: 本项目存在 cwd 污染(AsyncLocal 死代码)和 Teammate 不隔离(WorktreeDecisionPolicy 未集成)两个实际风险 -->
<!-- 验证: 文档已更新,未修改任何生产代码 ✅ -->
