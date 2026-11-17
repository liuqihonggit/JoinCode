# TASK087 — 参数名归一化两条路径统一：exe 与宿主给出同一个答案

> 状态：已完成（2026-11-18 真机验证通过）
> 起点：TASK086 把工具参数契约统一后，发现同一条调用约定在两条路径上宽严不一。

## 一、为什么要做

TASK086 之后，`--参数 值` 对全部 635 个工具都通了。但真机抽查发现**同一个参数名，
两条路径的宽严不一样**：

```
$ jcc tool call file read --filePath AGENTS.md      # host 路径 → 自动归一化，成功
$ jcc tool call file read --file_path AGENTS.md     # local 路径 → 成功
$ jcc-tool-file.exe read --filePath AGENTS.md      # local 路径 → 直接拒绝
工具 'read' 本地执行失败（file 本地执行层拦截）：read 缺少必填参数 --file_path
```

**宿主路径有归一化，exe 路径没有。** 根因是归一化规则写在
`Abstractions.Primitives.Utils` 的 `ParameterNameRepairer` 里，
而独立工具 exe 受**依赖墙硬约束**（`modules/tools/cli_nodes/cli_node.props`：
只允许引用 `Abstractions.Primitives.Tools` 契约层），根本引用不到。

于是同一个决策（参数名该怎么归一）在这条链路上是**两个实现**：
宿主侧有、exe 侧没有。调用方（尤其是模型）拿到的信号是矛盾的 ——
「有时能过，有时不能」，只能逐个试错。

## 二、方案：规则下沉到契约层，两边共用

### 2.1 规则下沉

`ToolParameterNameRepairer`（新，`Abstractions.Primitives.Tools`）成为参数名规则的**唯一真相**：

- 别名表（40 条）
- `ToSnakeCase` / `ToCamelCase` 变体
- `RepairParameterNames(arguments, schema)` —— 宿主侧用（对着 `ToolSchema`）
- `RepairNames(rawNames, knownNames)` —— exe 侧用（对着帮助文本里声明的参数名）

`Abstractions.Primitives.Utils` 的 `ParameterNameRepairer` 改为**薄委托**，
不再是第二份实现（utils 本来就引用 tools，依赖方向成立）。

### 2.2 exe 侧接线

`CliNodeRunner` 是 13 个分组 exe **共用**的入口逻辑（`-h` / `--list-local` / 参数解析 /
本地分发 / 转发宿主 / 退出码）。归一化插在这里：

```
ParseOptions(args) → NormalizeOptionNames(options, help, toolId)
```

`DeclaredParameterNames` 从帮助文本的详情段解析该工具声明的参数名
（形如 `    --file_path  string  必填  说明` 的行）。
**解析不到就返回空集合、不做归一化** —— 宁可让工具报「缺少必填参数」并给出正确写法，
也不要把参数映射到错的参数上。

归一化规则：直接命中 > 别名变体 > snake/camel 变体；都不命中保持原样。

### 2.3 为什么归一化放在「解析后」而不是「解析时」

`ParseOptions` 的键是原样保留的（去 `--` 前缀），字典本身大小写不敏感。
归一化要改的是**键的写法**（`filePath` → `file_path`），所以只能在解析完成后做 ——
这也是为什么它需要「该工具声明了哪些参数名」这份信息，而这信息在帮助文本里。

## 三、真机证据

`Verify-ToolParamNames.py`（新）逐个 exe 抽查：把帮助文本里声明的必填参数改成 camelCase
再调用，若仍报「缺少必填参数 --<原名>」即判定分叉。

```
$ python non_deliverables_tools/scripts/Verify-ToolParamNames.py --per-group 12
  OK  file/file_batch_edit: --oldString → --old_string 已被识别
  OK  file/read: --filePath → --file_path 已被识别
  OK  git/git_branch: --branchName → --branch_name 已被识别
  OK  mcp/mcp_auth_apikey: --authName → --auth_name 已被识别
  OK  search/search_affected_projects: --filePath → --file_path 已被识别
  OK  shell/powershell_script: --scriptPath → --script_path 已被识别
  OK  system/fix_merge_conflict: --sourceWorktreePath → --source_worktree_path 已被识别
  OK  tools/skill_stuck: --currentApproach → --current_approach 已被识别
  OK  web/download_file: --filePath → --file_path 已被识别
  …
参数名归一化一致：117 项抽查全部通过（camelCase 与 snake_case 等价）
```

直接证据：

```
$ jcc-tool-file.exe read --filePath "AGENTS.md"     # 修复前：缺少必填参数 --file_path
# AGENTS.md
## 核心规则：每次回复结束前必须调用 `ask_user` 工具
```

回归：host 路径 `jcc tool call search search_symbol --query TaskUpdate` 仍通；
`host.tests` 1308 通过；契约层测试 `JoinCode.CliNodes.Tests` 48 通过（新增 9 条）；
13 个分组 exe 全部重编通过并重新装配。

## 四、踩过的坑

1. **契约层不能引用 utils**：exe 的依赖墙是硬约束，规则必须住在 exe 引用得到的程序集里。
   反过来 utils → tools 的依赖本来就成立（utils 引用 tools），所以「下沉」方向是通的。
2. **`DeclaredParameterNames` 解析不到就返回空**：不伪造参数名。
   伪造会把参数映射到错的参数上 —— 那是静默错误，比报错危险得多。
3. **先直接命中再变体**：`ResolveCanonical` 第一步是 OrdinalIgnoreCase 直接匹配，
   所以真名就是 camelCase 的工具（`oldString`）不会被误转成 `old_string`。
4. **契约层改了要重编 13 个 exe 并重新装配**：`Pack-ToolNodes.ps1` 只做装配，
   检测到 `project.assets.json` 存在就**跳过编译** —— 只跑装配会拿到旧 exe。

## 五、记录

- 2026-11-18：规则下沉 + exe 侧接线 + 真机验证 + CI 门禁完成。
