# 交接文档 08: GitHub 剩余工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。
> 已测试：gh_pr_list, gh_repo_list, gh_run_list, gh_issue_list ✅

## 工具列表（20个）

### PR 相关
1. gh_pr_view - 查看PR详情
2. gh_pr_checks - PR CI检查状态
3. gh_pr_create - 创建PR
4. gh_pr_merge - 合并PR
5. gh_pr_close - 关闭PR
6. gh_pr_update - 更新PR

### Issue 相关
7. gh_issue_view - 查看Issue详情
8. gh_issue_create - 创建Issue（已测试✅）
9. gh_issue_close - 关闭Issue
10. gh_issue_comment - 评论Issue
11. gh_issue_list - Issue列表（已测试✅）
12. gh_issue_update - 更新Issue

### Run/Workflow 相关
13. gh_run_view - 查看CI运行详情
14. gh_run_rerun - 重跑CI
15. gh_run_cancel - 取消CI运行
16. gh_workflow_list - 列出Workflows

### Repo/Release 相关
17. gh_repo_view - 查看仓库详情
18. gh_repo_list - 仓库列表（已测试✅）
19. gh_release_list - 列出Release
20. gh_api - 通用REST API调用

## 测试命令

### 1. gh_pr_view
```powershell
jcc mcp_schema gh_pr_view
jcc mcp_call gh_pr_view --% "{\"pr_number\":1}"
```
预期：返回PR详情或友好报错"PR不存在"

### 2. gh_pr_checks
```powershell
jcc mcp_schema gh_pr_checks
jcc mcp_call gh_pr_checks --% "{\"pr_number\":1}"
```
预期：返回CI检查状态

### 3. gh_issue_view
```powershell
jcc mcp_schema gh_issue_view
jcc mcp_call gh_issue_view --% "{\"issue_number\":1}"
```
预期：返回Issue详情

### 4. gh_run_view
```powershell
jcc mcp_schema gh_run_view
jcc mcp_call gh_run_view --% "{\"run_id\":1}"
```
预期：返回CI运行详情

### 5. gh_workflow_list
```powershell
jcc mcp_schema gh_workflow_list
jcc mcp_call gh_workflow_list --% "{}"
```
预期：列出仓库Workflows

### 6. gh_release_list
```powershell
jcc mcp_schema gh_release_list
jcc mcp_call gh_release_list --% "{}"
```
预期：列出Releases

### 7. gh_api
```powershell
jcc mcp_schema gh_api
jcc mcp_call gh_api --% "{\"method\":\"GET\",\"endpoint\":\"/repos/octocat/Hello-World\"}"
```
预期：返回仓库信息

### 其余工具依此类推

## 验收标准

- 不存在的PR/Issue/Run友好报错
- JSON输出精简（已修复✅）
- API调用返回结构化数据
- 超过30s的工具必须备注
