namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Hybrid BVLS solver that converts vertex deltas into ARKit blendshape
///     weights [0, 1]. Uses a precomputed matrix (from LU-decomposed normal
///     equations without temporal) as initial guess, then refines with
///     projected gradient descent using the full system matrix (including
///     temporal regularization).
/// </summary>
public sealed class PgdBlendshapeSolver : IBlendshapeSolver
{
    private const int MaxIterations = 10;
    private const double ConvergenceThreshold = 1e-6;

    /// <summary>
    ///     Precomputed A = D^T @ D + L2 + L1 + temporal regularization [K x K], row-major.
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
    ///     Precomputed M = inv(A_noTemporal) @ D^T [K x V], row-major.
    ///     Used for initial guess: x0 = clip(M @ delta, 0, 1).
    /// </summary>
    private readonly double[] _m;

    /// <summary>
    ///     Number of masked vertex components (V).
    /// </summary>
    private readonly int _maskedPositionCount;

    /// <summary>
    ///     Previous frame weights for temporal smoothing [K].
    /// </summary>
    private readonly double[] _prevWeights;

    /// <summary>
    ///     Step size for projected gradient descent, derived from power-iteration eigenvalue estimate.
    /// </summary>
    private readonly double _stepSize;

    /// <summary>
    ///     Temporal regularization scale added to the b vector.
    /// </summary>
    private readonly double _temporalScale;

    // Pre-allocated working buffers for Solve()
    private readonly double[] _dtDelta;
    private readonly double[] _xBuf;
    private readonly double[] _bBuf;
    private readonly float[] _resultBuf;

    // Pre-allocated buffer for LuSolve()
    private readonly double[] _pb;

    /// <summary>
    ///     Initializes the solver by precomputing the system matrix, LU-based
    ///     initial-guess matrix, and PGD step size.
    /// </summary>
    /// <param name="deltaMatrix">
    ///     [V x K] row-major delta matrix where V = masked positions and K = active
    ///     blendshapes.
    /// </param>
    /// <param name="maskedPositionCount">V: number of masked vertex components.</param>
    /// <param name="activeCount">K: number of active blendshapes.</param>
    /// <param name="maskedNeutralFlat">
    ///     Masked neutral vertex positions, length = maskedPositionCount.
    ///     Used to compute the bounding-box scale factor.
    /// </param>
    /// <param name="templateBBSize">Template bounding box size from config.</param>
    /// <param name="strengthL2">L2 regularization strength.</param>
    /// <param name="strengthL1">L1 regularization strength.</param>
    /// <param name="strengthTemporal">Temporal smoothing regularization strength.</param>
    public PgdBlendshapeSolver(
        float[] deltaMatrix,
        int maskedPositionCount,
        int activeCount,
        float[] maskedNeutralFlat,
        float templateBBSize,
        float strengthL2,
        float strengthL1,
        float strengthTemporal
    )
    {
        _maskedPositionCount = maskedPositionCount;
        _activeCount = activeCount;
        _prevWeights = new double[activeCount];

        var K = activeCount;
        var V = maskedPositionCount;

        // Compute bounding-box scale from masked neutral vertices
        var numVerts = maskedPositionCount / 3;
        var scale = 1.0;

        if (numVerts > 0 && templateBBSize > 0)
        {
            var bbSize = SolverMath.ComputeBoundingBoxDiagonal(maskedNeutralFlat);

            if (bbSize > 0)
            {
                scale = Math.Pow(bbSize / templateBBSize, 2);
            }
        }

        // Build D^T [K x V] from D [V x K]
        _dt = new double[K * V];
        SolverMath.ComputeTranspose(deltaMatrix, V, K, _dt);

        // Compute DtD = D^T @ D [K x K]
        var dtd = new double[K * K];
        SolverMath.ComputeDtD(_dt, K, V, dtd);

        // Build A_noTemporal = DtD + L2_reg + L1_reg
        var aNoTemp = new double[K * K];
        Array.Copy(dtd, aNoTemp, K * K);

        // Add L2 regularization to diagonal
        for (var i = 0; i < K; i++)
        {
            aNoTemp[i * K + i] += SolverMath.L2Multiplier * scale * strengthL2;
        }

        // Add L1 regularization to ALL entries
        for (var i = 0; i < K; i++)
        {
            for (var j = 0; j < K; j++)
            {
                aNoTemp[i * K + j] += SolverMath.L1Multiplier * scale * strengthL1;
            }
        }

        // Build full A = A_noTemporal + temporal_reg (for PGD refinement)
        _temporalScale = SolverMath.TemporalMultiplier * scale * strengthTemporal;
        _a = new double[K * K];
        Array.Copy(aNoTemp, _a, K * K);

        for (var i = 0; i < K; i++)
        {
            _a[i * K + i] += _temporalScale;
        }

        // Compute step size via power iteration estimate of max eigenvalue of A
        var maxEig = EstimateMaxEigenvalue(_a, K);
        _stepSize = 1.0 / maxEig;

        // Compute M = inv(A_noTemporal) @ D^T via LU decomposition
        // A_noTemporal is K x K (small), so LU is trivial
        var luPivot = new int[K];
        _pb = new double[K];
        LuDecompose(aNoTemp, K, luPivot);

        _m = new double[K * V];
        var col = new double[K];
        var solCol = new double[K];

        for (var vi = 0; vi < V; vi++)
        {
            // Extract column vi of D^T (which is row vi across all K blendshapes)
            for (var k = 0; k < K; k++)
            {
                col[k] = _dt[k * V + vi];
            }

            LuSolve(aNoTemp, K, luPivot, col, solCol);

            for (var k = 0; k < K; k++)
            {
                _m[k * V + vi] = solCol[k];
            }
        }

        // Pre-allocate working buffers for Solve()
        _dtDelta = new double[K];
        _xBuf = new double[K];
        _bBuf = new double[K];
        _resultBuf = new float[K];
    }

    /// <summary>
    ///     Solves for blendshape weights given a vertex delta vector.
    /// </summary>
    /// <param name="delta">Vertex delta [V] (masked positions).</param>
    /// <returns>Blendshape weights [K], each clamped to [0, 1].</returns>
    public float[] Solve(ReadOnlySpan<float> delta)
    {
        var k = _activeCount;
        var v = _maskedPositionCount;

        // 1. Compute D^T @ delta
        for (var i = 0; i < k; i++)
        {
            var sum = 0.0;

            for (var j = 0; j < v; j++)
            {
                sum += _dt[i * v + j] * delta[j];
            }

            _dtDelta[i] = sum;
        }

        // 2. Initial guess from precomputed matrix: x = clip(M @ delta, 0, 1)
        for (var i = 0; i < k; i++)
        {
            var sum = 0.0;

            for (var j = 0; j < v; j++)
            {
                sum += _m[i * v + j] * delta[j];
            }

            _xBuf[i] = Math.Clamp(sum, 0.0, 1.0);
        }

        // 3. Compute b with temporal: b = D^T @ delta + temporalScale * prevWeights
        for (var i = 0; i < k; i++)
        {
            _bBuf[i] = _dtDelta[i] + _temporalScale * _prevWeights[i];
        }

        // 4. PGD refinement (using full A with temporal)
        for (var iter = 0; iter < MaxIterations; iter++)
        {
            var maxChange = 0.0;

            for (var i = 0; i < k; i++)
            {
                var g = -_bBuf[i];

                for (var j = 0; j < k; j++)
                {
                    g += _a[i * k + j] * _xBuf[j];
                }

                var oldX = _xBuf[i];
                _xBuf[i] = Math.Clamp(oldX - _stepSize * g, 0.0, 1.0);

                var change = Math.Abs(_xBuf[i] - oldX);

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

        // 5. Store for temporal smoothing and convert to float output
        for (var i = 0; i < k; i++)
        {
            _prevWeights[i] = _xBuf[i];
            _resultBuf[i] = (float)_xBuf[i];
        }

        return _resultBuf;
    }

    /// <summary>
    ///     Resets the temporal smoothing state (previous frame weights).
    /// </summary>
    public void ResetTemporal()
    {
        Array.Clear(_prevWeights);
    }

    public double[] SaveTemporal()
    {
        var saved = new double[_prevWeights.Length];
        Array.Copy(_prevWeights, saved, _prevWeights.Length);
        return saved;
    }

    public void RestoreTemporal(double[] saved)
    {
        Array.Copy(saved, _prevWeights, _prevWeights.Length);
    }

    private static double EstimateMaxEigenvalue(double[] a, int k, int iterations = 50)
    {
        var v = new double[k];
        v[0] = 1.0;
        var av = new double[k];

        for (var iter = 0; iter < iterations; iter++)
        {
            for (var i = 0; i < k; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < k; j++)
                    sum += a[i * k + j] * v[j];
                av[i] = sum;
            }

            var norm = 0.0;
            for (var i = 0; i < k; i++)
                norm += av[i] * av[i];
            norm = Math.Sqrt(norm);

            if (norm < 1e-15)
                break;

            for (var i = 0; i < k; i++)
                v[i] = av[i] / norm;
        }

        var lambda = 0.0;
        for (var i = 0; i < k; i++)
        {
            var avi = 0.0;
            for (var j = 0; j < k; j++)
                avi += a[i * k + j] * v[j];
            lambda += v[i] * avi;
        }

        return Math.Max(lambda, 1e-10);
    }

    /// <summary>
    ///     LU decomposition with partial pivoting. Overwrites <paramref name="a" /> in-place.
    /// </summary>
    private static void LuDecompose(double[] a, int n, int[] pivot)
    {
        for (var i = 0; i < n; i++)
        {
            pivot[i] = i;
        }

        for (var col = 0; col < n; col++)
        {
            // Find pivot
            var maxVal = Math.Abs(a[col * n + col]);
            var maxRow = col;

            for (var row = col + 1; row < n; row++)
            {
                var val = Math.Abs(a[row * n + col]);

                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = row;
                }
            }

            // Swap rows
            if (maxRow != col)
            {
                (pivot[col], pivot[maxRow]) = (pivot[maxRow], pivot[col]);

                for (var j = 0; j < n; j++)
                {
                    (a[col * n + j], a[maxRow * n + j]) = (a[maxRow * n + j], a[col * n + j]);
                }
            }

            // Eliminate
            var diagVal = a[col * n + col];

            if (Math.Abs(diagVal) < 1e-12)
            {
                continue;
            }

            for (var row = col + 1; row < n; row++)
            {
                a[row * n + col] /= diagVal;

                for (var j = col + 1; j < n; j++)
                {
                    a[row * n + j] -= a[row * n + col] * a[col * n + j];
                }
            }
        }
    }

    /// <summary>
    ///     Solve LU @ x = b via forward/back substitution.
    /// </summary>
    private void LuSolve(double[] lu, int n, int[] pivot, double[] b, double[] x)
    {
        // Permute b using pre-allocated _pb
        for (var i = 0; i < n; i++)
        {
            _pb[i] = b[pivot[i]];
        }

        // Forward substitution (L @ y = pb)
        for (var i = 0; i < n; i++)
        {
            var sum = _pb[i];

            for (var j = 0; j < i; j++)
            {
                sum -= lu[i * n + j] * _pb[j];
            }

            _pb[i] = sum;
        }

        // Back substitution (U @ x = y)
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = _pb[i];

            for (var j = i + 1; j < n; j++)
            {
                sum -= lu[i * n + j] * x[j];
            }

            x[i] = sum / lu[i * n + i];
        }
    }
}
