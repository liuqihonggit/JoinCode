namespace Core.Tests.Web;

/// <summary>
/// 测试用 IConfigRoot 桩件 — 按路径存取配置值，未命中返回 false（回退硬编码默认值）。
/// <para>支持链式 Set：new FakeConfigRoot().Set("a.b", "v")</para>
/// </summary>
internal sealed class FakeConfigRoot : IConfigRoot {
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>设置指定路径的配置值（链式）</summary>
    public FakeConfigRoot Set(string path, string? value) {
        _values[path] = value;
        return this;
    }

    /// <inheritdoc />
    public bool TryGet<T>(string path, [NotNullWhen(true)] out T? value) {
        if (typeof(T) == typeof(string) && _values.TryGetValue(path, out var s) && s is not null) {
            value = (T)(object)s;
            return true;
        }
        value = default;
        return false;
    }

    /// <inheritdoc />
    public T Get<T>(string path, T defaultValue)
        => TryGet<T>(path, out var v) ? v : defaultValue;

    /// <inheritdoc />
    public IConfigSection GetSection(string path) => new FakeConfigSection(this, path);

    /// <inheritdoc />
    public void Invalidate(string path) { }

    /// <inheritdoc />
    public IConfigLayer User => new FakeConfigLayer(this);

    /// <inheritdoc />
    public IConfigLayer Pro => new FakeConfigLayer(this);

    /// <inheritdoc />
    public IConfigLayer Local => new FakeConfigLayer(this);

    private sealed class FakeConfigLayer : IConfigLayer {
        private readonly FakeConfigRoot _root;
        public FakeConfigLayer(FakeConfigRoot root) => _root = root;
        public bool Exists => true;
        public bool TryGet<T>(string path, [NotNullWhen(true)] out T? value)
            => _root.TryGet<T>(path, out value);
    }

    private sealed class FakeConfigSection : IConfigSection {
        private readonly FakeConfigRoot _root;
        private readonly string _prefix;
        public FakeConfigSection(FakeConfigRoot root, string prefix) {
            _root = root;
            _prefix = prefix;
        }
        public bool TryGet<T>(string relativePath, [NotNullWhen(true)] out T? value)
            => _root.TryGet<T>($"{_prefix}.{relativePath}", out value);
        public IConfigSection GetSection(string relativePath)
            => new FakeConfigSection(_root, $"{_prefix}.{relativePath}");
    }
}
