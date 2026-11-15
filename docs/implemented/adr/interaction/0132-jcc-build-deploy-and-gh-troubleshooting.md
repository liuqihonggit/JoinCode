# ADR 0132: jcc 编译产物部署与 gh/rg 工具指南

## 状态

accepted

## 背景

jcc 是独立实现的 GitHub CLI + ripgrep 兼容搜索工具。`gh`/`rg` 命令通过 bash 脚本转发到 `jcc gh`/`jcc rg`，编译产物部署到 `C:\Users\54076\bin\jcc.d\dev\`。

开发过程中发现 `gh` 命令执行时间超长（~6 秒启动开销），排查后发现多个问题：
1. jcc 启动开销 ~6 秒（`BuildHostAsync` 每次创建完整 DI 容器 + 注册 400+ MCP 工具）
2. `gh repo clone` 的 optional 参数不能用位置参数
3. 20 个 `_git.ExecuteAsync` 调用没有超时，git 命令卡住会一直等
4. `gh api -f` 短选项不支持

## 决策

### 1. 编译产物部署流程

#### ✅ 推荐方式：用 Deploy-JccDev.ps1 脚本（全量编译 + 全量部署）

```bash
# 一键全量编译 + 部署（推荐）
pwsh non_deliverables_tools/scripts/Deploy-JccDev.ps1

# 跳过编译，仅部署（已手动编译后）
pwsh non_deliverables_tools/scripts/Deploy-JccDev.ps1 -SkipBuild
```

脚本逻辑：
1. 调用 `Build-AllDomains.ps1` 全量编译 16 域 + 卫星项目
2. **先**拷贝 `artifacts/bin/gh/Debug/net10.0/` 与 `artifacts/bin/rg/Debug/net10.0/` 的产物到 `C:/Users/54076/bin/jcc.d/dev/`
3. **后**拷贝 `artifacts/bin/JoinCode/Debug/net10.0/` 的所有文件（dll/exe/json/pdb/xml）覆盖上去
4. 装配工具节点：13 个分组 exe 到 `dev/tools/<组>/`，委托执行器 `jcc-gh.exe` 到 `dev/tools/` 根（复用 `Pack-ToolNodes.ps1`）
5. 验证：`jcc.exe`/`jcc-gh.dll`/`jcc-rg.exe`/`jcc.deps.json` 存在；主项目 219 个文件与产物**逐项比大小**；委托链路自报路径必须落在 `dev/tools/`；`file_read` 实跑退出 0；`jcc tool tree` 零环境变量列出 13 组 / 640 工具

⚠️ **顺序不能反（2026-11-16 实测事故）**：gh/rg 是卫星项目，它们的输出目录里带着整套引擎 DLL（gh 179 个文件，含 `Abstractions.*`）。三个 exe 装进同一目录，**谁后拷谁的同名 DLL 生效**。主项目先拷、插件后拷时，插件里那份更旧的 `Abstractions.Primitives.Tools.dll`（04:33）会盖掉主项目刚编的（05:08），症状是「部署完行为没变、还拒绝 `mode: delegate`」，且全程无报错。所以：插件先铺、主项目最后覆盖，并在部署后用逐项比大小兜底。

⚠️ **卫星产物缺失要失败，不能跳过**：`jcc rg` 曾在部署后直接 `TypeLoadException: Could not load type 'SearchOutputMode'` —— 因为 `artifacts/bin/rg/` 从未编译过，部署目录里留着 11-05 的旧 `jcc-rg.exe`，与新版 `Abstractions.Primitives` 不匹配。「跳过」等于把旧 exe 留在原地继续用，所以脚本现在缺产物即失败并提示该编哪个 csproj。

⚠️ **必须全量拷贝**，包括 `*.deps.json` 和 `*.runtimeconfig.json`。只拷贝 dll 不拷贝 deps.json 会导致 `Could not load file or assembly` 错误（deps.json 包含程序集依赖图，旧 deps.json 不含新增依赖）。

#### 手动方式（不推荐，容易遗漏）

```bash
# 1. 编译（Debug 模式，增量编译）
dotnet build modules/apps/cli/JoinCode.csproj -c Debug

# 2. 新增/修改 [Register] 类后必须 --no-incremental 全量重建
dotnet build modules/apps/cli/JoinCode.csproj --no-incremental -c Debug

# 3. 部署编译产物到 dev 目录 — ⚠️ 必须全量复制,禁止只复制 jcc.exe
# ⚠️ 禁止用 robocopy /MIR — 跨盘符(D:→C:)时时间戳比较不可靠,DLL 不更新(见下方"部署坑")
# ⚠️ 禁止只复制 jcc.exe — jcc.exe(162KB)依赖同目录 Mcp.dll 等多个 DLL,只复制 exe 会运行旧 DLL
# ⚠️ 必须额外部署 jcc-gh.dll — gh 工具编译为独立项目 jcc-gh.dll,不在 JoinCode 输出目录中(见下方"部署坑:jcc-gh.dll 遗漏")
# ⚠️ 必须拷贝 *.deps.json — deps.json 包含程序集依赖图,不拷贝会导致 AssemblyLoadContext 找不到新增依赖
# ✅ 用2步部署: 先复制 JoinCode 产物, 再复制 jcc-gh 产物
cp -f D:/project/w4/artifacts/bin/JoinCode/Debug/net10.0/*.dll \
     C:/Users/54076/bin/jcc.d/dev/
cp -f D:/project/w4/artifacts/bin/JoinCode/Debug/net10.0/*.exe \
     C:/Users/54076/bin/jcc.d/dev/
cp -f D:/project/w4/artifacts/bin/JoinCode/Debug/net10.0/*.json \
     C:/Users/54076/bin/jcc.d/dev/
# 额外部署 jcc-gh.dll + jcc-gh.exe (gh 工具独立编译产物)
cp -f D:/project/w4/artifacts/bin/gh/Debug/net10.0/jcc-gh.dll \
     C:/Users/54076/bin/jcc.d/dev/
cp -f D:/project/w4/artifacts/bin/gh/Debug/net10.0/jcc-gh.exe \
     C:/Users/54076/bin/jcc.d/dev/

# 4. 部署后验证(强制) — 对比关键 DLL 时间戳,确认确实更新
ls -la "C:/Users/54076/bin/jcc.d/dev/Mcp.dll" \
       "D:/project/w4/artifacts/bin/JoinCode/Debug/net10.0/Mcp.dll"
ls -la "C:/Users/54076/bin/jcc.d/dev/jcc-gh.dll" \
       "D:/project/w4/artifacts/bin/gh/Debug/net10.0/jcc-gh.dll"
# 两个时间戳应接近(差<2分钟),大小一致;不符则 cp -f 重复制
```

#### 部署坑:robocopy /MIR 跨盘符不更新 DLL

**症状**: `robocopy /MIR` 从 D: 复制到 C: 后,目标 `Mcp.dll` 时间戳/大小未变(旧 DLL),jcc 运行旧代码无新功能。

**根因**: robocopy `/MIR` 默认按时间戳判断"新"文件。跨盘符(D:→C:)时 Windows 文件系统时间戳精度差异(NTFS 100ns vs FAT 2s)或夏令时偏移导致 robocopy 误判"源不比目标新",跳过复制。bash 调用 robocopy 还可能有路径转义问题。

**修复**: 改用 `cp -f`(bash 原生,强制覆盖不依赖时间戳比较),部署后必须对比 DLL 时间戳验证。

#### 部署坑:jcc-gh.dll 遗漏

**症状**: 修改了 `SimpleJqEvaluator.cs` 或 `GitHubToolHandlers.*.cs` 等 gh 工具代码,编译 JoinCode 并部署后,`jcc gh` 仍运行旧代码 — `--jq 'length'` 等新功能不生效。

**根因**: gh 工具是独立项目 `modules/tools/gh/gh.csproj`,编译产物为 `jcc-gh.dll`/`jcc-gh.exe`,输出到 `artifacts/bin/gh/Debug/net10.0/`。`JoinCode.csproj` 不直接引用 `gh.csproj`,所以 `jcc-gh.dll` **不在** `artifacts/bin/JoinCode/Debug/net10.0/` 中。`cp -f artifacts/bin/JoinCode/Debug/net10.0/*.dll` 只复制了 JoinCode 的 DLL,遗漏了 `jcc-gh.dll`。

**修复**: 部署时必须额外从 `artifacts/bin/gh/Debug/net10.0/` 复制 `jcc-gh.dll` 和 `jcc-gh.exe`。部署后对比 `jcc-gh.dll` 时间戳验证。

**通用教训**: 任何独立编译的卫星项目(如 `jcc-rg`、`jcc-audit-cli` 等)都需要在部署流程中额外复制其编译产物,不能只复制主项目 `JoinCode` 的输出。

### 2. 目录结构

```
C:\Users\54076\bin\
├── gh                          # bash 脚本，调 jcc gh "$@"
├── rg                          # bash 脚本，调 jcc rg "$@"
├── jcc                         # bash 脚本，优先找 jcc.d/dev/jcc.exe
├── gh.cmd                      # cmd 脚本，优先 jcc.d/dev/jcc.exe gh %*
├── rg.cmd                      # cmd 脚本，优先 jcc.d/dev/jcc.exe rg %*
├── jcc.cmd                     # cmd 脚本，优先 jcc.d/dev/jcc.exe %*
└── jcc.d\
    └── dev\                    # 编译产物目标目录
        ├── jcc.exe
        ├── jcc.dll
        └── ...                 # 所有依赖 DLL
```

三个工具 `jcc`/`gh`/`rg` 统一指向 `jcc.d\dev\jcc.exe`，优先级：`JCC_BRANCH` 环境变量 > `dev` > `main`。

### 3. gh 问题修复原则

**遇到 `gh` 命令问题时，必须查看/修复 jcc 源码，不是系统 gh CLI。**

- `gh`0` 命令是 `jcc gh` 的 bash 脚本包装，不是系统 gh CLI
- 参数名差异：jcc 用 snake_case（`auto_merge`），系统 gh CLI 用 kebab-case + 缩写（`--auto`/`--squash`）
- 别名映射在 `GhCommandResolver.ResolveGhCliAlias`（长选项别名）和 `ResolveGhShortOption`（短选项映射）
- handler 实现在 `kit/mcp/git_hub/GitHubToolHandlers.*.cs`
- 工具名枚举在 `lib/abstractions/abs_core/core_utils/constants/tool_names/GitHubToolName.cs`

#### 转发层三条铁律（2026-11-16，三次死锁踩出来的）

`gh` / `rg` 都是「jcc 拉起卫星 exe、再透传三条标准流」。任何一条做错都表现为**静默挂死**——不是报错，是零输出卡住，属于最难排查的一类失败。三条约束缺一不可：

1. **stdin 转发不能进 await 链**。Windows 上重定向 stdin 是同步原生 `ReadFile`，取消令牌打断不了阻塞中的读；进 await 链就会出现「子进程已退出、输出也打完了，父进程还挂着」（实测 15 秒不返回）。放独立长驻线程、不 await，线程随进程退出回收。
2. **下游关闭输出时必须终止子进程**。`| head`、`| grep -m1` 这类下游读够就走并关闭管道，本进程写失败；此时若只是「等子进程退出」，它会永远阻塞在写满的管道上。终止它并返回 141，等价 shell 给上游发 SIGPIPE。
3. **程序自己猜出来的输入必须有上限**。「没给路径就猜 stdin」是宽容，但 stdin 可能是打开却永不 EOF 的管道。猜出来的路径设 3 秒上限并明确报错；只有用户**显式**指定的（`-`）才允许无限等。

可观测性配套：这些命令的耗时应在「单层 .NET 启动 200–320 ms × 层数」量级。任何一条明显超时，先怀疑这三条，而不是网络慢或搜索慢。

### 4. rg 引擎与功能

**rg 子命令是 jcc 内置的 ripgrep 兼容搜索**，独立实现于 `RgEngine`（mmap + PLINQ 并行 + 零 GC Span 行遍历）。

#### 引擎选型

| 层面 | 实现 | ADR |
|------|------|-----|
| 正则引擎 | `RegexOptions.NonBacktracking`（.NET 9+ DFA 线性时间 + AOT 兼容 + 无灾难性回溯），不支持原子组等特性时 fallback 到解释器引擎 | 0070 |
| 正则超时 | 5s（`Regex(pattern, options, TimeSpan.FromSeconds(5))`）防御灾难性回溯 | — |
| `-F` 固定字符串 | `ReadOnlySpan<char>.IndexOf(pattern)`（.NET 内部 SIMD 加速），不走 Regex | — |
| ASCII 快速路径 | `MemoryMarshal.Cast<byte, char>` 跳过 UTF-8 解码，直接在 byte span 上搜索 | 0071 |
| mmap 零拷贝 | 大文件(>64KB) 用 `MemoryMappedFile` + `ToArray()` + ASCII 检测 | 0071 |
| 文件级并行 | `AsParallel().WithDegreeOfParallelism(ProcessorCount)` | — |
| 行遍历 | `LineSpanIndexer.BuildLineRanges(ReadOnlySpan<char>)` 零 GC | — |
| 行号定位 | 二分查找 O(log n) | — |

#### 功能对齐标准 rg/grep

| 功能 | 标准 rg | jcc rg | 说明 |
|------|---------|--------|------|
| 正则搜索 | ✅ PCRE2 | ✅ .NET Regex(NonBacktracking) | 引擎不同，AOT 兼容 |
| 默认输出 | ✅ content | ✅ content(默认) | 已对齐（原为 files-with-matches） |
| `-F` 固定字符串 | ✅ SIMD Boyer-Moore | ✅ Span.IndexOf(SIMD) | — |
| `-v` 反向匹配 | ✅ | ✅ | 输出不匹配的行 |
| `-e` 多模式 | ✅ | ✅ | OR 交替合并 |
| `-x` 整行匹配 | ✅ | ✅ | `^(?:pattern)$` |
| `--max-count` | ✅ | ✅ | 每文件匹配上限 |
| `-i/-S/-w/-o/-r/-n` | ✅ | ✅ | 全部对齐 |
| `-A/-B/-C` 上下文 | ✅ | ✅ | 全部对齐 |
| `-l/-c` 文件名/计数 | ✅ | ✅ | 全部对齐 |
| `--json` | ✅ | ✅ | AOT 兼容 DTO 序列化 |
| 裸选项宽容 | ❌ | ✅ | `content`/`count`/`json` 等不带 `--` 也识别 |

#### 宽容策略

1. PowerShell 把 `\s` 传成 `\\s` → 自动修复为 `\s`
2. 裸选项名（`content`/`count`/`files-with-matches`/`json`/`hidden`/`no-ignore`）不带 `--` 前缀也识别
3. 缺少 path → 立即报错退出（禁止无路径搜索，避免扫盘卡死）
4. 根目录（`C:\` / `/`）→ 拒绝扫盘
5. 超时 → 硬终止返回退出码 2
6. 无匹配 → 退出码 1（对齐 rg）
7. 二进制文件自动跳过，遵守 .gitignore
8. NonBacktracking 不支持的正则特性 → fallback 到解释器引擎（同样 AOT 兼容）

### 5. 已知限制

| 问题 | 状态 | 说明 |
|------|------|------|
| jcc 启动 ~4.4 秒 | ⚠️ 待优化 | 瓶颈：`McpInitModule.ConfigureAsync` 中 515 个 MCP 工具通过 AsyncLock 近似串行注册 + 8 个插件通过 Actor mailbox 串行加载 + `WirePluginSkillBridge` 首次解析深依赖链。4.4s 接近 `McpInitPlugin` 的 5s 超时上限。优化方向：批量工具注册(单次锁)、并行插件加载、延迟 schema 构造 |
| optional 参数不能用位置参数 | ⚠️ 设计限制 | `gh repo clone owner/repo target-dir` 报错，需用 `--dir target-dir` |
| `gh api -f` POST 请求 | ⚠️ 设计差异 | `-f` 映射到查询参数 fields，POST 请求需用 `--body` 传请求体 |
| `required_status_checks` 子端点 PUT 返回 404 | ✅ 已查明 | GitHub API 的 `branches/{branch}/protection/required_status_checks` 子端点不支持单独 PUT（返回 404）。必须用完整 `branches/{branch}/protection` 端点 PUT，body 包含完整保护规则（`required_status_checks` + `enforce_admins` + 其他字段）。`gh api --method PUT --body_file <file> repos/{owner}/{repo}/branches/{branch}/protection` 可用 |
| `gh api --method PUT --body_file` | ✅ 可用 | jcc 的 `gh api` handler 已支持 `--method`/`--body`/`--body_file` 参数，通过 `GhArgsBinder.Bind` 正确绑定到 `GhApiAsync` handler。`--body_file` 从文件读取 body，彻底绕开命令行转义问题（推荐） |
| `gh branch audit-protection` | ✅ 可用 | 对比 CI yml matrix 与 GitHub required_status_checks，报告匹配/缺失/多余三类差异。`CiMatrixParser` 解析 yml，`BranchProtectionAuditor` 封装审计逻辑 |
| `gh run view --expand jobs` 截断 | ✅ 已修复(2026-10-08) | `MergeJsonArrays` 支持对象结构分页合并（识别 jobs/check_runs/items 包裹键），双向对比验证 62 jobs 一致 |
| `gh api --jq` 字段提取 | ✅ 已修复(2026-10-08) | `SimpleJqEvaluator` 支持 .field/[]/select(.f=="v")/{k:.f}/\| 管道，AI 无需 Python 二次解析 |
| `--jq` length 函数 | ✅ 已修复(2026-11-04) | `SimpleJqEvaluator` 新增 `length` 函数支持（数组返回元素数，对象返回字段数）。之前 `.check_runs \| length` 返回 `[]` |
| `--jq` and 逻辑 | ✅ 已修复(2026-11-04) | `SimpleJqEvaluator` 新增 `and` 逻辑（与原有 `or` 配合，and 优先级高于 or）。之前 `select(.a=="x" and .b=="y")` 不支持 |
| `--json` + `--jq` 同时使用 | ✅ 已修复(2026-11-04) | `ApplyExplicitFilters` 改为先 `FilterJsonFields` 再 `SimpleJqEvaluator`（之前 jq 优先导致 json_fields 被忽略，`--json headRefOid --jq'headRefOid'` 返回完整 PR 对象） |
| `--json` 字段名映射 | ✅ 已修复(2026-11-04) | `TryResolveFlattenedAlias` 新增 `databaseId→id` 和 `nameWithOwner→full_name` 映射。之前 `--json databaseId` 返回 null |
| `jcc-gh.dll` 部署遗漏 | ✅ 已查明(2026-11-04) | gh 工具是独立项目，编译产物 `jcc-gh.dll` 不在 JoinCode 输出目录中。部署时必须额外从 `artifacts/bin/gh/Debug/net10.0/` 复制 `jcc-gh.dll`+`jcc-gh.exe` |
| `--jq` 仅 gh_api 支持 | ✅ 已修复(2026-10-18) | `GitHubCommonOptions.Jq` 加入公共选项，所有 gh 工具通过 `FormatGhOutput` 统一支持 `--jq` 后处理（优先级：jq > json_fields > verbosity）。27 个 `FormatGhOutput` 调用点 + `GhRunListAsync` 独立处理 |
| `gh run list --workflow` 传显示名称报 404 | ✅ 已修复(2026-10-18) | `ResolveWorkflowIdentifierAsync` 自动将显示名称（如 "CI Daily - Uncovered Tests"）解析为文件名（如 ci-daily-uncovered.yml）：查工作流列表匹配 `name` 字段，提取 `path` 纯文件名。数字 ID/已含 .yml 后缀直接返回 |
| `gh pr checks` 大量 check 截断 | ✅ 已修复(2026-10-08) | check-runs 端点加 per_page=100 + paginate=true，配合对象分页合并获取全部 |
| robocopy /MIR 部署 DLL 不更新 | ✅ 已查明 | 跨盘符(D:→C:)时间戳比较不可靠,robocopy 跳过复制。改用 `cp -f` 强制覆盖+部署后验证 DLL 时间戳 |
| 只复制 jcc.exe 部署 | ✅ 已查明 | jcc.exe(162KB)依赖同目录 Mcp.dll 等多个 DLL,只复制 exe 会运行旧 DLL 导致修复不生效。必须全量复制 *.dll+*.exe+*.json |
| 只复制 dll 不复制 deps.json | ✅ 已查明(2026-11-05) | deps.json 包含程序集依赖图，旧 deps.json 不含新增依赖（如 Abstractions.Guard），导致 `Could not load file or assembly` 错误。必须拷贝 *.deps.json + *.runtimeconfig.json。用 `Deploy-JccDev.ps1` 脚本可避免此问题 |
| `gh run rerun/cancel/wait/download/delete/watch` 用 run number 报 Not Found | ✅ 已修复(2026-10-10) | 6 个 handler 添加 `ResolveRunIdAsync` 自动将短数字(run number)转换为完整 run ID(10+位)。`GhRunViewAsync` 已有此逻辑,其余 6 个遗漏 |
| `gh run view --log` cancelled job 返回原始 404 XML | ✅ 已修复(2026-10-10) | `GetRunLogsAsync`/`GetJobLogsAsync` 检测 404 时返回中文提示"日志不存在(job 可能被取消或未产生日志)",非 404 错误仍返回原始信息 |
| `gh run view --expand steps` cancelled job 只显示 `1 行 ERROR` | ✅ 已修复(2026-10-10) | 提取 `HandleExpandStepsAsync`/`BuildStepsListResultAsync` 扁平化嵌套(满足 JCC10009),日志下载失败时回退到 API job 详情(`GetJobStepsFromApiAsync`),显示步骤名/状态/结论 |
| `gh pr checks --json_fields name,state` 缺 state | ✅ 已修复(2026-10-10) | check-runs API 返回 `status`+`conclusion` 而非 `state`。新增 `FilterCheckRunFields` 映射 `state`(对齐 GraphQL: PENDING/SUCCESS/FAILURE/NEUTRAL) |
| rg 默认输出只文件名 | ✅ 已修复(2026-10-10) | 默认从 `files-with-matches` 改为 `content`（对齐标准 rg）。`-l` 显式指定文件名模式 |
| rg `RegexOptions.Compiled` 不兼容 AOT | ✅ 已修复(2026-10-10) | 改用 `NonBacktracking`（.NET 9+ DFA 线性时间 + AOT 兼容），不支持时 fallback 到解释器 |
| rg `-F` 走 Regex 性能差 | ✅ 已修复(2026-10-10) | `-F` 单独使用时走 `Span.IndexOf`（SIMD 加速），不走 Regex |
| rg 无 ASCII 快速路径 | ✅ 已修复(2026-10-10) | `MemoryMarshal.Cast<byte, char>` 跳过 UTF-8 解码，ASCII 文件直接在 byte span 上搜索 |
| rg 缺 `-v`/`-e`/`-x`/`--max-count` | ✅ 已修复(2026-10-10) | 新增反向匹配/多模式OR/整行匹配/每文件上限 |
| `jcc gh` / `jcc rg` 在 stdin 重定向且未 EOF 时挂死 | ✅ 已修复(2026-11-16) | 两个子命令原来各自 `Console.In.ReadToEndAsync` 把整个 stdin 读完再喂子进程，而 stdin 若是「打开但未关闭」的管道（CI / 包装脚本 / agent 执行器），EOF 永不到来 → 父子互等死锁（实测 20 秒零输出）。现收成 `StdinForwarder`：流式转发 + 放在独立长驻线程且**不进 await 链**（Windows 上重定向 stdin 是同步原生读，`Cancel` 打断不了阻塞中的 ReadFile，await 它仍会挂）。修后 `jcc gh auth status` 1180ms、`jcc rg -c "id:" path` 668ms |
| `jcc rg ... \| head -1` 挂死 8 秒以上 | ✅ 已修复(2026-11-16) | 同一条链上的**第三种**死锁：转发先 `await WaitForExitAsync()` 等子进程、再等输出转发。下游 `head` 读够就退出并关闭管道，本进程写 Console 失败，而子进程仍在往没人读的管道里写、缓冲区满后阻塞在写调用上 —— 父进程永远等不到它退出。现改为输出转发失败时按 SIGPIPE 语义**终止子进程**并返回 141（与 shell 给上游发 SIGPIPE 一致）。修后 `jcc rg ... \| head -1` 0.35s、`jcc gh api user \| head -1` 2.0s |
| `jcc rg <pattern>`（不给路径）挂死 15 秒以上 | ✅ 已修复(2026-11-16) | 解析层在「无路径且 stdin 被重定向」时补一个 `-` 去猜 stdin；`ReadToEnd` 在「打开但永不 EOF」的管道上永不返回。现把猜出来的 `-` 标记为 `StdinPathInferred`，读 stdin 上限 3 秒、超时报错退出；用户**显式**写 `-` 不设上限（`git diff \| jcc rg x -` 这类慢管道要能等完，实测 5 秒后给数据仍成功）。顺带修 `-c`/`-l` 把带 GUID 的临时文件路径当文件名输出（统一显示 `<stdin>`），以及 help 缺 `--replace` 说明 —— `-r` 是 replace 且会吃掉后续参数，使 paths 为空进而触发上面的猜 stdin，正是踩坑入口 |
| 部署时卫星项目旧 DLL 盖掉主项目新 DLL | ✅ 已查明(2026-11-16) | gh 产物含整套引擎（179 文件）。主先拷、插件后拷 → 旧 `Abstractions.Primitives.Tools.dll` 覆盖新编版本，表现为「部署后行为没变」。改为插件先、主项目后，并逐项比大小兜底（见部署流程） |
| `jcc rg` 部署后 TypeLoadException | ✅ 已查明(2026-11-16) | `artifacts/bin/rg/` 从未编译，`dev/` 里是 11-05 的旧 `jcc-rg.exe`，与新版 `Abstractions.Primitives` 不匹配。部署脚本原「输出目录不存在→跳过」把旧 exe 留在原地继续用；现改为缺产物即失败并提示编译 `modules/tools/rg/rg.csproj` |
| `jcc gh api user --jq login` 未应用 jq | ⚠️ 待确认 | 实测返回完整 JSON 而非 `login` 字段值（`auth status` 路径正常）。下次排查 `GhApi` 分支是否绕过 `FormatGhOutput` |

### 6. 启动开销实测（2026-11-16，dev 部署，Debug 框架依赖版）

| 命令 | 耗时 | 构成 |
|------|------|------|
| `jcc --version` | 316 ms | jcc 自身 .NET 启动 + 参数解析 |
| `jcc tool tree` | 323 ms | 同上 + 扫 `tools/` 13 组 node.yaml |
| `jcc tool call file file_read` | 471 ms | jcc 300 + 子进程 `jcc-tool-file.exe` 204 |
| `jcc rg -r -c` | 668 ms | jcc 300 + 子进程 `jcc-rg.exe` 268 |
| `jcc gh auth status` | 1180 ms | jcc 300 + `jcc-gh.exe` 359 + token 校验/网络 |
| 直跑 `jcc-tool-file.exe` | 204 ms | 单个 .NET apphost 启动 + 工具逻辑 |
| 直跑 `jcc-gh.exe` | 359 ms | 同上 + LSM 读 hosts.yml |
| 直跑 `jcc-rg.exe` | 268 ms | 同上 + 递归扫描 13 个 yaml |

结论：**这四条路径都不初始化引擎**，4.4 秒的 `BuildHostAsync` 只在需要宿主上下文的命令（`jcc -p`、MCP 转发）才付。当前基线是「每层 .NET 进程 ~200-320 ms」，两层就是 0.5-0.7 秒。要「瞬间」（<50 ms）只能 NativeAOT；本机 AOT 链接缺 `advapi32.lib`、`reg.exe` 又被安全策略拦截，AOT 体积/启动收益待 CI 补测。

## 原因
1. **文档化部署流程**：新开发者需要知道如何编译和部署 jcc，避免手动复制遗漏 DLL
2. **明确修复方向**：AI 训练数据中系统 gh CLI 用法占主导，遇到问题时可能误修系统 gh CLI 而非 jcc 源码
3. **记录已知限制**：避免重复排查已分析过的问题（启动开销、optional 位置参数等）

## 替代方案

1. **用系统 gh CLI** — 已拒绝（ADR 0089：禁止系统 gh CLI，GitHub 的 PR/CI 一律走 jcc 自带工具）
2. **Release + AOT 编译** — 启动开销会从 6 秒降到 <0.5 秒，但 Debug 开发模式仍慢
3. **Host 缓存** — CLI 子命令间复用 Host，但进程退出后缓存丢失，需 daemon 模式

## 参考

- ADR 0089: 禁止系统 gh CLI
- ADR 0090: jcc gh CLI 子命令
- `app/cli/core/commands/core/GhCommandResolver.cs` — 别名/短选项映射
- `app/cli/core/commands/core/GhSubCommand.cs` — gh 子命令入口
- `lib/infrastructure/io/process/GitCommandRunner.cs` — git 命令执行器（含默认超时）

---

## 2026-11-16 补记：host 工具「看起来隔离、其实全不可用」与调用耗时

### 真缺陷：411 个 host 工具在 CLI 里 100% 不可达

`jcc tool call <组> <工具>` 对 host 工具转发 `jcc mcp_call`。但 `McpCliCommand.BuildHostAsync` 长期用
`SkipChatServices=true` 建宿主，`EngineApplicationBuilder.BuildHost` 因此只注册 `gh_*` 工具（原本为 `jcc gh` 提速）；
非 gh 工具在注册表里根本不存在，报「未找到工具」。远端没有 Hub 时更早一步就被「Hub 服务不可达」挡住。
**这不是能力边界，是调用链断了**——`config_list` / `skill_list` / `build_output` 这些纯本地服务工具也全挂。

修法（三处、都只动元命令路径）：

1. `CommandLineOptions.ToolNameFilter` → `BuildHost` 只放行本次要执行的工具名，注册表只构造那一个 handler（实测 3 个 handler → 1 个工具，注册 30ms）。
2. `BuildHostAsync` 内用 `EnvVarScope` 临时允许直连配置：工具元命令不读模型配置，不该被 `jcc -p` 的 Hub 门槛挡住；退出即恢复默认保护。
3. 新增 `SkipProviderConfig`：工具元命令只从 `settings.json` 取 UI 偏好，不构 `ProviderDefinitionRegistry`。
   `JCC_DEBUG_TIMING=1` 实测 `LoadConfig` **1282ms → 约 200ms**；`jcc tool call system config_list` **2.5s → 1.4s**。
   需要全量目录的工具（`tool_search` / `tool_score` / `tool_hypergraph` / `tool_list_templates`）走 `SkipToolFilter`，多花约 1 秒。
4. `requiredTool` 已在编译期注册表时就跳过 1.6s 的 MCP 插件后台链等待；只有查不到时才等。

副作用修正：`SessionIdFactory.GetCurrentBranch` 原本不重定向 stderr，非仓库目录下每次调用都会把
`fatal: not a git repository` 混进工具的 stderr，看上去像工具报错；现在排空 stderr 后消失。

### 目录 id 必须是运行时名（`[EnumValue]`），不是枚举成员名

生成器 `migrate_mcp_tools_to_cli_nodes.py` 原先用 `to_snake(CodeToolNameEnumConstants.CodeIndexStats)` 得到
`code_index_stats`，而内核真正注册的名字是 `[EnumValue("search_stats")]`。同类假 id 共 **33 个**：
`file_read/write/edit/apply_patch`、`code_index_*`→`search_*`、`mcp_auth_o_auth2`→`mcp_auth_oauth2`、
`sub_agent_control`→`subagent_control` 等。后果分两层：

- `jcc tool call search code_index_stats` 被目录校验层拦（注册表只有 `search_stats`）；
- **首轮 tools 只剩 bash 一个**：`BuildBaseTools` 用 `function.Name` 去 `BaseToolRegistry.Get()`，
  `base.yaml` 写 `file_read` 而内核叫 `read`，三条全匹配不上，LLM 根本拿不到文件工具。

修复：生成器按枚举类型 + `[EnumValue]` 解析真名（处理 `XxxEnumConstants` 包装类与同名成员歧义），解析不到即 fail-fast；
`file` 组本地实现键与 `base.yaml` / `ToolsSection` / 相关测试同步改为 `read` / `write` / `edit` / `apply_patch`。
真机复核：`jcc tool base` 4 条齐；`mcp_schema read` 有 schema；`read → write → edit → read` 闭环退出 0；
`jcc tool call search search_stats` 退出 0。改名不改变总数；同轮补收 `list_peers` / `subscribe_pr` 两个真实工具后，目录总数由 638 调整为 **640**（541 MCP + 99 slash）。

### gh 的 jq 两个缺陷

- `gh api user -q .login` 报「多余的位置参数」：`-q→jq` 的短选项规则只覆盖 view/list，现补 `gh_api`。
- `--jq login`（漏点号）静默打印整份 JSON：`SimpleJqEvaluator` 对不支持的管道阶段是 `continue`，
  用户以为筛选生效。改成 `TryEvaluate` 返回原因，`FormatGhOutput` / `gh_api` / `gh_run_list` 统一抛
  `JqSyntaxException`，`GhSubCommand` 转成 stderr + 退出码 2。现有「不支持语法宽容跳过」测试已按新语义改写。

**测试**：gh.tests 787/787、契约层 33/33、llm.tests 613/613、prompt ToolsSection 4/4、host.tests CLI 相关 216/216。
**编译**：改动项目逐个 0 警告 0 错误；未跑全仓编译（用户明确要求只编改动项目）。
