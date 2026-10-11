# MCP 工具测试计划索引

> 第二轮:脚本批量验证 — 快速定位坏点
> 总计 12 个计划,覆盖 375+ 个 MCP 工具方法,55 个 ToolCategory

## 计划列表

| 编号 | 文件 | 标题 | 分类 | 工具数 |
|------|------|------|------|--------|
| MCP001 | [MCP001-desktop-control.md](MCP001-desktop-control.md) | 桌面控制 | DesktopControl | 32 |
| MCP002 | [MCP002-code-index-graph.md](MCP002-code-index-graph.md) | 代码索引与图 | CodeIndex + Graph | 36 |
| MCP003 | [MCP003-github.md](MCP003-github.md) | GitHub | GitHub | 29 |
| MCP004 | [MCP004-analytics-agent.md](MCP004-analytics-agent.md) | 分析与智能体 | Analytics + Agent | 29 |
| MCP005 | [MCP005-skill-memory.md](MCP005-skill-memory.md) | 技能与记忆 | Skill + Memory | 27 |
| MCP006 | [MCP006-task-vision.md](MCP006-task-vision.md) | 任务与视觉 | Task + Vision | 25 |
| MCP007 | [MCP007-file-plan.md](MCP007-file-plan.md) | 文件与计划 | File + Plan | 23 |
| MCP008 | [MCP008-lsp-team-notebook.md](MCP008-lsp-team-notebook.md) | LSP/团队/笔记本 | Lsp + Team + Notebook | 30 |
| MCP009 | [MCP009-mcpclient-git-search-worktree.md](MCP009-mcpclient-git-search-worktree.md) | MCP客户端/Git/搜索/Worktree | McpClient + Git + Search + Worktree | 34 |
| MCP010 | [MCP010-error-workflow-auth-permission-sandbox.md](MCP010-error-workflow-auth-permission-sandbox.md) | 错误恢复/工作流/认证/权限/沙箱 | ErrorRecovery + Workflow + McpAuth + Permission + Sandbox | 34 |
| MCP011 | [MCP011-shell-ps-mcpres-cron-analysis-web-voice-config.md](MCP011-shell-ps-mcpres-cron-analysis-web-voice-config.md) | Shell/PS/MCP资源/定时/分析/Web/语音/配置 | Shell + PowerShell + McpResource + Cron + CodeAnalysis + Web + Voice + Config | 35 |
| MCP012 | [MCP012-misc-small-categories.md](MCP012-misc-small-categories.md) | 其余小类别 | Build + CodeExecution + Todo + Vcr + Goal + CodeGeneration + Brief + StructuredOutput + Sleep + Policy + Context + Snip + Terminal + Repl + Browser + Peers + PrSubscription + RemoteTrigger + Monitor + Notification + StepEvidence + Interaction + FileTransfer | 36 |

## 使用说明

### 第二轮:脚本批量验证

每个计划文档包含:
1. **工具清单** — 按分类列出所有 MCP 工具方法
2. **批量验证脚本** — PowerShell 脚本循环调用 `jcc.exe mcp_call`
3. **只读工具冒烟测试** — 安全的只读工具快速验证
4. **验收标准** — 7 项检查清单
5. **风险提示** — 副作用工具警告
6. **问题记录表** — 记录发现的坏点

### 执行流程

```
1. 编译 jcc.exe (Release)
2. 按计划编号顺序执行
3. 每个计划:
   a. 运行只读工具冒烟测试 → 快速验证
   b. 运行批量验证脚本 → 定位坏点
   c. 记录问题到问题记录表
   d. 修复坏点 → 面向笨蛋客户调整
4. 全部完成后进入第三轮(交接文档)
```

### 验收标准(通用)

- 每个工具调用返回格式正确的 JSON
- `Error:false` 且有非空输出(空输出 = 隐患)
- 无崩溃/超时/死锁(超时返回 1234)
- 启动参数格式统一(`--trust --bypass mcp_call <tool> [args]`)
- 只读工具可安全重复调用
- 副作用工具在无参数时给出明确错误提示而非崩溃

### 常见问题排查

| 问题 | 可能原因 | 解决方案 |
|------|----------|----------|
| 崩溃 | 参数缺失/类型错误 | 加参数校验,给默认值 |
| 超时 | 死锁/网络慢 | 加超时+Actor管道模式 |
| 空输出 | 无数据/序列化失败 | 必须返回信息,即使冗余 |
| Error:true | 业务逻辑错误 | 检查参数,给出明确提示 |
| 格式错误 | JSON序列化问题 | 用 RelaxedJsonSerializer |
| 词不达意 | 功能未实现 | 补全实现,对齐 MCP 名称 |

## 统计信息

- **总分类数**: 55 个 ToolCategory
- **总工具数**: 375+ 个 MCP 工具方法
- **总 Handler 类**: 91 个
- **计划数**: 12 个
- **平均每计划**: ~31 个工具
