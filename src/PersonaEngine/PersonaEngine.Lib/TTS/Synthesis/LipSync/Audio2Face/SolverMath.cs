namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Shared math utilities for blendshape solvers. Centralizes bounding-box
///     diagonal computation, transpose/DtD products, and regularization
///     application so that BVLS and PGD solvers stay DRY.
/// </summary>
internal static class SolverMath
{
    /// <summary>Audio2Face SDK default L2 (diagonal) regularization multiplier.</summary>
    internal const double L2Multiplier = 10.0;

    /// <summary>Audio2Face SDK default L1 (full-matrix) regularization multiplier.</summary>
    internal const double L1Multiplier = 0.25;

    /// <summary>Audio2Face SDK default temporal (diagonal) regularization multiplier.</summary>
    internal const double TemporalMultiplier = 100.0;

    /// <summary>
    ///     Computes the bounding-box diagonal of vertices stored as flat [N*3].
    /// </summary>
    /// <param name="neutralFlat3">Flat array of vertex positions [x0,y0,z0, x1,y1,z1, ...].</param>
    /// <returns>Euclidean length of the bounding-box diagonal, or 0 if fewer than 1 vertex.</returns>
    public static double ComputeBoundingBoxDiagonal(ReadOnlySpan<float> neutralFlat3)
    {
        var nVerts = neutralFlat3.Length / 3;

        if (nVerts == 0)
        {
            return 0.0;
        }

        double minX = double.MaxValue,
            minY = double.MaxValue,
            minZ = double.MaxValue;
        double maxX = double.MinValue,
            maxY = double.MinValue,
            maxZ = double.MinValue;

        for (var i = 0; i < nVerts; i++)
        {
            var x = (double)neutralFlat3[i * 3];
            var y = (double)neutralFlat3[i * 3 + 1];
            var z = (double)neutralFlat3[i * 3 + 2];

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

        var dx = maxX - minX;
        var dy = maxY - minY;
        var dz = maxZ - minZ;

        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    ///     Computes D^T [K x V] from D [V x K] row-major.
    /// </summary>
    /// <param name="d">Source matrix D in [V x K] row-major layout.</param>
    /// <param name="v">Number of rows in D (masked vertex components).</param>
    /// <param name="k">Number of columns in D (active blendshapes).</param>
    /// <param name="dt">Destination buffer for D^T in [K x V] row-major layout. Must be at least k*v elements.</param>
    public static void ComputeTranspose(ReadOnlySpan<float> d, int v, int k, Span<double> dt)
    {
        for (var vi = 0; vi < v; vi++)
        {
            for (var ki = 0; ki < k; ki++)
            {
                dt[ki * v + vi] = d[vi * k + ki];
            }
        }
    }

    /// <summary>
    ///     Computes D^T @ D [K x K] from D^T [K x V].
    /// </summary>
    /// <param name="dt">D^T in [K x V] row-major layout.</param>
    /// <param name="k">Number of rows in D^T (active blendshapes).</param>
    /// <param name="v">Number of columns in D^T (masked vertex components).</param>
    /// <param name="dtd">Destination buffer for DtD in [K x K] row-major layout. Must be at least k*k elements.</param>
    public static void ComputeDtD(ReadOnlySpan<double> dt, int k, int v, Span<double> dtd)
    {
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                var sum = 0.0;

                for (var p = 0; p < v; p++)
                {
                    sum += dt[i * v + p] * dt[j * v + p];
                }

                dtd[i * k + j] = sum;
            }
        }
    }

    /// <summary>
    ///     Applies L2 (diagonal), L1 (full), and temporal (diagonal) regularization to a [K x K] matrix.
    /// </summary>
    /// <param name="a">The [K x K] row-major matrix to modify in-place.</param>
    /// <param name="k">Matrix dimension.</param>
    /// <param name="scale">Bounding-box scale factor (bbSize / templateBBSize)^2.</param>
    /// <param name="strengthL2">L2 regularization strength.</param>
    /// <param name="strengthL1">L1 regularization strength.</param>
    /// <param name="strengthTemporal">Temporal smoothing regularization strength.</param>
    /// <returns>The temporal scale value (TemporalMultiplier * scale * strengthTemporal).</returns>
    public static double ApplyRegularization(
        Span<double> a,
        int k,
        double scale,
        float strengthL2,
        float strengthL1,
        float strengthTemporal
    )
    {
        var l2Weight = L2Multiplier * scale * strengthL2;
        var l1Weight = L1Multiplier * scale * strengthL1;
        var temporalScale = TemporalMultiplier * scale * strengthTemporal;

        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                a[i * k + j] += l1Weight;

                if (i == j)
                {
                    a[i * k + j] += l2Weight + temporalScale;
                }
            }
        }

        return temporalScale;
    }
}
