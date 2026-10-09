# ADR 0132: jcc 编译产物部署与 gh 问题修复指南

## 状态

accepted

## 背景

jcc 是独立实现的 GitHub CLI 工具（HttpClient 直调 REST API），不是系统 gh CLI 的包装/转发。`gh` 命令通过 bash 脚本转发到 `jcc gh`，编译产物部署到 `C:\Users\54076\bin\jcc.d\dev\`。

开发过程中发现 `gh` 命令执行时间超长（~6 秒启动开销），排查后发现多个问题：
1. jcc 启动开销 ~6 秒（`BuildHostAsync` 每次创建完整 DI 容器 + 注册 400+ MCP 工具）
2. `gh repo clone` 的 optional 参数不能用位置参数
3. 20 个 `_git.ExecuteAsync` 调用没有超时，git 命令卡住会一直等
4. `gh api -f` 短选项不支持

## 决策

### 1. 编译产物部署流程

```bash
# 1. 编译（Debug 模式，增量编译）
dotnet build app/cli/JoinCode.csproj -c Debug

# 2. 新增/修改 [Register] 类后必须 --no-incremental 全量重建
dotnet build app/cli/JoinCode.csproj --no-incremental -c Debug

# 3. 部署编译产物到 dev 目录 — ⚠️ 必须全量复制,禁止只复制 jcc.exe
# ⚠️ 禁止用 robocopy /MIR — 跨盘符(D:→C:)时时间戳比较不可靠,DLL 不更新(见下方"部署坑")
# ⚠️ 禁止只复制 jcc.exe — jcc.exe(162KB)依赖同目录 Mcp.dll 等多个 DLL,只复制 exe 会运行旧 DLL
# ✅ 用 cp -f 全量复制 dll+exe+json(bash 原生,跨盘符可靠)
cp -f D:/project/w3/artifacts/bin/JoinCode/Debug/net10.0/*.dll \
     C:/Users/54076/bin/jcc.d/dev/
cp -f D:/project/w3/artifacts/bin/JoinCode/Debug/net10.0/*.exe \
     C:/Users/54076/bin/jcc.d/dev/
cp -f D:/project/w3/artifacts/bin/JoinCode/Debug/net10.0/*.json \
     C:/Users/54076/bin/jcc.d/dev/

# 4. 部署后验证(强制) — 对比关键 DLL 时间戳,确认确实更新
ls -la "C:/Users/54076/bin/jcc.d/dev/Mcp.dll" \
       "D:/project/w3/artifacts/bin/JoinCode/Debug/net10.0/Mcp.dll"
# 两个时间戳应接近(差<2分钟),大小一致;不符则 cp -f 重复制
```

#### 部署坑:robocopy /MIR 跨盘符不更新 DLL

**症状**: `robocopy /MIR` 从 D: 复制到 C: 后,目标 `Mcp.dll` 时间戳/大小未变(旧 DLL),jcc 运行旧代码无新功能。

**根因**: robocopy `/MIR` 默认按时间戳判断"新"文件。跨盘符(D:→C:)时 Windows 文件系统时间戳精度差异(NTFS 100ns vs FAT 2s)或夏令时偏移导致 robocopy 误判"源不比目标新",跳过复制。bash 调用 robocopy 还可能有路径转义问题。

**修复**: 改用 `cp -f`(bash 原生,强制覆盖不依赖时间戳比较),部署后必须对比 DLL 时间戳验证。

### 2. 目录结构

```
C:\Users\54076\bin\
├── gh                          # bash 脚本，调 jcc gh "$@"
├── jcc                         # bash 脚本，优先找 jcc.d/dev/jcc.exe
└── jcc.d\
    └── dev\                    # 编译产物目标目录
        ├── jcc.exe
        ├── jcc.dll
        └── ...                 # 所有依赖 DLL
```

### 3. gh 问题修复原则

**遇到 `gh` 命令问题时，必须查看/修复 jcc 源码，不是系统 gh CLI。**

- `gh`0` 命令是 `jcc gh` 的 bash 脚本包装，不是系统 gh CLI
- 参数名差异：jcc 用 snake_case（`auto_merge`），系统 gh CLI 用 kebab-case + 缩写（`--auto`/`--squash`）
- 别名映射在 `GhCommandResolver.ResolveGhCliAlias`（长选项别名）和 `ResolveGhShortOption`（短选项映射）
- handler 实现在 `kit/mcp/git_hub/GitHubToolHandlers.*.cs`
- 工具名枚举在 `lib/abstractions/abs_core/core_utils/constants/tool_names/GitHubToolName.cs`

### 4. 已知限制

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
| `gh pr checks` 大量 check 截断 | ✅ 已修复(2026-10-08) | check-runs 端点加 per_page=100 + paginate=true，配合对象分页合并获取全部 |
| robocopy /MIR 部署 DLL 不更新 | ✅ 已查明 | 跨盘符(D:→C:)时间戳比较不可靠,robocopy 跳过复制。改用 `cp -f` 强制覆盖+部署后验证 DLL 时间戳 |
| 只复制 jcc.exe 部署 | ✅ 已查明 | jcc.exe(162KB)依赖同目录 Mcp.dll 等多个 DLL,只复制 exe 会运行旧 DLL 导致修复不生效。必须全量复制 *.dll+*.exe+*.json |
| `gh run rerun/cancel/wait/download/delete/watch` 用 run number 报 Not Found | ✅ 已修复(2026-10-10) | 6 个 handler 添加 `ResolveRunIdAsync` 自动将短数字(run number)转换为完整 run ID(10+位)。`GhRunViewAsync` 已有此逻辑,其余 6 个遗漏 |
| `gh run view --log` cancelled job 返回原始 404 XML | ✅ 已修复(2026-10-10) | `GetRunLogsAsync`/`GetJobLogsAsync` 检测 404 时返回中文提示"日志不存在(job 可能被取消或未产生日志)",非 404 错误仍返回原始信息 |
| `gh run view --expand steps` cancelled job 只显示 `1 行 ERROR` | ✅ 已修复(2026-10-10) | 提取 `HandleExpandStepsAsync`/`BuildStepsListResultAsync` 扁平化嵌套(满足 JCC10009),日志下载失败时回退到 API job 详情(`GetJobStepsFromApiAsync`),显示步骤名/状态/结论 |
| `gh pr checks --json_fields name,state` 缺 state | ✅ 已修复(2026-10-10) | check-runs API 返回 `status`+`conclusion` 而非 `state`。新增 `FilterCheckRunFields` 映射 `state`(对齐 GraphQL: PENDING/SUCCESS/FAILURE/NEUTRAL) |

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
