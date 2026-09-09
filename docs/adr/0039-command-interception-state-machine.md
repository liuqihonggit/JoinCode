# 0039. 命令拦截全状态机 + 守卫 + [Flags]

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组
- 取代：[0034](docs/adr/0034-command-interception-layered.md)
- 验证：Guard 编译 0 警告 0 错误，285 测试全通过 ✅
- 实现说明：命令拦截无状态，[Flags] 用于属性检测优化，保留 Guard 守卫模式，不引入状态机

## 背景

ADR 0034 放弃全状态机，理由是"状态空间爆炸"，改用 Guard+Interceptor 分层。但全状态机 + 守卫 + [Flags] 位标志可以有效降低状态爆炸（见 ADR 0038）：

- **守卫**：转换上的条件，同一状态的不同转换用守卫区分
- **[Flags] 位标志**：状态属性组合通过位运算表示，无需为每个组合定义独立状态
- **二次确认**：去抖窗口消除误报

ADR 0034 的放弃理由（状态空间爆炸）在引入 [Flags] + 守卫后不成立。

## 决策

**命令拦截用全状态机 + 守卫 + [Flags]，而非分层 Guard+Interceptor。**

1. **状态定义**：`[Flags] enum InterceptionState` 表示拦截状态属性组合
2. **守卫**：转换条件检查状态属性，而非完整状态
3. **决策模型**：保留 `CommandDecision` sealed record（Allow/Rewrite/Deny/Redirect/Handoff），作为状态机的输出
4. **统一调度**：用状态机统一调度，不再分 Guard/Interceptor 两层

**迁移策略**：渐进式（ADR 0007），现有 5 个 Guard 实现逐步迁移为状态机守卫。

## 路径大小写守卫(Windows 防误删)

**背景**:生产事故 — Windows 下执行 `rm -rf src/`,因文件系统大小写不敏感,`src` 与 `SRC` 指向同一目录,导致核心源码被误删;叠加 `git reset --hard` 数据无法恢复。现有 [0008](docs/adr/0008-archive-to-xxx-not-delete.md)(禁删→归档)是 AI 行为规范,0039 命令拦截架构未覆盖"路径大小写比对"这一具体守卫。

**决策**:在命令拦截状态机的删除转换上加"路径大小写守卫" — 删除命令(rm/del/Remove-Item/rmdir/rd/erase)的目标路径与文件系统真实大小写路径比对,叶子名大小写不一致则 Deny 并提示真实路径,阻断误删。

**实现**:
- `IRealPathResolver` / `FileSystemRealPathResolver`(`Core.Security.Services`):枚举父目录取条目真实大小写,注入 `IFileSystem`(JCC9001 合规,可 mock)
- `PathCaseSensitiveGuard`(`Core.Security.DangerClassification`):纯逻辑守卫,比对命令路径叶子名与真实路径叶子名(Ordinal),不匹配返回拦截结果
- 接入 `DangerousCommandProtectionMiddleware`:Shell 危险命令检测到 FileDeletion/DirectoryDeletion 风险后、自动通过前调用守卫,拦截则 `Rejected(真实路径提示)`
- 测试:`PathCaseSensitiveGuardTests` 11 用例(mock resolver,不依赖 OS),全量 156 测试无回归

**与 0008 关系**:0008 是策略层(禁删→移到 `.xxx/`),本守卫是机制层(Hook 强制大小写校验,即使 AI 写错路径也删不到)。两者互补:守卫拦大小写错误,0008 引导正确归档。

## 替代方案

1. **分层 Guard+Interceptor（ADR 0034 原方案）**：放弃。状态爆炸理由不成立，分层增加协调成本。
2. **仅 [Flags] 不守卫**：放弃。无守卫则所有转换无条件执行，无法区分条件。
3. **仅守卫不 [Flags]**：放弃。状态组合仍需独立枚举值，爆炸未解决。

## 后果

- 正面：统一状态机调度，不分层；[Flags] 降低状态爆炸；守卫灵活区分条件
- 负面：现有 5 个 Guard 实现需迁移；状态机设计需仔细定义状态属性和守卫
- 中性：`CommandDecision` sealed record 保留作为状态机输出；迁移按渐进式进行
