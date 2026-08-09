# 任务：EffortLevel 推理力度对齐 GUI

## 背景
GUI 设置面板已有温度/最大长度/流式/系统提示词，但缺 CLI `/effort`（推理力度）能力。
CLI `/effort low|medium|high|max|auto|unset` 会：
1. 更新 `IExecutionSettingsProvider.EffortLevel`（进程内生效，ChatOptionsFactory 消费）
2. 持久化 `ConfigKeyConstants.EffortLevel`（"effortLevel"）到 settings.json（auto/unset → Remove，其它 → Set）

**根因（GUI 缺注册）**：
- `ExecutionSettingsProvider` 在 `app/JoinCode/Services/Core/`（JoinCode 程序集，`[Register]`）
- CLI 的 `CoreModule` 调用 `AddAiWorkflowServices` + `AddJccAutoRegisteredServices`（注册 JoinCode 程序集 [Register]）
- GUI 的 `JccChatSession.CreateAsync` 只调用 `AddAiWorkflowServices` + `AddAllPipelines`，**不含 JoinCode 程序集注册** → GUI DI 无 `IExecutionSettingsProvider` → `ChatOptionsFactory._executionSettingsProvider` 为 null → EffortLevel 永远不生效
- GUI 管道虽含 `EffortLevelMiddleware`（`[Inject] IExecutionSettingsProvider?` 可空注入），但因缺注册而静默跳过

## 目标
1. GUI DI 能解析 `IExecutionSettingsProvider`，EffortLevel 真实生效（ChatOptionsFactory 消费链完整）
2. GUI 设置面板加 EffortLevel 选择器（low/medium/high/max/auto），读写与 CLI `/effort` 同源持久化键 `"effortLevel"`
3. 消除两套实现：`ExecutionSettingsProvider` 下沉到 composition 共享层，CLI + GUI 同时经 `AddJoinCodeCompositionAutoRegisteredServices` 自动注册

## 架构约束
- `IExecutionSettingsProvider` 接口保持在 Abstractions（`foundation/Abstractions/01-ai/LLM/Execution/IExecutionSettingsProvider.cs`）
- 下沉目标：`composition/Composition/src/` 下新建目录（`[Register]` 自动扫描）
- `EffortLevel` 枚举 + `EffortLevelHelper`（Abstractions）复用；持久化键 `ConfigKeyConstants.EffortLevel`
- CLI 不引 JoinCode 新增依赖；下沉后 JoinCode 程序集原类删除（避免两套实现）

## 解决方案
1. **下沉 `ExecutionSettingsProvider`** 到 composition 共享层（改为 composition 命名空间 + `[Register]`），CLI/GUI 的 `AddAiWorkflowServices` → `AddJoinCodeCompositionAutoRegisteredServices` 都会自动注册
2. **GUI 设置面板加 Effort 选择器**：`MainViewModel` 加 `SelectedEffortLevel` observable property，绑定 `IExecutionSettingsProvider.EffortLevel`；变更时持久化 `effortLevel` 键（对齐 CLI：auto → Remove，其它 → Set）
3. **门面暴露 Effort**：`IJccChatSession` 加 `EffortLevel CurrentEffortLevel` 读 + `SetEffortLevelAsync` 写，Hosting 层收敛 DI 解析
4. 移除 JoinCode 程序集原 `ExecutionSettingsProvider`（防重复注册）

## 发现的原实现 Bug（下沉时修复）
`ExecutionSettingsProvider.EffortLevel` getter 原逻辑：
```csharp
get => _effortLevelLazy.IsValueCreated ? _effortLevelLazy.Value : _effortLevel; // _effortLevel 默认 0=Low
```
- **Bug1（默认值）**：Lazy 未求值时返回字段默认 `Low`，而非预期的 `Auto`。CLI `/effort` 无参显示当前力度时，未持久化场景会误显示 `low` 而非 `auto`。
- **Bug2（Set 丢失）**：Lazy 已求值（首次 get 触发）后，`setter` 只改 `_effortLevel` 字段，getter 仍返回 Lazy.Value 旧值 → `EffortLevel` 设置不生效。
- **修复**：改为双变量模式（规则3）——首次访问触发一次持久化加载，`set` 立即生效并标记已加载：
```csharp
private EffortLevel _effortLevel = EffortLevel.Auto;
private bool _isLoaded;
public EffortLevel EffortLevel
{
    get { if (!_isLoaded) { _effortLevel = LoadPersistedEffort(); _isLoaded = true; } return _effortLevel; }
    set { if (_effortLevel != value) { telemetry...; } _effortLevel = value; _isLoaded = true; }
}
```
- **影响**：下沉类被 CLI + GUI 同时复用，此修复两边受益（对齐 CLI `/effort` 的 auto 默认语义）。


## 涉及改动
| 文件 | 改动 |
|------|------|
| `composition/Composition/src/**/ExecutionSettingsProvider.cs` | 新增：下沉版（`[Register]`，composition 命名空间） |
| `app/JoinCode/Services/Core/ExecutionSettingsProvider.cs` | 删除（移到 `.xxx/`） |
| `app/JoinCodeGui/Hosting/IJccChatSession.cs` | 加 EffortLevel 读写 |
| `app/JoinCodeGui/Hosting/JccChatSession.cs` | 解析 `IExecutionSettingsProvider` + 持久化 |
| `app/JoinCodeGui/ViewModels/MainViewModel.cs` | 加 EffortLevel 选择属性/命令 |
| `app/JoinCodeGui/Views/MainWindow.axaml` | 设置面板加 Effort 选择器 |
| 测试 | GUI DI 解析、Effort 读写/持久化 |

## 里程碑记录
（每步：红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交）
