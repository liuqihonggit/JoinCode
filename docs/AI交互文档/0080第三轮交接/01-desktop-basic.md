# 交接文档 01: Desktop 基础工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（10个）

1. list_windows - 枚举所有可见顶层窗口
2. focus_window - 按标题或进程名激活窗口到前台
3. close_window - 关闭指定窗口
4. list_processes - 枚举运行中进程
5. kill_process - 按PID或名称结束进程
6. start_process - 启动新进程
7. move_window - 移动并调整窗口大小
8. get_environment_state - 获取桌面环境状态
9. get_operation_history - 获取操作历史
10. undo_last_action - 撤销上一步操作

## 测试命令

### 1. list_windows
```powershell
jcc mcp_schema list_windows
jcc mcp_call list_windows --% "{}"
```
预期：列出当前所有可见窗口

### 2. focus_window
```powershell
jcc mcp_schema focus_window
jcc mcp_call focus_window --% "{\"title\":\"记事本\"}"
```
预期：激活记事本窗口（如不存在则友好报错）

### 3. close_window
```powershell
jcc mcp_schema close_window
jcc mcp_call close_window --% "{\"title\":\"不存在的窗口\"}"
```
预期：友好报错"窗口不存在"

### 4. list_processes
```powershell
jcc mcp_schema list_processes
jcc mcp_call list_processes --% "{\"name_filter\":\"dotnet\"}"
```
预期：列出 dotnet 相关进程

### 5. kill_process
```powershell
jcc mcp_schema kill_process
jcc mcp_call kill_process --% "{\"name\":\"不存在的进程\"}"
```
预期：友好报错"进程不存在"

### 6. start_process
```powershell
jcc mcp_schema start_process
jcc mcp_call start_process --% "{\"command\":\"notepad\",\"args\":\"\"}"
```
预期：启动记事本，返回PID

### 7. move_window
```powershell
jcc mcp_schema move_window
jcc mcp_call move_window --% "{\"title\":\"记事本\",\"x\":100,\"y\":100,\"width\":800,\"height\":600}"
```
预期：移动记事本窗口

### 8. get_environment_state
```powershell
jcc mcp_schema get_environment_state
jcc mcp_call get_environment_state --% "{}"
```
预期：返回光标状态和弹窗检测

### 9. get_operation_history
```powershell
jcc mcp_schema get_operation_history
jcc mcp_call get_operation_history --% "{\"count\":10}"
```
预期：返回最近10步操作历史

### 10. undo_last_action
```powershell
jcc mcp_schema undo_last_action
jcc mcp_call undo_last_action --% "{}"
```
预期：撤销上一步操作或友好报错"无操作可撤销"

## 验收标准

- 每个命令都能执行（不崩溃）
- 错误信息友好（不裸抛异常）
- 输出格式统一（结构化表格或JSON）
- 超过30s的工具必须备注
