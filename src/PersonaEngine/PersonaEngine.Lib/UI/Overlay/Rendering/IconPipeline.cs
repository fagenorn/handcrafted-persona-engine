using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Flat-color rectangle batch for drawing pixel-art icon glyphs. Callers
///     push rectangles via <see cref="PushRect" />, then call <see cref="Flush" />
///     to upload and draw them all in one batch.
/// </summary>
internal sealed class IconPipeline(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    // Sized for the most detailed single-button icon (the resize diagonal
    // arrow uses ~20 rects: two triangular heads plus a stepped diagonal
    // line). Icons are uploaded per-button, so this is a per-draw ceiling
    // not a combined one. 32 leaves comfortable headroom.
    private const int MaxIconRects = 32;
    private const int VerticesPerRect = 6;
    private const int IconVertexFloats = 2;
    private const int IconBufferFloats = MaxIconRects * VerticesPerRect * IconVertexFloats;

    private const string ShaderSource = """
        cbuffer IconCB : register(b0) {
            float2 Viewport;
            float2 _pad0;
            float4 Color;
            float  Alpha;
            float3 _pad1;
        };

        struct VSIn { float2 pos : POSITION; };
        struct VSOut { float4 pos : SV_Position; };

        VSOut VSMain(VSIn i) {
            VSOut o;
            float2 ndc = (i.pos / Viewport) * 2.0 - 1.0;
            o.pos = float4(ndc.x, -ndc.y, 0.0, 1.0);
            return o;
        }

        float4 PSMain(VSOut i) : SV_Target {
            float a = Color.a * Alpha;
            return float4(Color.rgb * a, a);
        }
        """;

    private readonly float[] _iconScratch = new float[IconBufferFloats];

    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _cb;

    private int _rectCount;

    public void Build()
    {
        var vsBytecode = ShaderCompiler.Compile(ShaderSource, "VSMain", "vs_5_0", "OverlayIcon");
        var psBytecode = ShaderCompiler.Compile(ShaderSource, "PSMain", "ps_5_0", "OverlayIcon");

        _vs = device.CreateVertexShader(vsBytecode.Span);
        _ps = device.CreatePixelShader(psBytecode.Span);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode.Span);

        var vbDesc = new BufferDescription
        {
            ByteWidth = IconBufferFloats * sizeof(float),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
        };
        _vertexBuffer = device.CreateBuffer(vbDesc);

        var cbDesc = new BufferDescription
        {
            ByteWidth = (uint)((Marshal.SizeOf<IconCBData>() + 15) & ~15),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _cb = device.CreateBuffer(cbDesc);
    }

    /// <summary>Resets the rectangle batch. Call before pushing a new icon's rects.</summary>
    public void Clear()
    {
        _rectCount = 0;
    }

    /// <summary>
    ///     Appends a flat-colour rectangle to the current batch. Silently drops
    ///     rectangles beyond <c>MaxIconRects</c>.
    /// </summary>
    public void PushRect(int x, int y, int w, int h)
    {
        if (_rectCount >= MaxIconRects)
        {
            return;
        }

        var idx = _rectCount * VerticesPerRect * IconVertexFloats;
        float x0 = x;
        float y0 = y;
        float x1 = x + w;
        float y1 = y + h;

        _iconScratch[idx + 0] = x0;
        _iconScratch[idx + 1] = y0;
        _iconScratch[idx + 2] = x1;
        _iconScratch[idx + 3] = y0;
        _iconScratch[idx + 4] = x1;
        _iconScratch[idx + 5] = y1;

        _iconScratch[idx + 6] = x0;
        _iconScratch[idx + 7] = y0;
        _iconScratch[idx + 8] = x1;
        _iconScratch[idx + 9] = y1;
        _iconScratch[idx + 10] = x0;
        _iconScratch[idx + 11] = y1;

        _rectCount++;
    }

    /// <summary>
    ///     Uploads the accumulated rectangles and draws them in one batch.
    /// </summary>
    public void Flush(int viewportW, int viewportH, float alpha, bool hot)
    {
        if (_rectCount == 0)
        {
            return;
        }

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

        UploadBuffer(_rectCount);

        var cb = new IconCBData
        {
            Viewport = new Vector2(viewportW, viewportH),
            Color = hot ? ChromeTheme.IconColorHot : ChromeTheme.IconColor,
            Alpha = alpha,
        };
        context.UpdateSubresource(in cb, _cb);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, _vertexBuffer, IconVertexFloats * sizeof(float), 0);

        context.VSSetShader(_vs);
        context.VSSetConstantBuffer(0, _cb);
        context.PSSetShader(_ps);
        context.PSSetConstantBuffer(0, _cb);

        context.Draw((uint)(_rectCount * VerticesPerRect), 0);
    }

    public void Dispose()
    {
        _vs?.Dispose();
        _ps?.Dispose();
        _inputLayout?.Dispose();
        _vertexBuffer?.Dispose();
        _cb?.Dispose();
    }

    private void UploadBuffer(int rectCount)
    {
        if (_vertexBuffer is null)
        {
            return;
        }

        var usedFloats = rectCount * VerticesPerRect * IconVertexFloats;
        var mapped = context.Map(_vertexBuffer, MapMode.WriteDiscard);
        try
        {
            unsafe
            {
                var dst = new Span<float>((void*)mapped.DataPointer, IconBufferFloats);
                _iconScratch.AsSpan(0, usedFloats).CopyTo(dst);
            }
        }
        finally
        {
            context.Unmap(_vertexBuffer);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 16)]
file struct IconCBData
{
    public Vector2 Viewport;
    public Vector2 _pad0;
    public Vector4 Color;
    public float Alpha;
    public Vector3 _pad1;
}
