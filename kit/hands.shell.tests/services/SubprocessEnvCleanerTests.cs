namespace Hands.Tests.Shell;

/// <summary>
/// SubprocessEnvCleaner 单元测试 — 验证敏感环境变量集合构建、key 匹配(含 INPUT_ 前缀)、
/// 字典/进程环境清洗。用 EnvVarScope 控制 JCC_SUBPROCESS_ENV_SCRUB 确定性测试。
/// 对齐 TS subprocessEnv,纯计算 + 受控环境变量确定性测试。
/// </summary>
public class SubprocessEnvCleanerTest {
    // ===== BuildSensitiveEnvVars 敏感环境变量集合构建 =====

    [Fact]
    public void BuildSensitiveEnvVars_ContainsProviderApiKeys() {
        var sensitive = SubprocessEnvCleaner.BuildSensitiveEnvVars();
        sensitive.Should().Contain("OPENAI_API_KEY");
        sensitive.Should().Contain("AZURE_OPENAI_API_KEY");
        sensitive.Should().Contain("ANTHROPIC_API_KEY");
        sensitive.Should().Contain("AGNES_API_KEY");
        sensitive.Should().Contain("DEEPSEEK_API_KEY");
        sensitive.Should().Contain("SENSENOVA_API_KEY");
        sensitive.Should().Contain("ZHIPUAI_API_KEY");
        sensitive.Should().Contain("JEV_API_KEY");
    }

    [Fact]
    public void BuildSensitiveEnvVars_ContainsOtherSensitiveVars() {
        var sensitive = SubprocessEnvCleaner.BuildSensitiveEnvVars();
        sensitive.Should().Contain("ANTHROPIC_AUTH_TOKEN");
        sensitive.Should().Contain("AWS_SECRET_ACCESS_KEY");
        sensitive.Should().Contain("AWS_SESSION_TOKEN");
        sensitive.Should().Contain("GOOGLE_APPLICATION_CREDENTIALS");
        sensitive.Should().Contain("AZURE_CLIENT_SECRET");
        sensitive.Should().Contain("GITHUB_TOKEN");
        sensitive.Should().Contain("ACTIONS_ID_TOKEN_REQUEST_TOKEN");
        sensitive.Should().Contain("ACTIONS_RUNTIME_TOKEN");
        sensitive.Should().Contain("ALL_INPUTS");
        sensitive.Should().Contain("SSH_SIGNING_KEY");
        sensitive.Should().Contain("OTEL_EXPORTER_OTLP_HEADERS");
    }

    [Fact]
    public void BuildSensitiveEnvVars_CaseInsensitive() {
        var sensitive = SubprocessEnvCleaner.BuildSensitiveEnvVars();
        // FrozenSet 用 OrdinalIgnoreCase,小写也应能找到
        sensitive.Contains("openai_api_key").Should().BeTrue();
        sensitive.Contains("GITHUB_token").Should().BeTrue();
    }

    [Fact]
    public void BuildSensitiveEnvVars_DoesNotContainSafeVars() {
        var sensitive = SubprocessEnvCleaner.BuildSensitiveEnvVars();
        sensitive.Contains("PATH").Should().BeFalse();
        sensitive.Contains("HOME").Should().BeFalse();
        sensitive.Contains("MY_SAFE_VAR").Should().BeFalse();
    }

    [Fact]
    public void BuildSensitiveEnvVars_IsNotEmpty() {
        var sensitive = SubprocessEnvCleaner.BuildSensitiveEnvVars();
        sensitive.Should().NotBeEmpty();
    }

    // ===== IsSensitiveKey key 匹配(含 INPUT_ 前缀) =====

    [Theory]
    [InlineData("OPENAI_API_KEY")]           // 直接匹配
    [InlineData("openai_api_key")]           // 大小写不敏感
    [InlineData("INPUT_OPENAI_API_KEY")]     // INPUT_ 前缀
    [InlineData("input_openai_api_key")]     // INPUT_ 前缀 + 大小写不敏感
    [InlineData("GITHUB_TOKEN")]
    [InlineData("INPUT_GITHUB_TOKEN")]
    [InlineData("ANTHROPIC_AUTH_TOKEN")]
    [InlineData("AWS_SECRET_ACCESS_KEY")]
    public void IsSensitiveKey_SensitiveKeys_ReturnsTrue(string key) {
        SubprocessEnvCleaner.IsSensitiveKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("PATH")]
    [InlineData("HOME")]
    [InlineData("MY_SAFE_VAR")]
    [InlineData("INPUT_PATH")]               // INPUT_ + 安全变量仍安全
    [InlineData("INPUT_MY_SAFE_VAR")]
    [InlineData("")]
    public void IsSensitiveKey_SafeKeys_ReturnsFalse(string key) {
        SubprocessEnvCleaner.IsSensitiveKey(key).Should().BeFalse();
    }

    // ===== ScrubDictionaryEnv 字典环境清洗 =====

    [Fact]
    public void ScrubDictionaryEnv_Disabled_ReturnsOriginalUnchanged() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "0");
        var dict = new Dictionary<string, string> {
            ["OPENAI_API_KEY"] = "sk-secret",
            ["PATH"] = "/usr/bin"
        };
        var result = SubprocessEnvCleaner.ScrubDictionaryEnv(dict);
        result.Should().BeSameAs(dict);
        result.Should().ContainKey("OPENAI_API_KEY");
        result.Should().ContainKey("PATH");
    }

    [Fact]
    public void ScrubDictionaryEnv_Enabled_RemovesSensitiveKeys() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "1");
        var dict = new Dictionary<string, string> {
            ["OPENAI_API_KEY"] = "sk-secret",
            ["GITHUB_TOKEN"] = "ghs_xxx",
            ["PATH"] = "/usr/bin",
            ["HOME"] = "/root"
        };
        var result = SubprocessEnvCleaner.ScrubDictionaryEnv(dict);
        result.Should().NotContainKey("OPENAI_API_KEY");
        result.Should().NotContainKey("GITHUB_TOKEN");
        result.Should().ContainKey("PATH");
        result.Should().ContainKey("HOME");
    }

    [Fact]
    public void ScrubDictionaryEnv_EnabledTrue_RemovesSensitiveKeys() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "true");
        var dict = new Dictionary<string, string> {
            ["ANTHROPIC_API_KEY"] = "sk-ant",
            ["SAFE_VAR"] = "ok"
        };
        var result = SubprocessEnvCleaner.ScrubDictionaryEnv(dict);
        result.Should().NotContainKey("ANTHROPIC_API_KEY");
        result.Should().ContainKey("SAFE_VAR");
    }

    [Fact]
    public void ScrubDictionaryEnv_Enabled_RemovesInputPrefixedKeys() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "1");
        var dict = new Dictionary<string, string> {
            ["INPUT_OPENAI_API_KEY"] = "sk-via-input",
            ["INPUT_GITHUB_TOKEN"] = "ghs-via-input",
            ["INPUT_SAFE_VAR"] = "keep"
        };
        var result = SubprocessEnvCleaner.ScrubDictionaryEnv(dict);
        result.Should().NotContainKey("INPUT_OPENAI_API_KEY");
        result.Should().NotContainKey("INPUT_GITHUB_TOKEN");
        result.Should().ContainKey("INPUT_SAFE_VAR");
    }

    [Fact]
    public void ScrubDictionaryEnv_EnabledCaseInsensitive_RemovesVariantCaseKeys() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "1");
        var dict = new Dictionary<string, string> {
            ["openai_api_key"] = "lowercase",
            ["Github_Token"] = "mixed-case"
        };
        var result = SubprocessEnvCleaner.ScrubDictionaryEnv(dict);
        result.Should().NotContainKey("openai_api_key");
        result.Should().NotContainKey("Github_Token");
    }

    [Fact]
    public void ScrubDictionaryEnv_EnabledEmptyDict_ReturnsEmpty() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "1");
        var dict = new Dictionary<string, string>();
        var result = SubprocessEnvCleaner.ScrubDictionaryEnv(dict);
        result.Should().BeEmpty();
    }

    // ===== ScrubProcessEnvironment 进程环境清洗 =====

    [Fact]
    public void ScrubProcessEnvironment_Disabled_KeepsAllVars() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "0");
        var psi = new ProcessStartInfo("test");
        psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-secret";
        psi.EnvironmentVariables["PATH"] = "/usr/bin";
        SubprocessEnvCleaner.ScrubProcessEnvironment(psi);
        psi.EnvironmentVariables["OPENAI_API_KEY"].Should().Be("sk-secret");
        psi.EnvironmentVariables["PATH"].Should().Be("/usr/bin");
    }

    [Fact]
    public void ScrubProcessEnvironment_Enabled_RemovesSensitiveKeys() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "1");
        var psi = new ProcessStartInfo("test");
        psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-secret";
        psi.EnvironmentVariables["GITHUB_TOKEN"] = "ghs_xxx";
        psi.EnvironmentVariables["PATH"] = "/usr/bin";
        SubprocessEnvCleaner.ScrubProcessEnvironment(psi);
        psi.EnvironmentVariables.ContainsKey("OPENAI_API_KEY").Should().BeFalse();
        psi.EnvironmentVariables.ContainsKey("GITHUB_TOKEN").Should().BeFalse();
        psi.EnvironmentVariables["PATH"].Should().Be("/usr/bin");
    }

    [Fact]
    public void ScrubProcessEnvironment_Enabled_RemovesInputPrefixedKeys() {
        using var env = EnvVarScope.Set(SubprocessEnvCleaner.ScrubEnvVar, "1");
        var psi = new ProcessStartInfo("test");
        psi.EnvironmentVariables["INPUT_OPENAI_API_KEY"] = "sk-via-input";
        psi.EnvironmentVariables["SAFE_VAR"] = "keep";
        SubprocessEnvCleaner.ScrubProcessEnvironment(psi);
        psi.EnvironmentVariables.ContainsKey("INPUT_OPENAI_API_KEY").Should().BeFalse();
        psi.EnvironmentVariables["SAFE_VAR"].Should().Be("keep");
    }
}
