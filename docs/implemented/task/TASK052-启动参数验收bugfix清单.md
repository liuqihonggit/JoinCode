# TASK052 — 启动参数验收发现的 bugfix 清单

> 状态：accepted
> 创建：2026-10-15
> 验收方式：实际编译 + 启动 exe + 传参验证（非 mock，ADR 0080）

## 1. 验收背景

通过实际编译 `jcc.exe` 并以启动参数逐个验收 24 个 exe 的功能完备性。验收覆盖：
- `jcc --version` / `--help` / `-h sub` / `-h options` / `schema`
- `jcc mcp_list` / `mcp_schema` / `mcp_search` / `mcp_call`
- `jcc slash_list` / `slash_schema`
- `jcc rg` / `jcc gh`
- `jcc -p --dry-run --non-interactive`
- `jcc-audit --help` / `top-files`
- `simd-vector-verify`

## 2. 发现的 3 个不完备问题

### BUG-1：`mcp_list` description/kind 字段全空

**严重度：中**
**验收命令：** `jcc mcp_list` / `jcc mcp_list --category github`

**现象：** 所有 400+ 工具的 `description` 和 `kind` 字段返回空字符串 `""`。

**根因：** `modules/apps/cli/core/commands/core/McpCommand.cs:45`

```csharp
// BUG：硬编码空字符串
var items = filtered
    .Select(t => new Cli.Output.CliToolListItem(t.Name, "", t.Category, null, ""))
    .ToList();
```

**对照：** `mcp_search`（同文件第 77-80 行）正确使用了 `t.Description`：

```csharp
// 正确：使用 t.Description
var items = result.MatchedToolNames.Select(name => new Cli.Output.CliToolSearchItem(
    name,
    allTools.TryGetValue(name, out var t) ? t.Description : null,
    allTools.TryGetValue(name, out var t2) ? t2.Category : null)).ToList();
```

**修复方案：** 将 `McpCommand.cs:45` 的空字符串改为从工具对象取值：

```csharp
var items = filtered
    .Select(t => new Cli.Output.CliToolListItem(t.Name, t.Description, t.Category, t.GroupName, t.Kind.ToString()))
    .ToList();
```

**验收标准：** `jcc mcp_list --category github` 返回的每个工具 `description` 非空。

---

### BUG-2：`--dry-run` 在非交互模式下未拦截 LLM 调用

**严重度：高**
**验收命令：** `jcc -p "echo hello" --dry-run --non-interactive --await 10`

**现象：** `--dry-run` 应"只显示将要执行的操作，不实际执行"，但实际仍尝试调用 LLM（agnes 供应商返回 400）。

**根因：** 待调查。`--dry-run` 标记为 `[read]` 风险等级，在非交互模式下可能未正确拦截 LLM 调用链。

**调查方向：**
1. 搜索 `DryRun` 在代码中的所有消费点
2. 确认非交互模式 `-p` 的执行路径是否检查 `DryRun` 标志
3. `--dry-run` 应在 LLM 调用前短路返回"试跑模式：将发送提示词 'echo hello' 到模型 xxx"

**修复方案：** 待调查后确定。

**验收标准：** `jcc -p "hello" --dry-run --non-interactive` 不发起任何网络请求，只输出"将要执行"的描述。

---

### BUG-3：Git Bash 路径展开破坏斜杠命令

**严重度：低**
**验收命令：** `jcc slash_schema /compact`

**现象：** Git Bash 将 `/compact` 展开为 `C:/Program Files/Git/compact`，导致 `slash_schema` 报"未找到斜杠命令"。

**根因：** Git Bash（MSYS2）的路径转换行为，将以 `/` 开头的参数视为 Unix 路径并展开为 Windows 路径。这不是 jcc 的 bug，但影响用户体验。

**临时绕过：** `MSYS_NO_PATHCONV=1 jcc slash_schema /compact` 正常工作。

**修复方案（可选）：** 在 jcc 入口处检测 `C:/Program Files/Git/xxx` 格式，恢复为 `/xxx`。或文档提示用户设置 `MSYS_NO_PATHCONV=1`。

**验收标准：** `jcc slash_schema /compact` 在 Git Bash 下无需 `MSYS_NO_PATHCONV=1` 即可正常工作（如果选择内部修复），或文档明确提示绕过方式。

---

### BUG-4：CtorScanner 扫描目录和输出路径过时

**严重度：中**
**验收命令：** `ctor-scanner.exe`

**现象：** `Found 0 .cs files` + `DirectoryNotFoundException: 'D:\project\w1\tool\ctor_scanner\ctor_scan_report.json'`

**根因：** `modules/tools/ctor_scanner/Program.cs`
- 第 5 行：`ScanDirs = ["lib", "kit", "server", "llm", "app"]` — 旧目录结构，已迁移到 `modules/` 16 功能域架构
- 第 118 行：`Path.Combine(repoRoot, "tool", "ctor_scanner", ...)` — 旧路径，应为 `modules/tools/ctor_scanner/`

**修复：**
- `ScanDirs` 改为 `["modules"]`
- 输出路径改为 `Path.Combine(repoRoot, "modules", "tools", "ctor_scanner", "ctor_scan_report.json")`

**验收标准：** `ctor-scanner.exe` 扫描 modules/ 下所有 .cs 文件，输出报告到正确路径。✅ 4952 文件, 157 风险构造函数。

## 3. 修复优先级

| 顺序 | Bug | 理由 |
|------|-----|------|
| 1 | BUG-1 | 根因已定位，最小改动（一行），立即修复 |
| 2 | BUG-2 | 需要调查执行路径，但严重度高 |
| 3 | BUG-3 | 环境问题，可选内部修复或文档提示 |

### BUG-5：10个 module.slnx 路径引用不匹配实际 csproj 文件名

**严重度：中**
**验收命令：** `jcc-audit layer-audit modules/apps/module.slnx`

**现象：** `layer-audit` 报警告 `.slnx 中引用的项目不存在: cli/cli.csproj` 等，9个 module.slnx 共32条无效引用。

**根因：** module.slnx 中写的是 snake_case 路径（如 `async_lock/async_lock.csproj`），实际 csproj 文件名是 PascalCase（如 `AsyncLock.csproj`）。Windows 文件系统大小写不敏感所以 `dotnet build` 能工作，但 layer-audit 和 Linux/CI 环境会失败。

**修复：** Python 脚本 `fix_slnx_paths.py` 批量修正30条路径 + 手动修正6条大小写 + 移除2个不存在的 csproj 引用（`tools/scripts`、`generators/shared`）。

**验收标准：** `layer-audit` 各 module.slnx 不再报"项目不存在"警告。✅ 10个 module.slnx 全部修复，commit `06fd307`。

---

## 4. 第二轮验收：jcc 子命令 + jcc-audit + AOT 工具

### jcc 12个子命令验收

| 子命令 | 分类 | 验收结果 |
|--------|------|----------|
| `gh` | GitHub | ✅ 之前验收 |
| `mcp_call` | MCP工具 | ✅ 之前验收 |
| `mcp_list` | MCP工具 | ✅ BUG-1已修复 |
| `mcp_schema` | MCP工具 | ✅ 之前验收 |
| `mcp_search` | MCP工具 | ✅ 之前验收 |
| `mcp_serve` | MCP工具 | ✅ stdio+HTTP模式正常,initialize+tools/list返回527工具 |
| `rg` | 搜索 | ✅ 之前验收 |
| `slash_call` | 斜杠命令 | ✅ /help输出完整,/compact dry_run正常 |
| `slash_list` | 斜杠命令 | ✅ 之前验收 |
| `slash_schema` | 斜杠命令 | ✅ BUG-3已修复 |
| `schema` | 自省 | ✅ 之前验收 |
| `remote-control` | 远程控制 | ✅ 需JCC_BRIDGE_MODE=1环境变量,设计行为非BUG |

### jcc-audit 8个子命令验收

| 子命令 | 验收结果 |
|--------|----------|
| `audit` | ⚠️ 需分析器DLL(构建问题非BUG) |
| `ctor-audit` | ✅ 找到2个胖构造函数(CliSession 8参,SessionController 7参) |
| `layer-audit` | ✅ BUG-5已修复,10个module.slnx路径修正 |
| `replace` | ⚠️ 需分析器DLL(构建问题非BUG) |
| `strip-bom` | ✅ 发现2个BOM文件,--dry-run正常 |
| `sync-async` | ✅ 扫描async方法列表正常 |
| `fix-jcc3016` | ✅ 未发现违规,exit=0 |
| `top-files` | ✅ 之前验收 |

### AOT测试工具验收

| 工具 | 验收结果 |
|------|----------|
| `aot-pithosdb-probe` | ✅ 5项测试全PASS(Put/Get,Scan,Delete,WriteBatch,Persistence) |
| `aot-httpclientfactory-test` | ✅ IHttpClientFactory兼容性检查通过 |
| `aot-browser-test` | ✅ PuppeteerSharp API检查完整 |
| `SkiaSharpAotTest` | ✅ PNG生成成功(413字节) |
| `simd-vector-verify` | ✅ 之前验收 |
| `PptrApiCheck` | ✅ 输出"Hello, World!" |

---

## 5. 第三轮验收：Mock服务器端点 + ONNX基准 + bugfix回归测试

### 5个Mock服务器端点验收

| Mock服务器 | 端点 | 验收结果 |
|-----------|------|----------|
| OpenAI | `/v1/chat/completions` + `/v1/embeddings` | ✅ 返回正确chat completion(含tool_calls) |
| Anthropic | `/v1/messages` | ✅ 返回正确message(含tool_use) |
| DeepSeek | `/v1/chat/completions` | ✅ 返回正确chat completion(含tool_calls) |
| Responses | `/v1/responses` | ✅ 接收并处理请求 |
| Mcp | `/mcp/` (initialize + tools/list) | ✅ 返回5个工具(echo/add/uppercase/reverse/length) |

### ONNX基准测试验收

| 工具 | 验收结果 |
|------|----------|
| `onnx-embedding-verify` | ✅ 原版86MB+量化22MB模型基准测试正常,DirectML环境不支持(非BUG) |

### bugfix回归测试

| BUG | 测试文件 | 用例数 | 状态 |
|-----|---------|--------|------|
| BUG-1 | `cli/CliOutputEnvelopeTests.cs` | 4 | ✅ |
| BUG-2 | `entry/steps/NonInteractiveExecuteStepTests.cs` | 3 | ✅ |
| BUG-3 | `cli/MsysPathExpansionTests.cs` | 13 | ✅ |
| BUG-4 | `cli/CtorScannerConfigTests.cs` | 4 | ✅ |
| BUG-5 | `cli/ModuleSlnxIntegrityTests.cs` | 5 | ✅ |
| **合计** | **29用例** | **全通过** | ✅ |

---

## 6. 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `McpCommand.cs:45` 填充 description/kind (BUG-1) | `jcc mcp_list` 输出 | ✅ | ✅ `mcp_list --category github` 全部有 description |
| `--dry-run` 拦截 LLM 调用 (BUG-2) | `jcc -p --dry-run` 非交互模式 | ✅ | ✅ 输出试跑信息,不调用 LLM,exit=0 |
| Git Bash 路径展开检测/恢复 (BUG-3) | `jcc slash_schema /xxx` | ✅ | ✅ 无需 MSYS_NO_PATHCONV=1 |
| CtorScanner 扫描目录更新 (BUG-4) | `ctor-scanner.exe` | ✅ | ✅ 扫描 modules/ 4952 文件,157 风险 |
| 10个module.slnx路径修正 (BUG-5) | `jcc-audit layer-audit` | ✅ | ✅ 32条路径修正+2条移除,layer-audit无警告 |

## 7. 决策记录

<!-- 🤖 Auto Decision: 2026-10-15 -->
<!-- 决策: 先写 bugfix 文档再修复，因为修复可能扩展到相似功能 -->
<!-- 原因: 用户明确要求"修复可能会因为相似功能不断扩展,所以先做一个bugfix文档" -->
<!-- 替代方案: 直接修复（可能遗漏相似问题）-->
<!-- 验证: 5个BUG全部修复验收通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-15 -->
<!-- 决策: 用Python脚本批量修复slnx路径，而非手动逐个修改 -->
<!-- 原因: 10个slnx文件共32条路径不匹配，手动修改易遗漏 -->
<!-- 替代方案: 手动edit逐个修改（耗时且易错）-->
<!-- 验证: layer-audit验证全部无警告 ✅ -->
