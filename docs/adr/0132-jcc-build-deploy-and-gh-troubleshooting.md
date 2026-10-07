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

# 3. 拷贝编译产物到部署目录（用 PowerShell robocopy /MIR 镜像）
powershell -Command "robocopy 'D:\project\w2\artifacts\bin\JoinCode\Debug\net10.0' 'C:\Users\54076\bin\jcc.d\dev' /MIR /NJH /NJS"
# EXIT=0 表示无变化，EXIT=1 表示有文件被拷贝
```

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
