using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JoinCode.Abstractions.Tools;

/// <summary>分组 exe 内的单个子命令实现：入参是已解析的 <c>--key value</c> 字典，返回值是进程退出码。</summary>
/// <param name="options">参数表，键不含前缀 <c>--</c>，大小写不敏感。</param>
/// <param name="cancellationToken">Ctrl+C 取消令牌。</param>
public delegate Task<int> CliNodeHandler(IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken);

/// <summary>
/// 分组 exe 的声明式定义 —— 各组 Program.cs 只需声明「我是谁 + 我有哪些本地实现」。
/// <para>存在理由：13 个分组 exe 的入口逻辑（-h / --list-local / 参数解析 / 本地分发 / 转发宿主 / 退出码）
/// 若各写一份，13 份就会慢慢长出 13 种细节差异（这正是"逐组做实"最容易失控的地方）。
/// 收成一处后，新增分组只写一个字典，行为天然一致。</para>
/// </summary>
public sealed class CliNodeDefinition {
    /// <summary>分组 ID，与 group_map.yaml / node.yaml 的 id 一致。</summary>
    public required string GroupId { get; init; }

    /// <summary>内嵌帮助资源的逻辑名。</summary>
    public required string HelpResource { get; init; }

    /// <summary>帮助输出里的分组名，如 <c>web 分组</c>。</summary>
    public required string Title { get; init; }

    /// <summary>按工具 ID 取本地实现；返回 null 表示该工具需转发宿主。</summary>
    public required Func<string, CliNodeHandler?> Resolve { get; init; }

    /// <summary>本 exe 真实注册的本地工具 ID，供 <c>--list-local</c> 自报。</summary>
    public required Func<IEnumerable<string>> LocalToolIds { get; init; }

    /// <summary>转发宿主时读取宿主 exe 路径的环境变量名。</summary>
    public string HostEnvironmentVariable { get; init; } = "JCC_HOST_EXE";

    /// <summary>转发宿主时的默认 exe 名。</summary>
    public string HostExecutable { get; init; } = "jcc";

    /// <summary>本地实现依赖宿主独有能力时的说明，出现在转发失败提示里。</summary>
    public string HostReason { get; init; } = "该工具依赖宿主独有能力（引擎服务 / 共享状态 / 模型调用），零依赖 exe 无法等价实现。";
}

/// <summary>
/// 分组工具 exe 的统一入口 —— 全部 13 个分组共用，避免入口行为逐组漂移。
/// <para>退出码约定（与 jcc tool call 执行器一致）：0 成功 / 2 参数或本地执行错误 / 3 宿主缺失或启动失败。</para>
/// </summary>
public static class CliNodeRunner {
    /// <summary>成功。</summary>
    public const int ExitSuccess = 0;

    /// <summary>参数错误或本地执行失败。</summary>
    public const int ExitInvalidArguments = 2;

    /// <summary>需要宿主但宿主不可用。</summary>
    public const int ExitHostUnavailable = 3;

    private const string HelpMarker = "  {0} — ";
    private const string ToolListHeader = "工具列表";

    /// <summary>运行分组 exe。</summary>
    /// <param name="definition">分组声明。</param>
    /// <param name="args">命令行参数（不含 exe 自身）。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> RunAsync(CliNodeDefinition definition, string[] args) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(args);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };
        return await RunCoreAsync(definition, args, cts.Token).ConfigureAwait(false);
    }

    private static async Task<int> RunCoreAsync(CliNodeDefinition definition, string[] args, CancellationToken ct) {
        var help = LoadHelp(definition.HelpResource);

        if (args.Length == 0 || IsHelp(args[0])) {
            Write(help);
            return ExitSuccess;
        }
        if (CliNodeCoverage.IsRequested(args[0])) {
            Write(CliNodeCoverage.Render(definition.GroupId, definition.LocalToolIds()));
            return ExitSuccess;
        }

        var toolId = args[0];
        if (args.Length > 1 && IsHelp(args[1])) {
            PrintToolHelp(definition, help, toolId);
            return ExitSuccess;
        }

        var options = ParseOptions(args.AsSpan(1));
        var handler = definition.Resolve(toolId);
        if (handler is not null) return await RunLocalAsync(definition, toolId, handler, options, ct).ConfigureAwait(false);

        var known = ListToolIds(help);
        if (known.Count > 0 && !known.Contains(toolId, StringComparer.Ordinal)) {
            WriteError(BuildUnknownToolMessage(definition, toolId, known));
            return ExitInvalidArguments;
        }
        return await ForwardAsync(definition, toolId, options, ct).ConfigureAwait(false);
    }

    private static async Task<int> RunLocalAsync(
        CliNodeDefinition definition, string toolId, CliNodeHandler handler,
        IReadOnlyDictionary<string, string> options, CancellationToken ct) {
        try {
            return await handler(options, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            WriteError($"工具 '{toolId}' 已取消。");
            return ExitInvalidArguments;
        } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                         or HttpRequestException or TimeoutException or NotSupportedException
                                         or InvalidOperationException) {
            var reason = ex.Message;
            WriteError($"工具 '{toolId}' 本地执行失败（{definition.GroupId} 本地执行层拦截）：{reason}");
            WriteError($"修复方式：{HintFor(ex, toolId)}");
            return ExitInvalidArguments;
        }
    }

    private static string HintFor(Exception ex, string toolId) => ex switch {
        ArgumentException => $"{toolId} -h 查看必填参数与取值范围。",
        HttpRequestException => "检查 URL 与网络连通性；受限站点改用宿主 web_fetch。",
        UnauthorizedAccessException => "目标路径无访问权限，换一个可写目录再试。",
        TimeoutException => "操作超时，缩小搜索范围（--path）或提高宿主超时后重试。",
        _ => "检查路径、磁盘空间与参数取值；必要时改用宿主执行。"
    };

    private static async Task<int> ForwardAsync(
        CliNodeDefinition definition, string toolId, Dictionary<string, string> options, CancellationToken ct) {
        var host = Environment.GetEnvironmentVariable(definition.HostEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(host)) host = definition.HostExecutable;

        var payload = JsonSerializer.Serialize(options, CliNodeJsonContext.Default.DictionaryStringString);
        var psi = new ProcessStartInfo(host) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("mcp_call");
        psi.ArgumentList.Add(toolId);
        psi.ArgumentList.Add(payload);

        try {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动宿主进程");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            Write(await stdout.ConfigureAwait(false));
            WriteError(await stderr.ConfigureAwait(false), newline: false);
            return process.ExitCode;
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
            WriteError($"工具 '{toolId}' 无本地实现且需宿主 jcc 才能执行（转发层拦截）：{ex.Message}");
            WriteError($"原因：{definition.HostReason}");
            WriteError($"修复方式：设置 {definition.HostEnvironmentVariable} 指向 jcc.exe，或把 jcc 加入 PATH；也可用 -h 只查看说明。");
            return ExitHostUnavailable;
        }
    }

    private static string BuildUnknownToolMessage(CliNodeDefinition definition, string toolId, List<string> known) {
        var builder = new StringBuilder(256);
        builder.Append("未知工具 '").Append(toolId).Append("'（").Append(definition.Title).AppendLine("内不存在）。");
        var suggestions = Suggest(toolId, known);
        if (suggestions.Count > 0) {
            builder.Append("你是不是想用：").AppendLine(string.Join(" / ", suggestions));
        }
        builder.Append("执行 ").Append(definition.GroupId).AppendLine(" -h 查看本组全部工具。");
        return builder.ToString();
    }

    /// <summary>近似名建议：包含关系优先，其次编辑距离不超过 2。</summary>
    internal static List<string> Suggest(string input, IReadOnlyList<string> candidates) {
        var lowered = input.ToLowerInvariant();
        var contains = candidates
            .Where(id => id.Contains(lowered, StringComparison.Ordinal) || lowered.Contains(id, StringComparison.Ordinal))
            .Take(5)
            .ToList();
        if (contains.Count > 0) return contains;

        return candidates
            .Select(id => (Id: id, Distance: EditDistance(lowered, id)))
            .Where(pair => pair.Distance <= 2)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Id)
            .Take(5)
            .ToList();
    }

    private static int EditDistance(string left, string right) {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++) {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++) {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            Array.Copy(current, previous, current.Length);
        }
        return previous[right.Length];
    }

    private static void PrintToolHelp(CliNodeDefinition definition, string help, string toolId) {
        var normalized = help.Replace("\r\n", "\n");
        var marker = string.Format(System.Globalization.CultureInfo.InvariantCulture, HelpMarker, toolId);
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) {
            Write(normalized);
            return;
        }
        var boundaries = new[] {
            normalized.IndexOf("\n\n  ", start + marker.Length, StringComparison.Ordinal),
            normalized.IndexOf("\n描述来源:", start, StringComparison.Ordinal),
            normalized.Length
        }.Where(index => index > start).ToArray();
        var end = boundaries.Length > 0 ? boundaries.Min() : normalized.Length;
        var mode = definition.Resolve(toolId) is not null
            ? "local（本 exe 内零依赖实现）"
            : "host（转发宿主 jcc mcp_call）";
        Write($"{definition.Title} / {toolId} — 参数说明（复用现有 [McpToolParameter] 特性）\n");
        Write($"执行方式: {mode}\n");
        Write(normalized[start..end].TrimEnd() + "\n");
    }

    private static string LoadHelp(string resourceName) {
        var entry = Assembly.GetEntryAssembly();
        if (entry is not null) {
            using var stream = entry.GetManifestResourceStream(resourceName);
            if (stream is not null) {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().Replace("\r\n", "\n");
            }
        }
        throw new InvalidOperationException($"未找到内嵌帮助资源 '{resourceName}'，请检查 csproj 的 EmbeddedResource 配置。");
    }

    /// <summary>从帮助文本的「工具列表」段解析工具 ID，用于未知工具判定与近似名建议。</summary>
    internal static List<string> ListToolIds(string help) {
        var ids = new List<string>();
        var normalized = help.Replace("\r\n", "\n");
        var headerIndex = normalized.IndexOf(ToolListHeader, StringComparison.Ordinal);
        if (headerIndex < 0) return ids;
        foreach (var line in normalized[headerIndex..].Split('\n')) {
            if (line.Length == 0) continue;
            if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("    ", StringComparison.Ordinal)) {
                var trimmed = line.Trim();
                var space = trimmed.IndexOf(' ');
                if (space <= 0) continue;
                var id = trimmed[..space].Trim();
                if (id.Length > 0 && ids.Count < 1000) ids.Add(id);
            } else if (ids.Count > 0 && !line.StartsWith(' ')) {
                break;
            }
        }
        return ids;
    }

    private static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++) {
            var token = args[i];
            if (IsOptionToken(token)) {
                // 单横线短选项（-i / -A）保留整串原样作键，与帮助里的「别名=-i」一一对应。
                var isLong = token.StartsWith("--", StringComparison.Ordinal);
                var key = isLong ? token[2..] : token;
                var equals = key.IndexOf('=');
                if (equals > 0) {
                    result[key[..equals]] = key[(equals + 1)..];
                    continue;
                }
                if (i + 1 < args.Length && !IsOptionToken(args[i + 1])) {
                    result[key] = args[++i];
                    continue;
                }
                result[key] = "true";
                continue;
            }
            result["arg" + (i + 1)] = token;
        }
        return result;
    }

    /// <summary>
    /// 选项判定：--long，或 -x（排除 -5 这类负数，避免把值当选项）。
    /// </summary>
    private static bool IsOptionToken(string token) {
        if (token.StartsWith("--", StringComparison.Ordinal)) return true;
        return token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]);
    }

    private static bool IsHelp(string token) => token is "-h" or "--help" or "help";

    private static void Write(string text) => Console.Out.Write(text);

    private static void WriteError(string text, bool newline = true) {
        if (newline) Console.Error.WriteLine(text);
        else Console.Error.Write(text);
    }
}

/// <summary>转发宿主时的参数序列化上下文 —— 源生成避免 AOT 下的反射序列化。</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CliNodeJsonContext : JsonSerializerContext { }
