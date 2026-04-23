using System.Numerics;
using System.Runtime.InteropServices;
using PersonaEngine.Lib.UI.Rendering.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Draws a rounded-rect SDF outline around the entire overlay viewport.
///     Uses a fullscreen clip-space quad; the pixel shader evaluates the SDF
///     against <c>SV_Position</c> to paint a thin accent ring whenever
///     hover chrome is visible.
/// </summary>
internal sealed class OutlinePipeline(ID3D11Device device, ID3D11DeviceContext context)
    : IDisposable
{
    private static readonly string ShaderSource = ShaderRegistry.GetSource(
        "hlsl/overlay/outline.hlsl"
    );

    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _cb;

    public void Build()
    {
        var vsBytecode = ShaderCompiler.Compile(ShaderSource, "VSMain", "vs_5_0", "OverlayOutline");
        var psBytecode = ShaderCompiler.Compile(ShaderSource, "PSMain", "ps_5_0", "OverlayOutline");

        _vs = device.CreateVertexShader(vsBytecode.Span);
        _ps = device.CreatePixelShader(psBytecode.Span);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode.Span);

        // Fullscreen triangle strip in clip space — same corners as QuadPipeline,
        // but we don't need UVs here because the pixel shader reads SV_Position
        // directly for its SDF evaluation.
        _vertexBuffer = FullscreenQuad.CreateClipSpaceTriangleStrip(device);

        var cbDesc = new BufferDescription
        {
            ByteWidth = (uint)((Marshal.SizeOf<OutlineCBData>() + 15) & ~15),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _cb = device.CreateBuffer(cbDesc);
    }

    public void Draw(int viewportW, int viewportH, float alpha)
    {
        if (
            _vs is null
            || _ps is null
            || _inputLayout is null
            || _vertexBuffer is null
            || _cb is null
        )
        {
            return;
        }

        var cb = new OutlineCBData
        {
            Viewport = new Vector2(viewportW, viewportH),
            Radius = ChromeTheme.OutlineCornerRadius,
            Thickness = ChromeTheme.OutlineThickness,
            Color = ChromeTheme.OutlineColor,
            Alpha = alpha,
            Halo = ChromeTheme.OutlineHaloColor,
            HaloRadius = ChromeTheme.OutlineHaloRadius,
        };
        context.UpdateSubresource(in cb, _cb);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.IASetVertexBuffer(0, _vertexBuffer, FullscreenQuad.VertexStride, 0);

        context.VSSetShader(_vs);
        context.PSSetShader(_ps);
        context.PSSetConstantBuffer(0, _cb);

        context.Draw(FullscreenQuad.VertexCount, 0);
    }

    public void Dispose()
    {
        _vs?.Dispose();
        _ps?.Dispose();
        _inputLayout?.Dispose();
        _vertexBuffer?.Dispose();
        _cb?.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
file struct OutlineCBData
{
    public Vector2 Viewport;
    public float Radius;
    public float Thickness;
    public Vector4 Color;
    public float Alpha;
    public Vector3 _pad0;
    public Vector4 Halo;
    public float HaloRadius;
    public Vector3 _pad1;
}
