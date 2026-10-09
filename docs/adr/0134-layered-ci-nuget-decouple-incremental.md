# 0134. 分层 CI + NuGet 包解耦 + 增量检测

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引
> 🔗 **关联**: [0081](0081-seven-layer-build-strategy.md)（七层 slnx 架构）、[0026](0026-pr-two-stage-pipeline.md)（PR 两段式验证）

- 状态：proposed
- 日期：2026-10-10
- 决策者：项目架构组

## 背景

当前 CI 成本过高，目标 **5 分钟内通过 CI**，且构建物达 1G。经分析：

### 当前 CI 结构（`ci.yml`）

```
detect → build(串行前置, timeout 15min) → unit-tests(40 job) + integration(5 job) + e2e(15 job) 并行
```

- `ci-build.yml`：windows-latest，编译 `JoinCode.slnx`（117 个项目）Release 模式，上传 1G artifact
- 所有测试 job `needs: build`，用 `--no-build`，通过 `setup-test-env` action 下载 1G artifact
- 关键路径 = build 时间 + max(测试 job 时间) ≈ 15 + 8 = 23 分钟

### 瓶颈定位

| 瓶颈 | 现状 | 影响 |
|------|------|------|
| build 串行前置 | 117 项目全量 Release 编译，timeout 15 分钟 | 所有测试等 build 完成 |
| 1G artifact | build 上传 1G → 60 个测试 job 各下载 1G | 上传+下载各 1-2 分钟 × 60 次 |
| 全量编译 | 每次 PR 编译全部 117 项目，含 benchmark/aot/non_deliverables 等 CI 不需要的 ~20 项目 | 白编译无关项目 |
| CLI 依赖整棵树 | `app/cli/JoinCode.csproj` → `kit/composition`（组合根）→ 传递依赖整个核心层 90+ 项目 | CLI 测试 job 独立 build 仍要编译 90+ 项目 |

### UI 拆分无效

`ci-build.yml` 已排除 `JoinCodeTui/` 和 `JoinCodeGui/`（不贡献 1G artifact），且 `JoinCode.slnx` 不含 UI 项目（UI 走独立 `ci-gui-daily.yml`）。拆分 UI 到其他仓库对 CI 时间和构建物体积基本无影响。

## 决策

采用 **分层 CI + 本地 NuGet 包解耦 + 增量检测**，在 monorepo 内实现 5 分钟 CI 目标。叠加 8 项优化：

### 1. 分层 CI（复用七层 slnx）

按 [ADR 0081](0081-seven-layer-build-strategy.md) 的七层架构，每层一个 build job：

```
① Generators → ② Foundation → ③ Infrastructure → ④ Core → ⑤ Services → ⑥ Composition → ⑦ App
```

每层 build 完成后：
- `dotnet pack` 打 NuGet 包到本地 feed（artifact）
- 下游层 `dotnet restore --source <本地feed>` 拉预编译 dll 包，**不编译上游源码**

### 2. 本地 NuGet 包解耦（核心机制）

层间不再传源码编译产物，而是传 NuGet 包：

| 层 | 产出 | 下游消费方式 |
|----|------|-------------|
| Foundation | `Abstractions.nupkg` + `Structura.nupkg` + ... | Infrastructure restore 拉包 |
| Infrastructure | `Infrastructure.nupkg` + `Transport.Impl.nupkg` + ... | Core restore 拉包 |
| Core | `Llm.nupkg` + `Brain.nupkg` + `Guard.nupkg` + ... | Services/Composition restore 拉包 |
| Services | `Mcp.nupkg` + `Dream.nupkg` + ... | App restore 拉包 |
| App | `jcc.exe` + 测试 dll | 直接运行测试 |

**效果：** CLI build = 编译 CLI 自己 + restore NuGet 包（预编译 dll），不再编译 lib/server 源码。

**源码生成器处理：** `gen/*` 项目打 NuGet analyzer 包（含 `analyzers/dotnet/` 目录），下游通过 `PackageReference` 引入，编译时自动运行生成器。NuGet analyzer 包是 Roslyn 分析器的标准分发方式。

### 3. 增量检测（git diff → 触发层）

```yaml
# 伪代码
- run: |
    BASE="${{ github.event.pull_request.base.sha }}"
    HEAD="${{ github.event.pull_request.head.sha }}"
    FILES=$(git diff --name-only "$BASE" "$HEAD")
    # 检测改动层
    if echo "$FILES" | grep -qE '^lib/abstractions/|^lib/structura/|^gen/'; then echo "layer=foundation" >> $GITHUB_OUTPUT; fi
    if echo "$FILES" | grep -qE '^lib/infrastructure/|^lib/transport/'; then echo "layer=infra" >> $GITHUB_OUTPUT; fi
    # ... 每层检测
```

**触发规则：** 改动某层 → 只 build 该层 + 所有下游层 + 相关测试。上游层不重编（用上次 NuGet 包缓存）。

| 改动层 | 触发 build | CI 预期 |
|--------|-----------|---------|
| App（app/ + kit/） | ⑦ App | 3-5 分钟 |
| Services（server/） | ⑤⑥⑦ | 5-7 分钟 |
| Foundation（lib/ + gen/） | ①②③④⑤⑥⑦ 全部 | 8-10 分钟（低频） |

### 4. 分层测试并行

每层 build 完成后，该层测试 job 并行执行（matrix），每个 job 只编译自己 csproj + restore 上下游 NuGet 包。

### 5. CI build 瘦身

从 CI build 排除 CI 不需要的项目：
- `test/benchmarks/`（benchmark 不在 CI 跑）
- `test/aot/`（AOT 探针不在 PR CI 跑）
- `non_deliverables_tools/`（开发工具）
- `test/integration/integration.desktop.tests/`（桌面集成走 daily）

117 项目 → ~80 项目。

### 6. Debug 编译

CI 用 Debug 配置（编译速度 2-3x 快于 Release），Release 编译留给 `daily-aot-publish.yml` 和发布流程。

### 7. artifact 瘦身

每层 NuGet 包 artifact < 100M（预编译 dll + 元数据），替代当前 1G 全量产物。不再传 `artifacts/bin/` 整个目录。

### 8. NuGet cache 精准化

```yaml
key: nuget-${{ runner.os }}-${{ matrix.layer }}-${{ hashFiles(format('{0}/**/*.csproj', matrix.layer_dir), 'Directory.Build.props') }}
```

按层分 cache key，避免全量 hash 导致 cache 频繁失效。

## 替代方案

### 方案 A：拆仓库（polyrepo）

将 monorepo 拆成 `joincode-foundation` / `joincode-infra` / `joincode-services` / `joincode-app` 多个仓库，各发 NuGet 包到 GitHub Packages。

**放弃原因：**
- 破坏 monorepo 原子提交：跨层改动要拆成多个 PR，无法一个 PR 完成一个 feature
- NuGet 包跨仓库版本协调复杂，破坏"约定大于配置"
- 当前项目已有 git submodule（`libs/Dock`），再加多仓库引用，依赖管理复杂度倍增
- 分层 CI + 本地 NuGet 包在 monorepo 内能达到相同效果（层间解耦），无需拆仓库

### 方案 B：job 独立 build（无共享 artifact）

每个测试 job 自己 restore + build + test，不依赖共享 build 产物。

**放弃原因：**
- CLI 测试 job 依赖 `kit/composition`（组合根）→ 传递依赖整个核心层 90+ 项目，独立 build 仍要编译 90+ 项目，跟全量 build 一样慢
- 底层测试 job（AsyncLock/Structura）会快，但 CLI/Host/Composition 测试 job 仍 8-10 分钟，无法满足 5 分钟目标
- 分层 CI + NuGet 包方案中，每层测试 job 已经是"独立 build 自己层 + 拉上游包"，包含了此方案的优势且解决了 CLI 瓶颈

### 方案 C：拆分 UI 到其他仓库

**放弃原因：**
- `ci-build.yml` 已排除 `JoinCodeTui/` 和 `JoinCodeGui/`，UI 不贡献 1G artifact
- `JoinCode.slnx` 不含 UI 项目，UI 走独立 `ci-gui-daily.yml`，不在主 CI 关键路径
- UI 只依赖 CLI 核心（`JoinCode.csproj`），拆走后核心仓库仍编译 115/117 项目，build 时间几乎不变

## 后果

- **正面：**
  - App 层改动 CI 3-5 分钟，大部分 PR 受益（app 层改动频率最高）
  - 1G 构建物消除，每层 artifact < 100M
  - CLI 不再编译 lib/server 源码，只 restore 预编译 NuGet 包
  - 保持 monorepo，跨层改动仍可单 PR
  - 增量检测避免无改动层重编
  - 复用已有七层 slnx 基础（ADR 0081），无需新建架构

- **负面：**
  - 层间 NuGet 包版本管理增加复杂度（本地 feed + 版本号策略）
  - 源码生成器打 analyzer 包需验证可行性（gen/* 项目 IsPackable + analyzer 目录结构）
  - 增量检测的层依赖图需维护（新增项目要标注所属层）
  - Foundation 层改动仍需全量级联（8-10 分钟），但底层改动频率低
  - Debug 编译可能掩盖 Release-only 问题（需 daily Release CI 补偿）

- **中性：**
  - `SkipLocalPack` 属性需启用并配置（当前 CI 用 `/p:SkipLocalPack=true` 跳过打包）
  - `Directory.Build.props` 需补充 NuGet 包版本/元数据配置
  - CI workflow 从 4 个扩展到按层 7+ 个，但每个更简单
