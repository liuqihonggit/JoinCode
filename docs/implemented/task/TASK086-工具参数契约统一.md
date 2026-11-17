# TASK086 — 工具参数契约统一：`--参数 值` 打通 245 个 host 工具 + 参数名精确校验

> 状态：已完成（2026-11-18 真机验证通过）
> 起点：TASK082 把 635 个工具按 `local / delegate / host` 分成三档，
> 但只验证过「工具能不能被找到」，没验证过「能不能用统一约定调起来」。

## 一、为什么要做

ADR 0157 v3 定的调用约定是**一套**：`jcc tool call <组> <工具> --参数 值`。
首次提示词里也是这么写的：

```
- 用法四条：… ④ `jcc tool call <组> <工具> --参数 值` 执行
```

但这条约定**只对 390 个 local/delegate 工具成立**。剩下 245 个 host 工具走的是
另一条路径，而那条路径根本不接受 `--参数 值`：

```
$ jcc tool call search search_symbol --query "Foo"
error: 未知选项 '--query'
  |
  | mcp_call search_symbol --query Foo
  |                        ^^^^^^^ 未知选项 '--query'
```

**提示词在骗 LLM**：它教给模型的写法，对 38% 的工具 100% 失败。
模型拿到这个错误只有两种反应——换个参数名再试，或者改用别的工具绕路。两条都会偏离正轨。

## 二、根因：同一批工具，两套参数契约

| 工具类型 | 数量 | 执行路径 | 接受的参数形式 |
|---|---|---|---|
| local / delegate | 390 | 独立 exe 进程 | `--参数 值` ✅ |
| host | 245 | jcc 进程内 `mcp_call` | 只有 `key=value` / JSON ❌ |

`mcp_call` 只认 `key=value`（`pattern=*.md`）和 JSON 两种形式，
而 `CliToolCommand.RunHostToolInProcessAsync` 把 `--参数 值` 片段**原样塞进 mcp_call 的 argv**：

```csharp
var argv = new List<string> { "mcp_call", toolId };
argv.AddRange(call.PassThrough);   // ← PassThrough 是 "--参数 值" 形式
return await McpCallCommand.ExecuteAsync(argv.ToArray(), ct);
```

`McpCallCommand` 入口处的未知选项检测只认元命令自己的选项，于是把工具参数当成拼错的选项拒掉。

**这不是参数解析的小 bug，是「同一个决策的两个实现」**——
`jcc tool call` 与 `mcp_call` 对「工具参数怎么传」各有一套约定。

## 三、方案：语法与语义分层

参数「哪些合法、哪些必填」的真相**只在工具注册表的 `ToolSchema` 里**。
CLI 层不猜参数名，只做语法解析；语义校验必须对着参数表做。

### 3.1 CLI 层只做语法解析（新增 `McpToolArgumentBinder`）

把三种来源归一成同一张参数字典：

- `--参数 值` / `--参数=值` → 工具参数（**新增**）
- `key=value` → 工具参数（原有）
- JSON / `--args-file` / `--args-stdin` → 工具参数（原有）

元命令自己的选项（`--json` / `--model` / `--args-file` …）优先，其余 `--xxx` 一律视为工具参数。
未知选项检测因此从「一刀切拒绝」改成「交给参数表判定」。

### 3.2 语义校验放到宿主内（对着 `ToolSchema`）

`McpCliCommand.ExecuteAgainstRegistryAsync` 在拿到工具参数表后校验：

- **未知参数名** → 列出该工具真实参数名 + 点出最相近的一个 + 指向 `jcc mcp_schema`
- **缺必填参数** → 列出缺了哪些 + 指向参数表

```
工具 'glob' 没有参数 'patern'。工具 'glob' 的可用参数: --pattern, --path;
是不是想写 --pattern?。用 jcc mcp_schema glob 查看完整参数表
```

**判定未知参数前必须先归一化**：工具分发中间件链里的 `ArgumentRepairMiddleware`
已经用同一套别名表把 `filePath` 归一成 `file_path`；CLI 若不先归一化，
就会把合法写法误判成未知参数。归一化复用 `LlmJsonHelper.RepairArguments`，不另造一套。

### 3.3 `jcc tool call` 的 host 路径不再 argv 往返

改为把参数解析成字典后**直连** `McpCliCommand.ExecuteCallWithArgumentsAsync`：

- 工具参数名不会被元命令选项（`--json` / `--model`）抢走
- 值不会因 shell 转义而变形
- 超时语义（124）保持不变

## 四、真机证据

```
# ① host 工具（修复前必失败）
$ jcc tool call search search_symbol --query "TaskUpdate"
{"ok":true,"data":"找到 28 个符号 (37ms): …"}

# ② mcp_call 新形式
$ jcc mcp_call glob --pattern "*.md"
{"ok":true,"data":"README.md\nAGENTS.md\n","schema_version":"1"}

# ③ mcp_call 旧形式仍兼容
$ jcc mcp_call glob 'pattern=*.md'
{"ok":true,"data":"README.md\nAGENTS.md\n","schema_version":"1"}

# ④ --参数=值 形式
$ jcc mcp_call glob '--pattern=*.md'
{"ok":true,"data":"README.md\nAGENTS.md\n","schema_version":"1"}

# ⑤ 未知参数 → 精确提示
$ jcc mcp_call glob --patern "*.md"
{"ok":false,"error":{"code":"TOOL_ERROR","message":"工具 'glob' 没有参数 'patern'。工具 'glob' 的可用参数: --pattern, --path; 是不是想写 --pattern?。用 jcc mcp_schema glob 查看完整参数表",…}}

# ⑥ 缺必填参数
$ jcc mcp_call glob
{"ok":false,"error":{"code":"TOOL_ERROR","message":"工具 'glob' 缺少必填参数: pattern。用 jcc mcp_schema glob 查看参数表",…}}

# ⑦ local 工具回归（走 exe，未受影响）
$ jcc tool call search glob --pattern "*.md"
docs/implemented/task/TASK085-gui装配归一与真机验证.md
…

# ⑧ bash 回归
$ jcc mcp_call bash 'command=echo hello-bash'
{"ok":true,"data":"hello-bash\n| ExitCode | Success | Duration |…"}
```

回归：`host.tests` 1308 通过（新增 9 条 `McpToolArgumentBinderTests`），
CLI 编译 0 警告 0 错误。

## 五、踩过的坑

1. **未知选项检测不能一刀切**：`DetectUnknownOptions` 只认元命令选项，
   放开之后「参数名拼错」会静默通过 —— 所以语义校验必须**同步**搬到参数表那一层，
   否则只是把「明确报错」换成「悄悄忽略」，后者更危险。
2. **保留选项必须连同其值一起保留**：位置参数收集靠「选项紧跟其值」的相邻关系
   排除选项值；拆开就会让 `--args-file args.json` 的 `args.json` 变成位置参数。
3. **判定未知前先归一化**：`ParameterNameRepairer` 的别名表方向是
   「schema 名 → 别名」，但 `ToSnakeCase` 兜底让 `filePath → file_path` 成立 ——
   这层兜底是 CLI 校验能成立的前提。
4. **错误描述要单行**：它会被塞进 JSON 的 `error.message`，
   多行的 Rust 风格位置指示线在 JSON 里反而更难读（纯文本终端才适合那种格式）。
5. **测试工程的 null-forgiving 被 JCC11003 拦**：用 `error.Should().NotBeNull()` 后
   直接 `error.Should().Contain(...)`，不要写 `error!`。

## 六、记录

- 2026-11-18：参数契约统一 + 真机验证 + 单元测试完成。
