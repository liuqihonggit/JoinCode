# ADR 0139: 权限管道路径边界短路修复 — 文件类工具不得被工具级分类提前短路

> 状态: accepted
> 日期: 2026-10-17
> 关联: [ADR 0138](0138-mcp-serve-auth-token.md)（mcp serve 鉴权，本决策在其验收中发现）、[TASK053](../task/TASK053-安全漏洞与未实现功能修复计划.md)（批次A P0-2 延伸）

## 1. 背景

ADR 0138 决策 2（tools/call 接入 `IToolExecutionGateway` 权限管道）落地后的手动 exe 验收
（ADR 0080）发现：serve 场景 `write` 向 `C:\Users\Public`（cwd 外、非敏感段）仍写入成功。

12 层真实权限管道逐层探针（`PipelinePathBoundaryTests` 前缀定位法）锁定缺陷：
**工具级粗分类层对带路径参数的文件类工具直接短路，路径级守卫永远不执行**：

1. `AutoClassifierMiddleware`（管道第 4 层）— 生产 DI 注入 `AutoModeClassifier`，
   `write` 属 `ToolSecuritySets.SafeWriteTools`（`[SecurityClass("safe-write", AutoAllowed=true)]`
   源码生成）→ 分类器 `AutoApprove` → 设置 `context.Result` 短路；
2. `ToolListPermissionMiddleware`（第 8 层）— `read`/`glob`/`grep` 在默认
   `PermissionConfig.AutoApprovedTools` → `Contains(toolName)` 直接 `Approved` 短路；
3. `PathPermissionMiddleware`（第 9 层）— `IPathPermissionChecker?` 可选注入，
   checker 未注册时 `next()` 静默放行（fail-open 配置缺口）。

三条短路共同使 `PathPermissionMiddleware` 的 9 步决策链
（UNC 防御 → 可疑 Windows 路径 → deny 规则 → ask 规则 → cwd 边界 → 敏感路径）
对带路径的文件读写工具形同虚设；serve 冒烟实测 `write` 越界写
`C:\Users\Public` / `C:\Windows\Temp` 成功、`read` 越界读出敏感内容。

工具级分类（SafeWrite/ReadOnly/AutoApprovedTools）的语义目标是"免弹窗"，
而非"免路径检查"——两者本应由不同层负责，短路破坏了纵深防御分层
（AGENTS.md 规则 7：拦截按层组织，禁止散落越层判定）。

## 2. 决策

工具级粗分类层**不得对带路径参数的文件类工具设置短路 Result**：

1. `AutoClassifierMiddleware`：`IsFileReadTool/IsFileWriteTool(toolName)` 且
   `ExtractPathFromArguments(arguments)` 非空 → 直接 `next()`，交由
   `PathPermissionMiddleware` 判定路径边界；其余（危险命令、敏感工具、未知操作）行为不变。
2. `ToolListPermissionMiddleware`：同上条件 → `next()`；无路径参数的文件类工具
   或非文件类工具仍按 auto-approved/auto-rejected 名单短路（保持开心路径不弹窗）。
3. `PathPermissionMiddleware`：checker 为 null（配置错误）时 fail-closed —
   对带路径的文件类工具返回 `PendingConfirmation` + 引导文案
   （说明层名、触发原因、正确做法），禁止静默放行。

职责边界：分类器/工具名单做"与路径无关的粗分类"，路径细节统一由
`PathPermissionChecker` 唯一数据源判定（对齐 TS `checkReadPermissionForTool` /
`checkWritePermissionForTool` 语义：auto-approve 仅适用于工作目录内路径）。

## 3. 被视为但放弃的替代方案

- **在 AutoModeClassifier 内做路径检查**：让分类器复用 `IPathPermissionChecker` —
  与 PathPermissionMiddleware 职责重复，形成两套路径判定入口，违背唯一数据源原则。
- **删除 AutoClassifier 对写操作的 AutoApprove**（全局收窄分类器）：
  波面过大，所有非路径写工具（agent 管理等）行为变更，回归风险不成比例。
- **默认 AutoApprovedTools 移除 read/glob/grep**：影响面全量聊天路径，
  且不解决写边界；路径治理应下沉到路径层而非删除便捷名单。
- **serve 强制 Unattended/Ask 模式**：改变服务端权限语义，治标不治本——
  chat 交互路径同样存在该短路缺陷，必须修管道本体。

## 4. 验收（ADR 0080 手动 exe + 单测）

| 要求 | 基建实现 | 消费点 | 已实现 | 已验收 |
|------|----------|--------|--------|--------|
| write 越界（非敏感 cwd 外）→ 待确认（serve 无确认器 → 拒绝） | `AutoClassifierMiddleware` 收窄 | 权限管道第 4 层 | ✅ | ✅ exe 冒烟 `write C:\Users\Public` → isError 拒绝，文件不存在 |
| read 越界不存在文件 → 报错（不再放行） | `ToolListPermissionMiddleware` 收窄 | 权限管道第 8 层 | ✅ | ✅ exe 冒烟 `read` 越界 → "路径不存在" |
| write Windows 目录保持拦截 | `DangerousCmd` 敏感路径 + PathPermission | 第 3/9 层 | ✅ | ✅ 单测 + 冒烟 |
| write/read cwd 内 → 免确认（开心路径） | PathPermission 步骤 6（工作目录内） | 第 9 层 | ✅ | ✅ exe 冒烟 `write`/`read` cwd 内成功 |
| shell 命令仍需确认 | `DangerousOperationMiddleware` | 第 10 层 | ✅ | ✅ 单测 + exe 冒烟 bash 拒绝 |
| checker 缺失 fail-closed | `PathPermissionMiddleware` null 分支 | 第 9 层 | ✅ | ✅ 单测 |
| 管道全量回归 | — | guard 1096 / host 1279 / mcp 1000 | ✅ | ✅ dotnet test 全绿 |

红测试：`modules/guard/guard.security.tests/permission/permission_pipeline/PipelinePathBoundaryTests.cs`
（真实 12 层管道 + 真实 `PathPermissionChecker` + 真实 `AutoModeClassifier`，无 mock 中间件）。
