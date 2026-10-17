# TASK053: 安全漏洞与未实现功能修复计划

> 状态: proposed
> 来源: 全工程安全审计 + 未实现功能盘点（静态扫描 + 人工复核，2026-10-17）
> 关联: AGENTS.md 纵深防御七层规范、ADR 0080（手动 exe 验收）、ADR 0079（反例清单）

---

## 1. 背景与审计方法

- 双代理并行审计：① 安全守卫链 / 命令注入 / 路径遍历 / 密钥泄漏 / MCP 服务端 / 插件加载；② 全工程"未实现"扫描（NotImplementedException / TODO / 中文标记 / 枚举↔handler 完整性 / 测试契约反查）。
- 关键证据均已人工复核（McpHttpServer.cs:206-211、JccMcpServer.cs:69、McpCommand.cs:181 等）。
- 生产代码 `NotImplementedException` 数量为 0，纪律良好；缺口集中在少数显式桩与功能缺口。

---

## 2. 漏洞修复计划（按严重级别）

### 批次A: P0-1 — `jcc mcp serve` 无鉴权 + 工具执行绕过权限管道

**漏洞描述（三者叠加成立）**：

| # | 问题 | 证据位置 |
|---|------|----------|
| a | HTTP 无任何鉴权，`ValidateOrigin` 在 `_allowedOrigins.Count == 0` 或客户端省略 Origin 头时直接放行 | `modules/mcp/mcp/mcp_protocol/McpHttpServer.cs:206-211` |
| b | 生产入口暴露全部内部工具（bash、file_write、gh_* 等约 400 个），`tools/call` 直接 `_registry.ExecuteToolAsync`，完全绕过 13 层权限管道（PermissionAwareToolExecutor / PermissionCheckMiddleware 等） | `modules/apps/cli/core/commands/core/JccMcpServer.cs:69`、`McpCommand.cs:174,181` |
| c | file_write 无工作目录边界：`ResolvePathAsync` 在沙箱未激活时原路径放行，绝对路径照单全收 | `modules/cognition/hands/tool_handlers/dev_tools/defense/SandboxGuardNode.cs:35`、`PathGuardNode.cs` |

**影响**: 本地任意进程（或 `--host 0.0.0.0` 时任意网络用户）可未授权执行 shell、向任意绝对路径写文件（含启动目录、计划任务）。
**缓解事实**: 默认 host=localhost（`McpServeCommand.cs:19`）；handler 内部守卫链仍拦 Dangerous 级命令；但"无鉴权 + 无权限层 + 无边界"组合不可接受。

**修复方案（三件套，先行编写 ADR）**：

| # | 修复 | 方案 | 备选方案（考虑过但放弃） |
|---|------|------|--------------------------|
| A1 | 默认 token 鉴权 | 启动时用 CSPRNG 生成 token，打印到 console；客户端带 `Authorization: Bearer <token>` 或 `Mcp-Token` 头；` McpHttpServer` 校验头，缺失/错误返回 401。支持 `--token` 显式指定（集成/自动化场景）。默认**必须**带 token，无豁免开关 | mTLS（NativeAOT 证书栈复杂，本地场景过重）；仅 localhost（进程内提权面不变）；allowlist IP（无法防本地任意进程） |
| A2 | tools/call 接入权限管道 | `JccMcpServer` 注入 `PermissionAwareToolExecutor`（或其薄包装），复用聊天路径的 13 层管道；serve 场景默认 `PermissionMode.Auto`，禁止 Bypass 透传。禁止直接调用 `LocalToolRegistry.ExecuteToolAsync` 作为消费点 | 在 JccMcpServer 内复制一套权限检查（重复逻辑，违反"提取重复"规则） |
| A3 | file 沙箱边界默认激活 | `FileOperationService`/`FileWriter` 写路径统一经 `PathPermissionChecker` 校验 cwd 边界；沙箱外绝对路径写入 → Ask/拒绝（带引导文案：为什么拒绝 + 正确写法） | 仅依赖分类器（Shell 管道内路径检查不能替代 file handler 自身边界） |

**验收表**：

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `McpHttpServer`/`McpTcpServer` token 校验（401 拒绝 + 引导文案） | `McpCommand.cs` serve 启动处生成并打印 `Mcp-Token`；`McpServeCommand`/`McpServeArg`/`JccCliArg` 新增 `--token`；`ApiKeyRedLine` 白名单放行 | ✅ [ADR 0138](../adr/0138-mcp-serve-auth-token.md) | ✅ [ADR 0080](../adr/engineering_ci_test/0080-manual-exe-testing-guide.md) exe 冒烟：无 token → 401；错 token → 401；对 token initialize → 200 |
| serve 路径权限管道接入 | `JccMcpServer.HandleCallToolAsync` → `IToolExecutionGateway`（PermissionAwareToolExecutor） | ✅ [ADR 0138](../adr/0138-mcp-serve-auth-token.md) | ✅ exe 冒烟：`bash` 被拒（用户拒绝工具执行）；单测 `JccMcpServerGatewayTests` |
| file 写路径 cwd 边界校验 | 权限管道第 4/8 层收窄短路 + 第 9 层 PathPermission 生效（`AutoClassifierMiddleware`/`ToolListPermissionMiddleware`/`PathPermissionMiddleware`） | ✅ [ADR 0139](../adr/0139-permission-pipeline-path-boundary-shortcircuit.md) | ✅ exe 冒烟：`write C:\Users\Public` 越界 → 拒绝且文件不存在；`write` cwd 内 → 成功；`read` 越界不存在 → 报错 |

补充: 落地验收中发现权限管道自身短路缺陷（分类器/工具名单对带路径文件类工具
直接短路，绕过 PathPermission 9 步链），修复与验收见 [ADR 0139](../adr/0139-permission-pipeline-path-boundary-shortcircuit.md)。
回归测试: `PipelinePathBoundaryTests`（12 层真实管道，9 用例）+ guard 1096 / host 1279 / mcp 1000 全绿。

**TDD 流程**: 🔴 E2E 红（mock_server.e2e 新增 serve 鉴权用例）→ 🔴 单元红（McpHttpServer token 校验 / JccMcpServer 权限管道 / FileWriter 边界）→ 🟢 绿 → ADR 0138（proposed→accepted）→ 提交。

---

### 批次B: P1 修复（四项）

#### B1. 守卫拦截器 fail-open → fail-closed

- **证据**: `modules/guard/guard/hooks/execution/interception/core/CommandInterceptionDispatcher.cs:110-116` — 拦截器抛异常 → `_logger` 记录后 `continue` 放行。测试 `CommandInterceptionDispatcherTests.cs:151` 固化了该行为。
- **修复**: 安全类拦截器异常 → **拒绝命令并返回 Rust 风格报错**（①为什么拒绝：守卫内部故障 ②触发参数 ③哪个守卫层 ④正确做法）；仅纯审计/统计类拦截器允许异常跳过（区分标准：拦截器声明 `IsSafetyCritical` 或按层名白名单）。同步反向修改测试。
- **验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| Dispatcher 异常处理策略化（fail-closed 默认） | `CommandInterceptionDispatcher.cs:110-116` | ☐ | ☐ 单元测试：安全拦截器抛异常 → 命令被拒绝 |

#### B2. SSRF DNS 失败放行 + TOCTOU 双解析

- **证据**: `modules/cognition/hands/web/middleware/WebSsrfGuardMiddleware.cs:50-52`（SocketException → 放行）；`WebFetchMiddleware.cs:68-70` 实际请求时 HttpClient 二次解析 DNS（rebinding 窗口）。
- **修复**: ① DNS 解析失败 → 拒绝请求（fail-closed，Rust 风格报错）；② 消除双解析：SSRF 守卫解析出 IP 后，后续请求锁定该 IP 直连（自签 HttpClientHandler 配置），或校验请求前再次解析结果一致；③ 重定向链同一策略复检。
- **验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| SSRF fail-closed + 单次解析锁定 | `WebSsrfGuardMiddleware.cs`、`WebFetchMiddleware.cs` | ☐ | ☐ 单元: DNS 失败 → 拒绝；E2E: rebinding 场景不穿透 |

#### B3. `InstallGitHubAppCommand` git remote URL 命令注入

- **证据**: `modules/composition/slash/hands/tools/InstallGitHubAppCommand.cs:130-141` — URL 仅 TrimStart/TrimEnd，无 shell 元字符校验；`repoName` 内插 `$"gh api repos/{repoName} ..."`；回退路径经 `cmd.exe /c`（行 590-600）。
- **修复**: ① `repoName` 提取后用白名单正则 `[A-Za-z0-9._-]+` 校验，不匹配 → 报错+引导（写明触发的参数与正确写法）；② 消灭 `cmd.exe /c` 回退路径，统一 `ProcessStartInfoBuilder` + ArgumentList；③ 白名单作为统一数据源，禁止多处重复正则。
- **验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| repoName 白名单校验 + ArgumentList 统一 | `InstallGitHubAppCommand.cs:130-141,590-600` | ☐ | ☐ 单元: 恶意 URL（含 ` && start calc`）→ 报错不执行 |

#### B4. 审计层（执行→审计）接线

- **证据**: `ICommandExecutionAuditor` 唯一消费点 `DangerousCommandProtectionMiddleware.cs:36` 且 `auditor: null` 默认，仅 Unattended 模式（行 312-327）有记录点；Auto/Ask/Bypass/Plan 全部模式零审计；DI 可选注入语义未确认。
- **修复**: ① 确认/修通 DI 注册（auditor 显式注册，非可选 null）；② 全部权限模式接入审计（执行落盘到 `.jcc/audit/`，含命令、模式、时间、结果）；③ 审计层在纵深防御管线中声明层名与优先级（AGENTS.md 第 7 条）。
- **验收表**:

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| auditor DI 显式注册 + 全模式消费 | `DangerousCommandProtectionMiddleware.cs:36`、DI 注册处 | ☐ | ☐ ADR 0080 手动 exe: 执行命令后 `.jcc/audit/` 有完整记录 |

---

### 批次C: P2 加固（六项)

| # | 问题 | 证据 | 修复方案 | 验收 |
|---|------|------|----------|------|
| C1 | API key 默认随子进程继承（`env` 可回显，提示注入可窃取） | `SubprocessEnvCleaner.cs:68-69`（默认关闭，需手动 opt-in） | bash/powershell 工具**默认开启**清洗，`JCC_SUBPROCESS_ENV_SCRUB=0` 反向 opt-out | ☐ 手动 exe: bash 内 `printenv` 无 key |
| C2 | Bypass 家族 `disableBypassPermissionsMode` 闸疑不覆盖 CLI 显式 flag | `PermissionChecker.cs:88`（仅拦环境变量路径） | `--force`/`--bypass`/`--permission-mode bypass` 一律过 settings.json 安全闸 | ☐ 单元: 闸开启时 CLI flag 被拦 |
| C3 | HTTP 请求体无 Content-Length 上限（DoS 面） | `McpHttpServer.cs:222-225` | 限制上限（如 10MB），超限 413 + 引导文案 | ☐ 单元测试 |
| C4 | 插件加载无签名/哈希/目录白名单；`PluginApprovalRegistry` 未接线 | `PluginManager.cs:330-456` | 加载主路径接入审批注册表；外部 exe 启动前校验审批状态 | ☐ 单元: 未审批插件 → 拒绝+引导 |
| C5 | `JCC_SHELL_PREFIX` 未 ShellQuote 内插 | `BashSystemActuator.cs:180-183` | prefix 同样 `ShellQuote` | ☐ 单元测试 |
| C6 | auth.json 明文存 key | `ConfigLoader.cs:289-315` | DPAPI 加密（Windows）/ 拒绝写明文（跨平台降级提示） | ☐ 手动 exe: 登录后文件非明文 |

---

## 3. 未实现功能处理计划（按决策分组）

### 决策 1: 枚举保留但无 handler 的命令 → 实现或从枚举移除（禁止两侧维护，AGENTS.md 第 11 条）

| 命令 | 证据 | 决策选项 | 默认建议 |
|------|------|----------|----------|
| `/output-style` | `ChatCommandName.cs:27` | 实现 handler（对齐 TS）或移除枚举 | **移除枚举**（GUI/TUI 简化场景无消费） |
| `/keybindings` | `ChatCommandName.cs:28` | 同上 | **移除枚举** |
| `/passes` | `ChatCommandName.cs:60` | 同上 | **移除枚举** |

☐ 状态: 未开始 | 验收: 编译通过 + 枚举消费完整性测试通过

### 决策 2: 显式桩补全（核心路径相关）

| 项 | 证据 | 方案 | 优先级 |
|----|------|------|--------|
| 团队上下文转录恢复恒 null | `TeammateReconnectService.cs:78-87` | 实现：读取会话转录重建么上下文（对齐 restart 场景） | D1 |
| MCP 有状态 GET SSE 推送 | `McpTcpServer.cs:128`（返回 200 空流） | 实现事件流推送或显式文档声明"有状态模式不订阅推送"（当前处于实验性方式） | D2 |
| `WindowsJobObjectSandbox` CPU 参数静默无效 | `WindowsJobObjectSandbox.cs:24,29`（接受参数但不设置 LIMIT） | **必须修复**：实现 CPU limit 或删除参数让调用方无误导 | D1 |
| `/ultraplan --steps` execute 模式被忽略 | `UltraplanCommand.cs:33,60` | 传入 PlanService 或显式文档"steps 仅规划模式" | D2 |
| `confirm_action` 工具预留 | `UserInteractionToolName.cs:11-15`（显式标注未实现） | 评估: `ask_user_question` 已覆盖主场景 → **从枚举移除预留位**（消除误导）或实现 | D2 |

### 决策 3: 快速清理（历史遗留）
> 安全无关、零风险，优先执行。

| 项 | 文件 |
|----|------|
| 删除 4 个过时的"当前测试为 RED/P0-A 红阶段"注释 | `test/integration/integration.tests/prefix_cache/misc/LenientCoercionTests.cs:8`、`test/unit/host.tests/chat_commands/brain/UltraplanCommandTests.cs:5-7`、`test/mock/mock_server.e2e.tests/e2e/s_to_z/UltraplanExecuteE2ETests.cs:4-7`、注释与实现不匹配处 |

### 决策 4: 体验类 backlog（不阻塞本任务）

GUI Float 浮动窗口（`DockPosition.cs:14`）、色盲主题（`ThemeConverter.cs:7`）、Vim 引擎完整键绑定（`VimEngine.cs:4`）、选择器搜索（`SelectorComponents.cs:31`）、Onboarding TUI（`StartupWorkflow.cs:78`）、从机编译跨进程请求-响应（`GlobalBuildQueue.cs:71`）、GitHub 工具族 8 个显式拒绝项（sub-issue/attach/recover/refresh 等）。

---

## 4. 执行顺序总览

| 批次 | 内容 | 前置 | TDD 层 | ADR |
|------|------|------|--------|-----|
| 0 | 决策3 过时注释清理 | — | 无（零风险） | 不需要 |
| A | P0-1 三件套（A1 鉴权 / A2 权限管道 / A3 沙箱边界） | ADR 0138 先写 | E2E+单元 | 0138 鉴权与权限管道 |
| B | B1 fail-closed / B2 SSRF / B3 注入 / B4 审计层 | 批次A 完成 | E2E+单元 | 审计层接线若涉及层编排则记 ADR |
| C | C1-C6 P2 加固 | 批次B 完成 | 单元为主 | C6 DPAPI 涉及跨平台策略可记 ADR |
| D | 决策1（移除枚举）→ 决策2 D1 → D2 | 可与批次并行截断 | 单元 | 未实现功能取舍决策 |

**强制流程（每批次）**: 红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交（多条小提交，禁止一次大提交）→ 手动 exe 验收（ADR 0080）。

---

## 5. 已知缓解与新发现待复核项

- ✅ DangerousCommandCatalog 被实际消费（6+ 消费点）；Bypass 不放行黑灯；进程启动 ArgumentList 强制；PowerShell AST fail-closed；路径权限 9 步链。
- ⚠️ 待人工复核: `InstallGitHubAppCommand` 构造参数可空 → 反射实例化时 runner 是否为 null 的实际 DI 行为；`McpCommand` serve 宿主 `TrustWorkspace=true` 的工作目录信任放行（`StartupWorkflow.cs:209-212`）——纳入批次A 验收范围。

---

<!-- 🤖 Auto Decision: 2026-10-17 -->
<!-- 决策: P0 鉴权采用"启动生成 token + Bearer 头必填"而非 mTLS/仅localhost -->
<!-- 原因: NativeAOT 证书栈复杂、本地代 任意进程可连 localhost；token 方案零依赖、一行集成 -->
<!-- 替代方案: mTLS（过重）、allowlist IP（防不了本地进程） -->
<!-- 验证: 待批次A 实现 -->
