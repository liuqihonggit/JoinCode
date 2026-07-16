namespace Hands.Tests.Shell;

[Trait("Category", "Unit")]
public class SubprocessEnvCleanerTests
{
    [Fact]
    public void ScrubProcessEnvironment_WhenScrubbingDisabled_DoesNotRemoveVars()
    {
        var originalValue = Environment.GetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, null);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["ANTHROPIC_API_KEY"] = "sk-test-123";

            SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

            psi.EnvironmentVariables["ANTHROPIC_API_KEY"].Should().Be("sk-test-123");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, originalValue);
        }
    }

    [Fact]
    public void ScrubProcessEnvironment_WhenScrubbingEnabled_RemovesSensitiveVars()
    {
        var originalValue = Environment.GetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, "true");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["ANTHROPIC_API_KEY"] = "sk-test-123";
            psi.EnvironmentVariables["AWS_SECRET_ACCESS_KEY"] = "secret";
            psi.EnvironmentVariables["PATH"] = "/usr/bin";

            SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

            psi.EnvironmentVariables.ContainsKey("ANTHROPIC_API_KEY").Should().BeFalse();
            psi.EnvironmentVariables.ContainsKey("AWS_SECRET_ACCESS_KEY").Should().BeFalse();
            psi.EnvironmentVariables["PATH"].Should().Be("/usr/bin");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, originalValue);
        }
    }

    [Fact]
    public void ScrubProcessEnvironment_WhenScrubbingEnabled_RemovesInputPrefixedVars()
    {
        var originalValue = Environment.GetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, "1");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["INPUT_ANTHROPIC_API_KEY"] = "sk-test-123";

            SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

            psi.EnvironmentVariables.ContainsKey("INPUT_ANTHROPIC_API_KEY").Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, originalValue);
        }
    }

    [Fact]
    public void ScrubProcessEnvironment_PreservesNonSensitiveVars()
    {
        var originalValue = Environment.GetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, "true");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["HOME"] = "/home/user";
            psi.EnvironmentVariables["LANG"] = "en_US.UTF-8";
            psi.EnvironmentVariables["TERM"] = "xterm-256color";

            SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

            psi.EnvironmentVariables["HOME"].Should().Be("/home/user");
            psi.EnvironmentVariables["LANG"].Should().Be("en_US.UTF-8");
            psi.EnvironmentVariables["TERM"].Should().Be("xterm-256color");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, originalValue);
        }
    }

    [Fact]
    public void ScrubProcessEnvironment_RemovesAllGhaScrubListVars()
    {
        var originalValue = Environment.GetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, "1");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["AZURE_CLIENT_SECRET"] = "azure-secret";
            psi.EnvironmentVariables["ACTIONS_RUNTIME_TOKEN"] = "gha-token";
            psi.EnvironmentVariables["SSH_SIGNING_KEY"] = "/path/to/key";

            SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

            psi.EnvironmentVariables.ContainsKey("AZURE_CLIENT_SECRET").Should().BeFalse();
            psi.EnvironmentVariables.ContainsKey("ACTIONS_RUNTIME_TOKEN").Should().BeFalse();
            psi.EnvironmentVariables.ContainsKey("SSH_SIGNING_KEY").Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubprocessEnvCleaner.ScrubEnvVar, originalValue);
        }
    }
}
