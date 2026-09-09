# 交接文档 10: 多分类剩余工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（按分类）

### plan 分类（剩余8个）
1. add_plan_step（已测试✅）
2. approve_plan_step（已测试✅）
3. enter_plan_mode（已测试✅）
4. execute_plan_steps（已测试✅）
5. exit_plan_mode（已测试✅）
6. get_plan_history（已测试✅）
7. modify_plan_step（已测试✅）
8. reject_plan_step（已测试✅）
9. remove_plan_step（已测试✅）

### task 分类（剩余10个）
10. task_can_execute（已测试✅）
11. task_create（已测试✅）
12. task_get（已测试✅）
13. task_get_dependencies（已测试✅）
14. task_list（已测试✅）
15. task_list_running（已测试✅）
16. task_output（已测试✅）
17. task_remove_dependency（已测试✅）
18. task_set_dependency（已测试✅）
19. task_stop（已测试✅）
20. task_stop_batch（已测试✅）
21. task_update（已测试✅）

### team 分类（剩余8个）
22. team_add_member（已测试✅）
23. team_broadcast（已测试✅）
24. team_create（已测试✅）
25. team_delete（已测试✅）
26. team_get（已测试✅）
27. team_get_messages（已测试✅）
28. team_list（已测试✅）
29. team_remove_member（已测试✅）
30. team_send_direct_message（已测试✅）
31. team_send_message（已测试✅）

### worktree 分类（剩余7个）
32. worktree_cleanup（已测试✅）
33. worktree_create（已测试✅）
34. worktree_find_git（已测试✅）
35. worktree_list（已测试✅）
36. worktree_list_all（已测试✅）
37. worktree_merge（已测试✅，已修复路径校验bug）
38. worktree_remove（已测试✅）
39. worktree_status（已测试✅）

### memory 分类（剩余11个）
40. memory_add_team_path（已测试✅）
41. memory_age（已测试✅）
42. memory_cleanup（已测试✅）
43. memory_daily_log_append（已测试✅）
44. memory_daily_log_get（已测试✅）
45. memory_health（已测试✅）
46. memory_list_team_paths（已测试✅）
47. memory_remove_team_path（已测试✅）
48. memory_scan（已测试✅）
49. memory_scan_team（已测试✅）
50. memory_search_history（已测试✅）
51. memory_team_status（已测试✅）
52. memory_team_sync（已测试✅）

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
