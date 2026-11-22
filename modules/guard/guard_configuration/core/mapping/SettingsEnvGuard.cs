namespace Core.Configuration;

/// <summary>
/// settings.json env 键危险黑名单 — 对齐 TS managedEnv 危险变量防线。
/// <para>拦截四类攻击面：① 进程基础设施劫持（PATH 改命令解析/USERPROFILE 改路径根基）
/// ② 动态库注入（LD_PRELOAD/DYLD_* 加载任意代码）③ TLS 校验关闭（NODE_TLS_REJECT_UNAUTHORIZED/
/// CA 证书覆盖 → 中间人）④ 流量劫持（HTTP(S)_PROXY 改全部出网路径）。</para>
/// <para>大小写不敏感（Windows 环境变量不区分大小写）。拒绝时记录四要素日志：
/// 为什么拒绝/什么键/哪层拦截/正确做法（用户级环境变量或供应商专属变量）。</para>
/// </summary>
public static class SettingsEnvGuard {
    private static readonly FrozenSet<string> DangerousKeys = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        [
            // 进程基础设施 — 改变进程行为根基
            "PATH", "PATHEXT", "COMSPEC", "SYSTEMROOT", "SYSTEMDRIVE", "WINDIR",
            "PSModulePath", "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS",
            "COMPUTERNAME", "USERNAME", "USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA",
            "TEMP", "TMP", "SHELL",
            // 动态库/运行时注入 — 加载任意代码
            "LD_PRELOAD", "LD_LIBRARY_PATH", "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH",
            "NODE_OPTIONS",
            // TLS 信任劫持 — 关闭校验或覆盖 CA 信任锚
            "NODE_TLS_REJECT_UNAUTHORIZED", "SSL_CERT_FILE", "SSL_CERT_DIR",
            "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "CURL_SSL_BACKEND",
            // 流量劫持 — 重定向全部出网流量
            "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
        ]);

    /// <summary>
    /// 判定 env 键是否危险（黑名单命中）。
    /// </summary>
    /// <param name="key">环境变量名。</param>
    /// <returns>true 表示危险键，注入层必须拒绝。</returns>
    public static bool IsDangerous(string key) => DangerousKeys.Contains(key);
}
