# TASK072 — 巡检行为验证升级(不崩溃 → 真实行为验证)

## 背景

当前工具巡检机制(ToolPatrolEngine + McpToolSmokeTests + e2e_tool_patrol.py)是**"不崩溃"级别** — 只验证工具不抛异常/不死锁/不超时,**不验证工具业务行为正确**。用户明确要求:"你的工作依然没有办法保证工具没有错误,这样不行,我要真实验证。"

## 现状问题

### 1. 参数构造硬编码,不保证语义正确

`ToolPatrolEngine.BuildMinimalArguments`(`modules/mcp/mcp_tool_dispatch/core/execution/ToolPatrolEngine.cs:120`)按参数名硬编码:

```csharp
"file_path" or "path" => "\"test.txt\"",     // 相对路径,但 write 要求绝对路径
"command" => "\"echo hello\"",
"query" => "\"test\"",
```

**问题**:同一参数名 `file_path` 在 write/read/edit 语义不同:
- write 要**可写绝对路径**
- read 要**已存在文件**
- edit 要**已存在 + old_string 匹配**

当前全传 `"test.txt"`,导致:
- write 工具可能因路径非绝对直接返回 [ERR],**写入逻辑根本没执行**
- read 工具因文件不存在返回 [ERR],**读取逻辑根本没执行**
- 巡检只记 [ERR] 继续,误以为"工具不崩溃 = 没问题"

### 2. 无配对测试,不验证行为闭环

write 写入文件后,没有 read 读回验证内容一致。工具可能:
- write 返回 [OK] 但实际没写入(沙箱拦截/权限不足静默失败)
- read 返回 [OK] 但内容是空/错误(缓存脏数据/编码错误)

当前巡检**完全检测不到**这类行为错误。

### 3. InMemoryFileSystem 可能被绕过

`McpToolSmokeTests`(`test/integration/integration.tests/mcp_tools/McpToolSmokeTests.cs:13`)注入 `InMemoryFileSystem`,但 `FileToolHandlers` 用 `IFileOperationService.WriteFileAsync`(`FileToolHandlers.Write.cs:36`)— `IFileOperationService` 实现可能直接调 `File.WriteAllText` 绕过 `IFileSystem`。需确认。

### 4. 覆盖率缺口

| 巡检层 | 覆盖 | 跳过 | 覆盖率 |
|--------|------|------|--------|
| 集成测试 McpToolSmokeTests | 328 | 200 | 62.1% |
| E2E 脚本 e2e_tool_patrol.py | 298 | 230 | 56.4% |

跳过 200 个工具中:
- gh_ 146 个:GitHub 写入副作用,可用 MockServer 覆盖
- desktop 39 个:Win32 SendInput,难 mock
- git_/worktree 14 个:可用临时 git 仓库覆盖
- mcp_connect 1 个:可用本地 mock MCP 覆盖

## 目标

将巡检从"不崩溃"级别升级为**"真实行为验证"级别**:

1. **真触发核心逻辑**:参数语义正确,触发工具核心代码路径(而非参数验证错误路径)
2. **配对验证行为闭环**:write→read→断言内容一致;create→list→断言存在;edit→read→断言修改生效
3. **临时目录隔离**:每工具/每测试独立临时目录,不污染用户仓库,测试完清理
4. **断言行为正确**:不只 `!result.IsError`,断言 `result.Content` 包含预期文本
5. **覆盖跳过工具**:gh_ 用 MockServer、git_ 用临时仓库,覆盖率 → 90%+

## 待做项

### A. 参数构造器升级(按工具语义构造)

- [ ] 替换 `BuildMinimalArguments` 为按工具名+参数语义构造
  - write: `file_path = Path.Combine(tmpDir, "test.txt")` (绝对路径), `content = "patrol-test"`
  - read: 先 write 创建文件,再 read 同一路径
  - edit: 先 write 创建文件,再 edit with old_string=原文 new_string=新文
  - bash: `command = "echo patrol"` (无副作用命令)
  - search: `pattern = "*.txt"`, `path = tmpDir`
- [ ] 参数构造器单测:每个工具构造的参数能通过工具自身参数校验

### B. 配对行为验证

- [ ] write→read 配对:write 写入 `tmp/test.txt` content="hello" → read 读回 → 断言 Content 含 "hello"
- [ ] create→list 配对:create `tmp/new.txt` → directory_list `tmp` → 断言结果含 "new.txt"
- [ ] edit→read 配对:write "old" → edit old→new → read → 断言含 "new" 不含 "old"
- [ ] delete→read 配对:write 文件 → delete → read → 断言 [ERR] "not found"

### C. 临时目录隔离

- [ ] 每轮巡检创建 `Path.Combine(Path.GetTempPath(), $"patrol-{Guid():N}")`
- [ ] 巡检结束清理临时目录(try-finally / using var)
- [ ] 临时目录路径注入参数构造器,所有 file_path 用绝对路径

### D. IFileOperationService 实现确认

- [ ] 确认 `IFileOperationService.WriteFileAsync` 是否走 `IFileSystem`(InMemoryFileSystem 拦截)
- [ ] 若直接调 `File.WriteAllText`:改为注入 `IFileSystem`,或巡检用真实临时目录(非 InMemory)

### E. 跳过工具覆盖

- [ ] gh_ 146 个:用 MockServer 拦截 HTTP 请求,返回假响应,不跳过
- [ ] git_/worktree 14 个:用临时 git 仓库(`git init tmp/repo`),不污染用户仓库
- [ ] mcp_connect:用本地 mock MCP 服务器,不跳过

### F. 断言行为正确

- [ ] write 成功:断言 `result.Content` 含 "File created successfully" 或 "updated successfully"
- [ ] read 成功:断言 `result.Content` 含文件内容
- [ ] list 成功:断言 `result.Content` 含文件名
- [ ] 不只断言 `!result.IsError`

## 验收标准

| 验收项 | 标准 |
|--------|------|
| 参数语义 | 每工具构造的参数触发核心逻辑,不触发参数验证错误 |
| 配对验证 | write→read→断言内容一致,至少覆盖 file 类工具 |
| 临时目录 | 巡检用独立临时目录,不污染用户仓库 |
| 覆盖率 | gh_+git_+worktree 覆盖后 ≥ 90% |
| 行为断言 | 不只 `!result.IsError`,断言 `result.Content` 含预期文本 |
| 真实运行 | 走 [ADR 0080](../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md) 手动 exe 验收 |

## 关联

- 巡检引擎: `modules/mcp/mcp_tool_dispatch/core/execution/ToolPatrolEngine.cs`
- 集成测试: `test/integration/integration.tests/mcp_tools/McpToolSmokeTests.cs`
- E2E 脚本: `non_deliverables_tools/scripts/e2e_tool_patrol.py`
- MockServer: ADR 0077(`docs/implemented/adr/engineering_ci_test/0077-mockserver-jcc-joint-testing.md`)
- 手动 exe 验收: ADR 0080(`docs/implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md`)

<!-- 🤖 Auto Decision: 2026-10-25 -->
<!-- 决策: 单独写 TASK072 文档记录巡检行为验证待做工作,不立即实现 -->
<!-- 原因: 用户明确要求"先做 mcp 参数和描述的语法分析器",行为验证升级是后续工作,先记录避免遗漏 -->
<!-- 替代方案: 立即实现行为验证升级 — 但用户优先级是问题 2 分析器,先做分析器 -->
<!-- 验证: 文档已写入 docs/proposed/task/TASK072-巡检行为验证升级.md ✅ -->
