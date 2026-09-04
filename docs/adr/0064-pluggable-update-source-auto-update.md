# 0064. 可插拔更新源与自动更新

- 状态：proposed
- 日期：2026-09-05
- 决策者：AI + 用户确认

## 背景

现有更新机制（`UpgradeService`）仅做版本检查（查 GitHub API），`/upgrade` 命令只提示"请访问 GitHub Releases 下载"，无自动下载安装。代码注释明确说明："TS 有自动下载安装，C# 为手动下载（NativeAOT 单文件发布）"。

但项目已有完整的 `RangeDownloader`（多线程断点续传）基建未接入自更新。`IUpgradeService` 接口缺 `DownloadUpdateAsync`/`ApplyUpdateAsync`，无法形成"检查→下载→替换→重启"闭环。

用户需求：设计一套虚拟服务器，根据服务器地址获取信息更新 jcc.exe，且更新源形态可配置切换。

## 决策

采用**可插拔更新源**设计：定义 `IUpdateSource` 抽象接口，提供四种实现，通过 `settings.json` 的 `update.sourceType` 配置切换。扩展 `IUpgradeService` 实现完整自动更新闭环。

### 1. 更新源类型枚举

```csharp
public enum UpdateSourceType
{
    [EnumValue("static")] Static,        // 静态文件托管（manifest.json + exe）
    [EnumValue("api")] HttpApi,          // HTTP API 服务器（动态端点）
    [EnumValue("github-mirror")] GitHubMirror,  // GitHub Release 镜像代理
    [EnumValue("local")] LocalFile,     // 本地文件清单（file:// 或 UNC）
}
```

### 2. UpdateManifest 数据结构

```csharp
public record UpdateManifest
{
    public required string LatestVersion { get; init; }
    public required string Channel { get; init; }  // stable/beta/canary
    public required IReadOnlyList<UpdateManifestEntry> Releases { get; init; }
}

public record UpdateManifestEntry
{
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
    public required string Sha256 { get; init; }  // 完整性校验
    public long SizeBytes { get; init; }
    public string? ReleaseNotes { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public string? MinUpgradeFrom { get; init; }  // 最低可升级版本（防止降级）
}
```

**manifest.json 示例**（静态文件托管格式）：
```json
{
  "latestVersion": "1.2.0",
  "channel": "stable",
  "releases": [
    {
      "version": "1.2.0",
      "downloadUrl": "https://update.jcc.dev/v1.2.0/jcc.exe",
      "sha256": "abc123...",
      "sizeBytes": 45000000,
      "releaseNotes": "修复...",
      "publishedAt": "2026-09-05T10:00:00Z"
    }
  ]
}
```

### 3. IUpdateSource 接口

**位置**：`foundation/Abstractions/00-core/Interfaces/Application/IUpdateSource.cs`

```csharp
public interface IUpdateSource
{
    UpdateSourceType Type { get; }
    Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default);
    Task<Stream> DownloadAsync(
        UpdateManifestEntry entry,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);
}
```

### 4. 四种实现

| 实现 | 位置 | 清单获取 | 下载方式 |
|------|------|---------|---------|
| `StaticFileUpdateSource` | `core/execution/Hands/src/Update/Sources/` | HTTP GET manifest.json | RangeDownloader（DownloadUrl 绝对/相对 URL） |
| `HttpApiUpdateSource` | 同上 | POST /api/version/check → JSON | GET /api/download/{version} 流式 |
| `GitHubMirrorUpdateSource` | 同上 | GET /releases/latest（镜像 GitHub API 格式） | RangeDownloader（镜像资产 URL） |
| `LocalFileUpdateSource` | 同上 | File.ReadAllText(manifest.json) | File.OpenRead（本地路径） |

**工厂**：`UpdateSourceFactory.Create(UpdateSourceConfig)` 根据 `sourceType` 返回对应实现。

### 5. UpdateSourceConfig

在 `settings.json` 新增 `update` 节点：

```json
{
  "update": {
    "sourceType": "static",
    "manifestUrl": "https://update.jcc.dev/manifest.json",
    "autoUpdate": false,
    "checkOnStartup": true,
    "checkIntervalHours": 24,
    "channel": "stable"
  }
}
```

- `sourceType`：四种之一，默认 `static`
- `manifestUrl`：清单地址（HTTP URL / 本地路径 / UNC 路径）
- `autoUpdate`：是否自动下载安装（false=仅通知）
- `checkOnStartup`：启动时检查更新
- `channel`：更新通道

### 6. 扩展 IUpgradeService

```csharp
public interface IUpgradeService
{
    // 现有
    Version GetCurrentVersion();
    Task<Version?> GetLatestVersionAsync(CancellationToken ct = default);
    Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default);
    // 新增
    Task<UpdateManifestEntry?> GetUpdateEntryAsync(CancellationToken ct = default);
    Task<UpdateResult> DownloadUpdateAsync(
        UpdateManifestEntry entry,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);
    Task<UpdateResult> ApplyUpdateAsync(string downloadedExePath, CancellationToken ct = default);
}

public record UpdateResult(bool Success, string? ErrorMessage, string? DownloadedPath);
```

### 7. 自动更新流程（Windows 原子替换）

```
1. GetUpdateEntryAsync → 获取最新版本条目
2. DownloadUpdateAsync → 下载到 %TEMP%\jcc-update\jcc.exe.new
   - 用 RangeDownloader（多线程断点续传）
   - 下载完 SHA256 校验，不匹配删除并报错
3. ApplyUpdateAsync → 原子替换：
   a. currentExe = Process.GetCurrentProcess().MainModule.FileName
   b. backup: File.Move(currentExe, currentExe + ".old")  // 同卷原子
   c. replace: File.Move(downloadedExePath, currentExe)
   d. 失败回滚: File.Move(currentExe + ".old", currentExe)
   e. 成功清理: File.Delete(currentExe + ".old")
4. 提示用户重启生效（或 --restart 自动重启）
```

**Windows 原子替换注意**：
- `File.Move` 同卷是原子的，跨卷不是 → 临时目录用 `%TEMP%` 可能跨卷，改为 exe 同目录的 `.update` 子目录
- exe 运行中 Windows 允许重命名/移动（不像 Linux 需要 unlink），但不能覆写 → 先备份再替换
- 替换后旧 exe 仍驻留内存，新 exe 下次启动生效

### 8. 虚拟更新服务器

**位置**：`services/Update/`（生产级，可独立部署）

```
services/Update/
  src/
    UpdateServer/           # Kestrel HTTP 服务器
      UpdateServer.cs       # 主机
      UpdateServerEndpoints.cs  # 端点定义
    UpdateServer.Abstractions/
      IUpdateManifestProvider.cs
  content/
    manifest.json           # 示例清单
    releases/               # 版本资产目录
      v1.2.0/jcc.exe
  tests/
    UpdateServer.Tests/
```

**端点**：
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/manifest.json` | 静态清单（StaticFile 模式） |
| GET | `/releases/{version}/jcc.exe` | 静态资产下载 |
| POST | `/api/version/check` | 动态版本检查（HttpApi 模式） |
| GET | `/api/download/{version}` | 动态流式下载 |
| GET | `/health` | 健康检查 |

复用现有 Kestrel 基建（参考 `tests/MockServers/MockServer.Core/KestrelMockServer.cs`）。

### 9. /upgrade 命令改造

```
/upgrade              → 检查 + 提示下载（现有行为，autoUpdate=false）
/upgrade --download    → 检查 + 下载到临时目录 + 校验
/upgrade --apply       → 下载 + 原子替换 + 提示重启
/upgrade --auto        → 全自动（检查+下载+替换+重启）
/upgrade --channel beta → 指定通道
/upgrade --source local --manifest-url D:\updates\manifest.json → 临时指定源
```

## 替代方案

- **仅支持 GitHub Release**：改动最小但无法满足"虚拟服务器"需求，且国内访问 GitHub 慢。未采用。
- **仅支持自建 HTTP API**：功能强但过度工程，静态文件托管已满足多数场景。未采用。
- **用 Squirrel/ClickOnce 等现成更新框架**：不支持 NativeAOT，且引入外部依赖。未采用。
- **下载后启动独立 updater.exe 替换**：比进程内替换更安全（不依赖 Windows 允许重命名运行中 exe），但多一个二进制。作为未来增强保留。

## 后果

- 正面：完整自动更新闭环（检查→下载→校验→替换→重启），四种更新源可配置切换，复用现有 RangeDownloader 基建，虚拟服务器可独立部署。
- 负面：新增 `IUpdateSource` 抽象 + 四种实现 + 服务器工程，代码量增加。原子替换在 Windows 上依赖"运行中 exe 可重命名"特性，若未来改用独立 updater.exe 更安全但更复杂。
- 中性：`IUpgradeService` 接口扩展，现有消费方（`UpgradeCommand`）需适配新方法。

## 反向引用

- AGENTS.md「ADR 0063」— 更新源地址通过 `JccEndpoints.DefaultUpdateManifestUrl` + `JCC_UPDATE_MANIFEST_URL` 环境变量管理
- AGENTS.md「枚举 + [EnumValue] 使用规范」— `UpdateSourceType` 枚举 + 源码生成器
- AGENTS.md「规则2 MCP工具覆盖原则」— `/upgrade` 命令扩展走 `[ChatCommand]` 模式
- AGENTS.md「交付优先级」— 先实现 StaticFile 源（开心路径），其他三种源后续补充
