# 阶段0 前置调研 — Directory.Build.props 差异分析

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [adr/](../adr/README.md)
> 🔗 **上游索引**: [design/README.md](README.md) — 修改本文档后须同步更新此索引

> 创建: 2026-09-13
> 状态: completed
> 关联: [ADR 0103](../adr/0103-folder-restructure-semantic-grouping-flat.md)

## 1. 现状

共 24 个 Directory.Build.props(排除 libs/ 第三方 4 个),分为 5 种:

| 组 | 行数 | 数量 | MD5 | 覆盖目录 |
|----|------|------|-----|---------|
| 根目录 | 119 | 1 | `5968be...` | `Directory.Build.props` |
| 生成器 | 62 | 1 | `7aa334...` | `00_generators/` |
| 中间层 | 67 | 17 | `fa36bc...` | `01_shared/`~`08_composition/`(全同) |
| 应用层 | 40 | 4 | `6882b6...` | `09_app_cli/`~`09_app_tui/`(全同) |
| 跨层测试 | 66 | 1 | `4a5cd9...` | `tests/` |

## 2. 各组内容摘要

### 2.1 根目录 (119 行)
- 基础: TargetFramework=net10.0, Nullable=enable, TreatWarningsAsErrors=true
- AOT/裁剪: IsAotCompatible=true(非Tests), PublishAot+TrimMode=full(Release)
- Exe Release: StackTraceSupport=false, UseWindowsThreadPool=true, InvariantGlobalization
- 版本号变量: 30+ 个 NuGet 包版本
- 全局 Using: System.Collections.Concurrent, System.Diagnostics, System.Text.Json*
- **分析器引用(硬编码路径)**: `00_generators/aot_safety.generator/src/AotSafety.Generator.csproj` + `00_generators/code_fixes/src/CodeFixes.csproj`

### 2.2 生成器 (62 行)
- `ImportDirectoryBuildProps=false` + 手动 Import 根目录(只取版本号)
- TargetFramework=netstandard2.0(生成器必须)
- EnforceExtendedAnalyzerRules=true
- 测试项目覆盖: net10.0
- NuGet 打包配置(analyzers/dotnet/cs 路径)

### 2.3 中间层 (67 行, 17 份全同)
- Import 根目录配置
- src 项目: IsPackable=true, GenerateDocumentationFile=true, Logging.Abstractions
- tests 项目: IsTestProject=true, 测试框架包引用, IsAotCompatible=false
- 全局 Using: Microsoft.Extensions.Logging, System.Linq
- tests Using: Xunit, Moq, FluentAssertions

### 2.4 应用层 (40 行, 4 份全同)
- Import 根目录配置
- EmbeddedResourceUseDependentUponConvention=true
- EnableConfigurationBindingGenerator=true
- NoWarn IL2104
- Exe: PublishSingleFile=false, SelfContained=false
- Using: Microsoft.Extensions.Logging, DependencyInjection, System.Linq

### 2.5 跨层测试 (66 行)
- Import 根目录配置
- 测试项目配置: GenerateDocumentationFile=false, TreatWarningsAsErrors=true
- 测试框架包引用(同中间层 tests 部分)
- Using: Xunit, Moq, FluentAssertions, Logging, System.Linq

## 3. 合并策略

迁移到语义分组后,从 24 个 Directory.Build.props 减少到 **8 个**:

| 语义文件夹 | 需要 props | 来源 | 说明 |
|------------|-----------|------|------|
| 根目录 | ✅ | 根目录 | 保留,更新分析器引用路径 `00_generators/...` → `gen/...` |
| `gen/` | ✅ | `00_generators/` | 生成器专用(netstandard2.0) |
| `lib/` | ✅ | `01_shared/`(67行) | 中间层统一配置 |
| `llm/` | ✅ | `01_shared/`(67行) | 同上(内容相同) |
| `kit/` | ✅ | `01_shared/`(67行) | 同上(内容相同) |
| `server/` | ✅ | `01_shared/`(67行) | 同上(内容相同) |
| `app/` | ✅ | `09_app_cli/`(40行) | 应用层专用 |
| `test/` | ✅ | `tests/`(66行) | 跨层测试专用 |
| `tool/` | ❌ | — | 继承根目录(non_deliverables_tools 无 props) |
| `build/` | ❌ | — | 只有 slnx 和脚本 |
| `libs/` | ❌ | — | 不纳入扁平化 |

### 3.1 lib/ llm/ kit/ server/ 内容相同为何各放一份?

MSBuild 向上查找机制:每个项目从自己目录向上找 `Directory.Build.props`。如果 `lib/` 有但 `llm/` 没有,`llm/` 的项目会跳过 `lib/` 直接找到根目录(缺少中间层配置: IsPackable, 测试框架包引用等)。

4 份相同内容比复杂的根目录条件判断更易维护,后续某语义文件夹需要不同配置时可单独修改。

### 3.2 根目录分析器引用路径更新

```xml
<!-- 旧路径 -->
<ProjectReference Include="$(MSBuildThisFileDirectory)00_generators/aot_safety.generator/src/AotSafety.Generator.csproj" .../>
<ProjectReference Include="$(MSBuildThisFileDirectory)00_generators/code_fixes/src/CodeFixes.csproj" .../>

<!-- 新路径 -->
<ProjectReference Include="$(MSBuildThisFileDirectory)gen/aot_safety.generator/AotSafety.Generator.csproj" .../>
<ProjectReference Include="$(MSBuildThisFileDirectory)gen/code_fixes/CodeFixes.csproj" .../>
```

## 4. 迁移后 Directory.Build.props 层级

```
根目录/Directory.Build.props          # 基础配置 + 版本号 + AOT + 分析器引用
├── gen/Directory.Build.props         # 生成器专用(netstandard2.0)
├── lib/Directory.Build.props         # 中间层(src: IsPackable+Doc; tests: 测试框架)
├── llm/Directory.Build.props         # 同 lib/
├── kit/Directory.Build.props         # 同 lib/
├── server/Directory.Build.props      # 同 lib/
├── app/Directory.Build.props         # 应用层(EmbeddedResource+Exe发布)
└── test/Directory.Build.props        # 跨层测试
```

## 5. 验证清单

- [ ] 根目录分析器引用路径更新(`00_generators/...` → `gen/...`)
- [ ] gen/ props 从 00_generators/ 迁移
- [ ] lib/ llm/ kit/ server/ props 从 01_shared/ 复制(4 份相同)
- [ ] app/ props 从 09_app_cli/ 迁移
- [ ] test/ props 从 tests/ 迁移
- [ ] 各语义文件夹编译验证
- [ ] 旧数字目录 props 移到 .xxx/
