using System.Numerics;
using FontStashSharp.Interfaces;
using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.UI.Rendering.Shaders;
using Silk.NET.OpenGL;
using Shader = PersonaEngine.Lib.UI.Common.Shader;
using Texture = PersonaEngine.Lib.UI.Common.Texture;

namespace PersonaEngine.Lib.UI.Rendering.Text;

internal class TextRenderer : IFontStashRenderer2, IDisposable
{
    private const int MAX_SPRITES = 2048;

    private const int MAX_VERTICES = MAX_SPRITES * 4;

    private const int MAX_INDICES = MAX_SPRITES * 6;

    private static readonly short[] indexData = GenerateIndexArray();

    private readonly GL _gl;

    private readonly BufferObject<short> _indexBuffer;

    private readonly Shader _shader;

    private readonly Texture2DManager _textureManager;

    private readonly VertexArrayObject _vao;

    private readonly BufferObject<VertexPositionColorTexture> _vertexBuffer;

    private readonly VertexPositionColorTexture[] _vertexData = new VertexPositionColorTexture[
        MAX_VERTICES
    ];

    private object _lastTexture;

    private int _vertexIndex = 0;

    private int _viewportHeight = 800;

    private int _viewportWidth = 1200;

    public unsafe TextRenderer(GL glApi)
    {
        _gl = glApi;

        _textureManager = new Texture2DManager(_gl);

        _vertexBuffer = new BufferObject<VertexPositionColorTexture>(
            _gl,
            MAX_VERTICES,
            BufferTargetARB.ArrayBuffer,
            true
        );
        _indexBuffer = new BufferObject<short>(
            _gl,
            indexData.Length,
            BufferTargetARB.ElementArrayBuffer,
            false
        );
        _indexBuffer.SetData(indexData, 0, indexData.Length);

        var vertSrc = ShaderRegistry.GetSource("glsl/t_shader.vert");
        var fragSrc = ShaderRegistry.GetSource("glsl/t_shader.frag");
        _shader = new Shader(_gl, vertSrc, fragSrc);
        _shader.Use();

        _vao = new VertexArrayObject(_gl, sizeof(VertexPositionColorTexture));
        _vao.Bind();

        var location = _shader.GetAttribLocation("a_position");
        _vao.VertexAttribPointer(location, 3, VertexAttribPointerType.Float, false, 0);

        location = _shader.GetAttribLocation("a_color");
        _vao.VertexAttribPointer(location, 4, VertexAttribPointerType.UnsignedByte, true, 12);

        location = _shader.GetAttribLocation("a_texCoords0");
        _vao.VertexAttribPointer(location, 2, VertexAttribPointerType.Float, false, 16);
    }

    public void Dispose()
    {
        Dispose(true);
    }

    public ITexture2DManager TextureManager => _textureManager;

    public void DrawQuad(
        object texture,
        ref VertexPositionColorTexture topLeft,
        ref VertexPositionColorTexture topRight,
        ref VertexPositionColorTexture bottomLeft,
        ref VertexPositionColorTexture bottomRight
    )
    {
        if (_lastTexture != texture)
        {
            FlushBuffer();
        }

        _vertexData[_vertexIndex++] = topLeft;
        _vertexData[_vertexIndex++] = topRight;
        _vertexData[_vertexIndex++] = bottomLeft;
        _vertexData[_vertexIndex++] = bottomRight;

        _lastTexture = texture;
    }

    ~TextRenderer()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _vao.Dispose();
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _shader.Dispose();
    }

    public void Begin()
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.CheckError();
        _gl.Enable(EnableCap.Blend);
        _gl.CheckError();
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.CheckError();

        _shader.Use();
        _shader.SetUniform("TextureSampler", 0);

        var transform = Matrix4x4.CreateOrthographicOffCenter(
            0,
            _viewportWidth,
            _viewportHeight,
            0,
            0,
            -1
        );
        _shader.SetUniform("MatrixTransform", transform);

        _vao.Bind();
        _indexBuffer.Bind();
        _vertexBuffer.Bind();
    }

    public void End()
    {
        FlushBuffer();
    }

    private void FlushBuffer()
    {
        unsafe
        {
            if (_vertexIndex == 0 || _lastTexture == null)
            {
                return;
            }

            _vertexBuffer.SetData(_vertexData, 0, _vertexIndex);

            var texture = (Texture)_lastTexture;
            texture.Bind();

            _gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)(_vertexIndex * 6 / 4),
                DrawElementsType.UnsignedShort,
                null
            );
            _vertexIndex = 0;
        }
    }

    private static short[] GenerateIndexArray()
    {
        var result = new short[MAX_INDICES];
        for (int i = 0, j = 0; i < MAX_INDICES; i += 6, j += 4)
        {
            result[i] = (short)j;
            result[i + 1] = (short)(j + 1);
            result[i + 2] = (short)(j + 2);
            result[i + 3] = (short)(j + 3);
            result[i + 4] = (short)(j + 2);
            result[i + 5] = (short)(j + 1);
        }

        return result;
    }

    public void OnViewportChanged(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }
}
