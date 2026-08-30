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

引入统一的 `CommandDangerLevel` 4级分级，作为权限决策的**唯一依据**：

| 等级 | 含义 | AI 行为 | 示例 |
|------|------|---------|------|
| `Safe` | 安全 | 自动批准 | ls, cat, grep, git status |
| `Dangerous` | 危险 | 需用户确认 | rm, del, mv, chmod, curl |
| `Critical` | 极危险 | 需显式确认，不可批量批准 | rm -rf, git reset --hard, shutdown, format |
| `Forbidden` | 绝对禁止 | AI 永远拒绝，引导用户手动执行 | rm -rf /, format c:, mkfs, fdisk, dd of=/dev/sda |

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

- [ ] 将 `DestructiveCommandDetector` 委托给 `CommandDangerClassifier`（目前为回退方案）
- [ ] PowerShell 危险命令分级（`PsDangerousCmdlets` 集成）
- [ ] AGENTS.md 更新权限设计章节

<!-- 🤖 Auto Decision: 2026-08-30 -->
<!-- 决策: 引入 CommandDangerLevel 4级分级，统一危险指令到 DangerousCommandCatalog -->
<!-- 原因: 原系统无"绝对禁止AI执行"等级，rm -rf / 和 rm file.txt 得到相同对待 -->
<!-- 替代方案: 5级分级(否决)、完全删除CommandRisk(否决)、扩展现有检测器(否决) -->
<!-- 验证: Core.slnx 编译通过，65个单元测试全部通过 ✅ -->
