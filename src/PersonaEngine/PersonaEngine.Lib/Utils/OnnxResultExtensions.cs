using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.Utils;

internal static class OnnxResultExtensions
{
    public static Tensor<float> MoveNextReturnValue(
        this IEnumerator<DisposableNamedOnnxValue> it,
        string expectedName
    )
    {
        while (it.MoveNext())
        {
            var cur = it.Current;
            if (cur.Name == expectedName)
                return (Tensor<float>)cur.Value;
        }
        throw new InvalidOperationException($"ONNX output '{expectedName}' not found.");
    }
}
