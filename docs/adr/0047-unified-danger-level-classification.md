# ADR 0047: 统一危险指令分级系统

**状态**: accepted

**日期**: 2026-08-30

## 背景

原权限系统的危险指令检测分散在多处：
- `DestructiveCommandDetector` — 静态映射表（83行命令定义）
- `PermissionConfig.DangerousCommandPatterns` — 配置中的危险命令模式
- `BashSecurityValidator` / `BashAstSecurityWalker` — Bash AST 检查
- `PsSecurityChecker` / `PsDangerousCmdlets` — PowerShell 检查
- `CodeSecurityValidator` — 代码安全
- `Core.Utils.DestructiveCommandAnalyzer` — Infrastructure 层正则分析器

存在两个独立的风险枚举无关联：
- `CommandRisk`（10个风险类型：FileDeletion/SystemModification/PathEscape 等）
- `CommandRiskLevel`（3个等级：Read/Write/Dangerous）

**核心问题**：没有"绝对禁止 AI 执行"的等级，所有危险命令统一处理，`rm -rf /` 和 `rm file.txt` 得到相同的对待。

## 决策

引入统一的 `CommandDangerLevel` **4 级分级**，作为权限决策的**唯一依据**。

### 四级分级总览

| 等级 | 数值 | 含义 | 颜色 | 可撤回 | AI 行为 | 同级别自动通过 |
|------|------|------|------|--------|---------|---------------|
| `Safe` | 0 | 只读操作 | — | — | 自动通过 | — |
| `LightValidation` | 1 | 轻校验/可撤回操作 | 🟢 绿色 ask | ✅ 可撤回 | 需用户确认 | ✅ 支持（会话级，不持久化） |
| `Execution` | 2 | 执行/不可撤回操作 | 🔴 红色 ask | ❌ 不可C | 需用户确认 | ✅ 支持（会话级，不持久化） |
| `Dangerous` | 3 | 危险操作 | — | ❌ 不可撤回 | **直接拒绝不提示** | ❌ 不支持 |

**核心区分**：绿色 ask = 可撤回操作（git commit 可 reset 撤回），红色 ask = 不可撤回操作（rm 删除不可恢复）。

**同级别自动通过**：用户选择后当前会话内同级别操作不再 ask，**不持久化**，每次打开新 exe 重新提示。用户可在 GUI 上点击标记按等级跳过。

### 各权限模式下的行为矩阵（CommandDangerLevel × PermissionMode）

| CommandDangerLevel ＼ PermissionMode | Plan | Auto | Ask | Bypass |
|--------------------------------------|------|------|-----|--------|
| `Safe` | 放行 | 放行 | 放行 | 放行 |
| `LightValidation`（🟢绿色ask/可撤回） | 放行（类似只读） | ❌ 拒绝+引导 | ⏸ 🟢绿色待确认 | 放行 |
| `Execution`（🔴红色ask/不可撤回） | ❌ 拒绝 | ❌ 拒绝+引导 | ⏸ 🔴红色待确认 | 放行 |
| `Dangerous` | ❌ 拒绝（不提示） | ❌ 拒绝（不提示） | ❌ 拒绝（不提示） | ❌ **拒绝（不提示）** |

> **关键设计**：`Dangerous` 级在**所有模式下都直接拒绝不提示**，包括 Bypass，确保 `rm -rf /`、`format c:` 等操作无论如何都不执行。

### 启动参数联动

权限模式通过以下入口设置，最终统一写入 `JCC_PERMISSION_MODE` 环境变量，由 `PermissionChecker.TryGetPermissionModeFromEnv` 读取：

| 入口 | 示例 | 优先级 | 说明 |
|------|------|--------|------|
| CLI 参数 `--permission-mode` | `jcc --permission-mode ask` | 最高 | 直接设置 `JCC_PERMISSION_MODE` |
| CLI 参数 `--bypass` / `--dangerously-skip-permissions` | `jcc --bypass` | 次之 | 等价于 `--permission-mode bypass` |
| 环境变量 `JCC_PERMISSION_MODE` | `JCC_PERMISSION_MODE=plan jcc` | 次之 | 直接读取 |
| `settings.json` `permissions.defaultMode` | `"defaultMode": "auto"` | 最低 | 配置文件默认值 |
| 硬编码默认值 | — | 兜底 | `PermissionMode.Auto` |

**安全闸**：`settings.json` 的 `permissions.disableBypassPermissionsMode = "true"` 时，即使设置了 `JCC_PERMISSION_MODE=bypass`，也会被忽略并回退到 `Auto`，防止不安全的 bypass 模式。

**数据流**：
```
CLI 参数 (--permission-mode / --bypass)
    ↓ ApplicationBuilder.ParseArgs → Environment.SetEnvironmentVariable("JCC_PERMISSION_MODE", ...)
环境变量 JCC_PERMISSION_MODE
    ↓ PermissionChecker.TryGetPermissionModeFromEnv(fs)
    ↓ 安全闸检查: disableBypassPermissionsMode → 忽略 bypass
PermissionMode 枚举值 (Plan/Auto/Ask/Bypass)
    ↓ PermissionChecker.CurrentMode
    ↓ DangerousCommandProtectionMiddleware.InvokeAsync(context)
    ↓ context.CurrentMode × CommandDangerLevel → 决策
ToolPermissionCheckResult (Allowed/Denied/PendingConfirmation)
```

### ask 确认交互联动（IPermissionConfirmationHandler）

当 `DangerousCommandProtectionMiddleware` 返回 `PendingConfirmation` 时（Dangerous/Critical 级在 Ask 模式下），由 `PermissionAwareToolExecutor.HandlePendingConfirmationAsync` 调用 `IPermissionConfirmationHandler.Confirm` 发起用户确认交互：

| 实现层 | 类 | 交互方式 | Allow 有效期 | AlwaysAllow 有效期 |
|--------|-----|---------|-------------|-------------------|
| CLI | `CliPermissionConfirmationHandler` | `^` 提示符: (y)允许 / (a)始终允许 / (n)拒绝 | 1 分钟 | 30 分钟 |
| TUI | `PermissionDialogView` | 三档按钮: 允许一次 / 始终允许 / 拒绝 | 5 分钟 | 24 小时 |
| GUI | `PermissionDialog.axaml.cs` | 对话框 | — | — |
| 非交互环境 | — | 自动拒绝 | — | — |

**确认动作**（`PermissionConfirmAction` 枚举）：
- `Deny` → 拒绝执行，返回错误结果
- `Allow` → 本次允许，`PermissionManager.ApproveToolTemporarily(toolName, 1~5分钟)` 加入临时批准列表
- `AlwaysAllow` → 始终允许，`PermissionManager.ApproveToolTemporarily(toolName, 30分钟~24小时)` 加入临时批准列表

**⚠️ 已知缺陷**：当前 `Critical` 级在 Ask 模式下返回 `PendingConfirmation`，但用户选择 `AlwaysAllow` 后仍会调用 `ApproveToolTemporarily` 临时批准，违反"不可批量批准"设计。需在后续任务中修复（见后续工作）。

### 统一实现位置

`core/safety/Guard/src/Security/DangerClassification/` — 统一危险命令目录：
- `DangerousCommandCatalog` — 集中所有命令定义（命令/参数/组合/路径），每条记录同时标注 `CommandRisk`（风险类型）和 `CommandDangerLevel`（危险等级）
- `CommandDangerClassifier` — 统一分类器实现 `ICommandDangerClassifier`

### CommandRisk 保留为消息构建依据

`CommandRisk` 不删除，降级为消息构建的辅助信息（不再用于决策）：
- `CommandDangerLevel` — 权限决策的唯一依据
- `CommandRisk` — 消息构建的辅助信息（"文件删除" vs "系统修改"）

### Forbidden 级处理

Forbidden 级命令在任何权限模式下都被拒绝，返回引导消息：
```
⛔ 绝对禁止 — AI 无法执行此操作（格式化系统盘 — 绝对禁止）。
此操作可能造成不可恢复的数据丢失或系统损坏，AI 在任何权限模式下都不会执行。
如确需执行，请你在终端手动执行以下命令:
  format c:
⚠️ 请务必确认命令正确后再执行，此操作不可逆。
```

## 替代方案

1. **5级分级（含 Caution）** — 被否决，4级已足够区分
2. **用 DangerLevel 替代 CommandRisk（完全删除）** — 被否决，保留 CommandRisk 用于消息构建
3. **扩展现有 DestructiveCommandDetector** — 被否决，分散问题未根本解决

## 后续工作

### 必须实现（核心联动）

- [ ] **同级别自动通过标记机制** — 会话级非持久化，用户选择后同级别操作不再 ask。需修改 `PermissionManager` 新增 `ConcurrentDictionary<CommandDangerLevel, bool>` 按级别批量批准，替代按工具名临时批准。每次打开新 exe 重新提示。
- [ ] **ask 颜色区分** — CLI: 🟢绿色/🔴红色 `^` 提示符 + `[轻校验]`/`[执行]` 级别标签。TUI: 绿色/红色对话框边框。GUI: 绿色/红色标题栏。
- [ ] **GUI 按等级跳过标记** — GUI 界面上提供按等级跳过的复选框/按钮，用户点击后当前会话内该级别不再 ask。
- [ ] **Dangerous 级穿透 Bypass** — 当前 `DangerousCommandProtectionMiddleware` 在 Bypass 模式下直接跳过检查。需在 Bypass 跳过之前先检查 `ICommandDangerClassifier.IsDangerous(command)`，确保 Dangerous 级在 Bypass 下也拒绝。

### 统一迁移

- [ ] 将 `DestructiveCommandDetector` 委托给 `CommandDangerClassifier`（目前为回退方案）
- [ ] PowerShell 危险命令分级（`PsDangerousCmdlets` 集成 `DangerousCommandCatalog`）
- [ ] `Core.Utils.DestructiveCommandAnalyzer`（Infrastructure 层正则分析器）对齐新4级分级

### 文档联动

- [ ] AGENTS.md 更新权限设计章节，引用本 ADR
- [ ] CLI `--help` 输出补充4级分级说明
- [ ] `settings.json` schema 补充 `permissions.dangerLevelOverrides` 配置项

<!-- 🤖 Auto Decision: 2026-08-30 -->
<!-- 决策: 4级分级 Safe/LightValidation/Execution/Dangerous，绿色ask=可撤回，红色ask=不可撤回 -->
<!-- 原因: git操作可撤回应为绿色ask，删除/联网不可撤回应为红色ask，整盘操作直接拒绝 -->
<!-- 验证: Core.slnx 编译通过，80个单元测试全部通过 ✅ -->
<!-- 联动: 启动参数(--permission-mode) → PermissionMode → CommandDangerLevel × PermissionMode → 决策 -->
<!-- 联动: ask确认 → 颜色区分(绿色/红色) + 同级别自动通过(会话级非持久化) -->
<!-- 待实现: 同级别自动通过标记、ask颜色区分、GUI跳过标记、Dangerous穿透Bypass -->
