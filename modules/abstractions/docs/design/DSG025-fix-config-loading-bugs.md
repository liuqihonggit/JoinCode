# 修复配置加载链 Bug 工单

> 创建时间: 2026-09-17
> 触发场景: Daily Command Test CI 报 `未知的 Provider 'deepseek'，可用值: azure`

## 根因链

```
settings.json 未加载
  ├─ ProviderDefinitionRegistry.ApplyVendorFromSettings 静默失败 (Bug D: Debug.WriteLine Release 编译掉)
  ├─ SettingsLoader.LoadSettingsFileAsync 静默失败 (Bug A: catch {} 无日志)
  ├─ ProviderConfig.Vendor 默认 "deepseek" (Bug B: 与 registry 默认 "azure" 不一致)
  └─ 报错 "未知的 Provider 'deepseek'，可用值: azure"
```

## Bug 清单

### Bug A (P0): `LoadSettingsFileAsync` 吞所有异常无日志

- **文件**: `lib/guard/configuration/configuration2/core/loading/core/SettingsLoader.cs:336-351`
- **问题**: `catch { return null; }` 完全静默
- **修复**: 加 `Trace.WriteLine` 日志
- **同步修复**: `LoadSettingsFileSync` (line 356-370) 同样问题

### Bug B (P0): 默认 vendor "deepseek" 与默认 registry "azure" 不一致

- **文件**: `lib/abstractions/abs_core/configuration/providers/ProviderConfig.cs:14`
- **文件**: `lib/guard/configuration/configuration2/core/providers/shared/ProviderDefinitionRegistry.cs:22-23`
- **问题**: settings.json 没加载时，vendor="deepseek" 但 registry 只有 "azure"
- **修复**: `ProviderDefinitionRegistry` 构造时也保证 "deepseek" 存在（作为默认 vendor 兜底）

### Bug C (P1): `ApplyVendorFromSettings` 异常处理范围太窄

- **文件**: `lib/guard/configuration/configuration2/core/providers/shared/ProviderDefinitionRegistry.cs:71`
- **问题**: 只捕获 IOException + JsonException，InvalidOperationException 会崩溃
- **修复**: 扩大 catch 到 `Exception`

### Bug D (P0): `ApplyVendorFromSettings` 用 `Debug.WriteLine` Release 下编译掉

- **文件**: `lib/guard/configuration/configuration2/core/providers/shared/ProviderDefinitionRegistry.cs:73`
- **问题**: `Debug.WriteLine` 在 Release 模式下被编译掉，生产环境无日志
- **修复**: 改用 `Trace.WriteLine`

### Bug E (P2): 两处独立读取 settings.json，解析器不同

- **文件**: `ProviderDefinitionRegistry.cs:51` (JsonNode.Parse) vs `SettingsLoader.cs:343` (RelaxedJsonSerializer)
- **问题**: 同一文件两处独立读取，解析器不同可能不一致
- **修复**: 暂不修（设计债务，后续重构）

## 修复顺序

1. Bug D: Debug → Trace（1行改动）
2. Bug A: catch {} 加 Trace 日志（2处，各+1行）
3. Bug B: ProviderDefinitionRegistry 加 deepseek 兜底（+3行）
4. Bug C: 扩大 catch 范围（1行改动）
5. 编译验证
6. 提交

## 风格要求

- LINQ 链式语法
- 万物皆 node，复用现有组件
- 无后向兼容（项目不需要）

## 验证

- 编译通过
- CI Debug 步骤应显示 settings.json 读取成功
- Daily Command Test CI 通过
