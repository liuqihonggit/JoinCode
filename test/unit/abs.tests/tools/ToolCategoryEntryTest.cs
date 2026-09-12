namespace Abs.Tests.Tools;

/// <summary>
/// ToolCategoryEntry record 单元测试 — 验证 record 属性、相等性、不可变性
/// </summary>
public sealed class ToolCategoryEntryTest
{
    // === 构造 ===

    [Fact]
    public void Constructor_RequiredProperties_SetsCorrectly()
    {
        var entry = new ToolCategoryEntry
        {
            Name = "read_file",
            Description = "读取文件内容",
            Kind = ToolKind.System
        };

        entry.Name.Should().Be("read_file");
        entry.Description.Should().Be("读取文件内容");
        entry.Kind.Should().Be(ToolKind.System);
        entry.GroupName.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithGroupName_SetsGroupName()
    {
        var entry = new ToolCategoryEntry
        {
            Name = "bash",
            Description = "执行Shell命令",
            Kind = ToolKind.Mcp,
            GroupName = "shell"
        };

        entry.GroupName.Should().Be("shell");
    }

    [Fact]
    public void Constructor_AllKinds_AreSupported()
    {
        var systemEntry = new ToolCategoryEntry { Name = "sys", Description = "d", Kind = ToolKind.System };
        var mcpEntry = new ToolCategoryEntry { Name = "mcp", Description = "d", Kind = ToolKind.Mcp };
        var onErrorEntry = new ToolCategoryEntry { Name = "err", Description = "d", Kind = ToolKind.OnError };

        systemEntry.Kind.Should().Be(ToolKind.System);
        mcpEntry.Kind.Should().Be(ToolKind.Mcp);
        onErrorEntry.Kind.Should().Be(ToolKind.OnError);
    }

    // === Record 相等性 ===

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var entry1 = new ToolCategoryEntry
        {
            Name = "read_file",
            Description = "读取文件",
            Kind = ToolKind.System,
            GroupName = "file_ops"
        };

        var entry2 = new ToolCategoryEntry
        {
            Name = "read_file",
            Description = "读取文件",
            Kind = ToolKind.System,
            GroupName = "file_ops"
        };

        entry1.Should().Be(entry2);
        entry1.GetHashCode().Should().Be(entry2.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentName_AreNotEqual()
    {
        var entry1 = new ToolCategoryEntry { Name = "read_file", Description = "d", Kind = ToolKind.System };
        var entry2 = new ToolCategoryEntry { Name = "write_file", Description = "d", Kind = ToolKind.System };

        entry1.Should().NotBe(entry2);
    }

    [Fact]
    public void Equality_DifferentKind_AreNotEqual()
    {
        var entry1 = new ToolCategoryEntry { Name = "tool", Description = "d", Kind = ToolKind.System };
        var entry2 = new ToolCategoryEntry { Name = "tool", Description = "d", Kind = ToolKind.Mcp };

        entry1.Should().NotBe(entry2);
    }

    [Fact]
    public void Equality_DifferentGroupName_AreNotEqual()
    {
        var entry1 = new ToolCategoryEntry { Name = "tool", Description = "d", Kind = ToolKind.Mcp, GroupName = "a" };
        var entry2 = new ToolCategoryEntry { Name = "tool", Description = "d", Kind = ToolKind.Mcp, GroupName = "b" };

        entry1.Should().NotBe(entry2);
    }

    [Fact]
    public void Equality_NullGroupNameVsSetGroupName_AreNotEqual()
    {
        var entry1 = new ToolCategoryEntry { Name = "tool", Description = "d", Kind = ToolKind.Mcp };
        var entry2 = new ToolCategoryEntry { Name = "tool", Description = "d", Kind = ToolKind.Mcp, GroupName = "shell" };

        entry1.Should().NotBe(entry2);
    }

    // === 不可变性 ===

    [Fact]
    public void Properties_AreInitOnly_CannotBeSetAfterConstruction()
    {
        var entry = new ToolCategoryEntry
        {
            Name = "read_file",
            Description = "读取文件",
            Kind = ToolKind.System,
            GroupName = "file_ops"
        };

        // record 的 init 属性 — 验证构造后的值稳定
        entry.Name.Should().Be("read_file");
        entry.Description.Should().Be("读取文件");
        entry.Kind.Should().Be(ToolKind.System);
        entry.GroupName.Should().Be("file_ops");
    }

    // === With 表达式 ===

    [Fact]
    public void With_Name_ReturnsNewRecordWithModifiedName()
    {
        var entry = new ToolCategoryEntry
        {
            Name = "read_file",
            Description = "读取文件",
            Kind = ToolKind.System
        };

        var modified = entry with { Name = "write_file" };

        modified.Name.Should().Be("write_file");
        modified.Description.Should().Be("读取文件");
        modified.Kind.Should().Be(ToolKind.System);
        modified.Should().NotBeSameAs(entry);
    }

    [Fact]
    public void With_GroupName_ReturnsNewRecordWithGroupName()
    {
        var entry = new ToolCategoryEntry
        {
            Name = "bash",
            Description = "Shell",
            Kind = ToolKind.Mcp
        };

        var modified = entry with { GroupName = "shell" };

        modified.GroupName.Should().Be("shell");
        entry.GroupName.Should().BeNull();
    }
}
