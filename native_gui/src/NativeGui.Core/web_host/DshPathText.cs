namespace JoinCode.Gui.WebHost;

/// <summary>
/// 宿主目录路径文本工具（TASK066 C6）：统一正斜杠表示，兼容 Windows 盘符/UNC 与内存文件系统的
/// 根路径，避免 <see cref="Path"/> 在不同平台对 <c>/foo</c> 的差异。所有方法只做字符串运算。
/// </summary>
internal static class DshPathText {
    /// <summary>规范化分隔符（正斜杠）并去掉尾部分隔符；根路径保留（<c>/</c>、<c>C:/</c>、<c>//server/share</c>）</summary>
    internal static string Normalize(string path) {
        var text = path.Replace('\\', '/');
        while (text.Length > 1 && text.EndsWith('/')) {
            text = text[..^1];
        }
        if (text.Length == 2 && text[1] == ':') {
            return text + "/";
        }
        return text;
    }

    /// <summary>是否绝对路径：以 / 开头（含 UNC）或盘符加斜杠（<c>C:/</c>）；拒绝 <c>C:foo</c> 这类盘符相对路径</summary>
    internal static bool IsAbsolute(string path) {
        var text = Normalize(path);
        return text.StartsWith('/')
            || (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '/');
    }

    /// <summary>取父目录；根路径返回 null</summary>
    internal static string? Parent(string path) {
        var text = Normalize(path);
        if (text == "/" || (text.Length == 2 && text[1] == ':') || IsUncRoot(text)) {
            return null;
        }
        if (text.StartsWith("//")) {
            var third = text.IndexOf('/', 2);
            return third < 0 ? null : text[..third];
        }
        var slash = text.LastIndexOf('/');
        if (slash < 0) {
            return null;
        }
        if (slash == 0) {
            return "/";
        }
        var parent = text[..slash];
        return parent.Length == 2 && parent[1] == ':' ? parent + "/" : parent;
    }

    /// <summary>从根到目标的完整祖先链（含目标）</summary>
    internal static List<string> Ancestors(string path) {
        var chain = new List<string>();
        for (var current = Normalize(path); current is not null; current = Parent(current)) {
            chain.Add(current);
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>显示名：根路径返回自身，其余返回最后一段</summary>
    internal static string DisplayName(string path) {
        var text = Normalize(path);
        if (text is "/" or "" || IsUncRoot(text) || (text.Length == 2 && text[1] == ':')) {
            return text;
        }
        var slash = text.LastIndexOf('/');
        return slash < 0 ? text : text[(slash + 1)..];
    }

    /// <summary>拼接子路径并规范化</summary>
    internal static string Join(string parent, string name) {
        var left = Normalize(parent);
        return Normalize(left == "/" ? "/" + name : left + "/" + name);
    }

    /// <summary>
    /// 把相对路径解析到根目录内；段级 pop-stack 防止 <c>..</c> 逃逸，越界返回 null。
    /// </summary>
    internal static string? ResolveWithin(string root, string relative) {
        var normalizedRoot = Normalize(root);
        var segments = new List<string>();
        foreach (var segment in relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            if (segment == ".") {
                continue;
            }
            if (segment == "..") {
                if (segments.Count == 0) {
                    return null;
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        var resolved = normalizedRoot;
        foreach (var segment in segments) {
            resolved = Join(resolved, segment);
        }
        return resolved;
    }

    /// <summary>最长公共根：判断路径是否位于根内（大小写不敏感，Windows 语义）</summary>
    internal static bool IsWithin(string root, string path) {
        var normalizedRoot = Normalize(root);
        var normalizedPath = Normalize(path);
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>UNC 根（<c>//server/share</c>，仅两段）</summary>
    private static bool IsUncRoot(string text) {
        if (!text.StartsWith("//")) {
            return false;
        }
        var segments = text[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 2;
    }
}
