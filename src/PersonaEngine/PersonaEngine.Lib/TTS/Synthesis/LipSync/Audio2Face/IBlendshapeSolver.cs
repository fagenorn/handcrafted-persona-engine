namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Strategy interface for solving blendshape weights from vertex deltas.
///     Implementations minimize ||A·x − b||² subject to 0 ≤ x ≤ 1 with
///     temporal regularization.
/// </summary>
public interface IBlendshapeSolver
{
    float[] Solve(ReadOnlySpan<float> delta);

    void ResetTemporal();

    double[] SaveTemporal();

    void RestoreTemporal(double[] saved);
}
