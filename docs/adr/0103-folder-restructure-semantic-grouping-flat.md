# ADR 0103: 文件夹扁平化重组 — 语义分组 + 组内扁平

> 状态: proposed
> 创建: 2026-09-13
> 决策者: 用户主导
> 影响: 112 个 csproj 全量迁移 + 10 个 slnx 归集 + 消除 src/+tests/ 双层
> 取代: [ADR 0102](0102-folder-restructure-functional-driven.md)(数字前缀方案,proposed → superseded by 0103)
> 详细设计: [docs/design/flatten-restructure-plan.md](../design/flatten-restructure-plan.md)

## 背景

ADR 0102 将项目从"脑/眼/手"领域驱动改为数字前缀功能驱动(`00_generators/` ~ `09_app_*`),已部分实施。用户已将大部分目录从驼峰转为小写+下划线(commit `a358b4dc3`),但仍存在以下问题:

| 问题 | 具体表现 |
|------|---------|
| 根目录爆炸 | 24 个数字文件夹 + 8 个 `.slnx` + 3 个 `.ps1` + 配置文件,根目录 50+ 项 |
| 数字撞车 | `04_mcp_dispatch`/`04_mcp_service` 共用 04,`08_brain`/`08_composition`/`08_pipelines` 共用 08 |
| 语义弱 | `05_server_*` 占 8 个位置,光看 `05` 不知道是 server |
| 内部不够扁 | 每个数字目录里还有 `src/`+`tests/` 子层 |
| 两套并存 | `core/`、`foundation/`、`services/` 老结构残留(经查全为 TestResults 垃圾) |
| 命名未统一 | 部分目录已转小写+下划线(`aot_safety.generator/`),部分仍 PascalCase(`Fsm.Generator/`、`02_llm/Agents/`) |

## 决策

### 决策1:语义分组替代数字前缀

根目录从 24 个数字文件夹缩减到 10 个语义文件夹:

```
build/   # .slnx + 构建脚本 + smoke-test
gen/     # 源码生成器(13 csproj + shared/)
lib/     # 基础库(23 csproj)
llm/     # LLM 抽象与实现(6 csproj)
kit/     # jcc 核心能力(18 csproj,合并原 03~08)
server/  # MCP server(15 csproj)
app/     # 应用入口(4 csproj)
test/    # 跨层测试(26 csproj)
tool/    # 辅助/非交付工具(7 csproj + scripts/ + stryker/)
libs/    # 第三方库(不动)
```

### 决策2:消除 src/+tests/ 双层

每个 csproj = 一个文件夹,文件夹内直接放源码文件。测试项目和源码项目在同一语义文件夹内平铺,测试用 `.Tests` 后缀区分。

- `01_shared/shared_guard/src/Guard.csproj` + `tests/Config/...` → `lib/guard/` + `lib/guard.config.tests/`

### 决策3:命名规范(对齐用户既有实践)

| 元素 | 规范 | 示例 |
|------|------|------|
| 语义文件夹 | 全小写 | `gen/ lib/ llm/ kit/ server/ app/ test/ tool/` |
| 项目文件夹 | csproj 名转小写+下划线,点号保留 | `AotSafety.Generator.csproj` → `aot_safety.generator/` |
| csproj 文件名 | 保持 PascalCase(不改) | `AotSafety.Generator.csproj` |
| 项目内部子目录 | 小写+下划线 | `slash_commands/ view_models/` |

PascalCase → snake_case 转换规则: 每个大写字母前插入下划线(首字母除外),全转小写,点号分隔的各段独立转换。

### 决策4:.slnx 归 build/sln/

10 个散在根目录的 .slnx 统一归集到 `build/sln/`。`build.ps1`/`publish.ps1`/`rebuild.ps1`/`smoke-test.json` 归 `build/`。

### 决策5:MSBuild/dotnet 硬性要求文件留根

`Directory.Build.props`、`Directory.Build.targets`、`global.json`、`nuget.config` 不能移(MSBuild 向上查找 + dotnet SDK 版本要求)。

## 替代方案

1. **保留 ADR 0102 数字前缀(放弃)** — 数字撞车(04×2, 08×3)、语义弱(光看 05 不知道是 server)、根目录爆炸(24 个数字文件夹)问题未解决
2. **仅消除 src/+tests/ 双层,保留数字前缀(放弃)** — 改动小但数字撞车和语义弱问题仍在,且数字前缀的排序机制与 ProjectReference 显式依赖冗余
3. **按 ADR 0102 的 private/shared 每工具一文件夹(放弃)** — 粒度太细,csproj 爆炸(73+ 个),编译慢;本方案保留 ADR 0102 的 private/shared 理念但作为项目内部组织,不提升到语义文件夹层级

## 后果

### 正面
- 根目录从 50+ 项缩减到 10 个语义文件夹 + 入口文件 + dotfiles
- 消除数字撞车,目录名即语义,可读性强
- 消除 src/+tests/ 双层,测试与源码同层,导航路径短
- .slnx 统一归 `build/sln/`,根目录整洁
- 命名统一(全小写+下划线),消除驼峰/下划线混用

### 负面
- 112 个 csproj 全量迁移,改动量大
- 所有 ProjectReference 路径需更新(410+ 个引用)
- 10 个 .slnx 内路径需更新
- CI yml 路径矩阵需同步(40+ 个引用)
- 各数字目录的 Directory.Build.props 需评估合并/保留策略(前置调研项)
- 源码生成器输出路径需验证

### 缓解
- 渐进式:11 个阶段,每阶段编译 + 测试 + 提交
- 从最低风险(build/)到最高风险(test/)逐层推进
- 每阶段编译验证 + 全量测试验证
- 残留垃圾移 `.xxx/` 而非删除(遵循 ADR 0008)

## 验证清单

- [ ] 前置调研:各 Directory.Build.props 差异分析
- [ ] 阶段 1-9:每阶段编译 + 测试 + 提交
- [ ] 全量编译通过(Debug)
- [ ] 全量测试通过
- [ ] 所有 .slnx 内路径正确
- [ ] 所有 ProjectReference 路径正确
- [ ] CI yml 路径同步
- [ ] AGENTS.md 架构索引更新
- [ ] README.md 更新(目录占位策略)
- [ ] ADR 0102 标记 superseded by 0103
- [ ] 拼留垃圾已移 .xxx/
- [ ] 根目录仅剩 10 个语义文件夹 + 入口文件 + dotfiles

## 与 ADR 0102 的关系

| 维度 | ADR 0102(数字前缀) | 本方案(语义分组) |
|------|---------------------|-------------------|
| 根目录文件夹数 | 24(数字前缀) | 10(语义) |
| 排序机制 | 数字前缀(00-09) | 无(靠语义名自然分组) |
| 数字撞车 | 有(04×2, 08×3) | 无(无数字) |
| src/+tests/ 双层 | 保留 | 消除(测试同层) |
| 语义可读性 | 弱(需看全名) | 强(目录名即语义) |
| 编译顺序保证 | 数字前缀隐式 | 靠 ProjectReference 显式 |
| .slnx 归集 | 散在根目录 | 统一 build/sln/ |
| 命名规范 | 混合(部分驼峰部分下划线) | 统一小写+下划线 |

ADR 0102 的"功能驱动"理念正确(废弃脑/眼/手领域分类),本方案继承此理念,仅改进**组织形式**(数字前缀 → 语义分组 + 消除双层 + 统一命名)。
