# 交接文档 06: LSP 工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。
> 前置：需安装 csharp-ls（`dotnet tool install -g csharp-ls`，版本 0.27.0+，支持 .sln + .slnx）

## 工具列表（10个）

1. lsp_hover - 悬停查看符号信息
2. lsp_goto_definition - 跳转定义
3. lsp_find_references - 查找引用
4. lsp_goto_implementation - 跳转实现
5. lsp_completion - 代码补全
6. lsp_document_symbols - 文档符号
7. lsp_prepare_call_hierarchy - 准备调用层次
8. lsp_incoming_calls - 传入调用
9. lsp_outgoing_calls - 传出调用
10. lsp_workspace_symbol - 工作区符号搜索（支持显式 server_name + workspace_path）

## 测试命令

> 注意：JSON 参数键名用 snake_case（如 `file_path`、`server_name`、`workspace_path`）
> 路径用正斜杠 `/` 避免反斜杠转义问题
> 参数含复杂引号时用 `--args-file` 方式传递

### 1. lsp_hover
```powershell
jcc mcp_schema lsp_hover
jcc mcp_call lsp_hover --% "{\"file_path\":\"D:/project/w3/foundation/Abstractions/05-memory/FileIO/GitWorkspaceResolver.cs\",\"line\":22,\"character\":30}"
```
预期：返回 FindGitRootAsync 方法签名

### 2. lsp_goto_definition
```powershell
jcc mcp_schema lsp_goto_definition
jcc mcp_call lsp_goto_definition --% "{\"file_path\":\"D:/project/w3/foundation/Abstractions/05-memory/FileIO/GitWorkspaceResolver.cs\",\"line\":22,\"character\":30}"
```
预期：返回定义位置

### 3. lsp_find_references
```powershell
jcc mcp_schema lsp_find_references
jcc mcp_call lsp_find_references --% "{\"file_path\":\"D:/project/w3/foundation/Abstractions/05-memory/FileIO/GitWorkspaceResolver.cs\",\"line\":22,\"character\":30}"
```
预期：返回所有引用位置

### 4. lsp_document_symbols
```powershell
jcc mcp_schema lsp_document_symbols
jcc mcp_call lsp_document_symbols --% "{\"file_path\":\"D:/project/w3/foundation/Abstractions/05-memory/FileIO/GitWorkspaceResolver.cs\"}"
```
预期：返回文档中所有符号

### 10. lsp_workspace_symbol
```powershell
jcc mcp_schema lsp_workspace_symbol
# 显式指定服务器名（推荐，方便 debug）
jcc mcp_call lsp_workspace_symbol --% "{\"query\":\"Program\",\"workspace_path\":\"D:/project/w3/lsp-test/Program.cs\",\"server_name\":\"csharp-ls\"}"
# 动态寻址（不指定 server_name，按文件扩展名自动匹配）
jcc mcp_call lsp_workspace_symbol --% "{\"query\":\"Calculator\",\"workspace_path\":\"D:/project/w3/lsp-test/Program.cs\"}"
```
预期：返回工作区中匹配的符号列表

### 其余工具依此类推

## lsp_workspace_symbol 参数说明

| 参数 | 类型 | 说明 |
|------|------|------|
| query | string | 搜索查询（必填） |
| workspace_path | string? | 工作区路径（文件或目录），用于启动 LSP 服务器。为 null 时用当前工作目录 |
| server_name | string? | 显式指定 LSP 服务器名称（如 "csharp-ls"）。为 null 时动态寻址 |

**服务器选择策略**（显式配置优先，动态寻址兜底）：
1. 有 `server_name` → 按名称查找服务器，找不到抛异常列出所有可用服务器
2. 有 `workspace_path` 且是文件 → 用 `EnsureServerStartedAsync` 按扩展名自动匹配
3. fallback → 找第一个已运行的服务器，或第一个配置的服务器

## 验收标准

- csharp-ls 未安装时友好报错（已验证）
- server_name 不存在时友好报错并列出可用服务器（已验证）
- 符号不存在时友好报错
- 诊断信息格式统一
- 超过30s的工具必须备注

## 已完成测试结果（10/10 通过）

| 工具名 | 结果 | 备注 |
|--------|------|------|
| lsp_hover | ✅ | 返回方法签名 |
| lsp_goto_definition | ✅ | 跳转到定义位置 |
| lsp_find_references | ✅ | 找到所有引用 |
| lsp_goto_implementation | ✅ | 跳转到实现位置 |
| lsp_completion | ✅ | 返回补全建议 |
| lsp_document_symbols | ✅ | 返回文档符号列表 |
| lsp_prepare_call_hierarchy | ✅ | 返回调用层次项 |
| lsp_incoming_calls | ✅ | 返回传入调用 |
| lsp_outgoing_calls | ✅ | 返回传出调用 |
| lsp_workspace_symbol | ✅ | 6 种场景全通过（.sln/.slnx/worktree/共存/动态寻址/错误提示） |
