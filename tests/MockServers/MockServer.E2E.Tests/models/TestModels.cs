namespace MockServer.E2E.Tests.Models;

/// <summary>
/// CLI进程配置
/// </summary>
public class CliProcessConfig
{
    public string ExecutablePath { get; }
    public string Arguments { get; }
    public Dictionary<string, string> EnvironmentVariables { get; }

    public CliProcessConfig(string executablePath, string arguments, Dictionary<string, string>? envVars = null)
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
        EnvironmentVariables = envVars ?? new Dictionary<string, string>();
    }
}

/// <summary>
/// 测试结果
/// </summary>
public class TestResult
{
    public string ScenarioName { get; }
    public string TestName => ScenarioName;
    public bool Passed { get; }
    public long DurationMs { get; }
    public Exception? Error { get; }

    public TestResult(string scenarioName, bool passed, long durationMs, Exception? error = null)
    {
        ScenarioName = scenarioName;
        Passed = passed;
        DurationMs = durationMs;
        Error = error;
    }
}
