# 0090. `jcc gh` CLI 子命令 — 扁平元动词 + schema 驱动参数绑定

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

ADR 0089 确立"禁止系统/宿主 `gh`，统一走 jcc 自带入口"，但 GitHub 操作当前只能通过 `jcc mcp_call gh_pr_checks '{"pr_number":"123"}'` 调用：

- **写 JSON 反人类**：Bash/PowerShell 里嵌 JSON 需处理引号转义，PowerShell 还会剥离双引号（已知坑，见 `LlmJsonHelper.RepairJson`）
- **无法对齐 `jcc rg`**：`jcc rg` 已是自然 CLI 形态（`jcc rg "pat" src/ -i`），`gh` 却要写 JSON，两套肌肉记忆
- **实测确认 `jcc gh` 不是子命令**：`jcc gh` / `jcc gh pr list` 会被当作提示词处理，输出"错误: 未提供提示词。"

同时 ADR 0089 替代方案4 已记录该增强"属独立增强，不阻塞本决策"，本次落地。

## 决策

### 决策1：形态为 `jcc gh <group> <action> [positional...] [--opt value] [--json]`

**选择**：

```bash
jcc gh pr view 123                  # → gh_pr_view   { pr_number: "123" }
jcc gh pr checks 123                # → gh_pr_checks { pr_number: "123" }
jcc gh pr list --limit 3            # → gh_pr_list   { limit: 3 }
jcc gh run view 123 --log --filter error   # → gh_run_view { run_id:"123", log:true, filter:"error" }
jcc gh issue comment 12 "body 文本"  # → gh_issue_comment { issue_number:"12", body:"body 文本" }
jcc gh api repos/o/r/issues         # → gh_api      { path: "repos/o/r/issues" }
```

**理由**：
- 与 `jcc rg` 同构：位置参数为主、`--` 选项为辅，符合 ADR 0069「扁平元动词 + 位置参数为主」
- 与 GitHub 官方 `gh pr view 123` 肌肉记忆一致，迁移成本为零
- `<group> <action>` 两级而非一级：31 个 `gh_*` 工具天然按 `pr/issue/repo/release/run/branch/api` 分组，两级命名自解释

### 决策2：工具名按约定拼接 `gh_{group}_{action}`，不做静态映射表

**选择**：`toolName = $"gh_{group}_{action}"`（`api` 组特例 → `gh_api`）。工具不存在时报错并列出该 group 下可用 action。

**理由**：
- 约定大于配置（AGENTS.md 项目风格）：新增 `gh_*` 工具零改动即可被 CLI 暴露
- 静态字典需双向维护（加工具忘改表 = 静默不可用），违反 ADR 0043「收口函数统一」
- 未知 action 时用 registry 反查同前缀工具给出建议，比表里查不到更友好

### 决策3：位置参数按 schema `required` 顺序绑定，不用静态参数名表

**选择**：运行时从 `IMcpToolRegistry.GetToolInfoAsync(toolName)` 取 `ToolSchema`，把 `Required` 数组**按声明顺序**作为位置参数槽位；剩余参数走 `--key=value` / `--key value`。

**理由**：
- 实测 31 个 `gh_*` 工具的 `required` 集合恰好等于"人类直觉上的位置参数"，且**最多 2 个**：

  | 工具 | required（位置参数顺序） |
  |------|--------------------------|
  | `gh_pr_view/checks/diff/merge/close/reopen/checkout` | `pr_number` |
  | `gh_run_view/rerun/cancel` | `run_id` |
  | `gh_issue_view/close` | `issue_number` |
  | `gh_issue_comment` | `issue_number`, `body` |
  | `gh_release_view/create/delete` | `tag` |
  | `gh_release_download` | `tag`, `dir` |
  | `gh_release_upload` | `tag`, `files` |
  | `gh_repo_create` | `name` |
  | `gh_repo_fork/clone` | `repo` |
  | `gh_api` | `path` |
  | `gh_pr_list/issue_list/run_list/release_list/repo_list/repo_view` | （无） |

- 零硬编码：schema 改了 CLI 自动跟着改，不会出现"文档说 pr_number，实际要 number"
- 复用 MCP 单一事实源，避免 schema 与 CLI 两份真相

**边缘处理**（开心路径优先，边缘显式报错而非猜）：
- 位置参数多于 required 槽位 → Rust 风格报错，列出该工具接受的参数名
- 必填参数缺失 → 报错并给出正确用法示例

### 决策4：执行与输出复用 `McpCliCommand`，不另起炉灶

**选择**：`GhSubCommand` 只做「解析 + 绑定」，绑定结果交给 `McpCliCommand` 的 `WithHostAsync` + `registry.ExecuteToolAsync` + `OutputResult`。

**理由**：
- 复用已有的 host 构建（抑制启动警告、NonInteractive、TrustWorkspace）、工具存在性校验、JSON 输出、错误输出
- 只把 `OutputResult` / `ParseValueToJsonElement` 从 `private` 提到 `internal`，改动面最小
- 避免第二套 host 生命周期 → 进程启动一次而非两次（`jcc mcp_call` 建 host 约数秒）

### 决策5：解析器拆成纯函数，便于单元测试

**选择**：

| 类型 | 职责 | 可测性 |
|------|------|--------|
| `GhToolNameResolver` | `args → (toolName, 剩余参数)` | 纯函数，无 DI |
| `GhParamSchemaParser` | `ToolSchema → GhParam[]`（名/必填/是否布尔） | 纯函数 |
| `GhArgsBinder` | `(剩余参数, GhParam[]) → Dictionary<string,string>` | 纯函数 |
| `GhSubCommand` | 编排：建 host → 取 schema → 绑定 → 执行 | 集成层 |

**理由**：AGENTS.md 要求 TDD；纯函数可在 `Host.Tests` 里零成本红绿循环，不必为每个用例建 host（建 host 需数秒且依赖配置）。

## 替代方案

### 方案1：静态字典硬编码 30 个工具的参数名

放弃。双向维护成本高、加工具易漏、与 schema 双份真相。违反约定大于配置。

### 方案2：只支持 `jcc gh <tool-suffix> <json>`（如 `jcc gh pr_view '{"pr_number":"1"}'`）

放弃。只是把 `mcp_call` 换了个前缀，没解决"写 JSON 反人类"的核心问题。

### 方案3：用 System.CommandLine 嵌套子命令（真实 `gh pr view` 语法树）

放弃。ADR 0069 已决策「扁平元动词，不经过 System.CommandLine 嵌套子命令」，且嵌套树需为每个工具写 Command 对象，与决策2的零维护目标冲突。

### 方案4：位置参数按 `Properties` 声明顺序而非 `Required`

放弃。`Properties` 顺序依赖 DTO 定义顺序（可能把 `repo`/`working_dir` 这类通用参数排前面），而 `Required` 顺序才是"人类直觉位置参数"，实测已验证。

## 后果

- 正面：
  - Bash 里操作 GitHub 不用再拼 JSON，`jcc gh pr checks 123` 即可
  - 与 `jcc rg` 形态统一，一套肌肉记忆
  - 新增 `gh_*` 工具自动获得 CLI 入口，零额外代码
  - 位置参数绑定由 schema 驱动，不会出现文档与实现不一致
- 负面：
  - 每次 `jcc gh` 调用需建 host 取 schema（与 `jcc mcp_call` 同开销，无额外成本）
  - 位置参数语义依赖 `required` 顺序，若未来某工具 required 顺序与直觉不符需单独兜底
- 中性：
  - 新增 `GhToolNameResolver.cs` / `GhArgsBinder.cs` / `GhSubCommand.cs`
  - `McpCliCommand.OutputResult` / `ParseValueToJsonElement` 由 private 改 internal
  - `CliSubCommand` 新增 `[EnumValue("gh")] Gh`，需 `--no-incremental` 重建让源码生成器重新扫描

## 渐进式执行顺序

1. ✅ 写 ADR 0090（本文档）
2. ✅ `GhCommandResolver`（工具名解析 + schema 抽取 + 参数绑定，纯函数）
3. ✅ 单元测试（15 个用例全通过，`Host.Tests/Cli/GhCommandResolverTests.cs`）
4. ✅ `CliSubCommand` 加 `Gh` + `FlatSubCommandRouter` 加 case + `GhSubCommand.ExecuteAsync`
5. ✅ `--no-incremental` 全量重建（0 错误）
6. ✅ 真实调用验证：`jcc gh repo view` / `jcc gh pr list --limit 3` / `jcc gh api <path>` / `jcc gh run view <id> --log --filter error` / 未知 action 报错并列出可用 action / 缺必填报错
7. ✅ 更新 AGENTS.md 与 ADR 0089（`jcc gh` 不再是"不是子命令"）
8. ✅ git 提交

## 实测记录（2026-09-08）

| 命令 | 结果 |
|------|------|
| `jcc gh --help` | 输出用法 + 分组 + 7 条示例 |
| `jcc gh repo view` | 直返 api.github.com REST JSON |
| `jcc gh pr list --limit 3` | `[]`（当前无 open PR） |
| `jcc gh pr list --json` | `{"isError":false,"content":[{"type":"text","text":"[]"}]}` |
| `jcc gh api repos/liuqihonggit/JoinCode --fields name,stargazers_count` | REST JSON |
| `jcc gh run view 34168854728 --log --filter error --max-lines 3` | 日志过滤输出（`--max-lines` 已归一化到 `max_lines`） |
| `jcc gh pr checks`（缺必填） | Rust 风格报错指向 `pr_number` + 用法 |
| `jcc gh pr bogus 123`（未知 action） | `gh 子命令 'gh_pr_bogus' 未找到` + `可用: checkout, checks, close, diff, list, merge, reopen, view` |
| `jcc gh`（无分组） | `缺少 gh 分组` + 用法 |

**补充决策（实现期新增）**：选项名支持 kebab-case 归一化（`--max-lines` → `max_lines`），因为真实 `gh` CLI 用连字符而工具 schema 用下划线；未匹配时仍按原样报错并列出可用选项。
