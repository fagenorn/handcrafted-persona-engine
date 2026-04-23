using PersonaEngine.Lib.UI.Rendering.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Draws a Spout-provided <see cref="ID3D11ShaderResourceView" /> across the
///     entire render target as a single triangle strip. The pixel shader
///     premultiplies RGB by A on output so the result is compatible with the
///     composition swap chain's <c>DXGI_ALPHA_MODE_PREMULTIPLIED</c> format.
///
///     Spout senders write their shared texture in DirectX orientation (Y-down)
///     and we sample with DirectX convention on the receive side, so no V-flip
///     is needed — unlike the prior GL path which had to flip.
/// </summary>
public sealed class QuadPipeline : IDisposable
{
    // Fullscreen triangle strip with interleaved position+UV. Clip-space corners
    // with DX-convention UVs (V=0 at top). Four vertices drive two triangles.
    private static readonly float[] QuadVertices =
    [
        // pos.xy, uv.xy
        -1f,
        -1f,
        0f,
        1f, // bottom-left
        1f,
        -1f,
        1f,
        1f, // bottom-right
        -1f,
        1f,
        0f,
        0f, // top-left
        1f,
        1f,
        1f,
        0f, // top-right
    ];

    private static readonly string ShaderSource = ShaderRegistry.GetSource(
        "hlsl/overlay/quad.hlsl"
    );

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;

    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11SamplerState? _sampler;
    private ID3D11BlendState? _blendDisabled;
    private ID3D11RasterizerState? _rasterizer;

    public QuadPipeline(ID3D11Device device, ID3D11DeviceContext context)
    {
        _device = device;
        _context = context;
        BuildShaders();
        BuildVertexBuffer();
        BuildSamplerAndBlend();
        BuildRasterizer();
    }

    /// <summary>
    ///     Draws the provided SRV fullscreen into the currently-bound RTV. The
    ///     caller is responsible for setting the RTV + viewport beforehand.
    ///     Blend is disabled (the background has been cleared to transparent), so
    ///     the shader output lands untouched.
    /// </summary>
    public void Draw(ID3D11ShaderResourceView source, int viewportWidth, int viewportHeight)
    {
        if (
            _vs is null
            || _ps is null
            || _inputLayout is null
            || _vertexBuffer is null
            || _sampler is null
            || _blendDisabled is null
        )
        {
            return;
        }

        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.IASetVertexBuffer(0, _vertexBuffer, VertexStride, 0);

        _context.VSSetShader(_vs);
        _context.PSSetShader(_ps);
        _context.PSSetShaderResource(0, source);
        _context.PSSetSampler(0, _sampler);

        _context.OMSetBlendState(_blendDisabled);
        _context.RSSetState(_rasterizer);
        _context.RSSetViewport(0, 0, viewportWidth, viewportHeight);

        _context.Draw(4, 0);

        // Unbind the SRV so it isn't held referenced across the next Spout
        // acquire — Spout shared textures can be reopened when the sender
        // resizes, and a stale binding would keep the old one alive.
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null);
    }

    public void Dispose()
    {
        _vs?.Dispose();
        _ps?.Dispose();
        _inputLayout?.Dispose();
        _vertexBuffer?.Dispose();
        _sampler?.Dispose();
        _blendDisabled?.Dispose();
        _rasterizer?.Dispose();
    }

    private void BuildRasterizer()
    {
        // No culling: the fullscreen quad's triangle-strip winding flips between
        // the two triangles, so a default CullBack state eats one of them. A 2D
        // overlay has no need for culling anyway.
        _rasterizer = _device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    private const int VertexStride = 4 * sizeof(float);

    private void BuildShaders()
    {
        var vsBytecode = ShaderCompiler.Compile(ShaderSource, "VSMain", "vs_5_0", "OverlayQuad");
        var psBytecode = ShaderCompiler.Compile(ShaderSource, "PSMain", "ps_5_0", "OverlayQuad");

        _vs = _device.CreateVertexShader(vsBytecode.Span);
        _ps = _device.CreatePixelShader(psBytecode.Span);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 2 * sizeof(float), 0),
        };

        _inputLayout = _device.CreateInputLayout(inputElements, vsBytecode.Span);
    }

    private void BuildVertexBuffer()
    {
        var bufferDesc = new BufferDescription
        {
            ByteWidth = (uint)(QuadVertices.Length * sizeof(float)),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.None,
        };

        _vertexBuffer = _device.CreateBuffer(QuadVertices.AsSpan(), bufferDesc);
    }

    private void BuildSamplerAndBlend()
    {
        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);

        // No blend: we overwrite the cleared-transparent RTV with the premultiplied
        // shader output. Chrome pass is what does alpha blending afterwards.
        var blendDesc = BlendDescription.Opaque;
        _blendDisabled = _device.CreateBlendState(blendDesc);
    }
}
