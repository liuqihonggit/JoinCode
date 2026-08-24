
namespace Core.Skills;

[Register(typeof(ICodeSandboxService), ServiceLifetime.Singleton)]
public sealed partial class CodeSandboxService : ServiceEntity, ICodeSandboxService
{
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger<CodeSandboxService>? _logger;

    public CodeSandboxService(IFileOperationService fileOperationService, IFileSystem fs, IProcessService processService, ITelemetryService? telemetryService = null, ILogger<CodeSandboxService>? logger = null)
    {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _telemetryService = telemetryService;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(string code, int timeoutMs, CancellationToken cancellationToken = default)
    {
        await using var span = _telemetryService?.StartSpan("sandbox.execute", TelemetrySpanKind.Server);
        span?.SetTag("sandbox.code_length", code.Length);
        span?.SetTag("sandbox.timeout_ms", timeoutMs);

        var tempDir = Path.Combine(Path.GetTempPath(), $"csharp_sandbox_{Guid.NewGuid():N}");
        _fs.CreateDirectory(tempDir);

        try
        {
            var codeFile = Path.Combine(tempDir, "Program.cs");
            await _fs.WriteAllTextAsync(codeFile, code, cancellationToken).ConfigureAwait(false);

            var projectFile = Path.Combine(tempDir, "Sandbox.csproj");
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
            await _fs.WriteAllTextAsync(projectFile, projectContent, cancellationToken).ConfigureAwait(false);

            var buildResult = await _processService.ExecuteAsync(new ProcessOptions
            {
                FileName = "dotnet",
                ArgumentList = new[] { "build", "--configuration", "Release", "--nologo" },
                WorkingDirectory = tempDir,
                TimeoutMs = 30000
            }, cancellationToken).ConfigureAwait(false);

            if (!buildResult.Success)
            {
                var buildError = string.IsNullOrWhiteSpace(buildResult.StandardError) ? buildResult.StandardOutput : buildResult.StandardError;

                span?.SetStatus(TelemetryStatusCode.Error, "Build failed");
                RecordSandboxMetrics(isSuccess: false, isTimeout: false);

                throw new InvalidOperationException(string.Format(CoreErrorMessages.CompilationFailed, buildError));
            }

            var exePath = Path.Combine(tempDir, "bin", "Release", "net10.0", "Sandbox.exe");
            if (!_fs.FileExists(exePath))
            {
                exePath = Path.Combine(tempDir, "bin", "Release", "net10.0", "Sandbox.dll");
            }

            ProcessResult runResult;
            try
            {
                runResult = await _processService.ExecuteAsync(new ProcessOptions
                {
                    FileName = "dotnet",
                    ArgumentList = new[] { exePath },
                    WorkingDirectory = tempDir,
                    TimeoutMs = timeoutMs
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                span?.SetStatus(TelemetryStatusCode.Error, "Execution timeout");
                RecordSandboxMetrics(isSuccess: false, isTimeout: true);

                throw new TimeoutException();
            }

            var resultBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(runResult.StandardOutput))
            {
                resultBuilder.AppendLine(L.T(StringKey.SandboxOutputLabel));
                resultBuilder.AppendLine(runResult.StandardOutput);
            }
            if (!string.IsNullOrWhiteSpace(runResult.StandardError))
            {
                resultBuilder.AppendLine(L.T(StringKey.SandboxErrorLabel));
                resultBuilder.AppendLine(runResult.StandardError);
            }
            if (runResult.ExitCode != 0)
            {
                resultBuilder.AppendLine(L.T(StringKey.SandboxExitCodeLabel, runResult.ExitCode));
            }

            span?.SetStatus(TelemetryStatusCode.Ok);
            RecordSandboxMetrics(isSuccess: runResult.Success, isTimeout: false);

            return resultBuilder.ToString().Trim();
        }
        finally
        {
            try
            {
                if (_fs.DirectoryExists(tempDir))
                {
                    await DeleteDirectoryAsync(tempDir, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "清理临时目录失败");
            }
        }
    }

    public async Task<string> EvaluateExpressionAsync(string expression, string? variables, CancellationToken cancellationToken = default)
    {
        var codeBuilder = new StringBuilder(512);
        codeBuilder.AppendLine("using System;");
        codeBuilder.AppendLine("using System.Linq;");
        codeBuilder.AppendLine("using System.Collections.Generic;");
        codeBuilder.AppendLine("using System.Text;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("public class Program");
        codeBuilder.AppendLine("{");
        codeBuilder.AppendLine("    public static void Main()");
        codeBuilder.AppendLine("    {");
        codeBuilder.AppendLine("        try");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine($"            var result = {expression};");
        codeBuilder.AppendLine("            Console.WriteLine((object?)result ?? \"null\");");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine("        catch (Exception ex)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            Console.WriteLine($\"Error: {ex.Message}\");");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine("    }");
        codeBuilder.AppendLine("}");

        return await ExecuteAsync(codeBuilder.ToString(), 10000, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var files = _fs.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
        var deleteTasks = files.Select(file => _fileOperationService.DeleteFileAsync(file, cancellationToken));
        await Task.WhenAll(deleteTasks).ConfigureAwait(false);
        _fs.DeleteDirectory(directoryPath, true);
    }

    private void RecordSandboxMetrics(bool isSuccess, bool isTimeout)
        => _telemetryService?.RecordCount("sandbox.execute.count", new Dictionary<string, string> { ["success"] = isSuccess.ToString(), ["timeout"] = isTimeout.ToString() }, description: "Sandbox execution count");
}
