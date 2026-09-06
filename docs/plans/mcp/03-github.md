# MCP 工具测试计划 03: GitHub

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 03/12 |
| 涵盖分类 | GitHub |
| 工具总数 | 29 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译,gh CLI 已认证 |

## 工具清单

### Category: GitHub (29 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `gh_api` | 通用 GitHub REST API | ⬜ |
| 2 | `gh_issue_list` | 列出 Issue | ⬜ |
| 3 | `gh_issue_view` | 查看 Issue | ⬜ |
| 4 | `gh_issue_create` | 创建 Issue | ⬜ |
| 5 | `gh_issue_close` | 关闭 Issue | ⬜ |
| 6 | `gh_issue_comment` | Issue 评论 | ⬜ |
| 7 | `gh_pr_view` | 查看 PR | ⬜ |
| 8 | `gh_pr_list` | 列出 PR | ⬜ |
| 9 | `gh_pr_diff` | PR Diff | ⬜ |
| 10 | `gh_pr_checks` | PR Checks | ⬜ |
| 11 | `gh_pr_merge` | 合并 PR | ⬜ |
| 12 | `gh_pr_checkout` | Checkout PR | ⬜ |
| 13 | `gh_pr_close` | 关闭 PR | ⬜ |
| 14 | `gh_pr_reopen` | 重开 PR | ⬜ |
| 15 | `gh_release_list` | 列出 Release | ⬜ |
| 16 | `gh_release_view` | 查看 Release | ⬜ |
| 17 | `gh_release_create` | 创建 Release | ⬜ |
| 18 | `gh_release_download` | 下载 Release | ⬜ |
| 19 | `gh_release_upload` | 上传 Release | ⬜ |
| 20 | `gh_release_delete` | 删除 Release | ⬜ |
| 21 | `gh_repo_view` | 查看仓库 | ⬜ |
| 22 | `gh_repo_clone` | 克隆仓库 | ⬜ |
| 23 | `gh_repo_create` | 创建仓库 | ⬜ |
| 24 | `gh_repo_fork` | Fork 仓库 | ⬜ |
| 25 | `gh_repo_list` | 列出仓库 | ⬜ |
| 26 | `gh_run_list` | 列出 CI Run | ⬜ |
| 27 | `gh_run_view` | 查看 CI Run | ⬜ |
| 28 | `gh_run_rerun` | 重跑 CI | ⬜ |
| 29 | `gh_run_cancel` | 取消 CI | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$tools = @(
    "gh_api", "gh_issue_list", "gh_issue_view", "gh_issue_create", "gh_issue_close",
    "gh_issue_comment", "gh_pr_view", "gh_pr_list", "gh_pr_diff", "gh_pr_checks",
    "gh_pr_merge", "gh_pr_checkout", "gh_pr_close", "gh_pr_reopen",
    "gh_release_list", "gh_release_view", "gh_release_create", "gh_release_download",
    "gh_release_upload", "gh_release_delete", "gh_repo_view", "gh_repo_clone",
    "gh_repo_create", "gh_repo_fork", "gh_repo_list", "gh_run_list", "gh_run_view",
    "gh_run_rerun", "gh_run_cancel"
)
foreach ($t in $tools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
# 只读工具,安全测试
& $jcc --trust --bypass mcp_call gh_repo_list
& $jcc --trust --bypass mcp_call gh_pr_list --args-file .\gh_pr_list_args.json
& $jcc --trust --bypass mcp_call gh_run_list
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `gh_api` 不使用 `--jq`(输出完整 JSON)
- [ ] CI 日志工具不超时(用重定向写文件,不用管道)
- [ ] 未认证时给出明确提示而非崩溃

## 风险提示

- **写操作工具**(create/close/merge/delete)可能修改真实 GitHub 数据,测试时用 `--dry-run` 或测试仓库
- `gh_run_view --log` 日志量大可能超时,必须用重定向写文件
- `gh_pr_merge` 可能触发 auto-merge,注意测试仓库选择
- `gh_repo_clone` 可能克隆大仓库,注意磁盘空间

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
