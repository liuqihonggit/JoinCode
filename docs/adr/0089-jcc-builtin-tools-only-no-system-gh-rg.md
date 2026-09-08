# 0089. jcc 自带工具统一入口 — 禁止系统/宿主环境的 gh / rg

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

用户诉求：`jcc.exe` 启动后已自带大量工具，**不要再用系统/宿主环境自带的 `gh` / `rg`**；GitHub 的 PR/CI 走 jcc 的 gh 工具，日常搜索走 jcc 的 rg 工具（其边缘错误已有友好提示）。

实测证据（2026-09-08，`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`）：

| 能力 | 实测命令 | 实测结果 |
|------|----------|----------|
| 工具总量 | `jcc mcp_list` | **390 个工具**，43 个分类（github / search / file / git / lsp / desktop …） |
| GitHub 域 | `jcc mcp_list --category github` | **30 个 `gh_*` 工具** |
| GitHub 直调 | `jcc mcp_call gh_repo_view '{}'` | 直返 `api.github.com` 完整 REST JSON，**未起任何 `gh` 子进程** |
| 搜索域（CLI） | `jcc rg` | 真子命令，`RgEngine`（mmap + PLINQ + 零 GC），见 ADR 0070 |
| 搜索域（MCP） | `jcc mcp_call grep '{...}'` | `search` 分类，ripgrep 驱动，8 个工具 |

两个域的"去外部依赖"改造此前已各自完成（ADR 0073 去 gh、ADR 0070 去 rg），但 AGENTS.md 里仍是"**优先**使用"的软措辞，AI 仍会顺手调用系统 `gh` CLI / 系统 `rg` / 宿主 IDE 内置 Grep 工具，导致：

- **依赖外泄**：与 ADR 0073 "jcc 自包含、可卸载系统 gh" 的目标直接冲突
- **环境不确定**：系统 `rg` 在 Windows 不保证存在；系统 `gh` 依赖 keyring 登录态
- **报错不可控**：系统工具的 stderr 是无格式纯文本，AI 无法自愈；jcc 侧已实现 Rust 风格 + 诊断块的友好提示

## 决策

### 决策1：禁止使用系统/宿主自带的 `gh` / `rg`，统一走 jcc 自带入口

**选择**：从"优先"升级为"**禁止**"。禁止在 Bash/PowerShell 中裸调 `gh`、`rg` 可执行文件，也禁止用宿主 IDE 内置的 Grep 工具替代 `jcc rg`。

**理由**：
- jcc 已自包含（390 工具 + `jcc rg` 子命令），外部 `gh`/`rg` 不再是必需依赖
- 单一入口 ⇒ 行为一致、报错格式统一、AI 可自愈
- 与 ADR 0073 决策1（删除 `RunGhAsync` → `GitHubCommandRunner` 调用链）闭环：既然实现层已无 gh，使用层也不该再调 gh

**"自带"的界定**：指操作系统 PATH 上的 `gh`/`rg` 可执行文件，以及宿主 Agent/IDE 内置的搜索工具；**不包含** jcc 自身的 `gh_*` MCP 工具与 `jcc rg` 子命令。

### 决策2：GitHub 的 PR / CI 问题一律用 jcc 的 gh 工具

**选择**：`jcc mcp_call gh_*`（`github` 分类 30 个工具，HttpClient 直调 GitHub REST API）。

**常用入口**（schema 已实测）：

```bash
jcc mcp_list --category github                          # 列出全部 gh_* 工具
jcc mcp_call gh_pr_list '{"limit":3}'                   # 列 PR；state/author/repo 可选
jcc mcp_call gh_pr_view '{"pr_number":"123"}'           # PR 详情
jcc mcp_call gh_pr_checks '{"pr_number":"123"}'         # CI 检查状态（必填 pr_number）
jcc mcp_call gh_run_view '{"run_id":"123"}'             # Run 详情/日志（必填 run_id）
jcc mcp_call gh_run_rerun '{"run_id":"123"}'            # 重跑失败的 job
```

**理由**：
- PR 与 CI 是高频链路（ADR 0026 两段式验证：PR CI → main CI），统一入口收益最大
- `gh_pr_checks` / `gh_run_view` 已内置结构化 drill down（ADR 0067），远优于 `gh run view --log` 逐页扫描
- 参数 schema 可用 `jcc mcp_schema <tool>` 自查询，AI 不必记忆

### 决策3：日常搜索一律用 jcc 的 rg 工具

**选择**：按调用形态二选一，**两者都走 jcc**，都禁止系统 `rg`：

| 场景 | 入口 | 说明 |
|------|------|------|
| CLI / Bash 脚本 | `jcc rg <pattern> <path> [path...]` | 快、可管道、退出码对齐 rg（0=有匹配/1=无匹配/2=超时） |
| MCP 工具调用 | `jcc mcp_call grep '{...}'` | 结构化输出，支持 `output_mode`/`head_limit`/`offset` |

**理由**：
- `jcc rg` 的 `<path>` 强制必填（ADR 0070 决策5），从架构层面消除"AI 忘输路径 → 扫盘卡死 120s"
- 边缘错误全部给出**可执行**提示，AI 能自愈而非盲目重试

**实测边缘提示（`jcc rg` 宽容策略，ADR 0070 决策5/6/7）**：

1. PowerShell 把 `\s` 传成 `\\s` → 自动修复为 `\s`
2. 缺少 `path` → 立即报错退出（禁止无路径搜索）
3. 根目录（`C:\` / `/`）→ 拒绝扫盘
4. 超时 → 硬终止返回退出码 2（默认 30s，最大 300s）
5. 无匹配 → 退出码 1（对齐 rg）
6. 二进制文件自动跳过，遵守 `.gitignore`
7. mmap 零拷贝读大文件（>64KB），PLINQ 并行，Span 零 GC

**实测边缘提示（`jcc mcp_call grep`）**：

| 边缘情况 | 实际输出 |
|----------|----------|
| 无匹配 | `No files found` + `[诊断] pattern:..., path:...` + `提示: 检查正则语法、搜索路径、文件类型过滤(glob/file_type)。` |
| 非法正则 | `Invalid regular expression: Invalid pattern '(unclosed' at offset 9. Not enough )'s.` |
| 路径不存在 | `Path does not exist: D://not_exist_dir. Note: Current working directory is D:\project\w2.` |
| 缺必填参数 | `grep 执行失败: Missing required parameter: pattern` |

### 决策4：AI 遇到 gh/rg 报错时按 jcc 提示自愈，不回退到系统工具

**选择**：报错时先按提示修正参数（正则语法 / 路径 / 过滤条件），**不得**因为 jcc 工具报错就改用系统 `gh`/`rg` 绕过去。

**理由**：绕过去等于把依赖重新引入，ADR 0073 的收益归零；jcc 侧提示已足够定位问题。

## 替代方案

### 方案1：保留系统 gh/rg 作为 fallback

放弃。ADR 0073 已论证："fallback 到不存在的工具更糟"。且双入口会让 AGENTS.md 规则退化为软建议，形同虚设。

### 方案2：只改 AGENTS.md 措辞，不写 ADR

放弃。ADR 工作流要求"新架构决策必须先写 ADR"，且这是跨 AGENTS.md / ADR 0084 / ADR 0073 的全局规则变更，需要可回溯的决策依据。

### 方案3：`jcc mcp_call grep` 完全替代 `jcc rg`（或反之）

放弃。两者语义不同：

- `jcc rg` 是 **CLI 子命令**，面向 Bash 管道、退出码驱动、脚本组合
- `grep` 是 **MCP 工具**，面向结构化输出、`head_limit`/`offset` 分页

合并会损失一方的语义。正确做法是"两个都走 jcc、按场景选用"，而非二选一。

### 方案4：把 `gh` 也做成 `jcc gh` CLI 子命令（对齐 `jcc rg`）

暂缓。当前 `jcc mcp_call gh_*` 已能覆盖全部 30 个工具，且 `mcp_call` 是统一的工具直调入口（ADR 0069）。实测 `jcc gh` **不是**已注册子命令（会被当作提示词处理），新增子命令属独立增强，不阻塞本决策。

## 后果

- 正面：
  - jcc 真正自包含，可卸载系统 `gh` / `rg`（ADR 0073 目标闭环）
  - GitHub PR/CI 与日常搜索行为一致、可复现
  - 边缘错误有统一友好提示，AI 自愈率提高，减少无效重试
  - 单一入口便于统计与治理（`jcc mcp_call` 可统一鉴权/限流）
- 负面：
  - AI 需改掉肌肉记忆（Bash 里裸调 `gh`/`rg`），初期可能误用，靠 AGENTS.md 红线约束
  - jcc 未构建时（`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe` 缺失）无可用替代，需先编译
- 中性：
  - AGENTS.md 从"优先"升级为"⛔ 禁止"，并新增统一入口小节
  - ADR 0084「脚本语言优先级」第 4/5 条同步升级为强制并反向引用本文档

## 渐进式执行顺序

1. ✅ 实测 `jcc --help` / `jcc mcp_list` / `jcc mcp_list --category github` / `jcc mcp_schema` 取证
2. ✅ 实测 `jcc mcp_call gh_repo_view` 确认为 REST 直调（无 gh 子进程）
3. ✅ 实测 `jcc mcp_call grep` 四类边缘错误提示
4. ✅ 实测 `jcc rg` 无参数友好报错 + 宽容策略 7 条
5. ✅ 创建 ADR 0089（本文档）
6. ✅ 更新 AGENTS.md：新增「jcc 自带工具统一入口」小节 + Git 规范表 + 优先级链措辞
7. ✅ 更新 ADR 0084 第 4/5 条：优先 → 强制，反向引用 0089
8. ✅ 修正 ADR 0084 中 0070 的失效链接（`0070-rgengine-independent-implementation.md` → `0070-rg-engine-mmap-plinq.md`）
