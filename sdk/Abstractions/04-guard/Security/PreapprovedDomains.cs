namespace JoinCode.Abstractions.Utils.Web;

/// <summary>
/// 预批准域名白名单，支持主机名匹配和路径前缀匹配
/// 对齐TS版 preapproved.ts 的 HOSTNAME_ONLY + PATH_PREFIXES
/// </summary>
public static class PreapprovedDomains
{
    /// <summary>
    /// 仅主机名匹配（无路径限制）
    /// </summary>
    public static readonly FrozenSet<string> Hosts = CreateHostSet();

    /// <summary>
    /// 路径前缀匹配（主机名 → 允许的路径前缀列表）
    /// </summary>
    public static readonly FrozenDictionary<string, string[]> PathPrefixes = CreatePathPrefixMap();

    private static FrozenSet<string> CreateHostSet()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Anthropic 自家
            "platform.claude.com",
            "code.claude.com",
            "modelcontextprotocol.io",
            "agentskills.io",

            // 编程语言官方文档
            "docs.python.org",
            "en.cppreference.com",
            "docs.oracle.com",
            "learn.microsoft.com",
            "developer.mozilla.org",
            "go.dev",
            "pkg.go.dev",
            "www.php.net",
            "docs.swift.org",
            "kotlinlang.org",
            "ruby-doc.org",
            "doc.rust-lang.org",
            "www.typescriptlang.org",

            // Web 框架
            "react.dev",
            "angular.io",
            "vuejs.org",
            "nextjs.org",
            "expressjs.com",
            "nodejs.org",
            "bun.sh",
            "jquery.com",
            "getbootstrap.com",
            "tailwindcss.com",
            "d3js.org",
            "threejs.org",
            "redux.js.org",
            "webpack.js.org",
            "jestjs.io",
            "reactrouter.com",

            // Python 库
            "docs.djangoproject.com",
            "flask.palletsprojects.com",
            "fastapi.tiangolo.com",
            "pandas.pydata.org",
            "numpy.org",
            "www.tensorflow.org",
            "pytorch.org",
            "scikit-learn.org",
            "matplotlib.org",
            "requests.readthedocs.io",
            "jupyter.org",

            // PHP
            "laravel.com",
            "symfony.com",
            "wordpress.org",

            // Java
            "docs.spring.io",
            "hibernate.org",
            "tomcat.apache.org",
            "gradle.org",
            "maven.apache.org",

            // .NET
            "asp.net",
            "dotnet.microsoft.com",
            "nuget.org",
            "blazor.net",

            // 移动端
            "reactnative.dev",
            "docs.flutter.dev",
            "developer.apple.com",
            "developer.android.com",

            // 数据科学
            "keras.io",
            "spark.apache.org",
            "huggingface.co",
            "www.kaggle.com",

            // 数据库
            "www.mongodb.com",
            "redis.io",
            "www.postgresql.org",
            "dev.mysql.com",
            "www.sqlite.org",
            "graphql.org",
            "prisma.io",

            // 云/DevOps
            "docs.aws.amazon.com",
            "cloud.google.com",
            "kubernetes.io",
            "www.docker.com",
            "www.terraform.io",
            "www.ansible.com",
            "docs.netlify.com",
            "devcenter.heroku.com",

            // 测试
            "cypress.io",
            "selenium.dev",

            // 游戏
            "docs.unity.com",
            "docs.unrealengine.com",

            // 工具
            "git-scm.com",
            "nginx.org",
            "httpd.apache.org",

            // 开发者社区
            "stackoverflow.com",
            "docs.github.com",
            "raw.githubusercontent.com",
        };

        return hosts.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string[]> CreatePathPrefixMap()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // TS版有路径前缀限制的域名
            ["github.com"] = ["/anthropics"],
            ["vercel.com"] = ["/docs"],
        };

        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查主机名是否为预批准域名
    /// </summary>
    public static bool IsPreapprovedHost(string hostname)
    {
        var stripped = hostname.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? hostname[4..]
            : hostname;
        return Hosts.Contains(stripped) || Hosts.Contains(hostname);
    }

    /// <summary>
    /// 检查URL是否为预批准URL（主机名+路径前缀匹配）
    /// 对齐TS版 isPreapprovedHost(hostname, pathname)
    /// </summary>
    public static bool IsPreapprovedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;

        var hostname = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? parsed.Host[4..]
            : parsed.Host;

        // 先查纯主机名集合
        if (Hosts.Contains(hostname) || Hosts.Contains(parsed.Host))
            return true;

        // 再查路径前缀映射
        if (PathPrefixes.TryGetValue(hostname, out var prefixes) ||
            PathPrefixes.TryGetValue(parsed.Host, out prefixes))
        {
            var path = parsed.AbsolutePath;
            foreach (var prefix in prefixes)
            {
                // 强制路径段边界：/anthropics 不匹配 /anthropics-evil/malware
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (path.Length == prefix.Length || path[prefix.Length] == '/')
                        return true;
                }
            }
        }

        return false;
    }
}
