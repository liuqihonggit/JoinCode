# 0004. 配置大于代码 — 模态能力显式注册

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

LLM 模型有多种模态能力（文本、图像识图、音频等）。早期代码通过模型 ID 字符串模式推断模态（如 ID 含 `vision` → 识图），但存在盲区：`image`/`claude`/`gpt-4o` 等实际识图但 ID 无 `vision`。用户指定 `JCC_MODEL_ID` 为未注册模型时，代码会静默推断补注册，导致能力判断错误。

## 决策

1. **模态能力由 `settings.json` 显式配置**：`vendor.{provider}.models` 节点声明 `Capabilities.Modalities`
2. **删除 `InferCapabilities` 硬编码推断**（2026-08-22 执行）
3. **`EnsureEnvModelInConfig` 无条件抛 `ConfigurationException[GRD016]`**：未注册模型直接报错，要求用户先在 settings.json 注册
4. **远程拉取新模型（`AutoFetchModels`）模态留默认 `Text`**：用户在 settings.json 手动配置需要的模态

## 替代方案

1. **保留 ID 字符串推断**：放弃。命名约定有盲区，`gpt-4o` 识图但 ID 无 `vision`，启发式不可靠。
2. **运行时探测模型能力**：放弃。需额外 API 调用，增加延迟和失败面，且部分供应商不提供能力查询接口。
3. **代码内硬编码已知模型能力表**：放弃。新模型发布需改代码重新编译，违反"配置大于代码"。

## 后果

- 正面：能力判断确定可靠；新模型只需改配置文件无需编译；配置可热重载
- 负面：用户首次使用新模型必须手动编辑 settings.json，门槛略高
- 中性：定位文件 `ConfigLoader.cs:582 EnsureEnvModelInConfig`、`ModelListMerger.cs:39 Merge`
