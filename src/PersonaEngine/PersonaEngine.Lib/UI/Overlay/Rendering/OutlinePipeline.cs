using System.Numerics;
using System.Runtime.InteropServices;
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
    private const string ShaderSource = """
        cbuffer OutlineCB : register(b0) {
            float2 Viewport;
            float  Radius;
            float  Thickness;
            float4 Color;
            float  Alpha;
            float3 _pad0;
            float4 Halo;
            float  HaloRadius;
            float3 _pad1;
        };

        struct VSIn { float2 pos : POSITION; };
        struct VSOut { float4 pos : SV_Position; };

        VSOut VSMain(VSIn i) {
            VSOut o;
            o.pos = float4(i.pos, 0.0, 1.0);
            return o;
        }

        float4 PSMain(VSOut i) : SV_Target {
            // SV_Position is in pixel coordinates for the pixel shader.
            float2 local = i.pos.xy - Viewport * 0.5;
            float2 halfSize = Viewport * 0.5;
            float2 d = abs(local) - halfSize + float2(Radius, Radius);
            float sd = length(max(d, 0.0)) - Radius + min(max(d.x, d.y), 0.0);

            // Accent outline band — sd in [-Thickness, 0], fading at both ends
            // so the stroke antialiases without jaggies on rounded corners.
            float inner = smoothstep(-Thickness - 1.0, -Thickness, sd);
            float outer = 1.0 - smoothstep(-1.0, 0.0, sd);
            float strokeMask = inner * outer;

            // Dark halo just INSIDE the outline — the window edge is at
            // sd = 0 and everything outside is transparent, so the halo has
            // to live on the inner side to remain visible. It peaks right at
            // the outline and fades toward the window interior, guaranteeing
            // contrast against any avatar colour.
            float haloFalloff = 1.0 - saturate((-sd) / HaloRadius);
            float haloInside = 1.0 - smoothstep(-0.5, 0.5, sd);
            float haloMask = haloFalloff * haloInside * (1.0 - strokeMask);

            // Accent stroke in front, dark halo behind (both straight-alpha).
            float strokeA = Color.a * strokeMask;
            float haloA = Halo.a * haloMask;
            float outA = strokeA + haloA * (1.0 - strokeA);
            float3 outRGB = Color.rgb * strokeA + Halo.rgb * haloA * (1.0 - strokeA);

            outA *= Alpha;
            outRGB *= Alpha;
            return float4(outRGB, outA);
        }
        """;

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
