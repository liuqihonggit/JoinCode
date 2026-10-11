# 严格语法规范与卫语句检测

## 基本信息

| 项目 | 内容 |
|------|------|
| 创建日期 | 2026-09-19 |
| 目标 | 设计更严格语法规范，编译报错强制，防止人/AI 写代码时不知道 |
| 前置 | TASK 已完成 K&R 大括号风格化 + EnforceCodeStyleInBuild=true |
| 备份 tag | `backup/before-kr-format-20260919-204844` |

## 背景

项目已有 62 条 JCC 自定义分析器规则（18 Error / 33 Warning / 11 Info），但 `.editorconfig` 仅 1 条显式 error。`EnforceCodeStyleInBuild=true` 已开启，但大部分风格规则仍是 suggestion 级别（不强制编译）。**无卫语句检测规则**。

## 三档递进方案

### 档1：格式风格强制（低风险）

`.editorconfig` 关键风格规则 → `:error`，编译时强制。因 `dotnet format` 已对齐，预期 0 错误。

| 规则 | 当前值 | 目标 | 说明 |
|------|--------|------|------|
| `csharp_prefer_braces` | `true` | `true:error` | 必须大括号 |
| `csharp_style_namespace_declarations` | `block_scoped` | `block_scoped:error` | 命名空间 block_scoped |
| `csharp_using_directive_placement` | `outside_namespace` | `outside_namespace:error` | using 放命名空间外 |
| `dotnet_style_qualification_for_field` | `false` | `false:error` | 禁 this. 字段限定 |
| `dotnet_style_qualification_for_method` | `false` | `false:error` | 禁 this. 方法限定 |
| `dotnet_style_qualification_for_property` | `false` | `false:error` | 禁 this. 属性限定 |
| `dotnet_style_qualification_for_event` | `false` | `false:error` | 禁 this. 事件限定 |
| `csharp_style_var_for_built_in_types` | `false` | `false:error` | 禁 var 内建类型 |
| `csharp_style_var_when_type_is_apparent` | `false` | `false:error` | 禁 var 类型明显 |
| `csharp_style_var_elsewhere` | `false` | `false:error` | 禁 var 其他 |
| `csharp_preferred_modifier_order` | `...` | `...:error` | 修饰符顺序 |
| `dotnet_style_readonly_field` | `true` | `true:error` | readonly 字段 |
| `csharp_style_prefer_top_level_statements` | `true` | `true:error` | 顶层语句 |

**验证标准**：编译 0 错误。若有错误，逐个修代码或降级规则。

### 档2：代码质量强制（中风险）

JCC Warning → Error，选择对代码质量影响大的规则：

| 规则 ID | 标题 | 当前 | 目标 | 说明 |
|---------|------|------|------|------|
| JCC3013 | 禁止空 catch 块 | Warning | Error | 空 catch 吞异常 |
| JCC5002 | 循环内字符串拼接 += | Warning | Error | ADR 0029 铁律 |
| JCC4001 | lock 在 async 方法 | Warning | Error | 死锁风险 |
| JCC9301 | IDisposable 字段未释放 | Warning | Error | 内存泄漏 |
| JCC3008 | ConfigureAwait(false) | Warning | Error | ADR 0045 规范 |
| JCC3006 | .Result/.Wait() 阻塞 | Warning | Error | 死锁风险 |
| JCC5001 | Thread.Sleep 阻塞 | Warning | Error | 性能 |

**验证标准**：编译，修违规代码。若错误过多，降级部分规则为 warning。

### 档3：新增卫语句检测分析器（高投入）

新写 JCC 分析器规则，检测嵌套 if 超过 3 层，建议卫语句扁平化。

| 项目 | 内容 |
|------|------|
| 规则 ID | JCC1009 |
| 标题 | 卫语句: if 嵌套超过3层，建议卫语句扁平化 |
| Severity | Warning（先 warning，验证后升 error） |
| CodeFix | 自动将嵌套 if 转为卫语句早返回 |
| 文件 | `gen/aot_safety.generator/GuardClauseRules.cs` |
| CodeFix | `gen/code_fixes/CodeFixProviders.cs` 新增 Provider |

**检测模式**：
```csharp
// ❌ 触发 JCC1009 — 嵌套3层
if (a) {
    if (b) {
        if (c) {
            // 主逻辑
        }
    }
}

// ✅ 卫语句扁平化
if (!a) return;
if (!b) return;
if (!c) return;
// 主逻辑
```

## 执行顺序

1. 档1 → 编译验证 → 提交
2. 档2 → 编译验证 → 修代码 → 提交
3. 档3 → 写分析器 → 编译验证 → 写 CodeFix → 提交

## 进度

- [x] 档1：格式风格强制（commit de158c24c）
- [x] 档2：代码质量强制（commit 6cee2afb8）
- [x] 档3：新增卫语句检测（commit e7fbbba2c）
  - JCC1009 分析器已写，Warning 级别，638 处违规
  - WarningsNotAsErrors=JCC1009 排除 TreatWarningsAsErrors 提升
  - insert_final_newline=true 已改
  - CodeFix 尚未写

## 后续

- 638 处 JCC1009 warning 待处理（修复或保持 warning）
- JCC1009 CodeFix 自动转换（可选）
- 验证后升 JCC1009 为 error
