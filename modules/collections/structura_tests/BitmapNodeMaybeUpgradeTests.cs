namespace Structura.Tests;

public class BitmapNodeMaybeUpgradeTests {
    private static ImmutableHamT<string, int>.LeafNode[] MakeLeaves(int count) {
        var leaves = new ImmutableHamT<string, int>.LeafNode[count];
        for (var i = 0; i < count; i++)
            leaves[i] = new ImmutableHamT<string, int>.LeafNode($"k{i}", i);
        return leaves;
    }

    [Fact]
    public void MaybeUpgrade_BelowThreshold_ReturnsBitmapNode() {
        var children = MakeLeaves(15);
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0x7FFF, children);
        result.Should().BeOfType<ImmutableHamT<string, int>.BitmapNode>();
    }

    [Fact]
    public void MaybeUpgrade_AtThreshold_ReturnsArrayNode() {
        var children = MakeLeaves(16);
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0xFFFF, children);
        result.Should().BeOfType<ImmutableHamT<string, int>.ArrayNode>();
    }

    [Fact]
    public void MaybeUpgrade_AboveThreshold_ReturnsArrayNode() {
        var children = MakeLeaves(20);
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0xFFFFF, children);
        result.Should().BeOfType<ImmutableHamT<string, int>.ArrayNode>();
    }

    [Fact]
    public void MaybeUpgrade_SingleChild_ReturnsBitmapNode() {
        var children = MakeLeaves(1);
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0x1, children);
        result.Should().BeOfType<ImmutableHamT<string, int>.BitmapNode>();
    }

    [Fact]
    public void MaybeUpgrade_EmptyChildren_ReturnsBitmapNode() {
        var children = Array.Empty<ImmutableHamT<string, int>.Node>();
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0, children);
        result.Should().BeOfType<ImmutableHamT<string, int>.BitmapNode>();
    }

    [Fact]
    public void MaybeUpgrade_ArrayNodePreservesCount() {
        var children = MakeLeaves(16);
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0xFFFF, children);
        var arrayNode = result.Should().BeOfType<ImmutableHamT<string, int>.ArrayNode>().Subject;
        arrayNode.Count.Should().Be(16);
    }

    [Fact]
    public void MaybeUpgrade_BitmapNodePreservesBitmap() {
        var children = MakeLeaves(5);
        const int bitmap = 0b10101;
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(bitmap, children);
        var bmNode = result.Should().BeOfType<ImmutableHamT<string, int>.BitmapNode>().Subject;
        bmNode.Bitmap.Should().Be(bitmap);
    }

    [Fact]
    public void MaybeUpgrade_JustBelowThreshold_ReturnsBitmapNode() {
        var children = MakeLeaves(15);
        var result = ImmutableHamT<string, int>.BitmapNode.MaybeUpgradeToFullArrayNode(0x7FFF, children);
        result.Should().BeOfType<ImmutableHamT<string, int>.BitmapNode>();
    }
}
