namespace NativeTui.Tests.Repl;

/// <summary>
/// S8 per-tool 权限对话框形态数据测试 — ToolPermissionForms.Derive 分发矩阵
///（shell 类命令段提取 → Bash 形态 / 文件类路径提取 → File 形态 / 其余或提取失败 → Fallback）
/// + 危险命令检测（DangerousCommandCatalog 全模式 — 对齐基准 destructiveCommandWarning.ts 15 类 + sudo）
/// + 命令/路径/patch 提取纯函数 + 命令文本折行（PermissionTextWrap）。
/// 零破坏锚点：("Bash", "run tests") 无命令载体 → Fallback（既有 PermissionDialogTests 渲染行为不变）。
/// </summary>
public sealed class PermissionFormsTests
{
    // ---- Derive 分发矩阵 ----

    /// <summary>Bash 工具 + 冒号段命令 → Bash 形态（标题/命令折行/危险警告）。</summary>
    [Fact]
    public void Derive_BashToolWithColonCommand_BashForm()
    {
        var form = ToolPermissionForms.Derive("Bash", "未知命令类型，需要确认: rm -rf /tmp/x", width: 40);

        Assert.Equal(PermissionFormKind.Bash, form.Kind);
        Assert.Equal("Bash command", form.Title);
        Assert.Equal(["rm -rf /tmp/x"], form.CommandLines);
        Assert.Equal("可能递归强制删除文件", form.DangerWarning);
    }

    /// <summary>Bash 工具 + 引号段命令（首个命令特征引号段命中 — 'Bash' 工具名段排除）。</summary>
    [Fact]
    public void Derive_BashToolWithQuotedCommand_BashForm()
    {
        var form = ToolPermissionForms.Derive("Bash", "工具 'Bash' 请求执行 'git push --force'", width: 60);

        Assert.Equal(PermissionFormKind.Bash, form.Kind);
        Assert.Equal(["git push --force"], form.CommandLines);
        Assert.Equal("可能覆盖远程历史", form.DangerWarning);
    }

    /// <summary>PowerShell 工具标题专属（基准 BashPermissionRequest title 语义）。</summary>
    [Fact]
    public void Derive_PowerShellTool_PowerShellTitle()
    {
        var form = ToolPermissionForms.Derive("PowerShell", "执行 'Get-ChildItem -Recurse'", width: 60);

        Assert.Equal(PermissionFormKind.Bash, form.Kind);
        Assert.Equal("PowerShell command", form.Title);
    }

    /// <summary>Bash 工具 + 无命令载体（纯描述）→ Fallback（既有渲染零行为变化锚点）。</summary>
    [Fact]
    public void Derive_BashToolPlainDescription_Fallback()
    {
        var form = ToolPermissionForms.Derive("Bash", "run tests", width: 40);

        Assert.Equal(PermissionFormKind.Fallback, form.Kind);
        Assert.Equal("Tool use", form.Title);
        Assert.Empty(form.CommandLines);
        Assert.Null(form.DangerWarning);
    }

    /// <summary>Bash 工具 + 引号段仅工具名单词 → Fallback（单词纯字母段非命令）。</summary>
    [Fact]
    public void Derive_BashToolQuotedToolLabelOnly_Fallback()
    {
        var form = ToolPermissionForms.Derive("Bash", "工具 'Bash' 需要确认", width: 40);

        Assert.Equal(PermissionFormKind.Fallback, form.Kind);
    }

    /// <summary>文件工具 + 引号路径 → File 形态（Read/Write/Edit 标题映射）。</summary>
    [Theory]
    [InlineData("Read", "Read file")]
    [InlineData("Write", "Write file")]
    [InlineData("Edit", "Edit file")]
    [InlineData("FileRead", "Read file")]
    [InlineData("FileWrite", "Write file")]
    [InlineData("FileEdit", "Edit file")]
    public void Derive_FileToolWithPath_FileForm(string tool, string expectedTitle)
    {
        var form = ToolPermissionForms.Derive(tool, $"编辑 'src/foo.cs'", width: 40);

        Assert.Equal(PermissionFormKind.File, form.Kind);
        Assert.Equal(expectedTitle, form.Title);
        Assert.Equal("src/foo.cs", form.FilePath);
    }

    /// <summary>文件工具 + 路径 + patch 统计 → File 形态携带摘要。</summary>
    [Fact]
    public void Derive_FileToolWithPatchSummary_CarriesCounts()
    {
        var form = ToolPermissionForms.Derive("Edit", "编辑 'src/a.cs' (+12 -3)", width: 40);

        Assert.Equal(PermissionFormKind.File, form.Kind);
        Assert.Equal("src/a.cs", form.FilePath);
        Assert.Equal(12, form.PatchAdded);
        Assert.Equal(3, form.PatchRemoved);
        Assert.True(form.HasPatchSummary);
    }

    /// <summary>文件工具 + 无路径特征 → Fallback（中文描述无路径降级）。</summary>
    [Fact]
    public void Derive_FileToolWithoutPath_Fallback()
    {
        var form = ToolPermissionForms.Derive("Read", "读取文件内容", width: 40);

        Assert.Equal(PermissionFormKind.Fallback, form.Kind);
        Assert.Null(form.FilePath);
    }

    /// <summary>未识别工具（如 WebFetch）→ Fallback（其余工具走现形态）。</summary>
    [Fact]
    public void Derive_UnknownTool_Fallback()
    {
        var form = ToolPermissionForms.Derive("WebFetch", "fetch https://example.com", width: 40);

        Assert.Equal(PermissionFormKind.Fallback, form.Kind);
    }

    /// <summary>工具名大小写不敏感（引擎工具名透传容错）。</summary>
    [Fact]
    public void Derive_ToolNameCaseInsensitive()
    {
        var form = ToolPermissionForms.Derive("bash", "需要确认: git status --help", width: 40);

        Assert.Equal(PermissionFormKind.Bash, form.Kind);
    }

    /// <summary>长命令按宽度折行（Bash 形态命令区多行等宽展示）。</summary>
    [Fact]
    public void Derive_LongCommand_WrapsToWidth()
    {
        var form = ToolPermissionForms.Derive("Bash", "需要确认: git push --force origin main", width: 23);

        Assert.Equal(PermissionFormKind.Bash, form.Kind);
        Assert.Equal(2, form.CommandLines.Length);
        Assert.Equal("git push --force", form.CommandLines[0]);
        Assert.Equal("origin main", form.CommandLines[1]);
    }

    // ---- ExtractCommand 提取 ----

    /// <summary>命令提取 — 引号段优先、冒号段兜底、单词段排除。</summary>
    [Theory]
    [InlineData("工具 'Bash' 请求执行 'git status --short'", "git status --short")]
    [InlineData("未知命令类型，需要确认: rm -rf /tmp", "rm -rf /tmp")]
    [InlineData("需要确认：格式化磁盘", null)]          // 中文冒号段 — 中文单 token 非命令
    [InlineData("run tests", null)]                     // 无载体
    [InlineData("工具 'Bash' 需要确认", null)]          // 引号段仅工具名单词
    public void ExtractCommand_CandidateMatrix(string description, string? expected)
    {
        Assert.Equal(expected, ToolPermissionForms.ExtractCommand(description));
    }

    // ---- ExtractPath 提取 ----

    /// <summary>路径提取 — 引号段/裸 token 路径特征（/ \ . 扩展名）。</summary>
    [Theory]
    [InlineData("编辑 'src/foo.cs'", "src/foo.cs")]
    [InlineData("write a.cs", "a.cs")]
    [InlineData("写入 /tmp/out.txt", "/tmp/out.txt")]
    [InlineData("写入 D:\\build\\out.txt", "D:\\build\\out.txt")]
    [InlineData("读取文件内容", null)]
    public void ExtractPath_CandidateMatrix(string description, string? expected)
    {
        Assert.Equal(expected, ToolPermissionForms.ExtractPath(description));
    }

    // ---- PatchSummary 解析 ----

    /// <summary>patch 统计 — +N/-M 数字对（"+12 -3" / "+5" 单侧 / 无统计 null）。</summary>
    [Theory]
    [InlineData("应用补丁 (+12 -3)", 12, 3)]
    [InlineData("+5 处新增", 5, 0)]
    [InlineData("无统计信息", 0, 0)]
    [InlineData("rm -rf", 0, 0)]
    public void PatchSummary_ParseMatrix(string text, int added, int removed)
    {
        var summary = ToolPermissionForms.PatchSummary(text);
        if (added == 0 && removed == 0)
        {
            Assert.Null(summary);
        }
        else
        {
            Assert.Equal((added, removed), summary);
        }
    }

    // ---- PermissionTextWrap 折行 ----

    /// <summary>折行 — 词边界换行 / 单行放得下 / 空文本空行集。</summary>
    [Fact]
    public void Wrap_WordBoundary()
    {
        Assert.Equal(["git push --force", "origin main"], PermissionTextWrap.Wrap("git push --force origin main", 20));
        Assert.Equal(["短文本"], PermissionTextWrap.Wrap("短文本", 40));
        Assert.Empty(PermissionTextWrap.Wrap(string.Empty, 40));
    }

    /// <summary>折行 — 超宽单词硬切（等宽切片不丢内容）。</summary>
    [Fact]
    public void Wrap_OverlongWord_HardChunks()
    {
        var lines = PermissionTextWrap.Wrap("averylongword", 5);

        Assert.Equal(["avery", "longw", "ord"], lines);
    }

    /// <summary>折行 — 显式换行符拆段（多行命令原样分段）。</summary>
    [Fact]
    public void Wrap_ExplicitNewline_Splits()
    {
        Assert.Equal(["echo a", "echo b"], PermissionTextWrap.Wrap("echo a\necho b", 40));
    }

    // ---- DangerousCommandCatalog 危险检测 ----

    /// <summary>危险模式矩阵 — 对齐基准 destructiveCommandWarning.ts 全模式（中文警告文案）。</summary>
    [Theory]
    [InlineData("git reset --hard HEAD~3", "可能丢弃未提交的更改")]
    [InlineData("git push --force origin main", "可能覆盖远程历史")]
    [InlineData("git push origin main -f", "可能覆盖远程历史")]
    [InlineData("git push --force-with-lease origin main", "可能覆盖远程历史")]
    [InlineData("git clean -fd", "可能永久删除未跟踪文件")]
    [InlineData("git checkout .", "可能丢弃所有工作区更改")]
    [InlineData("git restore .", "可能丢弃所有工作区更改")]
    [InlineData("git stash drop", "可能永久移除储藏的更改")]
    [InlineData("git stash clear", "可能永久移除储藏的更改")]
    [InlineData("git branch -D feature", "可能强制删除分支")]
    [InlineData("git commit --no-verify -m x", "可能跳过安全钩子")]
    [InlineData("git merge --no-verify", "可能跳过安全钩子")]
    [InlineData("git commit --amend", "可能重写最后一次提交")]
    [InlineData("rm -rf /tmp/x", "可能递归强制删除文件")]
    [InlineData("rm -fr /tmp/x", "可能递归强制删除文件")]
    [InlineData("rm -r build", "可能递归删除文件")]
    [InlineData("rm -f a.txt", "可能强制删除文件")]
    [InlineData("DROP TABLE users;", "可能删除或清空数据库对象")]
    [InlineData("truncate table logs", "可能删除或清空数据库对象")]
    [InlineData("DELETE FROM users;", "可能删除表中所有行")]
    [InlineData("kubectl delete pod api-pod", "可能删除 Kubernetes 资源")]
    [InlineData("terraform destroy", "可能销毁 Terraform 基础设施")]
    [InlineData("sudo apt install curl", "以管理员权限执行，请谨慎确认")]
    public void MatchWarning_DangerousPatterns(string command, string expected)
    {
        Assert.Equal(expected, DangerousCommandCatalog.MatchWarning(command));
    }

    /// <summary>安全命令 — 无危险命中（null）。</summary>
    [Theory]
    [InlineData("ls -la")]
    [InlineData("git status")]
    [InlineData("git push origin main")]
    [InlineData("git clean -fdn")]
    [InlineData("rm a.txt")]
    [InlineData("git checkout src/main.cs")]
    [InlineData("DELETE FROM users WHERE id=1;")]
    [InlineData("")]
    public void MatchWarning_SafeCommands_ReturnsNull(string command)
    {
        Assert.Null(DangerousCommandCatalog.MatchWarning(command));
    }

    /// <summary>复合命令 — 词扫描任意起点命中（cd x && rm -rf … 对齐基准 [;&|\n] 起点语义）。</summary>
    [Fact]
    public void MatchWarning_CompoundCommand_HitsAtAnyStart()
    {
        Assert.Equal("可能递归强制删除文件", DangerousCommandCatalog.MatchWarning("cd src && rm -rf build"));
    }

    /// <summary>大小写不敏感（引擎提示文本大小写不可控）。</summary>
    [Fact]
    public void MatchWarning_CaseInsensitive()
    {
        Assert.Equal("可能丢弃未提交的更改", DangerousCommandCatalog.MatchWarning("GIT RESET --HARD"));
    }
}
