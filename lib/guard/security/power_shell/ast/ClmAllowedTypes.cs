
namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PowerShell Constrained Language Mode (CLM) 允许的类型白名单。
/// 微软的 CLM 在 AppLocker/WDAC 系统锁定下限制 .NET 类型使用为此白名单。
/// 不在此白名单中的类型被视为不安全。
/// 反转逻辑：不在白名单中的类型字面量 → 需要询问权限。
/// 来源: https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_language_modes
/// </summary>
public static class ClmAllowedTypes
{
    /// <summary>
    /// CLM 允许的类型白名单（全部小写存储，匹配时需规范化）
    /// 安全移除: adsi/adsisearcher (AD网络绑定), wmi/wmiclass/wmisearcher (WMI远程查询), cimsession (CIM远程连接)
    /// </summary>
    private static readonly FrozenSet<string> AllowedTypes = FrozenSet.ToFrozenSet(
    [
        // 类型加速器（短名称，如 AST TypeName.Name 中出现的形式）
        "alias", "allowemptycollection", "allowemptystring", "allownull",
        "argumentcompleter", "argumentcompletions", "array", "bigint",
        "bool", "byte", "char", "cimclass", "cimconverter", "ciminstance",
        // cimsession 已移除 — 网络连接风险
        "cimtype", "cmdletbinding", "cultureinfo", "datetime", "decimal",
        "double", "dsclocalconfigurationmanager", "dscproperty", "dscresource",
        "experimentaction", "experimental", "experimentalfeature", "float",
        "guid", "hashtable", "int", "int16", "int32", "int64",
        "ipaddress", "ipendpoint", "long", "mailaddress",
        "norunspaceaffinity", "nullstring", "objectsecurity", "ordered",
        "outputtype", "parameter", "physicaladdress", "pscredential",
        "pscustomobject", "psdefaultvalue", "pslistmodifier", "psobject",
        "psprimitivedictionary", "pstypenameattribute", "ref", "regex",
        "sbyte", "securestring", "semver", "short", "single", "string",
        "supportswildcards", "switch", "timespan", "uint", "uint16",
        "uint32", "uint64", "ulong", "uri", "ushort", "validatecount",
        "validatedrive", "validatelength", "validatenotnull",
        "validatenotnullorempty", "validatenotnullorwhitespace",
        "validatepattern", "validaterange", "validatescript", "validateset",
        "validatetrusteddata", "validateuserdrive", "version", "void",
        "wildcardpattern",
        // wmi/wmiclass/wmisearcher 已移除 — WMI 远程查询风险
        "x500distinguishedname", "x509certificate", "xml",

        // System.* 全限定名（AST 可能输出短名或全限定名）
        "system.array", "system.boolean", "system.byte", "system.char",
        "system.datetime", "system.decimal", "system.double", "system.guid",
        "system.int16", "system.int32", "system.int64",
        "system.numerics.biginteger", "system.sbyte", "system.single",
        "system.string", "system.timespan", "system.uint16", "system.uint32",
        "system.uint64", "system.uri", "system.version", "system.void",
        "system.collections.hashtable",
        "system.text.regularexpressions.regex",
        "system.globalization.cultureinfo", "system.net.ipaddress",
        "system.net.ipendpoint", "system.net.mail.mailaddress",
        "system.net.networkinformation.physicaladdress",
        "system.security.securestring",
        "system.security.cryptography.x509certificates.x509certificate",
        "system.security.cryptography.x509certificates.x500distinguishedname",
        "system.xml.xmldocument",

        // System.Management.Automation.* — PS 特有加速器的全限定名
        "system.management.automation.pscredential",
        "system.management.automation.pscustomobject",
        "system.management.automation.pslistmodifier",
        "system.management.automation.psobject",
        "system.management.automation.psprimitivedictionary",
        "system.management.automation.psreference",
        "system.management.automation.semanticversion",
        "system.management.automation.switchparameter",
        "system.management.automation.wildcardpattern",
        "system.management.automation.language.nullstring",

        // Microsoft.Management.Infrastructure.* — CIM 加速器的全限定名
        // cimsession 全限定名已移除 — 同样的网络连接风险
        "microsoft.management.infrastructure.cimclass",
        "microsoft.management.infrastructure.cimconverter",
        "microsoft.management.infrastructure.ciminstance",
        "microsoft.management.infrastructure.cimtype",

        // 其他全限定名
        // DirectoryEntry/DirectorySearcher/ManagementObject/ManagementClass/ManagementObjectSearcher 已移除
        "system.collections.specialized.ordereddictionary",
        "system.security.accesscontrol.objectsecurity",
        "object", "system.object",
        "microsoft.powershell.commands.modulespecification"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 规范化类型名称。处理数组后缀([])和泛型参数([...])。
    /// 数组类型的允许类型也是允许的（如 [string[]]），规范化时去除 [] 后缀。
    /// 泛型参数保守处理：只检查外层类型（如 List[int] → 检查 list）。
    /// </summary>
    public static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return string.Empty;

        var name = typeName.ToLowerInvariant().Trim();

        // 去除数组后缀: "String[]" → "string"
        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            name = name[..^2];
        }

        // 去除泛型参数: "List[int]" → "list"
        var genericStart = name.IndexOf('[');
        var genericEnd = name.LastIndexOf(']');
        if (genericStart >= 0 && genericEnd > genericStart)
        {
            name = name[..genericStart];
        }

        return name.Trim();
    }

    /// <summary>
    /// 判断类型名称（来自 AST）是否在微软 CLM 白名单中。
    /// 不在白名单中的类型触发权限询问 — 它们访问 CLM 阻止的系统 API。
    /// </summary>
    public static bool IsClmAllowedType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        return AllowedTypes.Contains(NormalizeTypeName(typeName));
    }
}
