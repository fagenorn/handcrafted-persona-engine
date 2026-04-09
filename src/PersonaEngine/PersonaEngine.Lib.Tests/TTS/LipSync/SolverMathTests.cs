using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class SolverMathTests
{
    [Fact]
    public void ComputeBoundingBoxDiagonal_UnitCube_ReturnsSqrt3()
    {
        // 8 vertices of a unit cube [0,1]^3
        var vertices = new float[]
        {
            0f,
            0f,
            0f,
            1f,
            0f,
            0f,
            0f,
            1f,
            0f,
            0f,
            0f,
            1f,
            1f,
            1f,
            0f,
            1f,
            0f,
            1f,
            0f,
            1f,
            1f,
            1f,
            1f,
            1f,
        };

        var diag = SolverMath.ComputeBoundingBoxDiagonal(vertices);

        Assert.Equal(Math.Sqrt(3.0), diag, precision: 10);
    }

    [Fact]
    public void ComputeBoundingBoxDiagonal_EmptySpan_ReturnsZero()
    {
        var diag = SolverMath.ComputeBoundingBoxDiagonal(ReadOnlySpan<float>.Empty);
        Assert.Equal(0.0, diag);
    }

    [Fact]
    public void ComputeTranspose_2x3_CorrectLayout()
    {
        // D is [V=2 x K=3] row-major:
        //   row0: 1, 2, 3
        //   row1: 4, 5, 6
        var d = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };
        var dt = new double[3 * 2]; // [K=3 x V=2]

        SolverMath.ComputeTranspose(d, v: 2, k: 3, dt);

        // D^T should be:
        //   row0: 1, 4
        //   row1: 2, 5
        //   row2: 3, 6
        Assert.Equal(1.0, dt[0 * 2 + 0]);
        Assert.Equal(4.0, dt[0 * 2 + 1]);
        Assert.Equal(2.0, dt[1 * 2 + 0]);
        Assert.Equal(5.0, dt[1 * 2 + 1]);
        Assert.Equal(3.0, dt[2 * 2 + 0]);
        Assert.Equal(6.0, dt[2 * 2 + 1]);
    }

    [Fact]
    public void ComputeDtD_Identity_ReturnsIdentity()
    {
        // D^T is [K=2 x V=2] identity
        var dt = new double[] { 1.0, 0.0, 0.0, 1.0 };
        var dtd = new double[2 * 2];

        SolverMath.ComputeDtD(dt, k: 2, v: 2, dtd);

        // DtD should be identity
        Assert.Equal(1.0, dtd[0 * 2 + 0], precision: 10);
        Assert.Equal(0.0, dtd[0 * 2 + 1], precision: 10);
        Assert.Equal(0.0, dtd[1 * 2 + 0], precision: 10);
        Assert.Equal(1.0, dtd[1 * 2 + 1], precision: 10);
    }

    [Fact]
    public void ApplyRegularization_AddsCorrectValues()
    {
        // Start with a zero 2x2 matrix
        var a = new double[4];
        var scale = 2.0;
        var strL2 = 0.5f;
        var strL1 = 1.0f;
        var strTemp = 0.3f;

        var temporalScale = SolverMath.ApplyRegularization(a, k: 2, scale, strL2, strL1, strTemp);

        // Expected values:
        var expectedL2 = 10.0 * scale * strL2; // 10.0
        var expectedL1 = 0.25 * scale * strL1; // 0.5
        var expectedTemp = 100.0 * scale * strTemp; // 60.0

        // Diagonal entries: L2 + L1 + temporal
        Assert.Equal(expectedL2 + expectedL1 + expectedTemp, a[0 * 2 + 0], precision: 10);
        Assert.Equal(expectedL2 + expectedL1 + expectedTemp, a[1 * 2 + 1], precision: 10);

        // Off-diagonal entries: L1 only
        Assert.Equal(expectedL1, a[0 * 2 + 1], precision: 10);
        Assert.Equal(expectedL1, a[1 * 2 + 0], precision: 10);

        // Returned temporal scale
        Assert.Equal(expectedTemp, temporalScale, precision: 10);
    }
}
