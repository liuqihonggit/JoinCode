# RangeDownloader 基建 PRD

> 版本: v1.0  日期: 2026-08-25  状态: 草案

## 1. 背景

### 1.1 问题现状

当前项目 MCP 调 bash 下载大文件存在三个性能瓶颈:

| 瓶颈 | 根因 | 证据 |
|------|------|------|
| 单线程串行下载 | bash 工具直接起进程跑 `curl -O url`,单连接 | `ShellExecutionMiddleware.cs:27` `StartWithBackgroundSupportAsync` 起进程等结果 |
| 无断点续传 | 网络中断 → 重头来过,大文件不可恢复 | 全局无 HTTP `Range`/`Accept-Ranges`/`Content-Range` 下载逻辑(仅 `HttpRequestSerializer.cs:174` 做 HTTP 头序列化) |
| Auto 模式禁 curl/wget | `RemoteExecutionRiskHandler.cs:17` Auto 模式直接拒绝 curl/wget | 要求改用 WebFetch,但 WebFetch 是抓网页(HTML→Markdown),不下二进制 |

### 1.2 现有代码排查结论

- **多线程下载**: 无。且 `PerformanceRules.cs:89` **禁止 `Parallel.For/ForEach`**,要求 PLINQ 或 `Task.WhenAll`
- **断点续传**: 无 HTTP Range 头实现
- **aria2/axel 集成**: 无
- **FileTransferService**: 仅生成 localhost 下载链接,无实际传输
- **UpgradeService**: 仅查 GitHub releases API 读 tag_name,不下二进制
- **MCPB 流拷贝**: `McpbExtractionMiddleware.cs:75` 用 `CopyToAsync` 单线程顺序写

### 1.3 为什么不直接改 bash 拦截

用户明确要求:**独立做,先做基建,不要接入**。理由:
1. 基建先行,验证通过后再考虑接入点(bash 拦截 / 新 MCP 工具 / 内部服务调用)
2. 避免一次性大改动引入风险,符合渐进式开发原则
3. 独立模块可单独测试,不依赖主链路 E2E

## 2. 目标

### 2.1 核心目标

构建一个**独立的多线程断点续传下载器基建**,满足:

| 目标 | 说明 |
|------|------|
| **断点续传** | 基于 HTTP `Range` 头,网络中断后可从已下载位置继续,不重头 |
| **多线程可选** | 通过 `MaxThreads` 参数控制: `1`=单线程顺序, `>1`=多线程并发分片 |
| **PLINQ 并发** | 用 PLINQ `.AsParallel().WithDegreeOfParallelism(n)` 做分片并发,符合项目规范(禁 `Parallel.For/ForEach`) |
| **独立基建** | 不注册为 MCP 工具,不接入 bash 拦截,不接入主链路。仅提供接口+实现+单元测试 |
| **AOT 兼容** | 禁 `dynamic`/反射 emit,用 `JsonSerializer` + `JsonContext` 源码生成器 |

### 2.2 非目标(本期不做)

- ❌ 不接入 bash 命令拦截(不做 curl/wget 改写)
- ❌ 不暴露为 MCP 工具(不做 `download_file` 工具)
- ❌ 不接入 DI 自动注册(不挂 `[Register]`,由调用方手动构造)
- ❌ 不做 E2E 集成测试(纯单元测试,不依赖真实网络)
- ❌ 不做下载速度限制/限流(后续接入时再加)
- ❌ 不做代理支持(复用传入的 `HttpClient`,代理由调用方配置)

## 3. 功能需求

### 3.1 FR-1: Range 支持探测

**输入**: URL + HttpClient  
**输出**: `RangeSupportResult { SupportsRange, ContentLength, ETag, LastModified }`  
**逻辑**:
1. 发送 `HEAD` 请求
2. 检查响应头 `Accept-Ranges: bytes` → `SupportsRange = true`
3. 读取 `Content-Length` → `ContentLength`
4. 读取 `ETag` / `Last-Modified` → 用于断点续传校验资源未变更
5. 若 HEAD 不支持(405),回退用 `GET` + `Range: bytes=0-0` 探测

### 3.2 FR-2: 分片规划

**输入**: `ContentLength` + `MaxThreads` + `ChunkSize`  
**输出**: `IReadOnlyList<DownloadChunk>`  
**逻辑**:
- `MaxThreads = 1` → 单分片 `[0, contentLength-1]`
- `MaxThreads > 1` → 按 `ChunkSize` 切分,每个分片 `[start, end]` 闭区间
- 最后一个分片 `end = contentLength - 1`(处理不能整除的情况)
- 分片大小默认: `contentLength / maxThreads`,最小 `1MB`,最大 `16MB`

### 3.3 FR-3: 断点续传元数据

**存储**: `{目标文件路径}.meta.json`  
**结构**:
```json
{
  "url": "https://...",
  "totalLength": 10485760,
  "eTag": "\"abc123\"",
  "lastModified": "2026-08-25T10:00:00Z",
  "chunks": [
    { "index": 0, "start": 0, "end": 1048575, "downloaded": 1048576, "completed": true },
    { "index": 1, "start": 1048576, "end": 2097151, "downloaded": 500000, "completed": false }
  ]
}
```

**逻辑**:
- 下载开始前: 若 `.meta.json` 存在且 URL/ETag/LastModified 匹配 → 复用已 completed 的分片,跳过
- 下载过程中: 每个分片每写入 N 字节更新 `downloaded` 字段(持久化到 `.meta.json`)
- 下载完成后: 删除 `.meta.json` 和所有 `.part` 临时文件

### 3.4 FR-4: 单分片下载

**输入**: `DownloadChunk` + URL + HttpClient + 目标 `.part` 文件路径  
**逻辑**:
1. 构造 `Range: bytes={start+downloaded}-{end}`(支持从分片中间续传)
2. `GET` 请求,`HttpCompletionOption.ResponseHeadersRead` 立即返回头
3. 流式 `CopyToAsync` 写入 `.part` 文件(`FileShare.ReadWrite`,符合 `JCC9006`)
4. 定期(每 64KB 或 1s)更新元数据 `downloaded` 字段
5. 写完标记 `completed = true`

### 3.5 FR-5: 多线程协调下载(PLINQ)

**输入**: URL + 文件路径 + `DownloadOptions`  
**逻辑**:
1. 探测 Range 支持 → 获取 `ContentLength`
2. 检查 `.meta.json` → 恢复或新建分片计划
3. 过滤未 completed 的分片
4. **PLINQ 并发**:
   ```csharp
   var results = pendingChunks
       .AsParallel()
       .WithDegreeOfParallelism(options.MaxThreads)
       .Select(chunk => chunkDownloader.DownloadAsync(chunk, ct))
       .ToArray();
   ```
5. 所有分片完成 → 合并 `.part` 文件为目标文件(按 index 顺序拼接)
6. 删除 `.meta.json` 和 `.part` 文件
7. 返回 `DownloadResult`

### 3.6 FR-6: 进度报告

**回调**: `IProgress<DownloadProgress>`  
**结构**: `DownloadProgress { TotalBytes, DownloadedBytes, SpeedBps, Percent, State, IsResumed }`  
**频率**: 每 500ms 或每 1MB 报告一次

### 3.7 FR-7: 状态机控制(暂停/继续/结束)

**核心**: `IDownloadSession` 暴露 `PauseAsync`/`ResumeAsync`/`CancelAsync`/`WaitForCompletionAsync`,由 `DownloadStateMachine` 校验状态转换合法性

**暂停语义**(`PauseAsync`):
1. 状态机校验 `Downloading → Paused`(非法状态抛 `[DOWN001]`)
2. 触发 `CancellationTokenSource.Cancel()`(优雅取消,不等超时)
3. 等待当前正在写入的分片完成当前 64KB 块后停止(不中断写操作,避免文件损坏)
4. 持久化 `.meta.json`(记录每个分片已下载字节数)
5. 释放 HTTP 连接(分片请求的 `HttpResponseMessage.Dispose`)
6. 状态机确认 `Paused`

**继续语义**(`ResumeAsync`):
1. 状态机校验 `Paused → Downloading`
2. 读取 `.meta.json`,校验 URL/ETag/LastModified 未变更(变更则 `[DOWN002]` 资源已变更,转 Failed)
3. 过滤未 completed 的分片
4. PLINQ 并发继续下载(每个分片从 `start + downloaded` 位置发 Range 请求)
5. 状态机确认 `Downloading`

**结束语义**(`CancelAsync`):
1. 状态机校验任意非终态 `→ Cancelled`
2. 中断所有分片下载
3. 清理 `.part` 临时文件和 `.meta.json`(彻底取消,不保留进度)
4. 状态机确认 `Cancelled`

**等待完成**(`WaitForCompletionAsync`):
1. 阻塞直到状态机进入终态(Completed/Cancelled/Failed)
2. 返回 `DownloadResult`(Cancelled → `Success=false, FinalState=Cancelled`;Failed → `Success=false, ErrorMessage=...`)

**状态机线程安全**:
- `State` 属性用 `volatile` 读
- 状态转换用 `lock` + 二次校验(防止 Pause 和 Cancel 并发竞争)
- `PauseAsync`/`ResumeAsync`/`CancelAsync` 可从 UI 线程调用,下载在工作线程,需跨线程安全

## 4. 非功能需求

| 维度 | 要求 |
|------|------|
| **目标框架** | net10.0 |
| **AOT 兼容** | `IsAotCompatible=true`,用 `JsonSerializerContext` 源码生成器,禁 `dynamic`/反射 emit |
| **并发规范** | PLINQ `.AsParallel().WithDegreeOfParallelism(n)`,禁 `Parallel.For/ForEach`(`PerformanceRules.cs:89`) |
| **文件 IO** | `FileStream` 用 `FileShare.ReadWrite`(`JCC9006`),临时文件用 `.part` 后缀 |
| **字符串** | 禁循环内 `+=` 拼(`JCC5002`),用 `StringBuilder` |
| **容器** | 分片查找用 `FrozenDictionary`/`Dictionary`,禁 `List.Contains` |
| **GlobalUsings** | `.cs` 文件内禁写 `using`,统一放 `GlobalUsings.cs` |
| **零警告** | `TreatWarningsAsErrors=true` |
| **超时** | 默认 100s,可通过 `DownloadOptions.Timeout` 覆盖 |
| **取消** | 全程响应 `CancellationToken` |

## 5. 架构设计

### 5.1 放置位置

```
infrastructure/Infrastructure/Network/Downloader/     ← 新建,属 Infrastructure 层(第③层)
  ├── Abstractions/       (接口 + DTO,纯文件)
  ├── StateMachine/       (状态机,纯文件)
  ├── Planning/           (分片规划,纯文件)
  ├── Metadata/           (断点续传元数据,纯文件)
  ├── Probing/            (Range 探测,纯文件)
  ├── Chunk/              (单分片下载,纯文件)
  └── Coordinator/        (主下载器协调,纯文件)
```

**测试位置**:
```
tests/Unit/Infra.Tests/Network/Downloader/             ← 新建
  (已被 Infra.Services.Tests.csproj 的 ..\Network\**\*.cs 包含)
```

**为什么放 Infrastructure 层**:
- 复用现有 `Infrastructure/Http/` 的 HttpClient 基础设施
- 不污染 Abstractions 层(不接入 = 不提升为公共抽象)
- 不依赖 Core 层(纯网络 IO,无业务语义)

### 5.2 文件清单(每个文件夹 < 10 文件,纯文件/纯文件夹)

| 文件夹 | 文件 | 职责 |
|--------|------|------|
| `Abstractions/` | `IDownloader.cs` | 下载器入口接口 |
| | `IDownloadSession.cs` | 可控制会话接口(Pause/Resume/Cancel/Wait) |
| | `DownloadOptions.cs` | 下载选项(MaxThreads/Resume/ChunkSize/Timeout) |
| | `DownloadResult.cs` | 下载结果(Success/FilePath/TotalBytes/Elapsed/FinalState) |
| | `DownloadProgress.cs` | 进度报告(TotalBytes/DownloadedBytes/SpeedBps/Percent/State) |
| | `DownloadState.cs` | 状态枚举(Idle/Downloading/Paused/Merging/Completed/Cancelled/Failed) |
| | `DownloadMetadata.cs` | 元数据模型(Url/TotalLength/ETag/Chunks) |
| | `DownloaderJsonContext.cs` | AOT 源码生成器 JsonContext |
| `StateMachine/` | `DownloadStateMachine.cs` | 状态机(转换校验+线程安全) |
| | `DownloadStateTransition.cs` | 转换结果记录(Success/NewState/Error) |
| `Planning/` | `DownloadChunk.cs` | 分片模型(Index/Start/End/Downloaded/Completed) |
| | `ChunkPlanner.cs` | 分片规划器(纯计算) |
| `Metadata/` | `MetadataStore.cs` | 元数据读写(.meta.json 持久化) |
| `Probing/` | `RangeSupportProbe.cs` | Range 支持探测(HEAD/GET 回退) |
| `Chunk/` | `ChunkDownloader.cs` | 单分片下载器(Range GET + 流式写 .part) |
| `Coordinator/` | `RangeDownloader.cs` | 主下载器(实现 IDownloader,协调+PLINQ+合并) |
| | `DownloadSession.cs` | 会话实现(实现 IDownloadSession,持有状态机+取消令牌) |

### 5.3 依赖关系

```
RangeDownloader (Coordinator, 实现 IDownloader)
  └── DownloadSession (Coordinator, 实现 IDownloadSession)
         ├── DownloadStateMachine (StateMachine)  ← 状态转换校验
         ├── RangeSupportProbe (Probing)
         ├── ChunkPlanner (Planning)
         ├── MetadataStore (Metadata)
         └── ChunkDownloader (Chunk)
                └── DownloadChunk (Planning)
```

**调用时序**:
```
用户 → IDownloader.StartDownload(url, path, opts)
         → 创建 DownloadSession(状态=Idle)
         → session.Start() → 状态机: Idle→Downloading
         → 返回 IDownloadSession(非阻塞)

用户 → session.PauseAsync()
         → 状态机: Downloading→Paused
         → CancellationTokenSource.Cancel(优雅)
         → 等待当前分片写完 → 持久化 .meta.json → 释放 HTTP

用户 → session.ResumeAsync()
         → 状态机: Paused→Downloading
         → 读取 .meta.json → 跳过已完成分片 → PLINQ 继续未完成分片

用户 → session.WaitForCompletionAsync()
         → 阻塞到 Completed/Cancelled/Failed
         → 返回 DownloadResult
```

### 5.4 状态机设计

#### 5.4.1 状态定义

```csharp
public enum DownloadState
{
    Idle,        // 已创建,未启动
    Downloading, // 下载中
    Paused,      // 已暂停(进度已持久化,可 Resume)
    Merging,     // 分片合并中(所有 chunk 完成,正在拼接 .part)
    Completed,   // 已完成(终态)
    Cancelled,   // 已取消(终态,临时文件已清理)
    Failed       // 已失败(终态,保留 .meta.json 供诊断)
}
```

#### 5.4.2 状态转换图

```
          Start()              Pause()              Resume()
   ┌─────────────┐        ┌─────────────┐        ┌─────────────┐
   │             ▼        │             ▼        │             ▼
   │        Downloading ──┤          Paused ────┘        Downloading
   │             │        │             │
   │             │ 全部完成│             │ 资源变更/校验失败
   │             ▼        │             ▼
   │          Merging     │           Failed(终态)
   │             │        │
   │             ▼        │
   │        Completed(终态)│
   │                      │
   │  Cancel()            │  Cancel()
   ▼  (任意非终态)         ▼  (任意非终态)
 Cancelled(终态)          Cancelled(终态)
```

**合法转换表**(非法转换抛 `InvalidOperationException[DOWN001]`):

| 当前状态 \ 操作 | Start | Pause | Resume | Cancel | (内部)完成 | (内部)失败 |
|----------------|-------|-------|--------|--------|-----------|-----------|
| Idle | →Downloading | ✗ | ✗ | →Cancelled | ✗ | ✗ |
| Downloading | ✗ | →Paused | ✗ | →Cancelled | →Merging | →Failed |
| Paused | ✗ | ✗ | →Downloading | →Cancelled | ✗ | →Failed |
| Merging | ✗ | ✗ | ✗ | →Cancelled | →Completed | →Failed |
| Completed | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Cancelled | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Failed | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |

#### 5.4.3 状态机实现风格(遵循 AGENTS.md 规则8)

- 显式 `DownloadState` 枚举 + `switch` 表达式实现转换,不用隐式 `if-else` + 标志变量
- 每次转换返回 `DownloadStateTransitionResult { Success, NewState, Error }`,调用方可观察
- 状态用 `volatile` + `lock` 保证线程安全(Pause 从 UI 线程、Download 从工作线程并发)
- 时钟通过 `TimeProvider? clock = null` 注入,测试可控

### 5.5 公开接口清单

> 本节列出**所有**对外暴露的 public 类型/方法,基建期仅这些可见,其余全部 internal

#### 5.5.1 IDownloader — 下载器入口

```csharp
public interface IDownloader
{
    /// 启动下载,返回可控制的会话(非阻塞,立即返回)
    IDownloadSession StartDownload(
        string url,
        string filePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

#### 5.5.2 IDownloadSession — 可控制的下载会话

```csharp
public interface IDownloadSession : IAsyncDisposable
{
    /// 当前状态(线程安全读取)
    DownloadState State { get; }

    /// 暂停下载(优雅停止:等待当前分片写完,持久化元数据,释放 HTTP 连接)
    /// 仅 Downloading 状态可调用,转换到 Paused
    Task PauseAsync(CancellationToken ct = default);

    /// 继续下载(从暂停处恢复:读取元数据,跳过已完成分片,继续未完成分片)
    /// 仅 Paused 状态可调用,转换到 Downloading
    Task ResumeAsync(CancellationToken ct = default);

    /// 取消下载(彻底取消:中断所有分片,清理 .part 和 .meta.json)
    /// 任意非终态可调用,转换到 Cancelled
    Task CancelAsync(CancellationToken ct = default);

    /// 等待完成(阻塞直到 Completed/Cancelled/Failed 终态)
    /// 返回最终结果,已 Cancelled/Failed 时 Result.Success=false
    Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default);
}
```

#### 5.5.3 DownloadOptions — 下载选项

```csharp
public sealed class DownloadOptions
{
    /// 并发线程数:1=单线程顺序,>1=多线程 PLINQ 并发分片。默认 1
    public int MaxThreads { get; init; } = 1;

    /// 是否启用断点续传: true=检查 .meta.json 并恢复, false=总是重头下载。默认 true
    public bool Resume { get; init; } = true;

    /// 分片大小: null=自动(totalLength/maxThreads,钳制到[1MB,16MB])。默认 null
    public long? ChunkSize { get; init; }

    /// 单分片超时。默认 100s
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// 期望的 Content-Length,用于校验(不匹配则报错)。默认 null=不校验
    public long? ExpectedContentLength { get; init; }

    /// 元数据持久化频率:每下载 N 字节刷新一次 .meta.json。默认 64KB
    public long MetadataFlushInterval { get; init; } = 64 * 1024;
}
```

#### 5.5.4 DownloadResult — 下载结果

```csharp
public sealed record DownloadResult(
    bool Success,
    string FilePath,
    long TotalBytes,
    long DownloadedBytes,
    TimeSpan Elapsed,
    DownloadState FinalState,
    string? ErrorMessage = null);
```

#### 5.5.5 DownloadProgress — 进度报告

```csharp
public sealed record DownloadProgress(
    long TotalBytes,
    long DownloadedBytes,
    double SpeedBps,
    double Percent,
    DownloadState State,
    bool IsResumed);
```

#### 5.5.6 DownloadState — 状态枚举(见 5.4.1)

#### 5.5.7 公开 API 总数

| 类型 | 可见性 | 用途 |
|------|--------|------|
| `IDownloader` | public interface | 下载入口 |
| `IDownloadSession` | public interface | 会话控制(Pause/Resume/Cancel/Wait) |
| `DownloadOptions` | public sealed class | 下载配置 |
| `DownloadResult` | public sealed record | 下载结果 |
| `DownloadProgress` | public sealed record | 进度报告 |
| `DownloadState` | public enum | 状态枚举 |
| `RangeDownloader` | public sealed class | `IDownloader` 实现(唯一实现类) |

**其余全部 internal**:`DownloadChunk`/`ChunkPlanner`/`MetadataStore`/`RangeSupportProbe`/`ChunkDownloader`/`DownloadMetadata`/`DownloadStateMachine` 等

## 6. 验收标准

### 6.1 单元测试(全部必须通过)

| 测试类 | 覆盖点 |
|--------|--------|
| `DownloadStateMachineTests` | 所有合法转换/非法转换抛[DOWN001]/终态不可转换/线程安全并发 Pause+Cancel |
| `ChunkPlannerTests` | 单分片/多分片/不能整除/最小分片/最大分片/零长度 |
| `MetadataStoreTests` | 写入/读取/删除/URL不匹配/ETag校验/损坏JSON/并发写不冲突 |
| `RangeSupportProbeTests` | Accept-Ranges支持/不支持/HEAD 405回退GET/无Content-Length |
| `ChunkDownloaderTests` | Range请求头正确/续传偏移/写入.part/更新元数据/取消/FileShare.ReadWrite |
| `DownloadSessionTests` | Start→Downloading/Pause→Paused/Resume→Downloading/Cancel→Cancelled/WaitForCompletion/资源变更转Failed |
| `RangeDownloaderTests` | 单线程完整下载/多线程PLINQ并发/断点续传恢复/合并正确/删除临时文件/状态终态正确 |

### 6.2 编译验收

- `dotnet build infrastructure/Infrastructure/Infrastructure.csproj -c Debug` 通过
- `dotnet build tests/Unit/Infra.Tests/Services/Infra.Services.Tests.csproj -c Debug` 通过
- 零警告(`TreatWarningsAsErrors`)

### 6.3 不接入验收

- 全局搜索 `[Register]` 不应出现在 `Downloader/` 任何文件中
- 全局搜索 `McpTool` 不应出现在 `Downloader/` 任何文件中
- 主工程编译产物不包含对 `RangeDownloader` 的调用

## 7. 任务拆解(TDD)

> 循环: 🔴单元红 → 🟢单元绿 → 🔵重构 → 编译 → git 提交

| 步骤 | 内容 | 状态 |
|------|------|------|
| T1 | DTO + 接口 + 枚举 + JsonContext(纯数据结构,无需 TDD) | ⬜ |
| T2 | 🔴DownloadStateMachine 红测试 → 🟢实现(转换校验+线程安全) → 编译 → 提交 | ⬜ |
| T3 | 🔴ChunkPlanner 红测试 → 🟢实现 → 编译 → 提交 | ⬜ |
| T4 | 🔴MetadataStore 红测试 → 🟢实现 → 编译 → 提交 | ⬜ |
| T5 | 🔴RangeSupportProbe 红测试 → 🟢实现 → 编译 → 提交 | ⬜ |
| T6 | 🔴ChunkDownloader 红测试 → 🟢实现 → 编译 → 提交 | ⬜ |
| T7 | 🔴DownloadSession 红测试(Pause/Resume/Cancel/Wait 状态流转) → 🟢实现 → 编译 → 提交 | ⬜ |
| T8 | 🔴RangeDownloader 红测试(单线程/多线程/续传/合并) → 🟢实现 → 编译 → 提交 | ⬜ |
| T9 | 全量编译验证(Infrastructure.slnx Debug) → 提交 | ⬜ |

## 8. 风险与对策

| 风险 | 对策 |
|------|------|
| 测试依赖真实网络 | 用 `HttpMessageHandler` mock,不发起真实 HTTP 请求 |
| PLINQ 异常聚合 | `.Select()` 内捕获为 `ChunkDownloadResult`,外层汇总,不抛 `AggregateException` |
| 元数据并发写冲突 | 每个 chunk 独立 `.part` 文件,元数据用 `ConcurrentDictionary` 累积,完成后一次性写 |
| AOT JsonContext | 单独 `DownloaderJsonContext`,只含 `DownloadMetadata`/`DownloadChunk` 等少量类型 |
| 临时文件残留 | 下载完成/异常都尝试清理 `.part` 和 `.meta.json`,用 `try-finally` |

## 9. 后续接入点(本期不实现,仅记录)

1. **bash 拦截接入**: `ShellCommandInterceptionMiddleware` 拦截 curl/wget 大文件 → 改用 `RangeDownloader`
2. **MCP 工具接入**: 新增 `download_file(url, path, threads, resume)` 工具,挂 `[McpTool]`
3. **DI 注册接入**: 挂 `[Register(typeof(IDownloader))]`,通过 `HttpClientFactory` 注入
4. **UpgradeService 接入**: 替换 `UpgradeService` 查版本后下载二进制升级包

---

<!-- 🤖 Auto Decision: 2026-08-25 -->
<!-- 决策: 下载器放 Infrastructure/Network/Downloader/,不挂 [Register],纯基建 -->
<!-- 原因: 用户要求独立做不接入,Infrastructure 层有现成 HttpClient 基础,不污染 Abstractions -->
<!-- 替代方案: 放 core/execution/Hands(接入主链路,违反"不接入"要求) / 放 Abstractions(过度暴露,违反"基建先行") -->
