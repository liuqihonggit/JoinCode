namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS cmdlet 路径参数配置 — 与 TS CMDLET_PATH_CONFIG 1:1 对齐
/// 定义每个 cmdlet 的路径参数、开关参数、值参数等
/// </summary>
public sealed class PsCmdletPathConfig
{
    /// <summary>操作类型</summary>
    public required FileOperationType OperationType { get; init; }

    /// <summary>接受文件路径的参数名（需验证）</summary>
    public required FrozenSet<string> PathParams { get; init; }

    /// <summary>开关参数（不消费下一个参数）</summary>
    public required FrozenSet<string> KnownSwitches { get; init; }

    /// <summary>接受值但非路径的参数（消费下一个参数但不做路径验证）</summary>
    public required FrozenSet<string> KnownValueParams { get; init; }

    /// <summary>仅接受叶子文件名的路径参数</summary>
    public FrozenSet<string> LeafOnlyPathParams { get; init; } = FrozenSet.ToFrozenSet<string>([], StringComparer.OrdinalIgnoreCase);

    /// <summary>跳过的前导位置参数数量</summary>
    public int PositionalSkip { get; init; }

    /// <summary>是否为可选写操作（无路径参数时仅为管道输出）</summary>
    public bool OptionalWrite { get; init; }
}

/// <summary>
/// PS cmdlet 路径配置注册表 — 与 TS CMDLET_PATH_CONFIG 1:1 对齐
/// </summary>
public static partial class PsCmdletPathRegistry
{
    /// <summary>
    /// 查找 cmdlet 路径配置
    /// </summary>
    public static bool TryGetConfig(string cmdletName, [NotNullWhen(true)] out PsCmdletPathConfig? config)
    {
        return Registry.TryGetValue(PsAliases.ResolveToCanonical(cmdletName), out config);
    }

    private static readonly FrozenDictionary<string, PsCmdletPathConfig> Registry = BuildRegistry();

    private static FrozenDictionary<string, PsCmdletPathConfig> BuildRegistry()
    {
        var dict = new Dictionary<string, PsCmdletPathConfig>(StringComparer.OrdinalIgnoreCase)
        {
            // ─── 写操作 ──────────────────────────────────────────────────
            ["set-content"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-passthru", "-force", "-whatif", "-confirm", "-usetransaction", "-nonewline", "-asbytestream"],
                ["-value", "-filter", "-include", "-exclude", "-credential", "-encoding", "-stream"]),

            ["add-content"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-passthru", "-force", "-whatif", "-confirm", "-usetransaction", "-nonewline", "-asbytestream"],
                ["-value", "-filter", "-include", "-exclude", "-credential", "-encoding", "-stream"]),

            ["remove-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-recurse", "-force", "-whatif", "-confirm", "-usetransaction"],
                ["-filter", "-include", "-exclude", "-credential", "-stream"]),

            ["clear-content"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm", "-usetransaction"],
                ["-filter", "-include", "-exclude", "-credential", "-stream"]),

            ["out-file"] = WriteConfig(
                ["-filepath", "-path", "-literalpath", "-pspath", "-lp"],
                ["-append", "-force", "-noclobber", "-nonewline", "-whatif", "-confirm"],
                ["-inputobject", "-encoding", "-width"]),

            ["tee-object"] = WriteConfig(
                ["-filepath", "-path", "-literalpath", "-pspath", "-lp"],
                ["-append"],
                ["-inputobject", "-variable", "-encoding"]),

            ["export-csv"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-append", "-force", "-noclobber", "-notypeinformation", "-includetypeinformation", "-useculture", "-noheader", "-whatif", "-confirm"],
                ["-inputobject", "-delimiter", "-encoding", "-quotefields", "-usequotes"]),

            ["export-clixml"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-noclobber", "-whatif", "-confirm"],
                ["-inputobject", "-depth", "-encoding"]),

            ["new-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm", "-usetransaction"],
                ["-itemtype", "-value", "-credential", "-type"],
                leafOnlyPathParams: ["-name"]),

            ["copy-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp", "-destination"],
                ["-container", "-force", "-passthru", "-recurse", "-whatif", "-confirm", "-usetransaction"],
                ["-filter", "-include", "-exclude", "-credential", "-fromsession", "-tosession"]),

            ["move-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp", "-destination"],
                ["-force", "-passthru", "-whatif", "-confirm", "-usetransaction"],
                ["-filter", "-include", "-exclude", "-credential", "-fromsession", "-tosession"]),

            ["rename-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-passthru", "-whatif", "-confirm", "-usetransaction"],
                ["-newname", "-credential"]),

            ["set-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-passthru", "-whatif", "-confirm", "-usetransaction"],
                ["-value", "-filter", "-include", "-exclude", "-credential", "-encoding"]),

            ["invoke-webrequest"] = new PsCmdletPathConfig
            {
                OperationType = FileOperationType.Write,
                PathParams = ToFrozenSet(["-outfile", "-infile"]),
                KnownSwitches = ToFrozenSet(["-usebasicparsing", "-preserveduthorizationonredirect"]),
                KnownValueParams = ToFrozenSet(["-uri", "-method", "-headers", "-body", "-contenttype", "-credential", "-proxy", "-proxycredential", "-proxyusedefaultcredentials", "-sessionvariable", "-websession", "-timeoutsec", "-maximumredirection", "-useragent", "-outstring", "-skipheadervalidation", "-nosproxy"]),
                PositionalSkip = 1,
                OptionalWrite = true,
            },

            ["invoke-restmethod"] = new PsCmdletPathConfig
            {
                OperationType = FileOperationType.Write,
                PathParams = ToFrozenSet(["-outfile", "-infile"]),
                KnownSwitches = ToFrozenSet(["-usebasicparsing", "-preserveduthorizationonredirect"]),
                KnownValueParams = ToFrozenSet(["-uri", "-method", "-headers", "-body", "-contenttype", "-credential", "-proxy", "-proxycredential", "-proxyusedefaultcredentials", "-sessionvariable", "-websession", "-timeoutsec", "-maximumredirection", "-useragent", "-outstring", "-skipheadervalidation", "-nosproxy"]),
                PositionalSkip = 1,
                OptionalWrite = true,
            },

            ["expand-archive"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp", "-destinationpath"],
                ["-force", "-whatif", "-confirm"],
                ["-credential"]),

            ["compress-archive"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp", "-destinationpath"],
                ["-force", "-passthru", "-whatif", "-confirm", "-update"],
                ["-compressionlevel"]),

            ["set-itemproperty"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm", "-passthru"],
                ["-name", "-value", "-filter", "-include", "-exclude", "-credential", "-type"]),

            ["new-itemproperty"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm", "-passthru"],
                ["-name", "-value", "-filter", "-include", "-exclude", "-credential", "-type"]),

            ["remove-itemproperty"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm"],
                ["-name", "-filter", "-include", "-exclude", "-credential"]),

            ["clear-item"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm"],
                ["-filter", "-include", "-exclude", "-credential"]),

            ["export-alias"] = WriteConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-append", "-force", "-passthru", "-whatif", "-confirm", "-noclobber"],
                ["-name", "-scope", "-description", "-as", "-encoding"]),

            // ─── 读操作 ──────────────────────────────────────────────────
            ["get-content"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-readcount", "-totalcount", "-tail", "-force", "-whatif", "-confirm", "-usetransaction", "-nonewline", "-asbytestream", "-delimiter", "-wait", "-raw"],
                ["-filter", "-include", "-exclude", "-credential", "-encoding", "-stream"]),

            ["get-childitem"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-recurse", "-force", "-name", "-whatif", "-confirm", "-usetransaction"],
                ["-filter", "-include", "-exclude", "-credential", "-depth", "-attributes"]),

            ["get-item"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm", "-usetransaction"],
                ["-filter", "-include", "-exclude", "-credential"]),

            ["get-itemproperty"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm"],
                ["-name", "-filter", "-include", "-exclude", "-credential"]),

            ["get-itempropertyvalue"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-force", "-whatif", "-confirm"],
                ["-name", "-filter", "-include", "-exclude", "-credential"]),

            ["get-filehash"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                [],
                ["-algorithm", "-stream"]),

            ["get-acl"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                [],
                ["-filter", "-include", "-exclude", "-audit", "-inputobject"]),

            ["format-hex"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                [],
                ["-inputobject", "-encoding", "-count", "-offset"]),

            ["test-path"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-isValid", "-pathtype", "-include", "-exclude"],
                ["-filter", "-credential"]),

            ["resolve-path"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-relative"],
                ["-credential"]),

            ["convert-path"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                [],
                ["-credential"]),

            ["select-string"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-simplematch", "-casesensitive"],
                ["-pattern", "-inputobject", "-include", "-exclude", "-encoding", "-context", "-culture"]),

            ["set-location"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-passthru"],
                ["-credential"]),

            ["push-location"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                ["-passthru"],
                ["-stackname"]),

            ["pop-location"] = ReadConfig(
                [],
                ["-passthru"],
                ["-stackname"]),

            ["select-xml"] = ReadConfig(
                ["-path", "-literalpath", "-pspath", "-lp"],
                [],
                ["-xpath", "-xml", "-namespace"]),

            ["get-winevent"] = new PsCmdletPathConfig
            {
                OperationType = FileOperationType.Read,
                PathParams = ToFrozenSet(["-path"]),
                KnownSwitches = ToFrozenSet([]),
                KnownValueParams = ToFrozenSet(["-logname", "-computername", "-credential", "-filterxpath", "-filterhashtable", "-maxevents", "-oldest"]),
            },
        };

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static PsCmdletPathConfig WriteConfig(
        string[] pathParams, string[] knownSwitches, string[] knownValueParams,
        string[]? leafOnlyPathParams = null)
    {
        return new PsCmdletPathConfig
        {
            OperationType = FileOperationType.Write,
            PathParams = ToFrozenSet(pathParams),
            KnownSwitches = ToFrozenSet(knownSwitches),
            KnownValueParams = ToFrozenSet(knownValueParams),
            LeafOnlyPathParams = ToFrozenSet(leafOnlyPathParams ?? []),
        };
    }

    private static PsCmdletPathConfig ReadConfig(
        string[] pathParams, string[] knownSwitches, string[] knownValueParams)
    {
        return new PsCmdletPathConfig
        {
            OperationType = FileOperationType.Read,
            PathParams = ToFrozenSet(pathParams),
            KnownSwitches = ToFrozenSet(knownSwitches),
            KnownValueParams = ToFrozenSet(knownValueParams),
        };
    }

    private static FrozenSet<string> ToFrozenSet(string[] items)
    {
        return FrozenSet.ToFrozenSet(items, StringComparer.OrdinalIgnoreCase);
    }
}
