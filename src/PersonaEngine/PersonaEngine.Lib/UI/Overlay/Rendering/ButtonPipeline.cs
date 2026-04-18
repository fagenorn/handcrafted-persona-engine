using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Draws a single rounded-rect button via SDF. A 6-vertex quad in pixel
///     space carries a local-offset attribute that drives the SDF evaluation
///     in the pixel shader.
/// </summary>
internal sealed class ButtonPipeline(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    private const int VerticesPerRect = 6;

    // Button vertex layout: position.xy (screen px) + local.xy (offset from center).
    private const int ButtonVertexFloats = 4;
    private const int ButtonVertexBytes = ButtonVertexFloats * sizeof(float);
    private const int ButtonBufferFloats = VerticesPerRect * ButtonVertexFloats;

    private const string ShaderSource = """
        cbuffer ButtonCB : register(b0) {
            float2 Viewport;
            float2 HalfSize;
            float  Radius;
            float  Alpha;
            float2 _pad0;
            float4 Fill;
            float4 Border;
            float4 Halo;
            float  HaloRadius;
            float3 _pad1;
        };

        struct VSIn {
            float2 pos   : POSITION;
            float2 local : TEXCOORD0;
        };

        struct VSOut {
            float4 pos   : SV_Position;
            float2 local : TEXCOORD0;
        };

        VSOut VSMain(VSIn i) {
            VSOut o;
            float2 ndc = (i.pos / Viewport) * 2.0 - 1.0;
            o.pos = float4(ndc.x, -ndc.y, 0.0, 1.0);
            o.local = i.local;
            return o;
        }

        float4 PSMain(VSOut i) : SV_Target {
            float2 d = abs(i.local) - HalfSize + float2(Radius, Radius);
            float sd = length(max(d, 0.0)) - Radius + min(max(d.x, d.y), 0.0);

            // Interior fill + accent border.
            float fillMask = 1.0 - smoothstep(-1.0, 0.0, sd);
            float borderIn = smoothstep(-2.5, -1.5, sd);
            float borderOut = 1.0 - smoothstep(-1.0, 0.0, sd);
            float borderMask = borderIn * borderOut;
            float4 c = lerp(Fill, Border, borderMask);

            // Outer dark halo — a ring of shadow just outside the button so the
            // silhouette stays readable against any background. Peaks near the
            // edge and falls off linearly to the outer halo radius.
            // Full strength at sd=0 falling to zero at sd=HaloRadius; masked
            // to only apply outside the shape to avoid darkening the fill.
            float haloFalloff = 1.0 - saturate(sd / HaloRadius);
            float haloOutside = smoothstep(-0.5, 0.5, sd);
            float haloMask = haloFalloff * haloOutside;

            // Porter-Duff "over": fill layer in front of halo layer.
            // Both colours are in straight alpha; output is premultiplied.
            float fillA = c.a * fillMask;
            float haloA = Halo.a * haloMask;
            float outA = fillA + haloA * (1.0 - fillA);
            float3 outRGB = c.rgb * fillA + Halo.rgb * haloA * (1.0 - fillA);

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
        var vsBytecode = ShaderCompiler.Compile(ShaderSource, "VSMain", "vs_5_0", "OverlayButton");
        var psBytecode = ShaderCompiler.Compile(ShaderSource, "PSMain", "ps_5_0", "OverlayButton");

        _vs = device.CreateVertexShader(vsBytecode.Span);
        _ps = device.CreatePixelShader(psBytecode.Span);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 2 * sizeof(float), 0),
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode.Span);

        var vbDesc = new BufferDescription
        {
            ByteWidth = ButtonBufferFloats * sizeof(float),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
        };
        _vertexBuffer = device.CreateBuffer(vbDesc);

        var cbDesc = new BufferDescription
        {
            ByteWidth = (uint)((Marshal.SizeOf<ButtonCBData>() + 15) & ~15),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _cb = device.CreateBuffer(cbDesc);
    }

    public void Draw(int viewportW, int viewportH, int x, int y, float alpha, bool hot)
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

        UpdateVertexBuffer(x, y);

        var cb = new ButtonCBData
        {
            Viewport = new Vector2(viewportW, viewportH),
            HalfSize = new Vector2(ChromeTheme.ButtonSize * 0.5f, ChromeTheme.ButtonSize * 0.5f),
            Radius = ChromeTheme.ButtonCornerRadius,
            Alpha = alpha,
            Fill = hot ? ChromeTheme.ButtonBgHot : ChromeTheme.ButtonBg,
            Border = hot ? ChromeTheme.ButtonBorderHot : ChromeTheme.ButtonBorder,
            Halo = ChromeTheme.ButtonHaloColor,
            HaloRadius = ChromeTheme.ButtonHaloRadius,
        };
        context.UpdateSubresource(in cb, _cb);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, _vertexBuffer, ButtonVertexBytes, 0);

        context.VSSetShader(_vs);
        context.VSSetConstantBuffer(0, _cb);
        context.PSSetShader(_ps);
        context.PSSetConstantBuffer(0, _cb);

        context.Draw(VerticesPerRect, 0);
    }

    public void Dispose()
    {
        _vs?.Dispose();
        _ps?.Dispose();
        _inputLayout?.Dispose();
        _vertexBuffer?.Dispose();
        _cb?.Dispose();
    }

    private void UpdateVertexBuffer(int x, int y)
    {
        if (_vertexBuffer is null)
        {
            return;
        }

        var halfSize = ChromeTheme.ButtonSize * 0.5f;
        // Expand the rendering quad beyond the button's rectangle so the pixel
        // shader can paint the outer halo (which extends ~2.5 px outside the
        // rounded-rect edge). Without this pad, the halo would be clipped to
        // the button's axis-aligned bounding box and only visible near the
        // rounded corners where shape < bounds.
        var pad = (float)Math.Ceiling(ChromeTheme.ButtonHaloRadius) + 1f;

        float x0 = x - pad;
        float y0 = y - pad;
        float x1 = x + ChromeTheme.ButtonSize + pad;
        float y1 = y + ChromeTheme.ButtonSize + pad;

        float lx0 = -halfSize - pad;
        float ly0 = -halfSize - pad;
        float lx1 = halfSize + pad;
        float ly1 = halfSize + pad;

        Span<float> verts = stackalloc float[ButtonBufferFloats];
        // Triangle 1: (x0, y0), (x1, y0), (x1, y1).
        verts[0] = x0;
        verts[1] = y0;
        verts[2] = lx0;
        verts[3] = ly0;
        verts[4] = x1;
        verts[5] = y0;
        verts[6] = lx1;
        verts[7] = ly0;
        verts[8] = x1;
        verts[9] = y1;
        verts[10] = lx1;
        verts[11] = ly1;
        // Triangle 2: (x0, y0), (x1, y1), (x0, y1).
        verts[12] = x0;
        verts[13] = y0;
        verts[14] = lx0;
        verts[15] = ly0;
        verts[16] = x1;
        verts[17] = y1;
        verts[18] = lx1;
        verts[19] = ly1;
        verts[20] = x0;
        verts[21] = y1;
        verts[22] = lx0;
        verts[23] = ly1;

        var mapped = context.Map(_vertexBuffer, MapMode.WriteDiscard);
        try
        {
            unsafe
            {
                var dst = new Span<float>((void*)mapped.DataPointer, ButtonBufferFloats);
                verts.CopyTo(dst);
            }
        }
        finally
        {
            context.Unmap(_vertexBuffer);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
file struct ButtonCBData
{
    public Vector2 Viewport;
    public Vector2 HalfSize;
    public float Radius;
    public float Alpha;
    public Vector2 _pad0;
    public Vector4 Fill;
    public Vector4 Border;
    public Vector4 Halo;
    public float HaloRadius;
    public Vector3 _pad1;
}
