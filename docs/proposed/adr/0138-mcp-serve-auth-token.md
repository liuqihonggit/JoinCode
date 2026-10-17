# ADR 0138: mcp serve 默认 token 鉴权 + 工具执行统一走权限管道

> 状态: proposed
> 日期: 2026-10-17
> 关联: [TASK053 修复计划](global:../../task/TASK053-安全漏洞与未实现功能修复计划.md)（批次A P0-1）、ADR 0069（mcp serve 子命令）、ADR 0080（手动 exe 验收）

## 1. 背景

安全审计（TASK053）确认 P0 组合漏洞：

1. `McpHttpServer` / `McpTcpServer` 对外服务端（`jcc mcp serve --transport http`）无任何鉴权；
   `ValidateOrigin` 在 `_allowedOrigins.Count == 0`（默认）或客户端省略 Origin 头时直接放行。
2. 生产入口暴露全部内部工具（bash、file_write、gh_* 等约 400 个）；
   `JccMcpServer.HandleCallToolAsync` 直接调用 `_registry.ExecuteToolAsync`
   (`modules/apps/cli/core/commands/core/JccMcpServer.cs:69`)，
   完全绕过聊天路径的 13 层权限管道（`PermissionAwareToolExecutor` →
   `PermissionCheckMiddleware` / `PathPermissionMiddleware` / `EarlyPathDenyMiddleware` 等）。
3. file_write 在 serve 路径下无工作目录边界（`PathPermissionMiddleware` 仅在权限管道中生效，
   `SandboxGuardNode.ResolvePathAsync` 在沙箱未激活时原路径放行）。

影响: 本地任意进程（或 `--host 0.0.0.0` 时任意网络用户）可未授权执行 shell、
向任意绝对路径写文件（含启动目录、计划任务）。

## 2. 决策

### 决策 1: 默认 token 鉴权（无豁免开关）

- `McpHttpServer` / `McpTcpServer` 构造函数新增 `string? authToken` 参数：
  - `null`（默认）→ 构造时自动用 CSPRNG 生成 64 位 hex token，暴露 `AuthToken` 属性供调用方读取；
  - 显式传入非空 token → 使用传入值（脚本集成场景）；
  - 显式传入空字符串 → `ArgumentException`（禁止任何形式关闭鉴权）。
- 校验点在 `HandleRequestAsync` 入口最前（所有 method 全覆盖）：
  - 接受 `Authorization: Bearer <token>` 或 `Mcp-Token: <token>`；
  - 失败 → `401` + `WWW-Authenticate: Bearer` + 引导文案（说明客户端应从 serve 启动输出复制 token）;
  - **常量时间比较**（`CryptographicOperations.FixedTimeEquals`）防时序侧信道。
- token 生成/校验统一为唯一数据源（`McpAuthToken`），禁止每处复制，
  对齐 AGENTS.md"硬编码变委托/复用现有"规则（ADR 0079 反例教训）。

### 决策 2: tools/call 统一走 IToolExecutionGateway（权限管道）

- `JccMcpServer` 构造函数注入 `IToolExecutionGateway`（必填），
  `HandleCallToolAsync` 执行路径从 `_registry.ExecuteToolAsync` 改为 `_gateway.ExecuteAsync`；
  tools/list、containsTool 判断仍走 `_registry`（列表/查询不是执行，无需权限层）。
- serve 流程构建 server 时通过 `GetRequiredService<IToolExecutionGateway>()` 获取
  （`PermissionAwareToolExecutor` 已 `[Register(typeof(IToolExecutionGateway), Singleton)]`，
  `ServiceRegistration.Mcp.cs:80` 确认注册）。
- 效果: serve 场景自动获得聊天路径同款 13 层管道 —
  Auto 模式下 `PathPermissionMiddleware` 兜底 file_write 的 cwd 边界
  （绝对路径越界 → Ask → serve 无确认处理器 → 拒绝），
  `DangerousCommandProtectionMiddleware` 兜底黑灯命令。
- 放弃的替代方案: 在 JccMcpServer 内复制一套权限检查逻辑
  （重复实现两层权限语义，违反"提取重复为公共组件"规则，且必然漂移）。

### 决策 3: serve 启动输出 token 与用法

- `jcc mcp serve` 启动时打印 `Mcp-Token: <token>` 与客户端用法示例
  （`Authorization: Bearer` 头），非 localhost 绑定时额外打印警告；
- stdio 模式为同进程标准流（TCP 之上的 HTTP 头不存在），无需 token，不打印。

## 3. 影响面

| 文件 | 变更 |
|------|------|
| `modules/mcp/mcp/mcp_protocol/McpHttpServer.cs` | 构造 + 401 校验 |
| `modules/mcp/mcp/mcp_protocol/McpTcpServer.cs` | 构造 + 401 校验 |
| `modules/mcp/mcp/mcp_protocol/McpAuthToken.cs`（新） | 生成 + 校验（唯一数据源） |
| `modules/apps/cli/core/commands/core/JccMcpServer.cs` | gateway 注入 + 执行路径切换 |
| `modules/apps/cli/core/commands/core/McpCommand.cs` | serve 流程获取 gateway + token 用法打印 |
| `modules/mcp/mcp.tests/core/mcp/McpHttpServerTests.cs` `McpHttpServerE2ETests.cs` | 存量用例补 token 头（回归防护） |
| `modules/mcp/mcp.tests/core/mcp/McpAuthTokenTests.cs` 等（新） | 红测试先行 |

## 4. 验收

| 要求 | 基建实现 | 消费点 | 验证方式 |
|------|----------|--------|----------|
| 无 token 请求 → 401 + 引导 | McpHttpServer/McpTcpServer 校验 | serve 流程 | 单元 + 真实 exe 冒烟（ADR 0080） |
| Bearer/Mcp-Token 正确 → 200 | 同上 | — | 单元 + 冒烟 |
| token 常量时间比较 | McpAuthToken | 两 server | 单元测试 |
| tools/call 走权限管道 | JccMcpServer → gateway | serve/stdio 两传输 | 单元 mock + 管道拦截回归测试 |
| file_write 越界路径拒绝 | PathPermissionMiddleware（Auto）生效于 serve | gateway 管道 | 集成测试（serve 场景 file_write cwd 外路径 → 拒绝） |
| 现有 E2E 全绿 | 存量用例补 token 头 | — | dotnet test |

## 5. 被视为但放弃的替代方案

- **mTLS / 证书校验**: NativeAOT 证书栈复杂，本地 serve 场景过重。
- **仅 localhost 限制**: 进程级本地任意代码仍可连 127.0.0.1，攻击面不变。
- **IP allowlist**: 无法区分"本地合法进程"与"本地恶意进程"。
- **仅加权限管道不加 token**: 管道兜底 `tools/call`，但 `initialize`/`tools/list` 协议面
  仍完全开放（可被任何本地进程探测/枚举工具清单），且 `--host 0.0.0.0` 场景网络面裸奔。
- **禁用 serve 命令**: 现有集成方（stdio/http 消费者）依赖此入口，直接禁用属于倒退。
