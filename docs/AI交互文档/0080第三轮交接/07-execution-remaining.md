# 交接文档 07: Execution 剩余工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（10个）

1. build_cancel - 取消构建
2. build_output - 获取构建输出
3. build_queue_status - 构建队列状态（已测试✅）
4. repl - REPL交互模式
5. shell_background_cancel - 取消后台任务
6. shell_background_get - 获取后台任务状态
7. shell_background_kill_all - 强制终止所有后台任务
8. shell_background_list - 后台任务列表（已测试✅）
9. shell_background_output - 获取后台任务输出
10. powershell_execution_policy - PowerShell执行策略

## 测试命令

### 1. build_cancel
```powershell
jcc mcp_schema build_cancel
jcc mcp_call build_cancel --% "{}"
```
预期：无构建时友好报错或返回"无构建可取消"

### 2. build_output
```powershell
jcc mcp_schema build_output
jcc mcp_call build_output --% "{\"start_line\":0,\"line_count\":10}"
```
预期：返回构建输出行

### 3. repl
```powershell
jcc mcp_schema repl
jcc mcp_call repl --% "{\"language\":\"csharp\",\"code\":\"1+1\"}"
```
预期：返回2

### 4. shell_background_get
```powershell
jcc mcp_schema shell_background_get
jcc mcp_call shell_background_get --% "{\"task_id\":\"nonexistent\"}"
```
预期：友好报错"任务不存在"

### 5. shell_background_kill_all
```powershell
jcc mcp_schema shell_background_kill_all
jcc mcp_call shell_background_kill_all --% "{}"
```
预期：终止所有后台任务（0个也成功）

### 6. powershell_execution_policy
```powershell
jcc mcp_schema powershell_execution_policy
jcc mcp_call powershell_execution_policy --% "{}"
```
预期：返回当前执行策略（需启用 powershell 工具）

## 验收标准

- 无构建/无任务时友好报错
- REPL 能执行简单表达式
- 后台任务管理不卡死
- 超过30s的工具必须备注
