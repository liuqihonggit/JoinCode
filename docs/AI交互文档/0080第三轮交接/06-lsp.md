# 交接文档 06: LSP 工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。
> 前置：需安装 OmniSharp（`dotnet tool install -g OmniSharp`）

## 工具列表（10个）

1. lsp_hover - 悬停查看符号信息
2. lsp_definition - 跳转定义
3. lsp_references - 查找引用
4. lsp_rename - 重命名符号
5. lsp_format - 格式化代码
6. lsp_diagnostic - 获取诊断信息
7. lsp_completion - 代码补全
8. lsp_signature - 签名帮助
9. lsp_code_action - 代码操作
10. lsp_document_symbol - 文档符号

## 测试命令

### 1. lsp_hover
```powershell
jcc mcp_schema lsp_hover
jcc mcp_call lsp_hover --% "{\"file_path\":\"D:\\project\\w2\\infrastructure\\Infrastructure\\IO\\Services\\FileOps\\FileWriter.cs\",\"line\":7,\"character\":22}"
```
预期：返回 FileWriter 类的 hover 信息

### 2. lsp_definition
```powershell
jcc mcp_schema lsp_definition
jcc mcp_call lsp_definition --% "{\"file_path\":\"D:\\project\\w2\\infrastructure\\Infrastructure\\IO\\Services\\FileOps\\FileWriter.cs\",\"line\":7,\"character\":22}"
```
预期：返回定义位置

### 3. lsp_references
```powershell
jcc mcp_schema lsp_references
jcc mcp_call lsp_references --% "{\"file_path\":\"D:\\project\\w2\\infrastructure\\Infrastructure\\IO\\Services\\FileOps\\FileWriter.cs\",\"line\":7,\"character\":22}"
```
预期：返回所有引用位置

### 4. lsp_diagnostic
```powershell
jcc mcp_schema lsp_diagnostic
jcc mcp_call lsp_diagnostic --% "{\"file_path\":\"D:\\project\\w2\\infrastructure\\Infrastructure\\IO\\Services\\FileOps\\FileWriter.cs\"}"
```
预期：返回诊断信息（警告/错误）

### 5. lsp_document_symbol
```powershell
jcc mcp_schema lsp_document_symbol
jcc mcp_call lsp_document_symbol --% "{\"file_path\":\"D:\\project\\w2\\infrastructure\\Infrastructure\\IO\\Services\\FileOps\\FileWriter.cs\"}"
```
预期：返回文档中所有符号

### 其余工具依此类推

## 验收标准

- OmniSharp 未安装时友好报错（已验证）
- 符号不存在时友好报错
- 诊断信息格式统一
- 超过30s的工具必须备注
