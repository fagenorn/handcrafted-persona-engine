using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Shared helper for fullscreen clip-space triangle-strip vertex buffers.
///     Used by pipelines that evaluate SDF in the pixel shader via SV_Position
///     and need no per-vertex UVs — just four clip-space corners.
/// </summary>
internal static class FullscreenQuad
{
    internal const int VertexStride = 2 * sizeof(float);
    internal const int VertexCount = 4;

    /// <summary>
    ///     Creates an immutable vertex buffer with 4 clip-space vec2 vertices
    ///     forming a triangle strip: [-1,-1], [1,-1], [-1,1], [1,1].
    /// </summary>
    internal static ID3D11Buffer CreateClipSpaceTriangleStrip(ID3D11Device device)
    {
        float[] quad = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];
        var desc = new BufferDescription
        {
            ByteWidth = (uint)(quad.Length * sizeof(float)),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        return device.CreateBuffer(quad.AsSpan(), desc);
    }
}
