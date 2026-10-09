# ADR 0134 实施任务清单

> ADR: [0134](../adr/0134-layered-ci-nuget-decouple-incremental.md) — 分层 CI + NuGet 包解耦 + 增量检测
> 状态：待实施

## 任务列表

### P0 — 技术验证（必须先通过）

| # | 任务 | 验证方式 | 状态 |
|---|------|---------|------|
| 1 | gen/* 项目能否打 NuGet analyzer 包（IsPackable + analyzers/dotnet/ 目录结构） | `dotnet pack gen/enum_metadata.generator/` 产出 .nupkg，下游 PackageReference 引入后生成器运行 | ❌ |
| 2 | SkipLocalPack 启用后各层 csproj 能否正常 `dotnet pack` | `dotnet pack lib/abstractions/ -p:SkipLocalPack=false` 产出 .nupkg | ❌ |
| 3 | 本地 NuGet feed + 下游 restore 拉包编译是否工作 | Foundation pack → Infrastructure restore --source 本地feed → build 成功 | ❌ |

### P1 — CI 瘦身（快速止血）

| # | 任务 | 验证方式 | 状态 |
|---|------|---------|------|
| 4 | 从 CI build 排除 benchmark/aot/non_deliverables（117→~80 项目） | CI build 产物体积下降，测试仍全通过 | ❌ |
| 5 | 不传 1G artifact，改传分层小 artifact 或 NuGet 包 | artifact 体积 < 100M/层 | ❌ |

### P2 — 分层 CI + NuGet 包

| # | 任务 | 验证方式 | 状态 |
|---|------|---------|------|
| 6 | 每层 build job：dotnet build slnx + dotnet pack → 本地 feed artifact | 7 个 build job 各产出 NuGet 包 artifact | ❌ |
| 7 | 下游层 restore --source 拉上游包，不编译上游源码 | CLI build 只编译 app/kit，不编译 lib/server 源码 | ❌ |
| 8 | 分层测试并行：每层 build 后 matrix 并行跑测试 | 测试 job 数不变，但每个只编译自己层 | ❌ |

### P3 — 增量检测

| # | 任务 | 验证方式 | 状态 |
|---|------|---------|------|
| 9 | git diff → 检测改动层 → 动态触发该层 + 下游 build | 改 app/ 只触发 App build，改 lib/ 触发全量 | ❌ |
| 10 | 增量 NuGet cache：未改动层用上次包缓存 | 上游层不重编，直接用缓存包 | ❌ |

### P4 — 优化

| # | 任务 | 验证方式 | 状态 |
|---|------|---------|------|
| 11 | CI 用 Debug 编译（Release 留 daily） | CI 编译速度提升，daily Release CI 补偿 | ❌ |
| 12 | NuGet cache 精准 key（按层分 key） | cache 命中率提升 | ❌ |

## 实施顺序

```
P0 技术验证 → P1 CI瘦身 → P2 分层CI → P3 增量检测 → P4 优化
```

P0 必须先通过，否则整个方案不可行（生成器/打包/restore 链路不通就白做）。
