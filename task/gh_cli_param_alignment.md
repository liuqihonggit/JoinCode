# jcc gh 命令对齐系统 gh CLI 参数差异清单

> 对比基准：系统 `C:\Program Files\GitHub CLI\gh.exe` v2.101.0 (2026-09-15) vs jcc.exe (JoinCode main, 编译于 2026-10-07)
>
> 对齐目标：jcc gh 的分组/子命令/参数与系统 gh CLI 对齐，确保 AI 和用户用 `jcc gh` 能完成系统 `gh` 的等价操作，无需回退到系统 gh.exe

## 1. 顶层命令覆盖差异

jcc gh 分组：`pr | issue | repo | release | run | branch | api`（7 组，32 工具）
系统 gh 顶层命令：38 个

| 系统 gh 命令 | jcc 是否支持 | 缺失子命令数 | 优先级 |
|-------------|:-----------:|:----------:|:------:|
| pr | ✅ | 9 缺失 / 17 总 | P1 |
| issue | ✅ | 10 缺失 / 15 总 | P1 |
| run | ✅ | 3 缺失 / 7 总 | P2 |
| release | ✅ | 4 缺失 / 10 总 | P2 |
| repo | ✅ | 13 缺失 / 18 总 | P3 |
| api | ✅ | 0 | — |
| auth | ❌ | 全缺 | P3 |
| workflow | ❌ | 全缺 | P2 |
| cache | ❌ | 全缺 | P4 |
| browse | ❌ | 全缺 | P4 |
| config | ❌ | 全缺 | P3 |
| label | ❌ | 全缺 | P3 |
| search | ❌ | 全缺 | P3 |
| secret / variable | ❌ | 全缺 | P3 |
| gist / org / project | ❌ | 全缺 | P4 |
| codespace / discussion / skill | ❌ | 全缺 | P4 |
| gpg-key / ssh-key / deploy-key | ❌ | 全缺 | P4 |
| attestation / ruleset / extension | ❌ | 全缺 | P4 |
| completion / alias / copilot / preview | ❌ | 全缺 | P4 |
| status / agent-task / licenses | ❌ | 全缺 | P4 |
| **branch (jcc 独有)** | ✅ | — | — |
| **subscribe_pr (jcc 独有)** | ✅ | — | — |

## 2. 通用参数差异（最高优先级 P0）

系统 gh 几乎所有子命令都支持以下 4 个通用输出格式化参数，jcc **普遍缺失**：

| 通用参数 | 系统 gh 含义 | jcc 现状 | 影响 |
|---------|------------|---------|------|
| `--json fields` | JSON 输出 + 字段选择 | ❌ 缺失 | 无法结构化输出，AI 难以解析 |
| `--jq expression` | jq 表达式过滤 JSON | ❌ 缺失 | 无法灵活提取字段 |
| `--template string` | Go template 格式化 | ❌ 缺失 | 无法自定义输出格式 |
| `--web` / `-w` | 浏览器打开 | ❌ 缺失 | 无法快速跳转网页 |

> jcc 独有：`working_dir`（工作目录）、部分 view 有 `verbose`（近似 --json 但语义不同）

## 3. pr 子命令参数差异（P1）

### 3.1 子命令覆盖

| 系统 gh pr 子命令 | jcc 是否支持 |
|------------------|:-----------:|
| list / view / create / checks / merge / close / diff / checkout / reopen | ✅ |
| status / edit / lock / ready / revert / review / unlock / update-branch / comment | ❌ |

### 3.2 pr list 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `--state` | `state` | ✅ |
| `--limit` | `limit` | ✅ |
| `--author` | `author` | ✅ |
| `--repo` / `-R` | `repo` | ✅ |
| `--label` / `-l` | — | ❌ |
| `--assignee` / `-a` | — | ❌ |
| `--base` / `-B` | — | ❌ |
| `--head` / `-H` | — | ❌ |
| `--draft` / `-d` | — | ❌ |
| `--search` / `-S` | — | ❌ |
| `--app` | — | ❌ |
| `--json` / `--jq` / `--template` / `--web` | — | ❌ (通用缺失) |

### 3.3 pr view 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `[number]` | `pr_number` | ✅ |
| `--repo` | `repo` | ✅ |
| `--comments` / `-c` | — | ❌ |
| `--json` | `verbose` | ⚠️ 语义近似但不等价 |
| `--jq` / `--template` / `--web` | — | ❌ |

### 3.4 pr create 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `--title` / `--head` / `--base` / `--body` / `--draft` / `--repo` | 同名 | ✅ |
| `--assignee` / `-a` | — | ❌ |
| `--label` / `-l` | — | ❌ |
| `--reviewer` / `-r` | — | ❌ |
| `--milestone` / `-m` | — | ❌ |
| `--project` / `-p` | — | ❌ |
| `--body-file` / `-F` | — | ❌ |
| `--template` / `-T` | — | ❌ |
| `--fill` / `--fill-first` / `--fill-verbose` | — | ❌ |
| `--editor` / `-e` | — | ❌ |
| `--attach` | — | ❌ |
| `--dry-run` | — | ❌ |
| `--recover` | — | ❌ |
| `--no-maintainer-edit` | — | ❌ |
| `--web` | — | ❌ |

### 3.5 pr checks 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `[number]` / `--repo` | `pr_number` / `repo` | ✅ |
| `--watch` | — | ❌ (轮询等待) |
| `--interval` / `-i` | — | ❌ |
| `--fail-fast` | — | ❌ |
| `--required` | — | ❌ |
| `--json` / `--jq` / `--template` / `--web` | — | ❌ |

### 3.6 pr merge 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `[number]` / `--repo` | `pr_number` / `repo` | ✅ |
| `--squash` / `--merge` / `--rebase` | `merge_method` | ✅ 合并为单参数 |
| `--auto` | `auto_merge` | ✅ |
| `--delete-branch` / `-d` | `delete_branch` | ✅ |
| `--admin` | — | ❌ |
| `--body` / `-b` | — | ❌ |
| `--body-file` / `-F` | — | ❌ |
| `--subject` / `-t` | — | ❌ |
| `--author-email` / `-A` | — | ❌ |
| `--disable-auto` | — | ❌ |
| `--match-head-commit` | — | ❌ |

### 3.7 pr close / diff / checkout / reopen 参数

| 子命令 | 系统 gh 独有参数 | jcc 状态 |
|--------|-----------------|:--------:|
| close | `--delete-branch` / `-d` | ❌ 缺失 |
| diff | `--name-only` / `--patch` / `--exclude` / `--color` / `--allow-escape-sequences` / `--web` | ❌ 全缺 |
| checkout | `--branch` / `--detach` / `--force` / `--recurse-submodules` / `--worktree` | ❌ 全缺 |
| reopen | `--comment` / `-c` | ❌ 缺失 |

## 4. issue 子命令参数差异（P1）

### 4.1 子命令覆盖

| 系统 gh issue 子命令 | jcc 是否支持 |
|---------------------|:-----------:|
| list / view / create / close / comment | ✅ |
| status / delete / develop / edit / lock / pin / reopen / transfer / unlock / unpin | ❌ |

### 4.2 参数差异

| 子命令 | jcc 缺失参数 |
|--------|------------|
| list | `--author` `--app` `--mention` `--milestone` `--search` `--type` + 通用4参数 |
| view | `--comments` `--jq` `--template` `--web`（`--json` 用 verbose 近似） |
| create | `--attach` `--body-file` `--editor` `--milestone` `--project` `--recover` `--template` `--type` `--parent` `--blocked-by` `--blocking` `--web` |
| close | `--reason` / `-r` `--duplicate-of` |
| comment | 需确认 `--edit-last` `--create-if-none` 等 |

## 5. run 子命令参数差异（P2）

### 5.1 子命令覆盖

| 系统 gh run 子命令 | jcc 是否支持 |
|-------------------|:-----------:|
| list / view / cancel / rerun | ✅ |
| delete / download / watch | ❌ |

### 5.2 参数差异

| 子命令 | jcc 缺失参数 | jcc 独有增强 |
|--------|------------|------------|
| list | `--all` `--commit` `--created` `--event` `--user` `--workflow` + 通用4参数 | — |
| view | `--attempt` `--exit-status` `--log-failed` `--verbose` `--web` + 通用3参数 | `max_lines` `skip_lines` `expand` `filter` `refresh`（日志增强） |
| rerun | `--debug` `--job` | — |

## 6. release / repo 子命令差异（P2/P3）

### release
jcc 有：list / view / create / delete / download / upload
jcc 缺：**delete-asset / edit / verify / verify-asset**

### repo
jcc 有：list / view / create / fork / clone
jcc 缺：**archive / autolink / delete / deploy-key / edit / gitignore / license / read-dir / read-file / rename / set-default / sync / unarchive**

## 7. 对齐计划

### P0 — 通用输出格式化参数（最高优先级）
为所有 list/view 子命令补齐 `--json` / `--jq` / `--web` 三个参数（`--template` Go template 暂缓，jcc 用 .NET 无 Go template）：
- `--json`：结构化 JSON 输出（jcc 已有 RelaxedJsonSerializer，复用）
- `--jq`：jq 表达式过滤（需引入 jq 解析库或简化实现）
- `--web`：调用 `Process.Start` 打开浏览器 URL

### P1 — pr/issue 高频缺失参数
- pr list: `--label` `--assignee` `--base` `--head` `--draft` `--search`
- pr create: `--assignee` `--label` `--reviewer` `--milestone` `--body-file` `--fill`
- pr checks: `--watch` `--interval` `--fail-fast` `--required`
- pr merge: `--admin` `--body` `--subject` `--disable-auto`
- pr close: `--delete-branch`
- pr diff: `--name-only` `--patch` `--exclude`
- pr checkout: `--branch` `--force` `--detach`
- pr reopen: `--comment`
- issue list: `--author` `--mention` `--milestone` `--search`
- issue create: `--milestone` `--project` `--body-file` `--template`
- issue close: `--reason` `--duplicate-of`

### P2 — run/release 缺失子命令和参数
- run list: `--event` `--workflow` `--user` `--commit` `--created`
- run view: `--attempt` `--exit-status` `--log-failed`
- run rerun: `--debug` `--job`
- run 新增: `download`（下载 artifact）
- release 新增: `edit` `delete-asset`

### P3 — repo/auth/config/label/search 等
- repo 新增: `edit` `delete` `rename` `set-default` `sync` `archive`
- auth: `status` `login` `refresh` `token`
- config: `get` `set`
- label: `list` `create` `delete`
- search: `repos` `issues` `prs`

### P4 — 低频命令（暂缓）
gist / org / project / codespace / discussion / attestation / ruleset / extension / copilot 等

## 8. 验收标准

| 验收项 | 标准 |
|--------|------|
| 参数对齐 | 每个对齐的参数在 jcc gh 和系统 gh 行为一致 |
| 回退兼容 | jcc 不支持的参数给出明确错误提示 + 引导（AGENTS.md 规则8） |
| 手动 exe 验收 | 按 ADR 0080 真实运行 jcc gh 验证，非 mock |
| 文档更新 | AGENTS.md gh 工具使用章节同步更新参数表 |

## 9. 决策记录

<!-- 🤖 Auto Decision: 2026-10-07 -->
<!-- 决策: P0 通用参数优先对齐 --json/--web，--jq 次之，--template 暂缓 -->
<!-- 原因: --json 是 AI 解析最常用的；--web 实现简单；--jq 需 jq 解析库；--template 是 Go template，.NET 项目无原生支持 -->
<!-- 替代方案: 全量对齐4参数（--template 需引入 Go template 移植，成本高）-->
<!-- 验证: 待对齐后编译+手动 exe 验收 -->
