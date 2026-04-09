namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Active-set bounded variable least squares solver matching scipy's
///     <c>lsq_linear(method="bvls")</c>.  Produces more accurate blendshape
///     weights than projected gradient descent at higher per-frame cost.
/// </summary>
public sealed class BvlsBlendshapeSolver : IBlendshapeSolver
{
    private const double Tolerance = 1e-10;
    private const int MaxOuterIterations = 200;

    private readonly double[] _a;
    private readonly int _activeCount;
    private readonly int _maskedPositionCount;
    private readonly double[] _dt;
    private readonly double _temporalScale;
    private readonly double[] _prevWeights;

    // Pre-allocated working buffers for Solve()
    private readonly double[] _b;
    private readonly double[] _x;
    private readonly bool[] _free;
    private readonly double[] _g;
    private readonly int[] _freeIdx;
    private readonly double[] _affMax;
    private readonly double[] _rhsMax;

    // Pre-allocated buffers for CholeskySolve()
    private readonly double[] _cholL;
    private readonly double[] _cholY;
    private readonly double[] _cholX;

    // Pre-allocated result buffer
    private readonly float[] _result;

    public BvlsBlendshapeSolver(
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
        _activeCount = activeCount;
        _maskedPositionCount = maskedPositionCount;
        var k = activeCount;
        var v = maskedPositionCount;

        // Build D^T [K x V] from D [V x K]
        _dt = new double[k * v];
        SolverMath.ComputeTranspose(deltaMatrix, v, k, _dt);

        // Compute DtD = D^T @ D [K x K]
        var dtd = new double[k * k];
        SolverMath.ComputeDtD(_dt, k, v, dtd);

        // Compute bounding-box scale
        var bbSize = SolverMath.ComputeBoundingBoxDiagonal(maskedNeutralFlat);
        var scale =
            bbSize > 0 && templateBBSize > 0
                ? (bbSize / templateBBSize) * (bbSize / templateBBSize)
                : 0.0;

        // Apply regularization
        _a = new double[k * k];
        Array.Copy(dtd, _a, k * k);
        _temporalScale = SolverMath.ApplyRegularization(
            _a,
            k,
            scale,
            strengthL2,
            strengthL1,
            strengthTemporal
        );

        _prevWeights = new double[k];

        // Pre-allocate all working buffers
        _b = new double[k];
        _x = new double[k];
        _free = new bool[k];
        _g = new double[k];
        _freeIdx = new int[k];
        _affMax = new double[k * k];
        _rhsMax = new double[k];
        _cholL = new double[k * k];
        _cholY = new double[k];
        _cholX = new double[k];
        _result = new float[k];
    }

    public float[] Solve(ReadOnlySpan<float> delta)
    {
        var k = _activeCount;
        var v = _maskedPositionCount;

        // Compute b = D^T @ delta + temporal * prevWeights
        for (var i = 0; i < k; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < v; j++)
                sum += _dt[i * v + j] * delta[j];
            _b[i] = sum + _temporalScale * _prevWeights[i];
        }

        // Clear working state
        Array.Clear(_x, 0, k);
        Array.Clear(_free, 0, k);

        for (var outer = 0; outer < MaxOuterIterations; outer++)
        {
            // Compute gradient g = A*x - b
            for (var i = 0; i < k; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < k; j++)
                    sum += _a[i * k + j] * _x[j];
                _g[i] = sum - _b[i];
            }

            var bestIdx = -1;
            var bestViolation = Tolerance;
            for (var i = 0; i < k; i++)
            {
                if (_free[i])
                    continue;
                var violation = _x[i] <= 0.0 ? -_g[i] : _g[i];
                if (violation > bestViolation)
                {
                    bestViolation = violation;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
                break;
            _free[bestIdx] = true;

            while (true)
            {
                var freeCount = 0;
                for (var i = 0; i < k; i++)
                    if (_free[i])
                        _freeIdx[freeCount++] = i;

                if (freeCount == 0)
                    break;

                // Build sub-system for free variables using pre-allocated buffers
                for (var fi = 0; fi < freeCount; fi++)
                {
                    var ii = _freeIdx[fi];
                    _rhsMax[fi] = _b[ii];
                    for (var fj = 0; fj < freeCount; fj++)
                        _affMax[fi * freeCount + fj] = _a[ii * k + _freeIdx[fj]];
                    for (var j = 0; j < k; j++)
                    {
                        if (!_free[j])
                            _rhsMax[fi] -= _a[ii * k + j] * _x[j];
                    }
                }

                if (!CholeskySolve(_affMax, _rhsMax, freeCount))
                {
                    _free[bestIdx] = false;
                    break;
                }

                // Result is in _cholX[0..freeCount-1]
                var allInBounds = true;
                var minAlpha = 1.0;
                var worstFreeIdx = -1;
                for (var fi = 0; fi < freeCount; fi++)
                {
                    if (_cholX[fi] < 0.0 || _cholX[fi] > 1.0)
                    {
                        allInBounds = false;
                        var xi = _x[_freeIdx[fi]];
                        double alpha;
                        if (_cholX[fi] < 0.0)
                            alpha = xi / (xi - _cholX[fi]);
                        else
                            alpha = (1.0 - xi) / (_cholX[fi] - xi);
                        if (alpha < minAlpha)
                        {
                            minAlpha = alpha;
                            worstFreeIdx = fi;
                        }
                    }
                }

                if (allInBounds)
                {
                    for (var fi = 0; fi < freeCount; fi++)
                        _x[_freeIdx[fi]] = _cholX[fi];
                    break;
                }

                for (var fi = 0; fi < freeCount; fi++)
                {
                    var idx = _freeIdx[fi];
                    _x[idx] += minAlpha * (_cholX[fi] - _x[idx]);
                }

                var worstGlobalIdx = _freeIdx[worstFreeIdx];
                _x[worstGlobalIdx] = _cholX[worstFreeIdx] < 0.0 ? 0.0 : 1.0;
                _free[worstGlobalIdx] = false;
            }
        }

        for (var i = 0; i < k; i++)
        {
            _prevWeights[i] = _x[i];
            _result[i] = (float)_x[i];
        }
        return _result;
    }

    public void ResetTemporal() => Array.Clear(_prevWeights);

    public double[] SaveTemporal()
    {
        var saved = new double[_prevWeights.Length];
        Array.Copy(_prevWeights, saved, _prevWeights.Length);
        return saved;
    }

    public void RestoreTemporal(double[] saved) =>
        Array.Copy(saved, _prevWeights, _prevWeights.Length);

    /// <summary>
    ///     Cholesky solve using pre-allocated instance buffers _cholL, _cholY, _cholX.
    ///     On success, result is in _cholX[0..n-1]. Returns false if decomposition fails.
    /// </summary>
    private bool CholeskySolve(double[] a, double[] b, int n)
    {
        // Copy a into _cholL
        Array.Copy(a, _cholL, n * n);

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = 0.0;
                for (var p = 0; p < j; p++)
                    sum += _cholL[i * n + p] * _cholL[j * n + p];

                if (i == j)
                {
                    var diag = _cholL[i * n + i] - sum;
                    if (diag <= 0)
                        return false;
                    _cholL[i * n + j] = Math.Sqrt(diag);
                }
                else
                {
                    _cholL[i * n + j] = (_cholL[i * n + j] - sum) / _cholL[j * n + j];
                }
            }
            for (var j = i + 1; j < n; j++)
                _cholL[i * n + j] = 0;
        }

        // Forward substitution: L @ y = b
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < i; j++)
                sum += _cholL[i * n + j] * _cholY[j];
            _cholY[i] = (b[i] - sum) / _cholL[i * n + i];
        }

        // Back substitution: L^T @ x = y
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = 0.0;
            for (var j = i + 1; j < n; j++)
                sum += _cholL[j * n + i] * _cholX[j];
            _cholX[i] = (_cholY[i] - sum) / _cholL[i * n + i];
        }

        return true;
    }
}
