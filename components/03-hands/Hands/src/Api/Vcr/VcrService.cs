
namespace Services.Api.Vcr;

[Register(typeof(IVcrService))]
[Register(typeof(JoinCode.Abstractions.Interfaces.IVcrService))]
public sealed partial class VcrService : IVcrService, JoinCode.Abstractions.Interfaces.IVcrService, IDisposable
{
    private readonly VcrOptions _options;
    [Inject] private readonly ILogger<VcrService>? _logger;
    private readonly IFileSystem _fs;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ConcurrentDictionary<string, VcrCassette> _cassetteCache = new(StringComparer.OrdinalIgnoreCase);

    private VcrMode _currentMode;

    public VcrMode CurrentMode => _currentMode;

    public VcrService(VcrOptions options, IFileSystem fs, ILogger<VcrService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        _options = options;
        _fs = fs;
        _logger = logger;
        _currentMode = options.Mode;
    }

    public async Task<VcrCassette> LoadCassetteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_cassetteCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var filePath = GetCassettePath(name);
            if (!_fs.FileExists(filePath))
            {
                var cassette = new VcrCassette { Name = name };
                _cassetteCache[name] = cassette;
                _logger?.LogDebug("创建新 cassette: {Name}", name);
                return cassette;
            }

            var json = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize(json, VcrJsonContext.Default.VcrCassette);
            if (loaded == null)
            {
                loaded = new VcrCassette { Name = name };
            }

            _cassetteCache[name] = loaded;
            _logger?.LogDebug("加载 cassette: {Name}, 交互数={Count}", name, loaded.Interactions.Count);
            return loaded;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveCassetteAsync(VcrCassette cassette, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cassette);
        ArgumentException.ThrowIfNullOrEmpty(cassette.Name);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var filePath = GetCassettePath(cassette.Name);
            var directory = Path.GetDirectoryName(filePath);
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);

            var json = JsonSerializer.Serialize(cassette, VcrJsonContext.Default.VcrCassette);
            await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            _cassetteCache[cassette.Name] = cassette;
            _logger?.LogDebug("保存 cassette: {Name}, 交互数={Count}", cassette.Name, cassette.Interactions.Count);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RecordInteractionAsync(string cassetteName, VcrRequest request, VcrResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cassetteName);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (_currentMode != VcrMode.Record)
        {
            _logger?.LogWarning("当前模式非录制模式，跳过录制");
            return;
        }

        var cassette = await LoadCassetteAsync(cassetteName, cancellationToken).ConfigureAwait(false);

        var interaction = new VcrInteraction
        {
            Request = _options.RecordHeaders ? request : request with { Headers = new Dictionary<string, string>() },
            Response = _options.RecordHeaders ? response : response with { Headers = new Dictionary<string, string>() },
            RecordedAt = DateTime.UtcNow
        };

        if (!_options.RecordContent)
        {
            interaction.Request.Body = null;
            interaction.Response.Body = null;
        }

        cassette.Interactions.Add(interaction);
        await SaveCassetteAsync(cassette, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("录制交互: {Method} {Uri} -> {Status}", request.Method, request.Uri, response.Status);
    }

    public async Task<VcrResponse?> FindMatchingInteractionAsync(string cassetteName, VcrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cassetteName);
        ArgumentNullException.ThrowIfNull(request);

        if (_currentMode != VcrMode.Playback)
        {
            _logger?.LogWarning("当前模式非回放模式，返回 null");
            return null;
        }

        var cassette = await LoadCassetteAsync(cassetteName, cancellationToken).ConfigureAwait(false);

        foreach (var interaction in cassette.Interactions)
        {
            if (MatchesRequest(interaction.Request, request))
            {
                _logger?.LogDebug("回放匹配: {Method} {Uri} -> {Status}", request.Method, request.Uri, interaction.Response.Status);
                return interaction.Response;
            }
        }

        if (_options.StrictPlayback)
        {
            throw new InvalidOperationException($"未找到匹配的录制交互: {request.Method} {request.Uri}");
        }

        _logger?.LogWarning("未找到匹配的录制交互: {Method} {Uri}", request.Method, request.Uri);
        return null;
    }

    public void SetMode(VcrMode mode)
    {
        _currentMode = mode;
        _logger?.LogInformation("VCR 模式切换为: {Mode}", mode);
    }

    private string GetCassettePath(string name)
    {
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_options.CassettesDirectory, $"{safeName}.json");
    }

    private static bool MatchesRequest(VcrRequest recorded, VcrRequest incoming)
    {
        if (!string.Equals(recorded.Method, incoming.Method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(recorded.Uri, incoming.Uri, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        _fileLock.Dispose();
    }

    async Task<JoinCode.Abstractions.Models.Vcr.VcrCassette> JoinCode.Abstractions.Interfaces.IVcrService.LoadCassetteAsync(string name, CancellationToken cancellationToken)
    {
        var cassette = await LoadCassetteAsync(name, cancellationToken).ConfigureAwait(false);
        return new JoinCode.Abstractions.Models.Vcr.VcrCassette
        {
            Name = cassette.Name,
            CreatedAt = cassette.RecordedAt,
            UpdatedAt = cassette.RecordedAt,
            InteractionCount = cassette.Interactions.Count
        };
    }

    JoinCode.Abstractions.Models.Vcr.VcrMode JoinCode.Abstractions.Interfaces.IVcrService.CurrentMode =>
        (JoinCode.Abstractions.Models.Vcr.VcrMode)CurrentMode;

    void JoinCode.Abstractions.Interfaces.IVcrService.SetMode(JoinCode.Abstractions.Models.Vcr.VcrMode mode)
    {
        SetMode((VcrMode)mode);
    }
}
