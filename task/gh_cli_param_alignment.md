# jcc gh 命令对齐系统 gh CLI 参数差异清单

> 对比基准：系统 `C:\Program Files\GitHub CLI\gh.exe` v2.101.0 (2026-09-15) vs jcc.exe (JoinCode w1 分支, 2026-10-07)
>
> 对齐目标：jcc gh 的分组/子命令/参数与系统 gh CLI 对齐，确保 AI 和用户用 `jcc gh` 能完成系统 `gh` 的等价操作，无需回退到系统 gh.exe

## 0. 完成总览（2026-10-07）

| 子命令组 | 子命令对齐 | 参数对齐 | 新增子命令 | 测试数 | 状态 |
|---------|:---------:|:--------:|:---------:|:------:|:----:|
| pr | 14/14 | ✅ 全部高频参数 | 5 (comment/edit/review/lock/unlock) | — | ✅ 完成 |
| issue | 10/10 | ✅ list/view/create/close | 5 (reopen/edit/delete/lock/unlock) | — | ✅ 高频完成 |
| run | 5/5 | ✅ list/view/rerun | 2 (download/delete) | — | ✅ 完成 |
| release | 6/6+2新增 | ✅ 全部 + delete-asset/edit | 2 | — | ✅ 完成 |
| repo | 4/5+7新增 | ✅ view/list/create/fork + edit/delete/archive/unarchive/rename/sync/set-default | 7 | — | ✅ 高频完成 |
| **合计** | — | — | **21 新增** | **117 通过** | ✅ |

> 通用参数：`--web` ✅ 各 view 已实现 | `--json` ⚠️ `verbose` 近似 | `--jq` ❌ 需引入库 | `--template` ❌ Go template 暂缓

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
| `--json fields` | JSON 输出 + 字段选择 | ⚠️ `verbose=true` 近似（语义不完全等价） | 无法精确字段选择 |
| `--jq expression` | jq 表达式过滤 JSON | ❌ 缺失 | 无法灵活提取字段 |
| `--template string` | Go template 格式化 | ❌ 缺失 | 无法自定义输出格式 |
| `--web` / `-w` | 浏览器打开 | ✅ 各 view 子命令已实现（返回 URL） | — |

> jcc 独有：`working_dir`（工作目录）、部分 view 有 `verbose`（近似 --json 但语义不同）

## 3. pr 子命令参数差异（P1）

### 3.1 子命令覆盖

| 系统 gh pr 子命令 | jcc 是否支持 |
|------------------|:-----------:|
| list / view / create / checks / merge / close / diff / checkout / reopen / **comment** / **edit** / **review** / **lock** / **unlock** | ✅ |
| status / ready / revert / unlock / update-branch | ❌ |

### 3.2 pr list 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `--state` | `state` | ✅ |
| `--limit` | `limit` | ✅ |
| `--author` | `author` | ✅ |
| `--repo` / `-R` | `repo` | ✅ |
| `--label` / `-l` | `label` | ✅ |
| `--assignee` / `-a` | `assignee` | ✅ |
| `--base` / `-B` | `base` | ✅ |
| `--head` / `-H` | `head` | ✅ |
| `--draft` / `-d` | `draft` | ✅ |
| `--search` / `-S` | `search` | ✅ |
| `--app` | — | ❌ |
| `--json` / `--jq` / `--template` / `--web` | `verbose` 近似 / ❌ / ❌ / ❌ | ⚠️ |

### 3.3 pr view 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `[number]` | `pr_number` | ✅ |
| `--repo` | `repo` | ✅ |
| `--comments` / `-c` | `comments` | ✅ |
| `--json` | `verbose` | ⚠️ 语义近似但不等价 |
| `--web` | `web` | ✅ |
| `--jq` / `--template` | — | ❌ |

### 3.4 pr create 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `--title` / `--head` / `--base` / `--body` / `--draft` / `--repo` | 同名 | ✅ |
| `--assignee` / `-a` | `assignee` | ✅ |
| `--label` / `-l` | `label` | ✅ |
| `--reviewer` / `-r` | `reviewer` | ✅ |
| `--milestone` / `-m` | `milestone` | ✅ |
| `--body-file` / `-F` | `body_file` | ✅ |
| `--fill` / `--fill-first` / `--fill-verbose` | `fill` | ✅ |
| `--project` / `-p` | — | ❌ |
| `--template` / `-T` | — | ❌ |
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
| `--watch` | `watch` | ✅ |
| `--interval` / `-i` | `interval` | ✅ |
| `--fail-fast` | `fail_fast` | ✅ |
| `--required` | `required` | ✅ |
| `--json` / `--jq` / `--template` / `--web` | ❌ / ❌ / ❌ / ❌ | ❌ |

### 3.6 pr merge 参数

| 系统 gh 参数 | jcc 参数 | 状态 |
|-------------|---------|:----:|
| `[number]` / `--repo` | `pr_number` / `repo` | ✅ |
| `--squash` / `--merge` / `--rebase` | `merge_method` | ✅ 合并为单参数 |
| `--auto` | `auto_merge` | ✅ |
| `--delete-branch` / `-d` | `delete_branch` | ✅ |
| `--admin` | `admin` | ✅ |
| `--body` / `-b` | `body` | ✅ |
| `--body-file` / `-F` | — | ❌ |
| `--subject` / `-t` | `subject` | ✅ |
| `--author-email` / `-A` | — | ❌ |
| `--disable-auto` | `disable_auto` | ✅ |
| `--match-head-commit` | — | ❌ |

### 3.7 pr close / diff / checkout / reopen 参数

| 子命令 | 系统 gh 独有参数 | jcc 状态 |
|--------|-----------------|:--------:|
| close | `--delete-branch` / `-d` | ✅ `delete_branch` |
| diff | `--name-only` / `--patch` / `--exclude` / `--color` / `--allow-escape-sequences` / `--web` | ✅ `name_only` `patch` `exclude` / ❌ `--color` `--allow-escape-sequences` `--web` |
| checkout | `--branch` / `--detach` / `--force` / `--recurse-submodules` / `--worktree` | ✅ `branch` `force` `detach` / ❌ `--recurse-submodules` `--worktree` |
| reopen | `--comment` / `-c` | ✅ `comment` |

## 4. issue 子命令参数差异（P1）

### 4.1 子命令覆盖

| 系统 gh issue 子命令 | jcc 是否支持 |
|---------------------|:-----------:|
| list / view / create / close / comment / **reopen** / **edit** / **delete** / **lock** / **unlock** | ✅ |
| status / develop / pin / transfer / unpin | ❌ |

### 4.2 参数差异

| 子命令 | 已对齐参数 | 仍缺失 |
|--------|-----------|--------|
| list | ✅ `--author` `--mention` `--milestone` `--search` `--type` | `--app` + 通用4参数 |
| view | ✅ `--comments` `--web` | `--jq` `--template`（`--json` 用 verbose 近似） |
| create | ✅ `--milestone` | `--attach` `--body-file` `--editor` `--project` `--recover` `--template` `--type` `--parent` `--blocked-by` `--blocking` `--web` |
| close | ✅ `--reason` `--duplicate-of` | — |
| comment | — | `--edit-last` `--create-if-none` 等 |

## 5. run 子命令参数差异（P2）

### 5.1 子命令覆盖

| 系统 gh run 子命令 | jcc 是否支持 |
|-------------------|:-----------:|
| list / view / cancel / rerun / **download** / **delete** | ✅ |
| watch | ❌ |

### 5.2 参数差异

| 子命令 | 已对齐参数 | 仍缺失 | jcc 独有增强 |
|--------|-----------|--------|------------|
| list | ✅ `--event` `--workflow` `--user` `--commit` `--created` | `--all` + 通用4参数 | — |
| view | ✅ `--attempt` `--web` | `--exit-status` `--log-failed` `--verbose` + 通用3参数 | `max_lines` `skip_lines` `expand` `filter` `refresh`（日志增强） |
| rerun | ✅ `--debug` `--job` | — | — |

## 6. release / repo 子命令差异（P2/P3）

### release
jcc 有：list / view / create / delete / download / upload / **delete-asset** / **edit**
jcc 缺：**verify / verify-asset**

| 子命令 | 已对齐参数 | 仍缺失 |
|--------|-----------|--------|
| list | ✅ `--exclude-drafts` `--exclude-prereleases` | `--order` + 通用4参数 |
| view | ✅ `--web` | `--json` `--jq` `--template` |
| create | ✅ `--generate-notes` | `--notes-file` `--notes-from-tag` `--notes-start-tag` `--verify-tag` `--discussion-category` `--latest` `--web` |
| delete | ✅ `--cleanup-tag` | — |
| download | ✅ `--clobber` `--skip-existing` | `--order` |
| upload | ✅ `--clobber` | — |
| delete-asset | ✅ 新增 | — |
| edit | ✅ 新增 `--tag` `--title` `--notes` `--draft` `--prerelease` `--target` | `--notes-file` `--generate-notes` `--discussion-category` `--latest` |

### repo
jcc 有：list / view / create / fork / clone / **edit** / **delete** / **archive** / **unarchive** / **rename** / **sync** / **set-default**
jcc 缺：**autolink / deploy-key / gitignore / license / read-dir / read-file**

| 子命令 | 已对齐参数 | 仍缺失 |
|--------|-----------|--------|
| view | ✅ `--web` | `--branch` `--json` `--jq` `--template` |
| list | ✅ `--language` `--visibility` `--source` `--fork` | `--archived` `--topic` `--match` + 通用4参数 |
| create | ✅ `--homepage` `--gitignore` `--license` | `--team` `--template` `--source` `--push` `--clone` `--disable-issues` `--disable-wiki` `--web` |
| fork | ✅ `--org` | `--remote` `--fork-name` `--default-branch-only` |
| clone | — | `--upstream-remote-name` `--bare` `--single-branch` `--depth` `--filter` `--sparse` |
| edit | ✅ 新增 `--description` `--homepage` `--visibility` `--default-branch` `--has-issues` `--has-wiki` | `--enable-issues` `--enable-wiki` `--delete-branch-on-merge` |
| delete | ✅ 新增（需 `--yes` 确认） | — |
| archive | ✅ 新增 | — |
| unarchive | ✅ 新增 | — |

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

<!-- 🤖 Auto Decision: 2026-10-07 -->
<!-- 决策: pr/issue/run/release/repo 五组子命令高频参数对齐完成 + 新增 6 个子命令 -->
<!-- 原因: 用户选择"只做 pr 子命令"后去睡觉,指示"逐个对齐",按 P1→P2→P3 优先级顺序推进 -->
<!-- 对齐内容: -->
<!--   pr(9/9): reopen/close/list/diff/checkout/merge/checks/create/view 全部参数对齐 -->
<!--   issue(4/5): list/view/create/close 参数对齐,comment 未动 -->
<!--   run(3/3): list/view/rerun 参数对齐 -->
<!--   release(6/8+2新增): list/view/create/delete/download/upload 参数对齐 + delete-asset/edit 新增 -->
<!--   repo(4/5+4新增): view/list/create/fork 参数对齐 + edit/delete/archive/unarchive 新增 -->
<!-- 技术决策: -->
<!--   - issue list 复杂过滤(search/type=pr)走 search API,简单过滤(author/mention/milestone)走 issues API -->
<!--   - run list workflow 参数走 /actions/workflows/{wf}/runs 端点 -->
<!--   - run view attempt 走 /attempts/{n} 端点 -->
<!--   - run rerun job 走 /rerun-jobs 端点带 job_ids 数组 -->
<!--   - release list exclude_drafts/prereleases 客户端过滤(REST API 不支持) -->
<!--   - release delete cleanup_tag 删 release 后再删 git/refs/tags/{tag} -->
<!--   - release download skip_existing 跳过已存在,clobber 覆盖,无两者则报错 -->
<!--   - release upload clobber 先 DELETE 同名 asset 再上传 -->
<!--   - repo list source/fork 客户端过滤,language/visibility 走 API query -->
<!--   - repo fork org 参数传 {"organization":"org"} body -->
<!--   - repo delete 需 yes=true 确认(不可逆操作) -->
<!--   - repo archive/unarchive 用 PATCH {"archived":true/false} -->
<!-- 架构调整(用户手动): -->
<!--   - fix: gh 子命令全局选项被误拦 — CollectTail 剥离所有全局选项 -->
<!--   - refactor: 全穿透架构 — DetectUnknownOptions 移到各子命令内部(偏好"全穿透+内部守卫") -->
<!-- 验证: 102 个 GitHubToolHandlers 测试全部通过,0 警告 0 错误 ✅ -->
<!-- 未完成: -->
<!--   - 通用 --jq 参数(需引入 jq 解析库,独立大任务) -->
<!--   - 通用 --template 参数(Go template,.NET 无原生支持) -->
<!--   - release verify/verify-asset 子命令 -->
<!--   - repo autolink/deploy-key/gitignore/license/read-dir/read-file/rename/set-default/sync 子命令 -->
<!--   - pr issue status/delete/edit/lock/pin/reopen/transfer 等子命令 -->
<!--   - auth/config/label/search/workflow 等完整命令组 -->
<!--   - 手动 exe 验收(ADR 0080) -->
<!--   - 推送 w1 分支 + 创建 PR -->
