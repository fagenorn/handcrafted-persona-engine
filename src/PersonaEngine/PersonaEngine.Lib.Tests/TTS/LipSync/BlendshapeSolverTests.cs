using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class BlendshapeSolverTests
{
    [Fact]
    public void Solve_IdentityDelta_RecoversSingleWeight()
    {
        // D: 3 positions, 2 blendshapes. BS0 moves pos0, BS1 moves pos1.
        var D = new float[] { 1f, 0f, 0f, 1f, 0f, 0f }; // [3x2] row-major
        var solver = new BlendshapeSolver(D, 3, 2, 1f, 0.01f, 0f, 0f);
        var delta = new float[] { 0.7f, 0f, 0f };
        var weights = solver.Solve(delta);
        Assert.Equal(2, weights.Length);
        Assert.InRange(weights[0], 0.6f, 0.8f);
        Assert.InRange(weights[1], -0.01f, 0.1f);
    }

    [Fact]
    public void Solve_WeightsClampedToZeroOne()
    {
        var D = new float[] { 1f, 0f }; // [1x2]
        var solver = new BlendshapeSolver(D, 1, 2, 1f, 0.01f, 0f, 0f);
        var delta = new float[] { 5.0f };
        var weights = solver.Solve(delta);
        Assert.True(weights[0] <= 1.0f);
        Assert.True(weights[0] >= 0.0f);
    }

    [Fact]
    public void Solve_ZeroDelta_ReturnsNearZeroWeights()
    {
        var D = new float[] { 1f, 0f, 0f, 1f }; // [2x2]
        var solver = new BlendshapeSolver(D, 2, 2, 1f, 0.1f, 0f, 0f);
        var delta = new float[] { 0f, 0f };
        var weights = solver.Solve(delta);
        Assert.InRange(weights[0], -0.01f, 0.01f);
        Assert.InRange(weights[1], -0.01f, 0.01f);
    }
}
