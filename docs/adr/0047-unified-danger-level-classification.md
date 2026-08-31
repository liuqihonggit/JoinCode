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

引入统一的 `CommandDangerLevel` **5 级分级**，作为权限决策的**唯一依据**。

### 五级分级总览（白/黄/绿/红/黑灯）

| 等级 | 数值 | 含义 | 颜色 | 可撤回 | AI 行为 | 同级别自动通过 |
|------|------|------|------|--------|---------|---------------|
| `Safe` | 0 | 只读操作（白灯） | — | — | 自动通过 | — |
| `Unknown` | 1 | 未知命令（黄灯） | 🟡 黄灯ask | — | 需用户确认 | ✅ 支持（会话级，不持久化） |
| `LightValidation` | 2 | 可撤回操作（绿灯） | 🟢 绿灯ask | ✅ 可撤回 | 需用户确认 | ✅ 支持（会话级，不持久化） |
| `Execution` | 3 | 不可撤回操作（红灯） | 🔴 红灯ask | ❌ 不可撤回 | 需用户确认 | ✅ 支持（会话级，不持久化） |
| `Dangerous` | 4 | 危险操作（黑灯） | — | ❌ 不可撤回 | **直接拒绝不提示** | ❌ 不支持 |

**核心区分**：黄灯=未知命令需确认，绿灯=可撤回操作（git commit 可 reset 撤回），红灯=不可撤回操作（rm 删除不可恢复），黑灯=直接拒绝。

**未知命令默认 Unknown（黄灯）**：未在 `DangerousCommandCatalog` 中登记的命令返回 Unknown 而非 Safe，防止恶意脚本（如 `./exploit.sh`）自动通过。常见只读命令（ls/cat/grep/echo/pwd/whoami 等 30+）已显式登记为 Safe。

**同级别自动通过**：用户选择后当前会话内同级别操作不再 ask，**不持久化**，每次打开新 exe 重新提示。用户可在 GUI 上点击标记按等级跳过。

### 各权限模式下的行为矩阵（CommandDangerLevel × PermissionMode）

| CommandDangerLevel ＼ PermissionMode | Plan | Auto | Ask | Bypass |
|--------------------------------------|------|------|-----|--------|
| `Safe`（白灯） | 放行 | 放行 | 放行 | 放行 |
| `Unknown`（🟡黄灯ask/未知） | ❌ 拒绝 | ❌ 拒绝+引导 | ⏸ 🟡黄灯待确认 | 放行 |
| `LightValidation`（🟢绿灯ask/可撤回） | 放行（类似只读） | ❌ 拒绝+引导 | ⏸ 🟢绿灯待确认 | 放行 |
| `Execution`（🔴红灯ask/不可撤回） | ❌ 拒绝 | ❌ 拒绝+引导 | ⏸ 🔴红灯待确认 | 放行 |
| `Dangerous`（黑灯） | ❌ 拒绝（不提示） | ❌ 拒绝（不提示） | ❌ 拒绝（不提示） | ❌ **拒绝（不提示）** |

> **关键设计**：`Dangerous` 级在**所有模式下都直接拒绝不提示**，包括 Bypass，确保 `rm -rf /`、`format c:` 等操作无论如何都不执行。Bypass 模式下中间件先调用 `TryRejectDangerousInBypass` 检查 Dangerous 级并拒绝，仅放行非 Dangerous 命令。

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

当 `DangerousCommandProtectionMiddleware` 返回 `PendingConfirmation` 时（Unknown/LightValidation/Execution 级在 Ask 模式下），由 `PermissionAwareToolExecutor.HandlePendingConfirmationAsync` 调用 `IPermissionConfirmationHandler.Confirm` 发起用户确认交互：

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

**⚠️ 已知缺陷**：确认处理器（CliPermissionConfirmationHandler/JccChatSession/TuiModeRunner）在用户确认后仅调用 `ApproveToolTemporarily(toolName)`，尚未联动 `ApproveLevelTemporarily(level)`。核心机制（PermissionChecker._approvedLevels + 中间件检查）已就绪，但需后续在确认流程中传递 dangerLevel 并调用 `ApproveLevelTemporarily`，同级别自动通过才会实际生效。

### 统一实现位置

`core/safety/Guard/src/Security/DangerClassification/` — 统一危险命令目录：
- `DangerousCommandCatalog` — 集中所有命令定义（命令/参数/组合/路径），每条记录同时标注 `CommandRisk`（风险类型）和 `CommandDangerLevel`（危险等级）
- `CommandDangerClassifier` — 统一分类器实现 `ICommandDangerClassifier`

### CommandRisk 保留为消息构建依据

`CommandRisk` 不删除，降级为消息构建的辅助信息（不再用于决策）：
- `CommandDangerLevel` — 权限决策的唯一依据
- `CommandRisk` — 消息构建的辅助信息（"文件删除" vs "系统修改"）

### Dangerous 级（黑灯）处理

Dangerous 级命令在任何权限模式下都被拒绝（包括 Bypass），返回引导消息：
```
⛔ 绝对禁止 — AI 无法执行此操作（格式化系统盘 — 绝对禁止）。
此操作可能造成不可恢复的数据丢失或系统损坏，AI 在任何权限模式下都不会执行。
如确需执行，请你在终端手动执行以下命令:
  format c:
⚠️ 请务必确认命令正确后再执行，此操作不可逆。
```

## 替代方案

1. **4级分级（不含 Unknown）** — 被否决，未知命令默认 Safe 存在安全漏洞（恶意脚本自动通过）
2. **用 DangerLevel 替代 CommandRisk（完全删除）** — 被否决，保留 CommandRisk 用于消息构建
3. **扩展现有 DestructiveCommandDetector** — 被否决，分散问题未根本解决
4. **[Flags] 二次方枚举** — 被否决，CommandDangerLevel 是互斥单一等级（一个命令只有一个等级），不是可组合标志位。[Flags] 会导致位或产生无效等级且 MergeLevels 取最高级变复杂

## 后续工作

### 已完成（本次修复）

- [x] **同级别自动通过标记机制** — PermissionChecker 维护 `_approvedLevels` 集合，中间件检查 `context.ApprovedLevels` 放行同等级。`IToolPermissionManager.ApproveLevelTemporarily(level)` 接口已暴露。会话级非持久化。
- [x] **ask 颜色区分** — CLI: `[黄灯ask]`/`[绿灯ask]`/`[红灯ask]` 级别标签。TUI/GUI 灯色标题。
- [x] **Dangerous 级穿透 Bypass** — `DangerousCommandProtectionMiddleware.TryRejectDangerousInBypass` 在 Bypass 分支内先检查 Dangerous 级并拒绝，仅放行非 Dangerous 命令。
- [x] **未知命令默认 Unknown（黄灯）** — `CommandDangerClassifier` 未知命令返回 Unknown 而非 Safe，catalog 显式登记 30+ 常见只读命令为 Safe。
- [x] **git push 等远程不可撤回操作升级红灯** — `IsGitIrreversibleSubcommand` 检测 git push/stash drop/tag -d/branch -D 升级为 Execution。

### 必须实现（核心联动）

- [ ] **确认处理器联动 ApproveLevelTemporarily** — 当前确认处理器仅调用 `ApproveToolTemporarily(toolName)`，需在确认流程中传递 dangerLevel 并调用 `ApproveLevelTemporarily(level)`，同级别自动通过才会实际生效。
- [ ] **GUI 按等级跳过标记** — GUI 界面上提供按等级跳过的复选框/按钮，用户点击后当前会话内该级别不再 ask。

### 统一迁移

- [ ] 将 `DestructiveCommandDetector` 委托给 `CommandDangerClassifier`（目前为回退方案）
- [ ] PowerShell 危险命令分级（`PsDangerousCmdlets` 集成 `DangerousCommandCatalog`）
- [ ] `Core.Utils.DestructiveCommandAnalyzer`（Infrastructure 层正则分析器）对齐新5级分级

### 文档联动

- [ ] AGENTS.md 更新权限设计章节，引用本 ADR
- [ ] CLI `--help` 输出补充5级分级说明
- [ ] `settings.json` schema 补充 `permissions.dangerLevelOverrides` 配置项

<!-- 🤖 Auto Decision: 2026-08-31 -->
<!-- 决策: 5级分级 Safe/Unknown/LightValidation/Execution/Dangerous（白/黄/绿/红/黑灯） -->
<!-- 原因: 未知命令默认黄灯ask防止恶意脚本自动通过，git可撤回绿灯，删除/联网不可撤回红灯，整盘操作黑灯直接拒绝 -->
<!-- 修复: Bypass黑灯穿透(#2)、未知命令默认Safe(#3)、git push误判可撤回(#4)、同级别自动通过(#5) -->
<!-- 验证: Guard.Security.Tests 105个单元测试全部通过 ✅ -->
<!-- 联动: 启动参数(--permission-mode) → PermissionMode → CommandDangerLevel × PermissionMode → 决策 -->
<!-- 联动: ask确认 → [黄灯ask]/[绿灯ask]/[红灯ask]标签 + 同级别自动通过(会话级非持久化) -->
<!-- 待实现: 确认处理器联动ApproveLevelTemporarily、GUI按等级跳过标记 -->
