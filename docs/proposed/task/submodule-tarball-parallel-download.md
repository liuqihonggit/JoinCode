# 自适应子模块并行下载 — tarball API + 端口跳变复用

## 背景

下载 git submodule 慢,根因是 GitHub 网络不稳定(端口经常跳变)。项目已实现 `GitHubApiClient` 端口探测 + 备用路由切换 + 5xx 退避重试,但仅用于 REST API 调用,未扩展到子模块下载。

本任务将现有网络韧性能力**复用**到子模块下载,并新增 GitHub tarball API 并行下载策略,实现**完全自动化**:探测高延迟 → 自动切 tarball → 并行下载 → 解压 → 补 git 元数据 → 清理临时包。

## 现有实现复用清单

| 现有能力 | 位置 | 复用到 |
|---------|------|--------|
| 端口探测 `IsPortReachableAsync` | `GitHubApiClient.PortCheck.cs:36` | 延迟探测,决定走 git 还是 tarball |
| 备用路由切换 `TrySwitchToFallbackRouteAsync` | `GitHubApiClient.PortCheck.cs:69` | tarball 下载端口不可达时自动切换 |
| 5xx 退避重试 `Max5xxRetries` + `Compute5xxBackoff` | `GitHubApiClient.cs:25` | tarball HTTP 下载失败重试 |
| token 解析 `ResolveToken` | `GitHubApiClient.cs` | tarball API 认证 |
| SSH URL 解析 `ParseGitHubRemoteUrl` | `GitHubToolHandlers.cs:510` | .gitmodules URL → owner/repo |
| zip 解压 `ZipArchive` | `GitHubApiClient.cs:307` | tarball 解压(改用 System.Formats.Tar) |
| 临时目录 `TempDirScope` | `scopes/TempDirScope.cs` | 临时压缩包自动清理(符合禁删规则) |
| `ParallelJobs = ProcessorCount * 2` | `GitToolHandlers.cs` | IO 密集型并行度 |

## 新增组件设计

```
SubmoduleParallelDownloader (主入口,集成到 git_submodule_update 工具)
├── SubmoduleManifest     — 解析 .gitmodules → 子模块列表(path, owner/repo, branch)
├── LatencyProbe          — 复用 IsPortReachableAsync 探测 GitHub API 延迟
├── TarballDownloader     — 复用 GitHubApiClient.SendAsync 并行下载 tarball
├── TarExtractor          — System.Formats.Tar + GZipStream 解压 .tar.gz
└── GitMetadataFixup      — git submodule update --init --force 补元数据
```

### 自适应策略流程

```
git_submodule_update 工具调用
  │
  ├─ LatencyProbe.ProbeAsync()  ← 复用 IsPortReachableAsync("api.github.com", 443)
  │   ├─ 低延迟(端口可达 + HEAD < 阈值 2s) → git submodule update --init --jobs=CPU×2
  │   └─ 高延迟(端口不可达 或 HEAD ≥ 阈值) → tarball 方案 ↓
  │
  ├─ SubmoduleManifest.Parse(.gitmodules)  ← 复用 ParseGitHubRemoteUrl
  │   → [(path="libs/Editor", owner="liuqihonggit", repo="Editor", ref="develop"), ...]
  │
  ├─ TarballDownloader.DownloadAllAsync(submodules, ParallelJobs)
  │   ├─ Task.WhenAll(submodules.Select(DownloadOneAsync))  ← 并行 HTTP
  │   ├─ 每个子模块: GET /repos/{owner}/{repo}/tarball/{ref}
  │   ├─ 复用 GitHubApiClient: token + 5xx 重试 + 路由切换
  │   └─ 下载到 TempDirScope(自动清理)
  │
  ├─ TarExtractor.ExtractAllAsync(downloaded, targetPaths)
  │   ├─ GZipStream 解压 .tar.gz → TarReader 逐 entry 写文件
  │   └─ 解压到子模块路径(如 libs/Editor/)
  │
  ├─ GitMetadataFixup.FixupAsync()
  │   └─ git submodule update --init --force  ← 让 git 识别已存在文件,补 .git 元数据
  │
  └─ TempDirScope.DisposeAsync()  ← 自动清理临时压缩包(不违反禁删规则)
```

## 实现步骤(渐进式)

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 1 | `SubmoduleManifest` — 解析 .gitmodules,复用 `ParseGitHubRemoteUrl` | 无 |
| 2 | `TarExtractor` — System.Formats.Tar + GZipStream 解压 .tar.gz | 无 |
| 3 | `TarballDownloader` — 并行 HttpClient 下载,复用 GitHubApiClient 能力 | 步骤1 |
| 4 | `SubmoduleParallelDownloader` — 主入口,延迟探测 + 策略选择 + 编排 | 步骤1-3 |
| 5 | 集成到 `GitSubmoduleUpdateAsync` — 加 `adaptive` 参数,默认 true | 步骤4 |
| 6 | 单元测试 + 编译 + commit | 步骤1-5 |

## 验收标准(四列验收表)

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `SubmoduleManifest.Parse` 解析 .gitmodules | `SubmoduleParallelDownloader` | ❌ | ❌ |
| `TarExtractor.Extract` 解压 .tar.gz | `SubmoduleParallelDownloader` | ❌ | ❌ |
| `TarballDownloader.DownloadAllAsync` 并行下载 | `SubmoduleParallelDownloader` | ❌ | ❌ |
| `LatencyProbe` 复用 `IsPortReachableAsync` | `SubmoduleParallelDownloader` | ❌ | ❌ |
| `GitSubmoduleUpdateAsync` 加 `adaptive` 参数 | `GitToolHandlers` | ❌ | ❌ |
| 端口跳变复用到 tarball 下载 | `TarballDownloader` | ❌ | ❌ |
| 临时包自动清理 `TempDirScope` | `SubmoduleParallelDownloader` | ❌ | ❌ |

## 关键约束

1. **禁止删除文件** — 临时压缩包用 `TempDirScope` 自动清理,不手动删
2. **复用不重复** — 端口探测/路由切换/token/重试全部委托 `GitHubApiClient`,不重写
3. **JSON DTO 双向转换** — .gitmodules 解析用 DTO,不手写字符串拼接
4. **ParallelJobs = CPU×2** — IO 密集型并行度动态计算
5. **tarball 无 git 历史** — 下载后必须 `git submodule update --init --force` 补元数据
6. **SSH URL → API URL** — `git@github.com:owner/repo.git` → `repos/owner/repo/tarball/branch`

## 决策记录

<!-- 🤖 Auto Decision: 2026-10-15 -->
<!-- 决策: 复用 GitHubApiClient 端口跳变能力,新增 tarball API 并行下载 -->
<!-- 原因: 用户反馈 GitHub 端口经常跳,项目已有端口探测+路由切换,应复用而非重写 -->
<!-- 替代方案: 纯 git --jobs(已实现,但高延迟下往返慢); 纯 tarball(无 git 元数据需补全) -->
<!-- 验证: 待实现后编译+测试验证 -->
