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
    private readonly double[] _dt;
    private readonly int _activeCount;
    private readonly int _maskedPositionCount;
    private readonly double _temporalScale;
    private readonly double[] _prevWeights;

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

        _dt = new double[k * v];
        for (var i = 0; i < k; i++)
        for (var j = 0; j < v; j++)
            _dt[i * v + j] = deltaMatrix[j * k + i];

        var dtd = new double[k * k];
        for (var i = 0; i < k; i++)
        for (var j = 0; j < k; j++)
        {
            var sum = 0.0;
            for (var p = 0; p < v; p++)
                sum += _dt[i * v + p] * _dt[j * v + p];
            dtd[i * k + j] = sum;
        }

        var neutral3 = maskedNeutralFlat;
        var nVerts = neutral3.Length / 3;
        float minX = float.MaxValue,
            minY = float.MaxValue,
            minZ = float.MaxValue;
        float maxX = float.MinValue,
            maxY = float.MinValue,
            maxZ = float.MinValue;
        for (var i = 0; i < nVerts; i++)
        {
            var x = neutral3[i * 3];
            var y = neutral3[i * 3 + 1];
            var z = neutral3[i * 3 + 2];
            if (x < minX)
                minX = x;
            if (x > maxX)
                maxX = x;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
            if (z < minZ)
                minZ = z;
            if (z > maxZ)
                maxZ = z;
        }
        var bbSize = Math.Sqrt(
            (maxX - minX) * (maxX - minX)
                + (maxY - minY) * (maxY - minY)
                + (maxZ - minZ) * (maxZ - minZ)
        );
        var scale = (bbSize / templateBBSize) * (bbSize / templateBBSize);

        _a = new double[k * k];
        Array.Copy(dtd, _a, k * k);
        var l2Weight = 10.0 * scale * strengthL2;
        var l1Weight = 0.25 * scale * strengthL1;
        _temporalScale = 100.0 * scale * strengthTemporal;

        for (var i = 0; i < k; i++)
        for (var j = 0; j < k; j++)
        {
            _a[i * k + j] += l1Weight;
            if (i == j)
                _a[i * k + j] += l2Weight + _temporalScale;
        }

        _prevWeights = new double[k];
    }

    public float[] Solve(ReadOnlySpan<float> delta)
    {
        var k = _activeCount;
        var v = _maskedPositionCount;

        var b = new double[k];
        for (var i = 0; i < k; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < v; j++)
                sum += _dt[i * v + j] * delta[j];
            b[i] = sum + _temporalScale * _prevWeights[i];
        }

        var x = new double[k];
        var free = new bool[k];

        for (var outer = 0; outer < MaxOuterIterations; outer++)
        {
            var g = new double[k];
            for (var i = 0; i < k; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < k; j++)
                    sum += _a[i * k + j] * x[j];
                g[i] = sum - b[i];
            }

            var bestIdx = -1;
            var bestViolation = Tolerance;
            for (var i = 0; i < k; i++)
            {
                if (free[i])
                    continue;
                var violation = x[i] <= 0.0 ? -g[i] : g[i];
                if (violation > bestViolation)
                {
                    bestViolation = violation;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
                break;
            free[bestIdx] = true;

            while (true)
            {
                var freeCount = 0;
                var freeIdx = new int[k];
                for (var i = 0; i < k; i++)
                    if (free[i])
                        freeIdx[freeCount++] = i;

                if (freeCount == 0)
                    break;

                var aff = new double[freeCount * freeCount];
                var rhs = new double[freeCount];

                for (var fi = 0; fi < freeCount; fi++)
                {
                    var ii = freeIdx[fi];
                    rhs[fi] = b[ii];
                    for (var fj = 0; fj < freeCount; fj++)
                        aff[fi * freeCount + fj] = _a[ii * k + freeIdx[fj]];
                    for (var j = 0; j < k; j++)
                    {
                        if (!free[j])
                            rhs[fi] -= _a[ii * k + j] * x[j];
                    }
                }

                var z = CholeskySolve(aff, rhs, freeCount);
                if (z == null)
                {
                    free[bestIdx] = false;
                    break;
                }

                var allInBounds = true;
                var minAlpha = 1.0;
                var worstFreeIdx = -1;
                for (var fi = 0; fi < freeCount; fi++)
                {
                    if (z[fi] < 0.0 || z[fi] > 1.0)
                    {
                        allInBounds = false;
                        var xi = x[freeIdx[fi]];
                        double alpha;
                        if (z[fi] < 0.0)
                            alpha = xi / (xi - z[fi]);
                        else
                            alpha = (1.0 - xi) / (z[fi] - xi);
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
                        x[freeIdx[fi]] = z[fi];
                    break;
                }

                for (var fi = 0; fi < freeCount; fi++)
                {
                    var idx = freeIdx[fi];
                    x[idx] += minAlpha * (z[fi] - x[idx]);
                }

                var worstGlobalIdx = freeIdx[worstFreeIdx];
                x[worstGlobalIdx] = z[worstFreeIdx] < 0.0 ? 0.0 : 1.0;
                free[worstGlobalIdx] = false;
            }
        }

        var result = new float[k];
        for (var i = 0; i < k; i++)
        {
            _prevWeights[i] = x[i];
            result[i] = (float)x[i];
        }
        return result;
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

    private static double[]? CholeskySolve(double[] a, double[] b, int n)
    {
        var l = new double[n * n];
        Array.Copy(a, l, n * n);

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = 0.0;
                for (var p = 0; p < j; p++)
                    sum += l[i * n + p] * l[j * n + p];

                if (i == j)
                {
                    var diag = l[i * n + i] - sum;
                    if (diag <= 0)
                        return null;
                    l[i * n + j] = Math.Sqrt(diag);
                }
                else
                {
                    l[i * n + j] = (l[i * n + j] - sum) / l[j * n + j];
                }
            }
            for (var j = i + 1; j < n; j++)
                l[i * n + j] = 0;
        }

        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < i; j++)
                sum += l[i * n + j] * y[j];
            y[i] = (b[i] - sum) / l[i * n + i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = 0.0;
            for (var j = i + 1; j < n; j++)
                sum += l[j * n + i] * x[j];
            x[i] = (y[i] - sum) / l[i * n + i];
        }

        return x;
    }
}
