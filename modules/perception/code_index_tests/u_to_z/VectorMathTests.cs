namespace JoinCode.CodeIndex.Tests;

public sealed class VectorMathTests {

    [Fact]
    public void CosineSimilarity_IdenticalVectors_Returns1() {
        float[] a = [1f, 2f, 3f, 4f];
        float[] b = [1f, 2f, 3f, 4f];

        var sim = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(1f, sim, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_Returns0() {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];

        var sim = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0f, sim, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsMinus1() {
        float[] a = [1f, 2f, 3f];
        float[] b = [-1f, -2f, -3f];

        var sim = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(-1f, sim, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_Returns0() {
        float[] a = [0f, 0f, 0f];
        float[] b = [1f, 2f, 3f];

        var sim = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0f, sim);
    }

    [Fact]
    public void CosineSimilarity_SimdAlignment_MatchesScalar() {
        var rnd = new Random(42);
        var dim = 256;
        var a = new float[dim];
        var b = new float[dim];
        for (var i = 0; i < dim; i++) {
            a[i] = (float)rnd.NextDouble();
            b[i] = (float)rnd.NextDouble();
        }

        var simdResult = VectorMath.CosineSimilarity(a, b);
        var scalarResult = ScalarCosineSimilarity(a, b);

        Assert.Equal(scalarResult, simdResult, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_DimensionMismatch_Throws() {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f];

        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
    }

    [Fact]
    public void Dot_BasicCalculation() {
        float[] a = [1f, 2f, 3f];
        float[] b = [4f, 5f, 6f];

        var dot = VectorMath.Dot(a, b);

        Assert.Equal(32f, dot, 0.0001f);
    }

    [Fact]
    public void Dot_LargeVector_MatchesScalar() {
        var rnd = new Random(123);
        var dim = 384;
        var a = new float[dim];
        var b = new float[dim];
        for (var i = 0; i < dim; i++) {
            a[i] = (float)rnd.NextDouble() * 2f - 1f;
            b[i] = (float)rnd.NextDouble() * 2f - 1f;
        }

        var simdDot = VectorMath.Dot(a, b);
        var scalarDot = ScalarDot(a, b);

        Assert.Equal(scalarDot, simdDot, 0.001f);
    }

    [Fact]
    public void L2Norm_KnownVector() {
        float[] a = [3f, 4f];

        var norm = VectorMath.L2Norm(a);

        Assert.Equal(5f, norm, 0.0001f);
    }

    [Fact]
    public void NormalizeInPlace_UnitNorm() {
        float[] a = [3f, 4f];

        VectorMath.NormalizeInPlace(a);

        var norm = VectorMath.L2Norm(a);
        Assert.Equal(1f, norm, 0.0001f);
    }

    [Fact]
    public void NormalizeInPlace_ZeroVector_NoChange() {
        float[] a = [0f, 0f, 0f];

        VectorMath.NormalizeInPlace(a);

        Assert.Equal([0f, 0f, 0f], a);
    }

    private static float ScalarCosineSimilarity(float[] a, float[] b) {
        var dot = ScalarDot(a, b);
        var normA = MathF.Sqrt(ScalarDot(a, a));
        var normB = MathF.Sqrt(ScalarDot(b, b));
        return normA == 0f || normB == 0f ? 0f : dot / (normA * normB);
    }

    private static float ScalarDot(float[] a, float[] b) {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++) {
            sum += a[i] * b[i];
        }
        return sum;
    }
}
