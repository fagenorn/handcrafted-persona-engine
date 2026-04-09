using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class BlendshapeSolverTests
{
    [Fact]
    public void Solve_IdentityDelta_RecoversSingleWeight()
    {
        // D: 3 positions (1 vertex), 2 blendshapes
        var D = new float[] { 1f, 0f, 0f, 1f, 0f, 0f };
        // Neutral: single vertex at origin → BB size = 0, so pass templateBBSize=1 for scale=0
        // Actually for a single vertex, BB is 0. Use a safe approach:
        var neutral = new float[] { 0f, 0f, 0f };
        var solver = new PgdBlendshapeSolver(D, 3, 2, neutral, 1f, 0.01f, 0f, 0f);
        var delta = new float[] { 0.7f, 0f, 0f };
        var weights = solver.Solve(delta);
        Assert.Equal(2, weights.Length);
        Assert.InRange(weights[0], 0.5f, 0.9f);
        Assert.InRange(weights[1], -0.01f, 0.15f);
    }

    [Fact]
    public void Solve_WeightsClampedToZeroOne()
    {
        var D = new float[] { 1f, 0f };
        var neutral = new float[] { 0f };
        var solver = new PgdBlendshapeSolver(D, 1, 2, neutral, 1f, 0.01f, 0f, 0f);
        var delta = new float[] { 5.0f };
        var weights = solver.Solve(delta);
        Assert.True(weights[0] <= 1.0f);
        Assert.True(weights[0] >= 0.0f);
    }

    [Fact]
    public void Solve_ZeroDelta_ReturnsNearZeroWeights()
    {
        var D = new float[] { 1f, 0f, 0f, 1f };
        var neutral = new float[] { 0f, 0f };
        var solver = new PgdBlendshapeSolver(D, 2, 2, neutral, 1f, 0.1f, 0f, 0f);
        var delta = new float[] { 0f, 0f };
        var weights = solver.Solve(delta);
        Assert.InRange(weights[0], -0.01f, 0.01f);
        Assert.InRange(weights[1], -0.01f, 0.01f);
    }

    [Fact]
    public void Solve_TemporalSmoothing_AffectsSecondFrame()
    {
        // With temporal smoothing, second solve should be influenced by first
        var D = new float[] { 1f, 0f, 0f, 1f }; // [2x2]
        var neutral = new float[] { 0f, 0f };
        var solver = new PgdBlendshapeSolver(D, 2, 2, neutral, 1f, 0.01f, 0f, 0.5f);

        // First solve with strong delta
        var delta1 = new float[] { 0.8f, 0f };
        solver.Solve(delta1);

        // Second solve with zero delta — temporal should pull weights up
        var delta2 = new float[] { 0f, 0f };
        var weights2 = solver.Solve(delta2);
        Assert.True(weights2[0] > 0.01f, $"Expected temporal influence, got {weights2[0]}");
    }

    [Fact]
    public void BvlsSolve_IdentityDelta_RecoversSingleWeight()
    {
        var K = 3;
        var V = 6;
        var delta = new float[V * K];
        delta[0 * K + 0] = 1f;
        delta[1 * K + 1] = 1f;
        delta[2 * K + 2] = 1f;

        var neutral = new float[V];
        var solver = new BvlsBlendshapeSolver(delta, V, K, neutral, 1f, 0.1f, 0.1f, 0.15f);

        var target = new float[V];
        target[0] = 1f;

        var weights = solver.Solve(target);

        Assert.Equal(K, weights.Length);
        Assert.True(weights[0] > 0.5f, $"Expected weight[0] > 0.5, got {weights[0]}");
        Assert.True(weights[1] < 0.1f, $"Expected weight[1] < 0.1, got {weights[1]}");
        Assert.True(weights[2] < 0.1f, $"Expected weight[2] < 0.1, got {weights[2]}");
    }

    [Fact]
    public void BvlsSolve_WeightsClampedToZeroOne()
    {
        var K = 2;
        var V = 4;
        var delta = new float[V * K];
        delta[0 * K + 0] = 0.01f;
        delta[1 * K + 1] = 0.01f;

        var neutral = new float[V];
        var solver = new BvlsBlendshapeSolver(delta, V, K, neutral, 1f, 0.1f, 0.1f, 0.15f);

        var target = new float[V];
        target[0] = 100f;
        target[1] = 100f;

        var weights = solver.Solve(target);

        foreach (var w in weights)
        {
            Assert.True(w >= 0f && w <= 1f, $"Weight {w} out of [0,1] range");
        }
    }
}
