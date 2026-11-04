# gh 命令测试进度跟踪

> 用途：记录已检查过的 gh 命令及结果，避免上下文压缩后重复检查
> 更新时间：2026-11-05

## 已完成的修复（本会话）

| # | 提交 | 修复内容 | 根因 |
|---|------|---------|------|
| 1 | 3e576fb24 | `gh api /users/xxx` 404 | Git Bash MSYS2 把 `/users/xxx` 转成 `C:/Program Files/Git/users/xxx`，添加 `UndoMsysPathConversion` 检测盘符前缀+API 顶级路径段还原 |
| 2 | 54e7d0d3a | `--jq '.a,.b'` 返回 null | `SimpleJqEvaluator` 不支持逗号分隔多表达式，添加 `SplitTopLevelComma` 顶层逗号分割 |
| 3 | 3f10c90d2 | `gh api /repos/xxx` 404 提示不友好 | `BuildApiErrorHint` 接收原始 path（带前导斜杠），条件不匹配，改为接收 cleanPath |
| 4 | 53a86a851 | `gh project list --owner` 报错 | 系统 gh CLI 用 `--owner`，jcc 原 `--org`，6 个 project handler 改名 |
| 5 | 2809776fd | `--json field --jq '.field'` 输出带引号 | `--json` 带值被误判为纯标志，`IsJsonFlagPresent` 检测 `--json` 后是否跟选项或末尾 |
| 6 | cf9a194eb | `gh gist create --desc`/`-d` 绑定失败 | ShortOptionMap 映射 `desc` 但 handler 参数是 `description`，改为 `('d',"description")` + LongAliasMap `desc`→`description` |
| 7 | cf9a194eb | `gh repo fork --remote-name` 绑定失败 | handler 参数是 `remote`，添加 LongAliasMap `remote-name`→`remote` (TakeNextToken) |
| 8 | 7a81ebf | 新增 `gh auth logout` 命令 | 从 hosts.yml 删除 host section（RemoveYamlHostSection） |
| 9 | 7a81ebf | 新增 `gh auth setup-git` 命令 | 配置 git credential helper 为 `!gh auth git-credential` |

## 已检查且正常工作的命令

### gh api（通用 API 调用）

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh api /users/liuqihonggit` | ✅ | 修复 #1 后正常 |
| `gh api users/liuqihonggit` | ✅ | |
| `gh api /user` | ✅ | |
| `gh api /repos/cli/cli` | ✅ | |
| `gh api /repos/cli/cli --jq '.full_name'` | ✅ | |
| `gh api /repos/cli/cli --jq '.stargazers_count'` | ✅ | |
| `gh api /repos/cli/cli --jq '.full_name,.stargazers_count'` | ✅ | 修复 #2 后正常 |
| `gh api /repos/cli/cli --jq '{name: .name, stars: .stargazers_count, lang: .language}'` | ✅ | |
| `gh api /repos/cli/cli --fields per_page=3` | ✅ | |
| `gh api /repos/cli/cli/languages` | ✅ | |
| `gh api /repos/cli/cli/tags --jq '.[].name' --max_lines 3` | ✅ | |
| `gh api /repos/cli/cli/contributors --jq '.[].login' --max_lines 3` | ✅ | |
| `gh api /repos/cli/cli/branches --jq '.[].name' --max_lines 3` | ✅ | |
| `gh api /repos/cli/cli/commits --jq '.[].commit.message' --max_lines 3` | ✅ | |
| `gh api /repos/cli/cli/contents/README.md --jq '.content'` | ✅ | |
| `gh api /repos/cli/cli/contents/README.md --jq '.name,.path,.size'` | ✅ | |
| `gh api /repos/cli/cli/pulls/1 --jq '{title: .title, state: .state, user: .user.login}'` | ✅ | |
| `gh api /repos/cli/cli/pulls/1 --jq 'select(.state == "closed") \| .title'` | ✅ | |
| `gh api /repos/cli/cli/pulls/1 --jq '.labels \| length'` | ✅ | 返回 0（PR 无标签） |
| `gh api /repos/cli/cli/topics` | ✅ | |
| `gh api /repos/nonexistent/repo` | ✅ | 修复 #3 后 404 提示友好 |
| `gh api graphql --fields 'query=query { viewer { login } }'` | ✅ | |
| `gh api /orgs/microsoft/repos --jq '.[].full_name' --max_lines 3` | ✅ | |

### gh search（搜索）

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh search code "TODO" --limit 3` | ✅ | |
| `gh search code "func main" --language go --limit 3` | ✅ | |
| `gh search repos "language:csharp stars:>1000" --limit 3` | ✅ | |
| `gh search repos "stars:>10000 language:go" --sort stars --limit 3` | ✅ | |
| `gh search repos "stars:>10000" --sort stars --order desc --limit 3` | ✅ | |
| `gh search repos "stars:>10000 language:go" --json full_name,stargazers_count --limit 3` | ✅ | |
| `gh search repos "stars:>10000 language:go" --json full_name --jq '.[].full_name' --limit 3` | ✅ | |
| `gh search issues "repo:cli/cli is:open label:bug" --limit 3` | ✅ | |
| `gh search prs "repo:cli/cli is:open" --limit 3` | ✅ | |
| `gh search commits "repo:cli/cli fix" --limit 3` | ✅ | |
| `gh search users` | ✅ | 新增命令 |

### gh repo（仓库）

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh repo view cli/cli` | ✅ | |
| `gh repo view https://github.com/cli/cli` | ✅ | |
| `gh repo view cli/cli --json` | ✅ | 纯 --json 标志 |
| `gh repo view cli/cli --json full_name` | ✅ | |
| `gh repo view cli/cli --json full_name --jq '.full_name'` | ✅ | 修复 #5 后正常 |
| `gh repo view cli/cli --json full_name,stargazers_count,language` | ✅ | |
| `gh repo view cli/cli --json full_name,stargazers_count --jq '.full_name,.stargazers_count'` | ✅ | |
| `gh repo view cli/cli --json --jq '.full_name'` | ✅ | 带引号是 JSON 语义 |
| `gh repo view nonexistent/repo` | ✅ | 404 提示友好 |
| `gh repo gitignore list --limit 3` | ✅ | 修复后支持 --limit |
| `gh repo license list --limit 3` | ✅ | 修复后支持 --limit |

### gh issue（Issue）

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh issue list --repo cli/cli --limit 3` | ✅ | |
| `gh issue list --repo cli/cli --state closed --limit 3` | ✅ | |
| `gh issue list --repo cli/cli --assignee mislav --limit 3` | ✅ | 空结果 |
| `gh issue list --repo cli/cli --search "bug" --limit 3` | ✅ | |
| `gh issue list --repo cli/cli --json number,title --limit 3` | ✅ | |
| `gh issue view 1 --repo cli/cli` | ✅ | |
| `gh issue view 999999 --repo cli/cli` | ✅ | 404 提示友好 |
| `gh issue status --repo cli/cli` | ✅ | |

### gh pr（Pull Request）

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh pr list --repo cli/cli --limit 3` | ✅ | |
| `gh pr list --repo cli/cli --state closed --limit 3` | ✅ | |
| `gh pr list --repo cli/cli --author mislav --limit 3` | ✅ | 空结果 |
| `gh pr list --repo cli/cli --search "fix" --limit 3` | ✅ | |
| `gh pr list --repo cli/cli --json number,title --limit 3` | ✅ | |
| `gh pr view 1 --repo cli/cli` | ✅ | |
| `gh pr diff 1 --repo cli/cli` | ✅ | |
| `gh pr status --repo cli/cli` | ✅ | |

### gh run / workflow（CI/CD）

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh run list --repo cli/cli --limit 3` | ✅ | |
| `gh run list --repo cli/cli --status success --limit 3` | ✅ | |
| `gh run list --repo cli/cli --event push --limit 3` | ✅ | |
| `gh run view 41889947837 --repo cli/cli` | ✅ | |
| `gh workflow list --repo cli/cli --limit 3` | ✅ | |
| `gh workflow view 25016 --repo cli/cli` | ✅ | |

### gh release / label / branch

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh release list --repo cli/cli --limit 3` | ✅ | |
| `gh release view v2.103.3 --repo cli/cli` | ✅ | |
| `gh release view v2.103.3 --repo cli/cli --json` | ✅ | |
| `gh label list --repo cli/cli --limit 3` | ✅ | |
| `gh branch list --repo cli/cli --limit 3` | ✅ | |

### gh gist / auth / config / ssh-key

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh gist list --limit 3` | ✅ | |
| `gh gist view 617c9f1ac9690a8732bb89d068ce1836` | ✅ | |
| `gh auth status` | ✅ | |
| `gh auth token` | ✅ | 返回测试值 |
| `gh config list` | ✅ | |
| `gh ssh-key list` | ✅ | |

### 其他命令

| 命令 | 结果 | 备注 |
|------|------|------|
| `gh extension list` | ✅ | |
| `gh alias list` | ✅ | |
| `gh status` | ✅ | |
| `gh browse --repo cli/cli` | ✅ | |
| `gh org list` | ✅ | 空结果 |
| `gh milestone list --repo cli/cli --limit 3` | ✅ | 空结果 |
| `gh notification list --limit 3` | ✅ | 空结果 |
| `gh collaborator list --repo cli/cli` | ✅ | 权限不足 |
| `gh ruleset list --repo cli/cli --limit 3` | ✅ | |
| `gh cache list --repo cli/cli --limit 3` | ✅ | |
| `gh discussion list --repo cli/cli --limit 3` | ✅ | |
| `gh project list` | ✅ | 权限不足 |
| `gh project list --owner2owner liuqihonggit` | ✅ | 修复 #4 后正常 |
| `gh team list --org microsoft` | ✅ | 权限不足 |
| `gh secret list --repo cli/cli` | ✅ | 权限不足 |
| `gh variable list --repo cli/cli` | ✅ | 权限不足 |
| `gh codespace list` | ✅ | 权限不足 |
| `gh gpg-key list` | ✅ | 权限不足 |

## 已确认缺失的命令

| 命令 | 备注 |
|------|------|
| `gh search labels` | 系统 gh CLI 也不支持 |

## 待测试的命令（未检查）

### 写操作（需小心不要真的创建资源）
- `gh repo edit` — 编辑仓库设置
- `gh repo create` — 创建仓库
- `gh repo delete` — 删除仓库
- `gh repo archive` — 归档仓库
- `gh repo unarchive` — 取消归档
- `gh repo rename` — 重命名仓库
- `gh repo fork` — Fork 仓库
- `gh repo sync` — 同步 fork
- `gh repo clone` — 克隆仓库
- `gh issue create` — 创建 Issue
- `gh issue close` — 关闭 Issue
- `gh issue reopen` — 重新打开 Issue
- `gh issue edit` — 编辑 Issue
- `gh issue delete` — 删除 Issue
- `gh issue comment` — 评论 Issue
- `gh issue transfer` — 转移 Issue
- `gh issue lock/unlock` — 锁定/解锁
- `gh issue pin/unpin` — 置顶/取消置顶
- `gh pr create` — 创建 PR
- `gh pr close` — 关闭 PR
- `gh pr reopen` — 重新打开 PR
- `gh pr edit` — 编辑 PR
- `gh pr merge` — 合并 PR
- `gh pr review` — 审查 PR
- `gh pr checkout` — 检出 PR
- `gh pr ready` — 标记就绪
- `gh pr revert` — 回退 PR
- `gh pr lock/unlock` — 锁定/解锁
- `gh pr update-branch` — 更新分支
- `gh label create/delete/edit` — 标签 CRUD
- `gh release create/delete/edit/download` — Release CRUD
- `gh gist create/delete/edit/rename` — Gist CRUD
- `gh run rerun/cancel/view --log` — Run 操作
- `gh workflow run/enable/disable` — Workflow 操作
- `gh branch create/delete/sync-protection` — Branch 操作
- `gh secret set/delete` — Secret CRUD
- `gh variable set/delete` — Variable CRUD
- `gh ssh-key add/delete` — SSH Key CRUD
- `gh gpg-key add/delete` — GPG Key CRUD
- `gh repo autolink-create/delete` — Autolink CRUD
- `gh repo deploy-key-add/delete` — Deploy Key CRUD
- `gh project create/delete/edit/close` — Project CRUD
- `gh discussion create` — 创建 Discussion

### 未测试的只读命令
- `gh repo set-default` — 设置默认仓库
- `gh repo set-default-branch` — 设置默认分支
- `gh issue develop` — 开发分支
- `gh pr checks` — PR CI 检查
- `gh run view --log` — Run 日志
- `gh run view --log --filter error` — Run 错误日志
- `gh release download` — 下载 Release
- `gh config get/set` — 配置读写
- `gh alias set/delete` — 别名 CRUD
- `gh extension install/remove/upgrade` — 扩展管理

## 测试统计

- 已测试命令：~80 个
- 正常工作：~77 个
- 已修复：5 个
- 缺失命令：2 个（auth logout, auth setup-git）
- 待测试：~60 个（主要是写操作）
