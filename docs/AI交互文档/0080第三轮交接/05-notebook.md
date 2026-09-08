# 交接文档 05: Notebook 工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（10个）

1. notebook_read - 读取Jupyter笔记本
2. notebook_create - 创建新笔记本
3. notebook_edit - 编辑笔记本cell
4. notebook_execute - 执行笔记本cell
5. notebook_add_cell - 添加cell
6. notebook_delete_cell - 删除cell
7. notebook_move_cell - 移动cell
8. notebook_get_output - 获取cell输出
9. notebook_clear_output - 清除cell输出
10. notebook_save - 保存笔记本

## 测试命令

### 准备测试笔记本
```powershell
# 创建一个简单的测试笔记本文件
jcc mcp_call notebook_create --% "{\"path\":\"test-notebook.ipynb\"}"
```

### 1. notebook_read
```powershell
jcc mcp_schema notebook_read
jcc mcp_call notebook_read --% "{\"notebook_path\":\"test-notebook.ipynb\"}"
```
预期：返回笔记本内容

### 2. notebook_create
```powershell
jcc mcp_schema notebook_create
jcc mcp_call notebook_create --% "{\"path\":\"test-notebook.ipynb\"}"
```
预期：创建空笔记本

### 3. notebook_add_cell
```powershell
jcc mcp_schema notebook_add_cell
jcc mcp_call notebook_add_cell --% "{\"notebook_path\":\"test-notebook.ipynb\",\"cell_type\":\"code\",\"source\":\"print(1+1)\"}"
```
预期：添加一个code cell

### 4. notebook_read (验证)
```powershell
jcc mcp_call notebook_read --% "{\"notebook_path\":\"test-notebook.ipynb\"}"
```
预期：包含刚添加的cell

### 5. notebook_execute
```powershell
jcc mcp_schema notebook_execute
jcc mcp_call notebook_execute --% "{\"notebook_path\":\"test-notebook.ipynb\",\"cell_index\":0}"
```
预期：执行cell并返回输出

### 其余工具依此类推

## 验收标准

- 不存在的笔记本友好报错
- cell 索引越界友好报错
- 执行结果正确返回
- 超过30s的工具必须备注
