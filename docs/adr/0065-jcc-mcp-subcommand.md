# 0065. jcc mcp CLI 子命令 — bash 直调内部 MCP 工具

- 状态：accepted
- 日期：2026-09-05
- 决策者：项目架构组

## 背景

项目已实现 296 个 MCP 工具（含新增的 29 个 GitHub 工具），但现有 `jcc tool exec <name>` 是空壳——只打印"请在交互模式下调用"，不真正执行。这导致：

1. **无法在 bash 里验证工具**：新写的 gh 工具没有真实调用过，无法保证参数构造和数据获取正确
2. **外部 LLM 无法消费 jcc 内部工具**：jcc 只能作为 MCP 客户端调用外部服务，不能作为服务端暴露自身工具
3. **AGENTS.md 避坑指南无法内化验证**：坑1-5（jq 引号、日志超时、skipping 误判等）需真实调用才能确认工具逻辑正确

## 决策

新增 `jcc mcp` CLI 入口级子命令（CliSubCommand.Mcp 枚举值），提供四个子动作：

### 1. `jcc mcp call <tool-name>` — bash 直接调用单工具

```powershell
jcc mcp call gh_pr_view --args '{"repo":"owner/repo","pr":177}' --json
jcc mcp call gh_pr_view --args-file args.json
echo '{"repo":"x","pr":1}' | jcc mcp call gh_pr_view --args-stdin
```

**参数传递**（JSON 为主 + 文件兜底，ADR 0065 决策）：
- `--args '<json>'`：内联 JSON（单引号包裹，bash 友好）
- `--args-file <path>`：从文件读 JSON（避免 bash 转义地狱）
- `--args-stdin`：从 stdin 读 JSON（管道友好）
- 解析用 `JsonDocument`（AOT 友好，禁用 dynamic）

**执行流程**：构建 DI 容器（复用 EngineSessionFactory.CreateCliSessionAsync）→ 从 IMcpToolRegistry 取工具 → 解析 JSON 为 JsonElement → 调用 ExecuteAsync → 输出 ToolResult（--json 则结构化 JSON，否则文本）

### 2. `jcc mcp list [--category <cat>]` — 列出已注册工具

```powershell
jcc mcp list
jcc mcp list --category github
jcc mcp list --json
```

从 IMcpToolRegistry.GetAllToolsAsync() 获取，按 ToolCategory 分组输出。

### 3. `jcc mcp search <query>` — 搜索工具

```powershell
jcc mcp search "pr view"
jcc mcp search "select:gh_pr_view,gh_run_list"
```

复用现有 ToolSearchEngine（与交互模式 tool_search 同引擎）。

### 4. `jcc mcp schema <tool-name>` — 输出工具参数 JSON Schema

```powershell
jcc mcp schema gh_pr_view --json
```

从 IMcpToolRegistry 取工具的 InputSchema，输出完整参数定义，供外部 LLM 或脚本消费。

### 路由

在 `ApplicationBuilder.RunSubCommandAsync` 中新增 `CliSubCommand.Mcp` 分支，构建独立 McpCommand（System.CommandLine.RootCommand 子命令），不经过交互模式 REPL。

## 替代方案

1. **扩展现有 `jcc tool exec` 空壳**：放弃。ToolCommand 现是 System.CommandLine RootCommand 子命令需重构；`jcc tool` 语义偏"工具管理"，混入"调用"不够清晰；无法独立做服务端模式。
2. **仅做 `jcc mcp serve` 服务端**：放弃。bash 验证多一步（先启服务），不直观。用户明确要 bash 直调。
3. **扁平参数 `--repo x --pr 177`**：放弃。29 个工具参数各不同，需运行时动态解析参数名和类型转换，System.CommandLine 不支持动态参数定义；复杂嵌套参数（数组）难表达；AOT 下动态类型转换需手写。
4. **JSON + 扁平混合**：放弃。两套解析路径，维护成本高，用户需记何时用哪个。

## 后果

- 正面：bash 可直接调用任意内部 MCP 工具验证；外部脚本/CI 可通过 `jcc mcp call` 集成；`jcc mcp list/search/schema` 提供工具发现能力
- 负面：`jcc mcp call` 需构建完整 DI 容器（含所有工具注册），启动开销约 1-2 秒；不适合高频调用场景（那应该用 `jcc mcp serve` 服务端模式，后续 ADR）
- 中性：新增 CliSubCommand.Mcp 枚举值，需全量重建生成器

## 后续

- ✅ `jcc mcp serve`（启动 MCP 服务端暴露给外部 LLM，复用 McpHttpServer）— 已实现，见下方"serve 实现"
- 启动参数精简已评估，决定不精简（30 个参数各有用途），改为 README 表格整理（已完成）

## serve 实现（2026-09-05）

新增 `jcc mcp serve` 子命令，把全部 387 个内部工具暴露为 MCP 协议（2025-11-25 Streamable HTTP）。

### 架构

- `McpServer.HandleListTools`/`HandleCallToolAsync` 改为 `protected virtual`（去 static），子类可 override
- `JccMcpServer : McpServer`（`app/JoinCode/Cli/Commands/JccMcpServer.cs`）注入 `IMcpToolRegistry`，override 两个方法
  - `HandleListTools`：调用 `GetAllToolsAsync()`，转 `IToolHandler` → `ToolDefinition`（InputSchema 用 `ContractsJsonContext.Default.ToolSchema` 序列化为 JsonElement）
  - `HandleCallToolAsync`：解析 `CallToolRequestParams`，调用 `ExecuteToolAsync`，转 `ToolResult` → `CallToolResult`
- `McpCommand.ExecuteServeAsync` 构建 host，创建 `JccMcpServer`，根据 `--transport` 启动 stdio 或 http 模式

### 用法

```powershell
jcc mcp serve --transport stdio                          # stdio 模式（MCP 客户端管道）
jcc mcp serve --transport http --port 9903               # HTTP 模式（无状态）
jcc mcp serve --transport http --port 9903 --host 0.0.0.0 # 监听所有网卡
```

### 冒烟验证

- `initialize` → serverName=jcc-mcp, protocolVersion=2025-11-25 ✅
- `tools/list` → 387 个工具（含 gh_*、tool_search、read、write 等）✅
- `tools/call get_environment_state` → 返回"光标状态: Normal..." ✅
