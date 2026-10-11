# gh pr/issue edit 全参数验收报告

> 日期: 2026-10-08
> ADR: [0132](../adr/0132-jcc-build-deploy-and-gh-troubleshooting.md)
> 仓库: liuqihonggit/JoinCode

## 验收环境

- jcc 部署路径: `C:\Users\54076\bin\jcc.d\dev\jcc.dll`
- 编译模式: Debug + `--no-incremental` 全量重建
- Token: PAT (无 `read:project` scope)
- 测试 issue: #388 (子), #389 (父) — 验收后已关闭

## 验收结果

### REST API 参数

| 参数 | pr edit | issue edit | 验收方式 |
|------|---------|------------|----------|
| add_label | ✅ | ✅ | 实际调用 `--add_label test-label`，返回成功 |
| remove_label | ✅ | ✅ | 实际调用 `--remove_label test-label`，返回成功 |
| remove_milestone | ✅ | ✅ | 实际调用 `--remove_milestone`，返回成功 |
| milestone (by name) | ✅ 单元测试 | ✅ 单元测试 | 单元测试验证 name→ID 解析 + PATCH |
| body_file | ✅ 单元测试 | ✅ 单元测试 | 单元测试验证文件读取 + body 替换 |

### GraphQL 参数

| 参数 | 结果 | 验收方式 | 备注 |
|------|------|----------|------|
| add_sub_issue | ✅ | issue 42 → 388，返回成功 | |
| remove_sub_issue | ✅ | issue 42 ← 388，返回成功 | |
| add_blocked_by | ✅ | issue 42 blocked-by 388，返回成功 | |
| remove_blocked_by | ✅ | 移除 blocked-by 关系，返回成功 | |
| add_blocking | ✅ | issue 42 blocking 388（反向 addBlockedBy），返回成功 | |
| remove_blocking | ✅ | 移除 blocking 关系（反向 removeBlockedBy），返回成功 | |
| parent | ✅ | issue 388 → parent 389，GraphQL 查询确认 `parent.number: 389` | |
| remove_parent | ✅ | 移除 parent 关系，返回成功 | |
| add_project | ⚠️ 环境限制 | Token 无 `read:project` scope | 单元测试通过 |
| remove_project | ⚠️ 环境限制 | 同上 | 单元测试通过 |
| attach | ⚠️ 未验收 | 需要实际文件上传 | 单元测试通过 |
| type | ⚠️ 环境限制 | 仓库无 issue types 配置 | 单元测试通过 |
| remove_type | ⚠️ 环境限制 | 同上 | 单元测试通过 |

### 单元测试

- pr edit 新参数测试: 10 个全通过
- issue edit 新参数测试: 31 个全通过
- 总计: 41 个全通过

## 实现的 GraphQL mutations

| Mutation | 用途 | Input 字段 |
|----------|------|-----------|
| addProjectV2ItemById | 添加到项目 | projectId, contentId |
| deleteProjectV2Item | 从项目移除 | projectId, itemId |
| addSubIssue | 添加子 issue / 设置 parent | issueId, subIssueId, replaceParent |
| removeSubIssue | 移除子 issue / 移除 parent | issueId, subIssueId |
| addBlockedBy | 添加 blocked-by / blocking 关系 | issueId, blockingIssueId |
| removeBlockedBy | 移除 blocked-by / blocking 关系 | issueId, blockingIssueId |
| updateIssueIssueType | 设置/移除 issue 类型 | issueId, issueTypeId (null=移除) |

## 新增文件

- `kit/mcp/git_hub/GitHubToolHandlers.GraphQLEdit.cs` — 13 个 GraphQL 辅助方法
- `kit/mcp.tests/core/a_to_t/GitHubToolHandlersPrEditExtendedTests.cs` — pr edit 测试
- `kit/mcp.tests/core/a_to_t/GitHubToolHandlersIssueEditExtendedTests.cs` — issue edit 测试

## 新增接口方法

- `IGitHubApiClient.UploadAttachmentAsync` — 附件上传到 `github.com/user-attachments/assets`

## 环境限制说明

1. **Token scope 不足**: 当前 PAT 无 `read:project` scope，无法查/改 Projects v2。需运行 `gh auth refresh -s project` 添加 scope。
2. **仓库无 issue types**: `liuqihonggit/JoinCode` 仓库未配置 issue types，`type`/`remove_type` 参数无法验收。需在仓库设置中启用 issue types。
3. **attach 未验收**: 附件上传需要实际文件和写权限，单元测试已验证 mock 调用链路。
