# ADR 0080 第三轮：交接文档索引

> 按第三轮要求：每个文档只处理少量命令，每个文档让用户开一个AI窗口处理。
> 每次AI只手动执行一个命令测试，遇到任何不适都需要改。

## 交接文档清单

| 文档 | 分类 | 工具数 | 说明 |
|------|------|--------|------|
| [01-desktop-basic.md](01-desktop-basic.md) | desktop | 10 | 窗口/进程管理基础工具 |
| [02-desktop-interaction.md](02-desktop-interaction.md) | desktop | 12 | 鼠标/键盘/截图交互工具 |
| [03-desktop-advanced.md](03-desktop-advanced.md) | desktop | 10 | 宏/观察/优化高级工具 |
| [04-vision.md](04-vision.md) | vision | 13 | 图片分析/OCR/对比工具 |
| [05-notebook.md](05-notebook.md) | notebook | 10 | Jupyter笔记本工具 |
| [06-lsp.md](06-lsp.md) | lsp | 10 | LSP语言服务工具 |
| [07-execution-remaining.md](07-execution-remaining.md) | execution | 10 | 构建/REPL/后台Shell工具 |
| [08-github-remaining.md](08-github-remaining.md) | github | 20 | GitHub PR/Issue/Run工具 |
| [09-agent-remaining.md](09-agent-remaining.md) | agent | 12 | 子Agent管理工具 |
| [10-misc-remaining.md](10-misc-remaining.md) | 多分类 | 30 | plan/task/team/worktree等剩余工具 |

## 使用方法

1. 为每个文档开一个AI窗口
2. AI窗口中执行文档中的测试命令
3. 遇到任何不适（错误、格式不友好、超时等）都修复
4. 修复后继续下一个命令
5. 完成后汇总结果

## 测试命令模板

```powershell
# 查看工具 schema
jcc mcp_schema <tool_name>

# 调用工具（JSON 参数）
jcc mcp_call <tool_name> --% '{"param":"value"}'

# 调用工具（key=value 参数）
jcc mcp_call <tool_name> param=value
```

## 已完成测试

第一轮已测试约70个工具，发现并修复2个问题：
- FileWriter BOM 污染 (commit 724589d15)
- tool_score 格式化错误 (commit f1b0962f9)

详见 [手动测试问题清单-0080第一轮-续.md](手动测试问题清单-0080第一轮-续.md)
