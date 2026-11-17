# TASK088 — 密钥红线按值判定：拦真密钥，放过「长得像密钥的名字」

> 状态：已完成（2026-11-18 真机验证通过）
> 起点：TASK086/TASK087 把工具参数契约统一后，暴露出 CLI 入口层还有一个同类问题 ——
> 密钥红线**按参数名**拦截，而 `--key` / `--token` / `--secret` 是常见业务参数名。

## 一、为什么要做

密钥红线在**程序入口**（`Program.Main`）执行，命中就 `return ArgumentParseError` ——
**直接阻断**。原判定规则是「参数名在危险名单里就拦」：

```csharp
"--api-key", "--apikey", "--key", "--token", "--secret",
"--access-key", "--secret-key", "--private-key",
```

问题出在 `--key` / `--token` / `--secret` 这三个**通用词**上：

```
$ jcc tool call system config_get --key theme
  ⚠ 警告: 检测到命令行参数 '--key' 可能包含 API Key。   ← 直接阻断，工具彻底调不通
```

「配置项叫 key」「认证方式是 token」「这段密文叫 secret」都是完全正常的用法。
按名字一刀切，代价是**某个工具永远调不通**，而调用方（尤其是模型）只会得出
「这个工具不能用」的错误结论。

反过来，真正要防的是**密钥值出现在进程列表里**（`ps` / `top` 能看到完整命令行）。
判定依据应该是**值**，不是名字。

### 顺带发现一个漏检

旧实现检查「参数值」时，只看**下一个 token**：

```csharp
if (i + 1 < args.Length && args[i].StartsWith('-') && !args[i + 1].StartsWith('-')) {
    var value = args[i + 1];   // ← 取的是下一个 token，不是 --opt=value 里的值
```

所以 `--api-key=sk-xxxx` 这种写法**完全漏检** —— 参数名匹配不中（`arg == "--api-key"` 为假），
值检查又去看了下一个 token。

## 二、方案：三层判定

| 层 | 依据 | 动作 |
|---|---|---|
| ① 明确密钥语义 | 参数名在 `--api-key` / `--access-token` / `--password` / `--credential` 等**只有密钥才会用**的名字里 | 拒绝，与值无关 |
| ② 密钥形态的值 | 值命中 `sk-` / `ghp_` / `glpat-` / `AKIA` / `eyJ` / `AIza` / `xoxb-` / `hf_` / `npm_` / `dckr_pat_` 等厂商前缀 | 拒绝，与参数名无关 |
| ③ 通用词 + 不透明形态 | 参数名是 `--key` / `--token` / `--secret`，且值**够长（≥24）且只含 token 字符集** | 拒绝 |

第 ③ 层兜住**自建服务的不透明密钥** —— 它们没有可识别的厂商前缀，但长度与字符集
和普通业务值（配置项名、路径、URL）明显不同。短值（如 `--key theme`）放行。

**明确的取舍**：一个长度短、又不带厂商前缀的自建密钥，现在会漏过。
这是刻意的 —— 把它拦下来就必须拦掉所有 `--key <短值>`，而那正是误伤的来源。
安全与可用性在这里冲突时，选择**可用性**，因为误伤会让功能彻底不可用，
而漏检只在「用户主动用短密钥走命令行」时发生。

同时修正：`--opt=value` 形式现在会被正确拆开检查（此前漏检）。

## 三、真机证据

```
# ① 通用词 + 普通值 —— 修复前被直接阻断
$ jcc tool call search search_symbol --key theme
{"ok":false,"error":{"code":"TOOL_ERROR","message":"工具 'search_symbol' 没有参数 'key'。工具 'search_symbol' 的可用参数: --query; 是不是想写 --query?。用 jcc mcp_schema search_symbol 查看完整参数表",…}}
   ↑ 红线已放行，进入工具层并给出精确引导

# ② 明确密钥语义 —— 仍拦
$ jcc mcp_call bash --api-key sk-secretvalue
  ⚠ 警告: 检测到命令行参数 '--api-key' 可能包含 API Key。禁止在命令行中传递密钥…

# ③ 通用词 + 密钥形态的值 —— 仍拦
$ jcc mcp_call bash --token ghp_abcdefghijklmnop
  ⚠ 警告: 检测到命令行参数值 'ghp_abcd...' 可能是 API Key。禁止在命令行中传递密钥…

# ④ 等号形式 —— 修复前漏检
$ jcc mcp_call bash --api-key=sk-abcdefghijklmnop
  ⚠ 警告: 检测到命令行参数 '--api-key=sk-abcdefghijklmnop' 可能包含 API Key。…

# ⑤ mcp serve 白名单 —— 未误伤
$ jcc mcp_serve --help
JoinCode - AI 智能体命令行工具
```

错误提示里的值做了**掩码**（只露前 8 字符）—— 提示本身不该把密钥抄进日志。

回归：`host.tests` 1329 通过（新增 21 条 `ApiKeyRedLineTests`）；CLI 编译 0 警告 0 错误。

## 四、踩过的坑

1. **红线在入口执行，早于子命令路由**：所以它拿不到「当前工具的参数表」，
   只能按通用的名字/值规则判断。要做到「按工具语义判断」得把检查下沉到工具调用层，
   那是另一件事（当前没有工具用 `--key` 这类名字，暂不需要）。
2. **`--opt=value` 必须单独拆**：旧实现只看下一个 token，等号形式整体漏检。
3. **提示里的值要掩码**：直接回显密钥等于把密钥抄进 CI 日志。
4. **测试里不能写 Windows 盘符路径**（JCC1018）：用 `/tmp/a.txt` 这类跨平台字面量。

## 五、记录

- 2026-11-18：按值判定改造 + 等号形式漏检修复 + 真机验证 + 单元测试完成。
