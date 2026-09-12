namespace Core.Security.Sandbox.Providers;

internal static class ShellCommandEscape
{
    public static string EscapeForSingleQuotedShell(string command)
    {
        if (command.Length == 0)
        {
            return "''";
        }

        var escaped = command.Replace("'", @"'\''");
        return $"'{escaped}'";
    }
}
