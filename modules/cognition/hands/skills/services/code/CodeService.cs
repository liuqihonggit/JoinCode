
namespace Core.Skills;

/// <summary>
/// 代码服务 — 通过中间件管道执行代码生成、分析、执行操作
/// </summary>
[Register(typeof(ICodeService), ServiceLifetime.Singleton)]
public sealed partial class CodeService : ServiceEntity, ICodeService {
    private readonly MiddlewarePipeline<CodeContext> _pipeline;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger<CodeService>? _logger;

    /// <summary>
    /// 创建 CodeService
    /// </summary>
    public CodeService(
        MiddlewarePipeline<CodeContext> pipeline,
        ITelemetryService? telemetryService = null,
        ILogger<CodeService>? logger = null) {
        _pipeline = pipeline;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GenerateCodeAsync(string prompt, CancellationToken cancellationToken = default) {
        await using var span = _telemetryService?.StartSpan("code.generate", TelemetrySpanKind.Server);
        try {
            _logger?.LogInformation(L.T(StringKey.CodeServiceGeneratingCode));
            var ctx = new CodeContext { Operation = CodeOperation.Generate, Input = prompt };
            await _pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
            return ctx.Result ?? L.T(StringKey.CodeServiceGenerateCodeFailed);
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.CodeServiceGenerateError));
            throw new ApiException(L.T(StringKey.CodeServiceGenerateException), ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> AnalyzeCodeAsync(string code, CancellationToken cancellationToken = default) {
        await using var span = _telemetryService?.StartSpan("code.analyze", TelemetrySpanKind.Server);
        try {
            _logger?.LogInformation(L.T(StringKey.CodeServiceAnalyzingCode));
            var ctx = new CodeContext { Operation = CodeOperation.Analyze, Input = code };
            await _pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
            return ctx.Result ?? L.T(StringKey.CodeServiceAnalyzeCodeFailed);
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.CodeServiceAnalyzeError));
            throw new ApiException(L.T(StringKey.CodeServiceAnalyzeException), ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> ExecuteCodeAsync(string code, CancellationToken cancellationToken = default) {
        await using var span = _telemetryService?.StartSpan("code.execute", TelemetrySpanKind.Server);
        try {
            _logger?.LogInformation(L.T(StringKey.CodeServiceExecutingInSandbox));
            var ctx = new CodeContext { Operation = CodeOperation.Execute, Input = code };
            await _pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
            return ctx.Result ?? L.T(StringKey.CodeServiceCodeCannotBeEmpty);
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.CodeServiceExecuteFailed));
            throw new CodeExecutionException(L.T(StringKey.CodeServiceExecuteException), ex);
        }
    }
}

/// <summary>
/// 缓存键生成器 — 基于内容哈希生成缓存键
/// </summary>
public static class CacheKeyGenerator {
    /// <summary>
    /// 生成缓存键 — 将内容通过 XxHash3 哈希后与前缀拼接
    /// </summary>
    /// <param name="prefix">缓存键前缀</param>
    /// <param name="content">用于生成哈希的内容</param>
    /// <returns>格式为 {prefix}:{hash} 的缓存键</returns>
    public static string GenerateCacheKey(string prefix, ReadOnlySpan<char> content) {
        var utf8Bytes = Encoding.UTF8.GetBytes(content.ToArray());
        var hash = XxHash3.Hash(utf8Bytes);
        var hashString = Convert.ToHexString(hash);
        return $"{prefix}:{hashString}";
    }
}