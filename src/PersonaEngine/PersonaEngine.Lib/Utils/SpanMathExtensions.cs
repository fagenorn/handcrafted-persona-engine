using System.Runtime.CompilerServices;

namespace PersonaEngine.Lib.Utils;

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
}
