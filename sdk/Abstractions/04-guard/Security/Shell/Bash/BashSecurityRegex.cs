namespace JoinCode.Abstractions.Security.Shell;

public static partial class BashSecurityRegex
{
    [GeneratedRegex(@"^\d+(\.\d+)?[smhd]?$")]
    public static partial Regex DurationRegex();

    [GeneratedRegex(@"^-?\d+$")]
    public static partial Regex NiceNumRegex();

    [GeneratedRegex(@"^-\d+$")]
    public static partial Regex NiceLegacyRegex();

    [GeneratedRegex(@"^-[oei]$")]
    public static partial Regex StdbufShortSepRegex();

    [GeneratedRegex(@"^-[oei][0Ll]$")]
    public static partial Regex StdbufShortFusedRegex();

    [GeneratedRegex(@"^--(?:output|error|input)=[0Ll]$")]
    public static partial Regex StdbufLongRegex();

    [GeneratedRegex(@"\bsystem\s*\(")]
    public static partial Regex JqSystemRegex();

    [GeneratedRegex(@"^(?:-[fL](?:$|[^A-Za-z])|--(?:from-file|rawfile|slurpfile|library-path)(?:$|=))")]
    public static partial Regex JqDangerousFlagsRegex();

    [GeneratedRegex(@"/proc/\S+/environ")]
    public static partial Regex ProcEnvironRegex();

    [GeneratedRegex(@"\n\s*#")]
    public static partial Regex NewlineHashRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    public static partial Regex ValidVarNameRegex();

    [GeneratedRegex(@"\$\{[A-Za-z_][A-Za-z0-9_]*\}")]
    public static partial Regex Ps4VarRefRegex();

    [GeneratedRegex(@"^[A-Za-z0-9 _+:.=/\[\]-]*$")]
    public static partial Regex Ps4SafeCharsetRegex();

    [GeneratedRegex(@"[\s*$?[\]{}<>~`'""\\|&;()#!]")]
    public static partial Regex BareVarUnsafeRegex();

    [GeneratedRegex(@"\\(.)")]
    public static partial Regex BackslashUnescapeRegex();

    [GeneratedRegex(@"^[0-9]+$")]
    public static partial Regex DigitsOnlyRegex();
}
