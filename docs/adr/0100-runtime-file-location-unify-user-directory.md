# 0100. 运行时文件落地位置统一到用户目录

- 状态：accepted
- 日期：2026-09-11
- 决策者：AI + 用户确认

## 背景

jcc.exe 运行时会在多个位置创建文件：用户目录 `~/.jcc/`、项目目录 `{cwd}/.jcc/`、临时目录 `%TEMP%`、Roaming AppData `%APPDATA%/JoinCode/`、exe 旁 `AppContext.BaseDirectory`、当前工作目录 `{cwd}/`。全面普查发现 **54 处已统一到 `~/.jcc/`**，但仍有 **12 处** 散落在 `%TEMP%`、`%APPDATA%`、`{cwd}/` 和相对路径（落 cwd），需统一。

### 问题清单

| 编号 | 当前位置 | 问题 |
|------|----------|------|
| P0 | `%TEMP%/jcc_error.log` 等 8 处 | 持久性日志落临时目录，OS 清理时丢失；多实例散落无法集中分析 |
| P1 | `%APPDATA%/JoinCode/*.json` 2 处 | 与 `~/.jcc/` 体系割裂，用户难以找到 |
| P2 | `{cwd}/.jcctui_perf/perf.log` | 污染用户工作目录 |
| P3 | `.jcc/analytics/`（相对路径，落 cwd） | 污染构建产物目录；exe 在只读位置时写入失败；`dotnet clean` 丢失 |

### 已有基础设施

- `AppDataPaths`（`foundation/Abstractions/00-core/Configuration/AppData/AppDataPaths.cs`）— 不可变 record，统一管理 `~/.jcc/` 下子路径，支持环境变量覆盖
- `XdgPathResolver`（`app/JoinCode/Cli/Output/XdgPathResolver.cs`）— XDG 标准路径解析器，已预留 `GetRuntimeDirectory()`（当前返回 `%TEMP%/jcc/`）
- `WorkflowConstants.Paths.JccDirectory` — 统一使用 `UserProfile/.jcc/`

## 决策

将所有**用户级运行时文件**统一到 `~/.jcc/runtime/`，遥测统一到 `~/.jcc/analytics/`，工具配置统一到 `~/.jcc/`。项目级 `{cwd}/.jcc/` 和临时性质文件保留不动。

### 统一后的目录结构

```
~/.jcc/                          # 用户级根目录（已有，54 项已在此）
├── analytics/                   # P3: 遥测事件（从相对路径改为绝对）
├── runtime/                     # P0/P2: 新增运行时目录
│   ├── jcc_error.log           #   错误日志（从 %TEMP%）
│   ├── jcc_await_timeout.log   #   超时日志（从 %TEMP%）
│   ├── crash-dumps/            #   崩溃快照（从 %TEMP%/jcc/）
│   ├── jcctui_diag/run.log     #   TUI 诊断（从 %TEMP%/jcctui_diag/）
│   ├── tool-results/           #   工具结果溢出（从 %TEMP%/jcc-tool-results/）
│   ├── bridge-session-{id}.log #   Bridge 子进程日志（从 %TEMP%/.jcc/）
│   ├── clipboard/              #   复制命令回退（从 %TEMP%/.jcc/）
│   └── perf.log                #   性能埋点（从 {cwd}/.jcctui_perf/）
├── tool-interventions.json      # P1: 从 %APPDATA%/JoinCode/ 移入
├── tool-health.json             # P1: 从 %APPDATA%/JoinCode/ 移入
└── ... (其他已有)
```

### 实现策略

1. **`XdgPathResolver.GetRuntimeDirectory()`** 改为返回 `~/.jcc/runtime/`（而非 `%TEMP%/jcc/`），保留 `XDG_RUNTIME_DIR` 环境变量覆盖
2. **`XdgPathResolver.GetErrorLogPath()` / `GetAwaitTimeoutLogPath()`** 改用 `GetRuntimeDirectory()` 而非 `Path.GetTempPath()`
3. **硬编码 `%TEMP%` 的持久性日志**改用 `XdgPathResolver.GetRuntimeDirectory()` 或 `AppDataPaths`
4. **`%APPDATA%/JoinCode/`** 改用 `XdgPathResolver.GetConfigDirectory()`（即 `~/.jcc/`）
5. **`AnalyticsFileSink` 默认 `outputDirectory`** 改为绝对路径 `~/.jcc/analytics/`（用 `Environment.GetFolderPath(SpecialFolder.UserProfile)`，不引入新依赖）

### 保留不动的

| 分类 | 原因 |
|------|------|
| 项目级 `{cwd}/.jcc/`（15 项） | 随项目走，项目隔离合理 |
| `%TEMP%` 临时文件（6 项） | TempDirScope/沙箱/cwd 跟踪等用后即删，临时性质合理 |
| 第三方约定（`~/.config/gh`、`~/.codex/`、`~/.npm/`、`ProgramData`） | 第三方工具约定，不可改 |

## 替代方案

### 方案 A：全部统一到 `%LOCALAPPDATA%/JoinCode/`（Windows 惯例）

- 优点：符合 Windows Store App 惯例
- 缺点：与项目已有 `~/.jcc/` 体系割裂，需迁移 54 项现有路径
- **放弃**：迁移成本高，且 `~/.jcc/` 已是项目事实标准

### 方案 B：保留 `%TEMP%` 但改用 `%TEMP%/jcc/` 子目录

- 优点：改动最小
- 缺点：OS 清理临时目录时日志丢失，无法持久保留运行时诊断信息
- **放弃**：运行时日志需要跨重启保留用于诊断

### 方案 C：每个文件类型单独配置路径

- 优点：最大灵活性
- 缺点：配置膨胀，用户认知负担重
- **放弃**：约定大于配置，统一 `runtime/` 子目录足够

## 验证

- [ ] P0：`XdgPathResolver.GetRuntimeDirectory()` 返回 `~/.jcc/runtime/`
- [ ] P0：`GetErrorLogPath()` / `GetAwaitTimeoutLogPath()` 返回 `~/.jcc/runtime/` 下
- [ ] P0：`ReplLoopStep` / `NonInteractiveExecuteStep` 用 `XdgPathResolver` 而非硬编码 `%TEMP%`
- [ ] P0：`JoinCodeTui` 诊断日志落 `~/.jcc/runtime/jcctui_diag/`
- [ ] P0：`SystemActuatorCommandContext` 工具结果溢出落 `~/.jcc/runtime/tool-results/`
- [ ] P0：`BridgeSubprocessManager` 日志落 `~/.jcc/runtime/`
- [ ] P0：`CopyCommand` 回退落 `~/.jcc/runtime/clipboard/`
- [ ] P1：`ToolInterventionManager` / `ToolHealthMonitor` 落 `~/.jcc/`
- [ ] P2：`PerfTap` 性能日志落 `~/.jcc/runtime/`
- [ ] P3：`AnalyticsFileSink` 默认路径为 `~/.jcc/analytics/`（绝对）
- [ ] 编译通过 + 单元测试通过
