namespace NativeTui.Core.Repl;

/// <summary>命令使用记录条目 DTO — ~/.jcc/command-history.json 的 entries 数组元素（AOT JsonContext 双向转换）。</summary>
/// <param name="Name">命令名（不含 / 前缀）。</param>
/// <param name="Count">使用计数。</param>
/// <param name="LastUsed">最近使用时间（UTC）。</param>
internal sealed record CommandUsageEntryDto(string Name, int Count, DateTimeOffset LastUsed);

/// <summary>命令使用历史文件 DTO — 顶层 { entries: [...] } 结构。</summary>
/// <param name="Entries">使用记录条目清单。</param>
internal sealed record CommandUsageFileDto(List<CommandUsageEntryDto>? Entries);

/// <summary>命令使用历史 JSON 上下文 — AOT 源生成（ADR 0141/0133：DTO 双向转换，禁止手写拼接）；
/// camelCase 字段命名对齐基准 JSON 风格（BridgeJsonContext 先例）。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(CommandUsageFileDto))]
internal sealed partial class CommandUsageJsonContext : JsonSerializerContext;
