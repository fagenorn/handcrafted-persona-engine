using System.Runtime.CompilerServices;

namespace PersonaEngine.Lib.Utils.Numerics;

public static class SpanMathExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ArgMax(this ReadOnlySpan<float> values)
    {
        var bestIdx = 0;
        var bestVal = values[0];
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > bestVal)
            {
                bestVal = values[i];
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    /// <summary>
    ///     Applies numerically stable log-softmax in-place: logits[i] = log(softmax(logits[i])).
    /// </summary>
    public static void LogSoftmaxInPlace(Span<float> logits)
    {
        var max = float.NegativeInfinity;
        for (var i = 0; i < logits.Length; i++)
        {
            if (logits[i] > max)
            {
                max = logits[i];
            }
        }

        var sumExp = 0f;
        for (var i = 0; i < logits.Length; i++)
        {
            sumExp += MathF.Exp(logits[i] - max);
        }

        var logSumExp = max + MathF.Log(sumExp);
        for (var i = 0; i < logits.Length; i++)
        {
            logits[i] -= logSumExp;
        }
    }
}
