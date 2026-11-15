namespace JoinCode.CliNode.Git;

/// <summary>解析 --key value / --key=value / 位置参数，供各分组工具复用。</summary>
internal sealed class ArgBag {
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public string[] Positional { get; }

    public ArgBag(string[] args) {
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++) {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal)) {
                positional.Add(token);
                continue;
            }
            var body = token[2..];
            var eq = body.IndexOf('=');
            if (eq > 0) {
                _options[body[..eq]] = body[(eq + 1)..];
                continue;
            }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                _options[body] = args[++i];
            } else {
                _options[body] = "true";
            }
        }
        Positional = positional.ToArray();
    }

    public string? Get(string key) => _options.TryGetValue(key, out var value) ? value : null;

    public string Get(string key, string fallback) => Get(key) ?? fallback;

    public string WorkingDirectory => Get("working_dir") ?? Get("working-dir") ?? Get("cwd") ?? Directory.GetCurrentDirectory();
}
