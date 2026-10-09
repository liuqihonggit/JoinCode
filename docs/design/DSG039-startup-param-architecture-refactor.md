# DSG039 启动参数架构级别重构方案

## 背景与问题

### 当前规模

| 命令族 | 工具数 | 参数数 | 平均参数/工具 | 参数定义模式 |
|--------|--------|--------|--------------|-------------|
| gh | 145 | 732 | 5.05 | `[McpToolParameter]` 特性驱动 |
| mcp | 26 | 44 | 1.69 | `[McpToolParameter]` 特性驱动 |
| workflow | 15 | 19 | 1.27 | `[McpToolParameter]` 特性驱动 |
| dev | 11 | 17 | 1.55 | `[McpToolParameter]` 特性驱动 |
| communication | 10 | 14 | 1.40 | `[McpToolParameter]` 特性驱动 |
| skill | 8 | 16 | 2.00 | `[McpToolParameter]` 特性驱动 |
| task | 7 | 14 | 2.00 | `[McpToolParameter]` 特性驱动 |
| terminal | 5 | 10 | 2.00 | `[McpToolParameter]` 特性驱动 |
| user | 4 | 7 | 1.75 | `[McpToolParameter]` 特性驱动 |
| rg | 0 | — | — | **手写 switch-case**（非生成器） |
| **总计** | **231** | **873** | **3.78** | — |

### 三大问题

#### 问题1：869 处参数描述手写重复

`[McpToolParameter("描述文本")]` 的描述是手写字符串字面量，源码生成器只透传不去重。

**Top 重复描述**：

| 次数 | 描述文本 | 变体数 |
|------|---------|--------|
| 101 | `工作目录(可选)` | 2 种变体（+`工作目录(可选,默认当前目录)` 3次） |
| 79 | `仓库(可选,默认当前仓库)` | 3 种变体（+`仓库名(owner/repo,可选,默认当前仓库)` 14次 +`仓库名(owner/repo)` 3次） |
| 33 | `输出档位(0=gh风格...)` | **4 种变体**（gh风格/gh风格简洁/gh风格表格/从缓存读） |
| 17 | `数量限制(默认 30)` | 3 种默认值变体（20/30/50） |
| 14 | `PR 编号或 URL` | 1 种 |
| 12 | `Issue 编号或 URL` | 1 种 |

**根因**：无统一数据源，每个工具独立手写，违反 AGENTS.md 第11条"硬编码变委托"和枚举唯一数据源原则。

#### 问题2：rg 子命令未接入生成器体系

`RgSubCommand.ParseArgs` 是唯一手写 switch-case 的子命令解析器，与 gh/mcp 的"约定大于配置"风格不一致。未来 rg 参数增长时维护成本线性上升。

#### 问题3：未来扩展瓶颈

用户指出：启动参数可能会超过 gh 的现在（145 工具/732 参数），尤其是 rg、mcp 等命令。当前架构下：
- 新增参数需手写描述文本，容易产生新的不一致变体
- 无编译期校验：描述文本拼写错误、变体不一致都不会报错
- 参数定义散落在 145+ 个文件中，无法全局视图管理

## 架构目标

1. **单一数据源** — 公共参数描述定义一次，引用处处
2. **源码生成器自动填充** — 根据枚举值自动展开描述文本
3. **编译期校验** — 描述不一致时编译警告→错误（TreatWarningsAsErrors）
4. **全子命令统一** — rg/mcp/gh 都用特性驱动 + 生成器生成
5. **AOT 兼容** — 全部通过源码生成器，无运行时反射
6. **可扩展** — 未来参数翻倍时架构不变，只增枚举值

## 方案设计

### Phase 1: WellKnown 参数元数据注册表

**新建文件**：`lib/abstractions/abs_core/core_attributes/mcp/ParamMetaAttribute.cs`

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class ParamMetaAttribute : Attribute {
    public string Description { get; }
    public bool Required { get; init; } = true;
    public string? DefaultValue { get; init; }
    public string? TypeName { get; init; }  // "string" / "int" / "bool" 等
    public ParamMetaAttribute(string description) => Description = description;
}
```

**新建文件**：`lib/abstractions/abs_core/core_utils/constants/params/WellKnownParam.cs`

```csharp
public enum WellKnownParam {
    [EnumValue("working_dir")]
    [ParamMeta("工作目录(可选)", Required = false, TypeName = "string")]
    WorkingDir,

    [EnumValue("repo")]
    [ParamMeta("仓库(可选,默认当前仓库)", Required = false, TypeName = "string")]
    Repo,

    [EnumValue("verbosity")]
    [ParamMeta("输出档位(0=gh风格简洁[默认] 1=精简JSON 2=完整JSON)", Required = false, TypeName = "int")]
    Verbosity,

    [EnumValue("limit")]
    [ParamMeta("数量限制(默认 30)", Required = false, DefaultValue = "30", TypeName = "int")]
    Limit,

    [EnumValue("json_fields")]
    [ParamMeta("JSON 字段过滤(可选,逗号分隔)", Required = false, TypeName = "string")]
    JsonFields,

    [EnumValue("pr_number")]
    [ParamMeta("PR 编号或 URL", Required = true, TypeName = "string")]
    PrNumber,

    [EnumValue("issue_number")]
    [ParamMeta("Issue 编号或 URL", Required = true, TypeName = "string")]
    IssueNumber,

    [EnumValue("run_id")]
    [ParamMeta("Run ID", Required = true, TypeName = "string")]
    RunId,

    [EnumValue("branch")]
    [ParamMeta("分支名", Required = true, TypeName = "string")]
    Branch,

    [EnumValue("owner")]
    [ParamMeta("仓库所有者(owner)", Required = true, TypeName = "string")]
    Owner,

    // ... 按需扩展
}
```

### Phase 2: 新源码生成器 `param_metadata.generator`

**新建项目**：`gen/param_metadata.generator/`

扫描 `WellKnownParam` 枚举的 `[EnumValue]` + `[ParamMeta]` 特性，生成：

```csharp
// auto-generated
public static class WellKnownParamConstants {
    public const string WorkingDir = "working_dir";
    public const string Repo = "repo";
    public const string Verbosity = "verbosity";
    // ...
}

public static class WellKnownParamDescriptions {
    public const string WorkingDir = "工作目录(可选)";
    public const string Repo = "仓库(可选,默认当前仓库)";
    public const string Verbosity = "输出档位(0=gh风格简洁[默认] 1=精简JSON 2=完整JSON)";
    // ...
}

public static class WellKnownParamMeta {
    public static readonly FrozenDictionary<string, ParamMetaEntry> Entries = new() {
        ["working_dir"] = new("工作目录(可选)", Required: false, TypeName: "string"),
        ["repo"] = new("仓库(可选,默认当前仓库)", Required: false, TypeName: "string"),
        // ...
    };
}

public sealed record ParamMetaEntry(string Description, bool Required, string? DefaultValue, string? TypeName);
```

**生成器实现**：继承 `AttributeRegistrationGeneratorBase` 或直接用 `CompilationProvider` 模式，扫描带 `[EnumValue]` + `[ParamMeta]` 的枚举字段。

### Phase 3: 增强 `[McpToolParameter]` 特性

**修改文件**：`lib/abstractions/abs_core/core_attributes/mcp/McpToolParameterAttribute.cs`

```csharp
public sealed class McpToolParameterAttribute : Attribute {
    public string Description { get; }
    public bool Required { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string[]? EnumValues { get; set; }
    public WellKnownParam? WellKnown { get; set; }  // ★ 新增

    public McpToolParameterAttribute(string description) => Description = description;
    public McpToolParameterAttribute(WellKnownParam wellKnown) {
        WellKnown = wellKnown;
        Description = "";  // 占位，生成器会从注册表填充
    }
}
```

**修改 `McpToolDispatchGenerator`**：当 `WellKnown` 非空时，从生成的 `WellKnownParamDescriptions` 常量类取描述，覆盖空字符串。

### Phase 4: 参数组模板（消除方法签名膨胀）

**新建文件**：`kit/mcp/git_hub/options/GitHubCommonOptions.cs`

```csharp
[McpToolOptions]
public sealed partial class GitHubCommonOptions {
    [McpToolParameter(WellKnownParam.Repo)]
    public string? Repo { get; init; }

    [McpToolParameter(WellKnownParam.WorkingDir)]
    public string? WorkingDir { get; init; }

    [McpToolParameter(WellKnownParam.Verbosity)]
    public int? Verbosity { get; init; }

    [McpToolParameter(WellKnownParam.JsonFields)]
    public string? JsonFields { get; init; }
}
```

**工具方法签名简化**：

```csharp
// 之前（7 行参数）
[McpTool("gh_pr_view", "查看 PR 详情", "github")]
public async Task<ToolResult> GhPrViewAsync(
    [McpToolParameter("PR 编号或 URL", Required = true)] string pr_number,
    [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
    [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
    [McpToolParameter("输出档位(0=gh风格简洁[默认] 1=精简JSON 2=完整JSON[从缓存读])", Required = false)] int? verbosity = null,
    [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 number,title,state)", Required = false)] string? json_fields = null,
    CancellationToken ct = default)

// 之后（3 行参数）
[McpTool("gh_pr_view", "查看 PR 详情", "github")]
public async Task<ToolResult> GhPrViewAsync(
    [McpToolParameter(WellKnownParam.PrNumber)] string pr_number,
    GitHubCommonOptions common,
    CancellationToken ct = default)
```

### Phase 5: 统一 rg 子命令

**新建文件**：`app/cli/core/services/RgArg.cs`

```csharp
public enum RgArg {
    [CliOption("pattern", "", "搜索模式(正则表达式)", AcceptsValue = true, Category = "核心")]
    Pattern,

    [CliOption("path", "", "搜索目录", AcceptsValue = true, Category = "核心")]
    Path,

    [CliOption("include", "", "文件名过滤(glob)", AcceptsValue = true, Category = "过滤")]
    Include,

    [CliOption("type", "t", "文件类型过滤", AcceptsValue = true, Category = "过滤")]
    Type,

    [CliOption("count", "c", "只输出匹配计数", Category = "输出")]
    Count,

    [CliOption("json", "", "JSON 输出格式", Category = "输出")]
    Json,

    // ... 所有 rg 参数
}
```

`cli_option.generator` 自动生成 `RgArgParser` + `RgArgParseResult`，替代手写 switch-case。

### Phase 6: 批量替换 869 处描述

Python 脚本批量替换（先单文件验证再推广）：

```python
replacements = {
    '[McpToolParameter("工作目录(可选)", Required = false)]':
        '[McpToolParameter(WellKnownParam.WorkingDir)]',
    '[McpToolParameter("工作目录(可选,默认当前目录)", Required = false)]':
        '[McpToolParameter(WellKnownParam.WorkingDir)]',  # 统一变体
    '[McpToolParameter("仓库(可选,默认当前仓库)", Required = false)]':
        '[McpToolParameter(WellKnownParam.Repo)]',
    '[McpToolParameter("仓库名(owner/repo,可选,默认当前仓库)", Required = false)]':
        '[McpToolParameter(WellKnownParam.Repo)]',  # 统一变体
    '[McpToolParameter("仓库名(owner/repo)", Required = false)]':
        '[McpToolParameter(WellKnownParam.Repo)]',  # 统一变体
    # ... verbosity 4 种变体统一
    # ... limit 3 种变体统一
}
```

### Phase 7: 编译期校验（可选，后续增强）

在 `aot_safety.generator` 中新增分析器规则：
- 扫描所有 `[McpToolParameter("...")]` 字符串字面量
- 如果参数名匹配 WellKnown 但描述文本与注册表不一致 → 发出 warning
- TreatWarningsAsErrors 下自动升级为 error，强制使用 `WellKnownParam` 枚举

## 实施顺序

| 步骤 | 内容 | 预计影响 |
|------|------|---------|
| 1 | Phase 1: 创建 `WellKnownParam` 枚举 + `[ParamMeta]` 特性 | 新增 2 文件 |
| 2 | Phase 2: 创建 `param_metadata.generator` 生成器 | 新增 1 项目 |
| 3 | Phase 2: 全量编译验证生成器产物正确 | --no-incremental |
| 4 | Phase 3: 增强 `[McpToolParameter]` + 修改 `McpToolDispatchGenerator` | 修改 2 文件 |
| 5 | Phase 3: 单元测试 — 验证 WellKnown 填充逻辑 | 新增测试 |
| 6 | Phase 6: 批量替换 gh 工具参数描述（先 Pr.cs 验证） | 修改 138 处 |
| 7 | Phase 6: 推广到所有 gh 文件 | 修改 869 处 |
| 8 | Phase 4: 提取 `GitHubCommonOptions` 参数组模板 | 新增 1 文件 |
| 9 | Phase 5: 统一 rg 子命令参数定义 | 修改 1 文件 + 新增 1 文件 |
| 10 | Phase 7: ADR + AGENTS.md 更新 | 文档 |

## 验收标准

| 验收项 | 标准 |
|--------|------|
| 重复描述消除 | "工作目录(可选)" 等高频描述在代码中出现 0 次（全部改为 `WellKnownParam` 枚举引用） |
| 变体统一 | working_dir 1 种、repo 1 种、verbosity 1 种（0 变体） |
| rg 统一 | `RgSubCommand.ParseArgs` 手写 switch-case 删除，改用生成器生成的 `RgArgParser` |
| 编译通过 | `dotnet build --no-incremental` 全量编译通过 |
| 测试通过 | 所有现有单元测试通过 |
| AOT 兼容 | 生成器用 netstandard2.0，生成代码用 FrozenDictionary，无反射 |

## 决策依据

| 方案 | 优点 | 缺点 | 选择 |
|------|------|------|------|
| **A: 源码生成器 + WellKnown 枚举** | 编译期填充、AOT 友好、编译期校验、与现有生成器体系一致 | 需新建生成器项目 | ✅ |
| B: 运行时字典查找 | 实现简单 | 违反 AOT、运行时开销、无编译期校验 | ❌ |
| C: 常量类手写 | 最简单 | 无生成器校验、仍需手写引用、无法自动填充 | ❌ |
| D: 分析器强制 + 手写修复 | 不改架构 | 869 处手写修复、无自动填充、治标不治本 | ❌ |

方案 A 与项目现有 10 个 IIncrementalGenerator 体系完全一致，利用已有的 `AttributeScanner` / `AttributeRegistrationGeneratorBase` 基建，AOT 兼容，且能在编译期检测不一致变体。
