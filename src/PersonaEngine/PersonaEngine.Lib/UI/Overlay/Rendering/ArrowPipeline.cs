using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Smooth SDF-based double-headed arrow drawn on a fullscreen quad. Evaluated
///     in a rotated local coordinate frame so the arrow is axis-aligned in shader
///     space — no pixel-grid artifacts like the rect-based approach suffered on
///     45-degree diagonals.
/// </summary>
internal sealed class ArrowPipeline(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    // Arrow geometry, tuned to sit cleanly inside the 30-px button with
    // ~6 px breathing room to each corner. Measurements are in button-
    // local pixels; the shader uses them in its rotated coordinate frame.
    private const float HalfLength = 11.0f; // tip-to-tip spans 22 px along the diagonal
    private const float StemHalfLength = 6.0f; // stem ends 5 px before each tip
    private const float StemHalfThick = 1.5f; // 3-px perpendicular stem thickness, matches drag bars
    private const float HeadHalfBase = 3.5f; // 7-px arrowhead base — clearly flares beyond stem

    // +pi/4 rotates the arrow to point NW<->SE. This matches the IDC_SIZENWSE
    // cursor shown on hover: the resize button sits in the bottom-right
    // corner, so dragging it stretches the bottom-right corner (top-left
    // pinned) — the NW-SE diagonal.
    private const float DiagonalAngle = 0.7853982f;

    private const string ShaderSource = """
        // Smooth SDF-based double-headed arrow. Evaluated in a rotated
        // local coordinate frame so the arrow is axis-aligned in shader
        // space — no pixel-grid artifacts like the rect-based approach
        // suffered on 45° diagonals. SDF primitives (line segment +
        // isosceles triangle) courtesy of Inigo Quilez,
        // https://iquilezles.org/articles/distfunctions2d/ .
        cbuffer ArrowCB : register(b0) {
            float2 Viewport;        // swap-chain size, used only by VS
            float2 Center;          // button center in pixel coords
            float  Angle;           // arrow axis angle in radians (0 = +x)
            float  HalfLength;      // half the tip-to-tip distance
            float  StemHalfLength;  // half the stem length (tip-less portion)
            float  StemHalfThick;   // half the stem thickness (perpendicular)
            float  HeadHalfBase;    // half the arrowhead base width
            float3 _pad0;
            float4 Color;
            float  Alpha;
            float3 _pad1;
        };

        struct VSIn { float2 pos : POSITION; };
        struct VSOut { float4 pos : SV_Position; };

        VSOut VSMain(VSIn i) {
            VSOut o;
            o.pos = float4(i.pos, 0.0, 1.0);
            return o;
        }

        // Distance from point p to line segment from a to b.
        float sdSegment(float2 p, float2 a, float2 b) {
            float2 pa = p - a;
            float2 ba = b - a;
            float h = saturate(dot(pa, ba) / dot(ba, ba));
            return length(pa - ba * h);
        }

        // Signed distance to an isosceles triangle with apex at origin,
        // growing in +y direction. q.x = half base width, q.y = height.
        float sdTriangleIsosceles(float2 p, float2 q) {
            p.x = abs(p.x);
            float2 a = p - q * saturate(dot(p, q) / dot(q, q));
            float2 b = p - q * float2(saturate(p.x / q.x), 1.0);
            float s = -sign(q.y);
            float2 d = min(
                float2(dot(a, a), s * (p.x * q.y - p.y * q.x)),
                float2(dot(b, b), s * (p.y - q.y))
            );
            return -sqrt(d.x) * sign(d.y);
        }

        float4 PSMain(VSOut i) : SV_Target {
            // Transform the pixel into arrow-local space: translate to button
            // center, then rotate by -Angle so the arrow axis becomes +x.
            float2 p = i.pos.xy - Center;
            float c = cos(-Angle);
            float s = sin(-Angle);
            float2 local = float2(c * p.x - s * p.y, s * p.x + c * p.y);

            float headLen = HalfLength - StemHalfLength;

            // Stem: a capsule along the x-axis from -StemHalfLength to +StemHalfLength.
            float stem = sdSegment(
                local,
                float2(-StemHalfLength, 0.0),
                float2( StemHalfLength, 0.0)
            ) - StemHalfThick;

            // Right arrowhead. sdTriangleIsosceles wants its apex at origin
            // growing +y; we want apex at (+HalfLength, 0) growing -x in
            // world-local, so map world (x, y) → local ((halfLen - x), y)
            // via a 90° rotation plus translation.
            float2 rotR = float2(local.y, HalfLength - local.x);
            float headR = sdTriangleIsosceles(rotR, float2(HeadHalfBase, headLen));

            // Left arrowhead: mirror — apex at (-HalfLength, 0).
            float2 rotL = float2(local.y, local.x + HalfLength);
            float headL = sdTriangleIsosceles(rotL, float2(HeadHalfBase, headLen));

            // Union of stem + both heads (SDFs combine via min for union).
            float d = min(min(stem, headR), headL);

            // 1-pixel antialiased band around the zero-level set.
            float mask = 1.0 - saturate(d + 0.5);
            float a = Color.a * mask * Alpha;
            return float4(Color.rgb * a, a);
        }
        """;

    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _cb;

    public void Build()
    {
        var vsBytecode = ShaderCompiler.Compile(ShaderSource, "VSMain", "vs_5_0", "OverlayArrow");
        var psBytecode = ShaderCompiler.Compile(ShaderSource, "PSMain", "ps_5_0", "OverlayArrow");

        _vs = device.CreateVertexShader(vsBytecode.Span);
        _ps = device.CreatePixelShader(psBytecode.Span);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode.Span);

        // Fullscreen triangle-strip quad in clip space — same as OutlinePipeline.
        // SV_Position in the PS then carries screen-space pixels which we
        // transform into button-local arrow space.
        _vertexBuffer = FullscreenQuad.CreateClipSpaceTriangleStrip(device);

        var cbDesc = new BufferDescription
        {
            ByteWidth = (uint)((Marshal.SizeOf<ArrowCBData>() + 15) & ~15),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _cb = device.CreateBuffer(cbDesc);
    }

    public void Draw(int viewportW, int viewportH, int buttonX, int buttonY, float alpha, bool hot)
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

        var cb = new ArrowCBData
        {
            Viewport = new Vector2(viewportW, viewportH),
            Center = new Vector2(
                buttonX + ChromeTheme.ButtonSize * 0.5f,
                buttonY + ChromeTheme.ButtonSize * 0.5f
            ),
            Angle = DiagonalAngle,
            HalfLength = HalfLength,
            StemHalfLength = StemHalfLength,
            StemHalfThick = StemHalfThick,
            HeadHalfBase = HeadHalfBase,
            Color = hot ? ChromeTheme.IconColorHot : ChromeTheme.IconColor,
            Alpha = alpha,
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
file struct ArrowCBData
{
    public Vector2 Viewport;
    public Vector2 Center;
    public float Angle;
    public float HalfLength;
    public float StemHalfLength;
    public float StemHalfThick;
    public float HeadHalfBase;
    public Vector3 _pad0;
    public Vector4 Color;
    public float Alpha;
    public Vector3 _pad1;
}
