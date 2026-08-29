# 0-GC 字符串效率审计报告

> 日期: 2026-08-24
> 范围: `core/safety/Guard/src` 字符串拼接热路径审计
> 原则: 识别真正热路径,避免过度优化; .NET 10 插值字符串已由编译器优化为 DefaultInterpolatedStringHandler

## 1. 审计结论

### 1.1 .NET 10 字符串优化现状

| 模式 | .NET 10 优化 | 是否需手动优化 |
|------|-------------|---------------|
| `$"{a}:{b}"` 插值 | 编译为 `DefaultInterpolatedStringHandler`,单次分配 | ❌ 否 |
| `string.Concat(a, b, c)` | 内部计算总长度后单次分配 | ❌ 否 |
| `string.Join(" ", items)` | 单次分配 | ❌ 否 |
| `string.Join(" ", tokens.Take(n).ToArray())` | **额外分配 n 元素数组** | ✅ 是 |
| 循环内 `+=` 拼字符串 | JCC5002 分析器已禁止 | ❌ 已强制 |

### 1.2 热路径识别

| 文件 | 行号 | 模式 | 调用频率 | 优化收益 |
|------|------|------|---------|---------|
| `ReadOnlyCommandDetector.cs` | 184,185 | `string.Join(" ", tokens.Take(2/3).ToArray())` | 每命令检测 | 中: 减少 1 数组分配/命令 |
| `PkceGenerator.cs` | 105 | `string.Concat(bytes.Select(...))` | OAuth 一次性 | 低: 非热路径 |
| `OAuthClient.cs` | 95 | `string.Join("&", queryParams.Select(...))` | OAuth 一次性 | 低: 非热路径 |
| `PermissionManager.cs` | 272,280 | `$"{toolName}:noargs"` 等 | 权限检查 | 低: 插值已优化 |
| `RemotePolicyService.cs` | 144,182,216,217 | `$"{ruleId}:{action}"` | 策略检查 | 低: 插值已优化 |

### 1.3 非热路径(不优化)

以下为错误消息/日志/提示,一次性构造,无需优化:
- `ConfigLoader.cs:234,245,246` — 警告消息
- `SettingsMapper.cs:62,193` — 异常消息
- `SandboxManager.cs:234,351` — 异常消息
- `PermissionToolHandlers.cs` 多处 — UI 响应消息(用 StringBuilder 已合理)
- `PsSecurityChecker.cs` 多处 — 安全提示消息

## 2. 优化方案

### 2.1 ReadOnlyCommandDetector 热路径优化(执行)

**问题**: `string.Join(" ", tokens.Take(2).ToArray())` 每次分配 string[2] 数组。

**优化**: 用 `string.Concat` 直接拼接,避免数组分配。

```csharp
// 改前
&& !CommandAllowlist.TryGetValue(string.Join(" ", tokens.Take(2).ToArray()), out config)
&& !CommandAllowlist.TryGetValue(string.Join(" ", tokens.Take(3).ToArray()), out config))

// 改后(边界安全 + Concat 直拼)
&& !CommandAllowlist.TryGetValue(TwoTokenKey(tokens), out config)
&& !CommandAllowlist.TryGetValue(ThreeTokenKey(tokens), out config))

// 辅助方法 — 避免数组分配
private static string TwoTokenKey(string[] tokens)
    => tokens.Length >= 2 ? string.Concat(tokens[0], " ", tokens[1]) : tokens[0];
private static string ThreeTokenKey(string[] tokens)
    => tokens.Length >= 3 ? string.Concat(tokens[0], " ", tokens[1], " ", tokens[2])
    : TwoTokenKey(tokens);
```

### 2.2 后续建议(不执行)

| 项 | 收益 | 建议 |
|----|------|------|
| PkceGenerator 用 stackalloc + 指针直写 | 0-GC | OAuth 非热路径,收益低 |
| OAuthClient 查询串用 StringBuilder | 减少 LINQ 开销 | OAuth 非热路径,收益低 |
| FrozenDictionary 支持 span key 查找 | 真 0-GC 查找 | 需 .NET 基础库支持,非本次范围 |

## 3. 决策记录

<!-- 🤖 Auto Decision: 2026-08-24 -->
<!-- 决策: 只优化 ReadOnlyCommandDetector 热路径, 不优化 OAuth/错误消息等非热路径 -->
<!-- 原因: .NET 10 插值字符串已由编译器优化为 DefaultInterpolatedStringHandler, 手动优化收益低且增加复杂度 -->
<!-- 替代方案: 全面 stackalloc + 指针直写 — 过度优化, 违反"可运行 > 完美"原则 -->
<!-- 验证: 待优化后编译+测试通过记录 ✅ -->
