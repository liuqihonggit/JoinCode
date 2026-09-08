# 交接文档 10: 多分类剩余工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（按分类）

### plan 分类（剩余8个）
1. add_plan_step
2. approve_plan_step
3. enter_plan_mode
4. execute_plan_steps
5. exit_plan_mode
6. get_plan_history
7. modify_plan_step
8. reject_plan_step
9. remove_plan_step

### task 分类（剩余10个）
10. task_can_execute
11. task_create（已测试✅）
12. task_get
13. task_get_dependencies
14. task_list（已测试✅）
15. task_list_running
16. task_output
17. task_remove_dependency
18. task_set_dependency
19. task_stop
20. task_stop_batch
21. task_update

### team 分类（剩余8个）
22. team_add_member
23. team_broadcast
24. team_create
25. team_delete
26. team_get
27. team_get_messages
28. team_list（已测试✅）
29. team_remove_member
30. team_send_direct_message
31. team_send_message

### worktree 分类（剩余7个）
32. worktree_cleanup
33. worktree_create
34. worktree_find_git
35. worktree_list（已测试✅）
36. worktree_list_all
37. worktree_merge
38. worktree_remove
39. worktree_status

### memory 分类（剩余11个）
40. memory_add_team_path
41. memory_age
42. memory_cleanup
43. memory_daily_log_append
44. memory_daily_log_get
45. memory_health（已测试✅）
46. memory_list_team_paths
47. memory_remove_team_path
48. memory_scan（已测试✅）
49. memory_scan_team
50. memory_search_history
51. memory_team_status
52. memory_team_sync

## 测试命令示例

### plan 分类
```powershell
jcc mcp_call enter_plan_mode --% "{}"
jcc mcp_call get_plan_status --% "{}"
jcc mcp_call add_plan_step --% "{\"description\":\"测试步骤\",\"tool_name\":\"read\"}"
jcc mcp_call get_plan_history --% "{}"
jcc mcp_call exit_plan_mode --% "{}"
```

### task 分类
```powershell
jcc mcp_call task_create --% "{\"title\":\"test\",\"description\":\"测试\"}"
jcc mcp_call task_list --% "{}"
jcc mcp_call task_get --% "{\"task_id\":\"task-0001\"}"
jcc mcp_call task_list_running --% "{}"
jcc mcp_call task_stop --% "{\"task_id\":\"nonexistent\"}"
```

### team 分类
```powershell
jcc mcp_call team_list --% "{}"
jcc mcp_call team_get --% "{\"team_id\":\"nonexistent\"}"
jcc mcp_call team_create --% "{\"name\":\"test-team\"}"
jcc mcp_call team_send_message --% "{\"team_id\":\"nonexistent\",\"message\":\"hello\"}"
```

### worktree 分类
```powershell
jcc mcp_call worktree_list --% "{}"
jcc mcp_call worktree_find_git --% "{\"path\":\"D:\\project\\w2\"}"
jcc mcp_call worktree_status --% "{\"agent_id\":\"nonexistent\"}"
jcc mcp_call worktree_cleanup --% "{}"
```

### memory 分类
```powershell
jcc mcp_call memory_health --% "{}"
jcc mcp_call memory_scan --% "{\"query\":\"test\",\"limit\":5}"
jcc mcp_call memory_age --% "{}"
jcc mcp_call memory_list_team_paths --% "{}"
jcc mcp_call memory_team_status --% "{}"
jcc mcp_call memory_daily_log_get --% "{}"
```

## 验收标准

- 不存在的ID友好报错
- plan mode 进出正确
- task/team/worktree CRUD 完整
- memory 查询不卡死
- 超过30s的工具必须备注
