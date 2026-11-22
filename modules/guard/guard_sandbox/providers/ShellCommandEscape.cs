namespace Core.Security.Sandbox.Providers;

internal static class ShellCommandEscape {
    /// <summary>为单引号 shell 转义命令字符串。</summary>
    /// <param name="command">待转义的命令。</param>
    /// <returns>转义后的命令字符串。</returns>
    public static string EscapeForSingleQuotedShell(string command) {
        if (command.Length == 0) {
            return "''";
        }

        var escaped = command.Replace("'", @"'\''");
        return $"'{escaped}'";
    }
}