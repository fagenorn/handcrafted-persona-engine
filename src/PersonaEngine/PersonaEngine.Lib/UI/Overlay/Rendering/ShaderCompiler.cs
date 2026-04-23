using System.Runtime.InteropServices;
using Vortice.D3DCompiler;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Shared HLSL compile helper used by all overlay D3D11 pipelines. Wraps
///     <see cref="Compiler.Compile" /> and copies the bytecode out of the
///     native blob before disposal. Source is assumed pre-validated by
///     <c>ShaderRegistry</c> (ASCII-only, includes expanded).
/// </summary>
internal static class ShaderCompiler
{
    internal static ReadOnlyMemory<byte> Compile(
        string source,
        string entryPoint,
        string profile,
        string name
    )
    {
        var hr = Compiler.Compile(
            source,
            entryPoint,
            name,
            profile,
            out var byteCode,
            out var errorBlob
        );

        using (errorBlob)
        using (byteCode)
        {
            if (hr.Failure || byteCode is null)
            {
                var message = errorBlob?.AsString() ?? hr.ToString();
                throw new InvalidOperationException(
                    $"{name} shader compile failed ({entryPoint}/{profile}): {message}"
                );
            }

            var bytes = new byte[byteCode.BufferSize];
            Marshal.Copy(byteCode.BufferPointer, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
