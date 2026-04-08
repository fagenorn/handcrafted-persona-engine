namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Bounded Variable Least Squares solver that converts vertex deltas
///     into ARKit blendshape weights [0, 1] using projected gradient descent
///     with L1, L2, and temporal regularization.
/// </summary>
public sealed class BlendshapeSolver
{
    private const int MaxIterations = 50;
    private const double ConvergenceThreshold = 1e-6;

    /// <summary>
    ///     Precomputed A = D^T @ D + regularization [K x K], row-major.
    /// </summary>
    private readonly double[] _a;

    /// <summary>
    ///     Number of active blendshapes (K).
    /// </summary>
    private readonly int _activeCount;

    /// <summary>
    ///     D^T [K x V], row-major.
    /// </summary>
    private readonly double[] _dt;

    /// <summary>
    ///     Number of masked vertex components (V).
    /// </summary>
    private readonly int _maskedPositionCount;

    /// <summary>
    ///     Previous frame weights for temporal smoothing [K].
    /// </summary>
    private readonly double[] _prevWeights;

    /// <summary>
    ///     Step size for projected gradient descent, derived from Gershgorin bound.
    /// </summary>
    private readonly double _stepSize;

    /// <summary>
    ///     Temporal regularization scale added to the b vector.
    /// </summary>
    private readonly double _temporalScale;

    /// <summary>
    ///     Initializes the solver by precomputing the system matrix and step size.
    /// </summary>
    /// <param name="deltaMatrix">
    ///     [V x K] row-major delta matrix where V = masked positions and K = active
    ///     blendshapes.
    /// </param>
    /// <param name="maskedPositionCount">V: number of masked vertex components.</param>
    /// <param name="activeCount">K: number of active blendshapes.</param>
    /// <param name="templateBBSize">Bounding box size from config (unused in current scale=1.0 path).</param>
    /// <param name="strengthL2">L2 regularization strength.</param>
    /// <param name="strengthL1">L1 regularization strength.</param>
    /// <param name="strengthTemporal">Temporal smoothing regularization strength.</param>
    public BlendshapeSolver(
        float[] deltaMatrix,
        int maskedPositionCount,
        int activeCount,
        float templateBBSize,
        float strengthL2,
        float strengthL1,
        float strengthTemporal
    )
    {
        _maskedPositionCount = maskedPositionCount;
        _activeCount = activeCount;
        _prevWeights = new double[activeCount];

        const double scale = 1.0;

        // Build D^T [K x V] from D [V x K]
        _dt = new double[activeCount * maskedPositionCount];

        for (var v = 0; v < maskedPositionCount; v++)
        {
            for (var k = 0; k < activeCount; k++)
            {
                _dt[k * maskedPositionCount + v] = deltaMatrix[v * activeCount + k];
            }
        }

        // Compute A = D^T @ D [K x K]
        _a = new double[activeCount * activeCount];

        for (var i = 0; i < activeCount; i++)
        {
            for (var j = 0; j < activeCount; j++)
            {
                var sum = 0.0;

                for (var v = 0; v < maskedPositionCount; v++)
                {
                    sum += _dt[i * maskedPositionCount + v] * _dt[j * maskedPositionCount + v];
                }

                _a[i * activeCount + j] = sum;
            }
        }

        // Add L2 regularization to diagonal
        for (var i = 0; i < activeCount; i++)
        {
            _a[i * activeCount + i] += 10.0 * scale * strengthL2;
        }

        // Add L1 regularization to ALL entries
        for (var i = 0; i < activeCount; i++)
        {
            for (var j = 0; j < activeCount; j++)
            {
                _a[i * activeCount + j] += 0.25 * scale * strengthL1;
            }
        }

        // Add temporal regularization to diagonal
        _temporalScale = 100.0 * scale * strengthTemporal;

        for (var i = 0; i < activeCount; i++)
        {
            _a[i * activeCount + i] += _temporalScale;
        }

        // Compute step size via Gershgorin bound: 1 / max row sum of |A|
        var maxRowSum = 0.0;

        for (var i = 0; i < activeCount; i++)
        {
            var rowSum = 0.0;

            for (var j = 0; j < activeCount; j++)
            {
                rowSum += Math.Abs(_a[i * activeCount + j]);
            }

            if (rowSum > maxRowSum)
            {
                maxRowSum = rowSum;
            }
        }

        _stepSize = maxRowSum > 0 ? 1.0 / maxRowSum : 1.0;
    }

    /// <summary>
    ///     Solves for blendshape weights given a vertex delta vector.
    /// </summary>
    /// <param name="delta">Vertex delta [V] (masked positions).</param>
    /// <returns>Blendshape weights [K], each clamped to [0, 1].</returns>
    public float[] Solve(ReadOnlySpan<float> delta)
    {
        var k = _activeCount;

        // Compute b = D^T @ delta + temporalScale * prevWeights [K]
        var b = new double[k];

        for (var i = 0; i < k; i++)
        {
            var sum = 0.0;

            for (var v = 0; v < _maskedPositionCount; v++)
            {
                sum += _dt[i * _maskedPositionCount + v] * delta[v];
            }

            b[i] = sum + _temporalScale * _prevWeights[i];
        }

        // Initialize x from diagonal: x[i] = clamp(b[i] / A[i,i], 0, 1)
        var x = new double[k];

        for (var i = 0; i < k; i++)
        {
            var diag = _a[i * k + i];
            x[i] = diag != 0.0 ? Math.Clamp(b[i] / diag, 0.0, 1.0) : 0.0;
        }

        // Projected gradient descent
        var grad = new double[k];

        for (var iter = 0; iter < MaxIterations; iter++)
        {
            var maxChange = 0.0;

            for (var i = 0; i < k; i++)
            {
                var g = 0.0;

                for (var j = 0; j < k; j++)
                {
                    g += _a[i * k + j] * x[j];
                }

                g -= b[i];
                grad[i] = g;

                var oldX = x[i];
                x[i] = Math.Clamp(oldX - _stepSize * g, 0.0, 1.0);

                var change = Math.Abs(x[i] - oldX);

                if (change > maxChange)
                {
                    maxChange = change;
                }
            }

            if (maxChange < ConvergenceThreshold)
            {
                break;
            }
        }

        // Store for temporal smoothing and convert to float output
        var result = new float[k];

        for (var i = 0; i < k; i++)
        {
            _prevWeights[i] = x[i];
            result[i] = (float)x[i];
        }

        return result;
    }

    /// <summary>
    ///     Resets the temporal smoothing state (previous frame weights).
    /// </summary>
    public void ResetTemporal()
    {
        Array.Clear(_prevWeights);
    }
}
