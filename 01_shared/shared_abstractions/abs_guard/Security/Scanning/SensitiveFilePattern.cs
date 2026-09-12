namespace JoinCode.Abstractions.Security.Scanning;

/// <summary>
/// 敏感文件模式分类 — 对齐 TS secretScanner.ts SensitiveFilePatterns
/// 枚举值仅作为分类标识,具体文件模式(Glob)在 SecurityPatterns.SensitiveFilePatternsByCategory 字典
/// </summary>
public enum SensitiveFilePattern
{
    /// <summary>.env / .env.local / .env.production 等环境变量文件变体</summary>
    [EnumValue("env-files")] EnvFiles,

    /// <summary>*.pem / *.key / *.p12 / *.pfx / *.jks 加密密钥文件(按扩展名)</summary>
    [EnumValue("crypto-keys")] CryptoKeys,

    /// <summary>id_rsa / id_ed25519 / id_ecdsa / id_dsa SSH 私钥文件</summary>
    [EnumValue("ssh-keys")] SshKeys,

    /// <summary>credentials.json / auth.json / service-account.json 凭据 JSON 文件</summary>
    [EnumValue("credentials")] Credentials,

    /// <summary>.npmrc / .pypirc / .netrc / .htpasswd 包管理器/服务凭据文件</summary>
    [EnumValue("package-registries")] PackageRegistries,

    /// <summary>secrets.yml / secrets.yaml / secrets.json 密钥配置文件</summary>
    [EnumValue("secrets-config")] SecretsConfig,

    /// <summary>.git / .git/** / .svn / .hg 等版本控制系统内部路径(防止路径穿越/泄露仓库元数据)</summary>
    [EnumValue("vcs-internal")] VcsInternal,
}
