# 0114. 配置模板源码生成器 — C# 类自动生成 JSON 模板

- 状态：accepted
- 日期：2026-09-17
- 决策者：用户 + AI

## 背景

当前配置系统存在以下问题：
1. `/init` 生成的默认 settings.json 是 `BuildDefaultSettingsJson()` 中的**硬编码字符串**，改 C# 类属性后 JSON 不自动更新
2. 配置文件分散在多个位置，无统一管理
3. 新增配置属性需要同时改 C# 类和硬编码 JSON 骨架，容易遗漏

## 决策

新增源码生成器 `ConfigTemplateGenerator`，编译时扫描 `[SettingsMerge]` 类的 `[JsonPropertyName]` 属性，自动生成 JSON 模板字符串。

### 架构

```
SettingsJson.cs ([SettingsMerge] + [JsonPropertyName])
      │  编译时扫描
      ▼
ConfigTemplateGenerator (源码生成器)
      │  生成
      ▼
ConfigTemplates.g.cs (自动生成)
  ├── GetSettingsJsonTemplate() → JSON 字符串
  ├── GetCurrentSettingsTemplate() → JSON 字符串
  └── GetProfileSettingsTemplate() → JSON 字符串
      │  /init 调用
      ▼
.jcc/config/ (统一配置文件夹)
  ├── settings.json (模板)
  └── ...
```

### 默认值映射规则

| C# 类型 | JSON 默认值 |
|---------|------------|
| `string?` | `null` |
| `bool?` | `null` |
| `int?` | `null` |
| `Dictionary<string, T>` | `{}` |
| `List<T>` | `[]` |
| 嵌套 `[SettingsMerge]` 类型 | 递归生成 |
| Enum | 第一个枚举值 |

### /init 更新

`/init` 调用 `ConfigTemplates.GetSettingsJsonTemplate()` 写入 `.jcc/config/settings.json`，不再使用硬编码骨架。

## 替代方案

1. **运行时序列化默认实例** — `JsonSerializer.Serialize(new SettingsJson())`。简单但需运行时构造，且默认值依赖构造函数初始化
2. **拆分现有硬编码骨架** — 最小改动但仍是硬编码，改类属性后 JSON 不自动更新
3. **不生成** — 维持现状，手动同步 C# 类和 JSON 骨架

## 后果

- 正面：改 C# 类属性 → 重新编译 → JSON 模板自动更新，单一数据源
- 负面：源码生成器增加编译时间（微秒级，可忽略）
- 中性：`BuildDefaultSettingsJson()` 保留向后兼容，新代码用 `ConfigTemplates`

## 相关

- 上游：[0113](0113-mtp-perturbation-bash-defense.md) MTP 扰动防御（暴露配置管理痛点）
- 配置加载：`SettingsLoader` + `ConfigLoader` 7 步管道
- 现有生成器：`SettingsMergeGenerator` 扫描相同特性，可复用扫描逻辑
