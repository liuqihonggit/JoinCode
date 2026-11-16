// search 组下沉 handler — project 依赖图 6 个（纯 BCL 解析 csproj/sln，自建图，行为与宿主一致）。
// 数据不依赖宿主索引：exe 直接扫描工作区所有 .csproj，用 XDocument 解析 ProjectReference/PackageReference，
// 构建与宿主 ProjectDependencyGraph 等价的图。create_index/update_index 需 Tree-sitter native、
// search_semantic/document 需 ONNX 模型、符号/graph 分析类依赖宿主建的索引快照——这些留宿主。
using System.Collections.Frozen;
using System.Xml.Linq;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Search;

internal static partial class ProjectGraphCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["search_project_deps"] = ProjectDepsAsync,
            ["search_project_dependents"] = ProjectDependentsAsync,
            ["search_affected_projects"] = AffectedProjectsAsync,
            ["search_project_nugets"] = ProjectNugetsAsync,
            ["search_nuget_projects"] = NugetProjectsAsync,
            ["search_all_projects"] = AllProjectsAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string id) => Handlers.TryGetValue(id, out var h) ? h : null;
    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(x => x, StringComparer.Ordinal);

    // ==================== 项目图（自建，纯 BCL） ====================

    private sealed class ProjInfo {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public List<string> Refs { get; set; } = [];       // ProjectReference 目标（规范化路径）
        public List<string> Packages { get; set; } = [];   // "Name,Version"
    }

    private static string Normalize(string p) {
        try { return Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant(); }
        catch { return (p ?? "").Replace('\\', '/').ToLowerInvariant(); }
    }

    private static string FindWorkspaceRoot() {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 32 && dir is not null; i++, dir = dir.Parent) {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            if (Directory.GetFiles(dir.FullName, "*.slnx").Length > 0 || Directory.GetFiles(dir.FullName, "*.sln").Length > 0) return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private static List<ProjInfo> BuildGraph(out Dictionary<string, List<string>> refsByTarget, out Dictionary<string, List<string>> packagesByName) {
        refsByTarget = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        packagesByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var root = FindWorkspaceRoot();
        var projects = new List<ProjInfo>();
        foreach (var file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)) {
            // 跳过 bin/obj/artifacts 下的产物
            var lower = file.Replace('\\', '/').ToLowerInvariant();
            if (lower.Contains("/bin/") || lower.Contains("/obj/") || lower.Contains("/artifacts/")) continue;
            ProjInfo info;
            try {
                var doc = XDocument.Load(file);
                info = new ProjInfo {
                    Path = Path.GetFullPath(file),
                    Name = Path.GetFileNameWithoutExtension(file),
                    TargetFramework = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "TargetFramework")?.Value ?? ""
                };
                foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference")) {
                    var inc = pr.Attribute("Include")?.Value;
                    if (string.IsNullOrEmpty(inc)) continue;
                    // 展开 MSBuild 变量（与宿主 CsprojParser.ReplaceMsBuildVariables 等价）：
                    // $(RepoRoot)/$(MSBuildThisFileDirectory) → 工作区根（Directory.Build.props 定义 RepoRoot=该目录）。
                    var rootSlash = root.Replace('\\', '/').TrimEnd('/') + "/";
                    inc = inc.Replace("$(RepoRoot)", rootSlash, StringComparison.OrdinalIgnoreCase)
                             .Replace("$(MSBuildThisFileDirectory)", rootSlash, StringComparison.OrdinalIgnoreCase);
                    if (inc.Contains("$")) continue; // 无法解析的其他变量，跳过（与宿主 ResolvePath 返回 null 一致）
                    var full = Path.IsPathRooted(inc.Replace('/', Path.DirectorySeparatorChar))
                        ? Path.GetFullPath(inc)
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, inc.Replace('\\', '/')));
                    info.Refs.Add(Normalize(full));
                    var key = Normalize(full);
                    if (!refsByTarget.TryGetValue(key, out var list)) refsByTarget[key] = list = [];
                    list.Add(Normalize(info.Path));
                }
                foreach (var pk in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference")) {
                    var name = pk.Attribute("Include")?.Value ?? "";
                    var ver = pk.Attribute("Version")?.Value ?? "";
                    if (name.Length == 0) continue;
                    info.Packages.Add($"{name},{ver}");
                    var nkey = name.ToLowerInvariant();
                    if (!packagesByName.TryGetValue(nkey, out var plist)) packagesByName[nkey] = plist = [];
                    plist.Add(Normalize(info.Path));
                }
            } catch (Exception) { continue; }
            projects.Add(info);
        }
        return projects;
    }

    private static string DisplayName(string normalizedPath, List<ProjInfo> projects) {
        var p = projects.FirstOrDefault(x => Normalize(x.Path) == normalizedPath);
        return p?.Path ?? normalizedPath;
    }

    // ==================== 6 个 handler ====================

    private static Task<int> ProjectDepsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "project_path", "search_project_deps");
        var projects = BuildGraph(out _, out _);
        var info = projects.FirstOrDefault(p => Normalize(p.Path) == Normalize(path) || p.Name == Path.GetFileNameWithoutExtension(path));
        if (info is null) { Console.Out.WriteLine($"项目不存在或未找到: {path}"); return Task.FromResult(0); }
        if (info.Refs.Count == 0) { Console.Out.WriteLine($"{info.Path} 无项目依赖"); return Task.FromResult(0); }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{info.Path} 的项目依赖 ({info.Refs.Count}):");
        sb.AppendLine();
        for (var i = 0; i < info.Refs.Count; i++) sb.AppendLine($"{i + 1}. {DisplayName(info.Refs[i], projects)}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> ProjectDependentsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "project_path", "search_project_dependents");
        var projects = BuildGraph(out var byTarget, out _);
        var key = Normalize(path);
        if (!byTarget.TryGetValue(key, out var deps) || deps.Count == 0) { Console.Out.WriteLine($"无项目依赖于: {path}"); return Task.FromResult(0); }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"依赖于 {path} 的项目 ({deps.Count}):");
        sb.AppendLine();
        for (var i = 0; i < deps.Count; i++) sb.AppendLine($"{i + 1}. {DisplayName(deps[i], projects)}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> AffectedProjectsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var file = Required(o, "file_path", "search_affected_projects");
        var projects = BuildGraph(out var byTarget, out _);
        var normalized = Normalize(file);
        // 找拥有该文件的最深层项目
        var owner = projects.Where(p => normalized.StartsWith(Normalize(Path.GetDirectoryName(p.Path)!) + "/", StringComparison.Ordinal))
            .OrderByDescending(p => Path.GetDirectoryName(p.Path)!.Length).FirstOrDefault();
        if (owner is null) { Console.Out.WriteLine($"文件不属于任何已知项目: {file}"); return Task.FromResult(0); }
        var visited = new HashSet<string>(StringComparer.Ordinal) { Normalize(owner.Path) };
        var queue = new Queue<string>(); queue.Enqueue(Normalize(owner.Path));
        while (queue.Count > 0) {
            var cur = queue.Dequeue();
            if (!byTarget.TryGetValue(cur, out var dependents)) continue;
            foreach (var d in dependents) if (visited.Add(d)) queue.Enqueue(d);
        }
        visited.Remove(Normalize(owner.Path));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"文件 {file} 影响的项目 ({visited.Count}):");
        foreach (var v in visited) sb.AppendLine($"  {DisplayName(v, projects)}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> ProjectNugetsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "project_path", "search_project_nugets");
        var projects = BuildGraph(out _, out _);
        var info = projects.FirstOrDefault(p => Normalize(p.Path) == Normalize(path) || p.Name == Path.GetFileNameWithoutExtension(path));
        if (info is null) { Console.Out.WriteLine($"项目不存在: {path}"); return Task.FromResult(0); }
        if (info.Packages.Count == 0) { Console.Out.WriteLine($"{info.Path} 无 NuGet 包引用"); return Task.FromResult(0); }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{info.Path} 的 NuGet 包 ({info.Packages.Count}):");
        foreach (var pk in info.Packages) sb.AppendLine($"  {pk}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> NugetProjectsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var pkg = Required(o, "package_name", "search_nuget_projects");
        var projects = BuildGraph(out _, out var byPkg);
        if (!byPkg.TryGetValue(pkg.ToLowerInvariant(), out var list) || list.Count == 0) { Console.Out.WriteLine($"无项目引用 NuGet 包: {pkg}"); return Task.FromResult(0); }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"引用 {pkg} 的项目 ({list.Count}):");
        foreach (var p in list.Distinct(StringComparer.Ordinal)) sb.AppendLine($"  {DisplayName(p, projects)}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> AllProjectsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var projects = BuildGraph(out _, out _);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"工作区项目 ({projects.Count}):");
        foreach (var p in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  {p.Name} [{p.TargetFramework}] {p.Path}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string tool) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"{tool} 缺少必填参数 --{key}。正确写法: jcc tool call search {tool} --{key} <值>");
}
