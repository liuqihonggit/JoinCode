# TASK052 — 启动参数验收发现的 bugfix 清单

> 状态：proposed
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

## 3. 修复优先级

| 顺序 | Bug | 理由 |
|------|-----|------|
| 1 | BUG-1 | 根因已定位，最小改动（一行），立即修复 |
| 2 | BUG-2 | 需要调查执行路径，但严重度高 |
| 3 | BUG-3 | 环境问题，可选内部修复或文档提示 |

## 4. 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `McpCommand.cs:45` 填充 description/kind (BUG-1) | `jcc mcp_list` 输出 | ❌ | ❌ |
| `--dry-run` 拦截 LLM 调用 (BUG-2) | `jcc -p --dry-run` 非交互模式 | ❌ | ❌ |
| Git Bash 路径展开检测/恢复 (BUG-3) | `jcc slash_schema /xxx` | ❌ | ❌ |

## 5. 决策记录

<!-- 🤖 Auto Decision: 2026-10-15 -->
<!-- 决策: 先写 bugfix 文档再修复，因为修复可能扩展到相似功能 -->
<!-- 原因: 用户明确要求"修复可能会因为相似功能不断扩展,所以先做一个bugfix文档" -->
<!-- 替代方案: 直接修复（可能遗漏相似问题）-->
<!-- 验证: 待实现 -->
