using PersonaEngine.Lib.UI.Rendering;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Draws an <see cref="IFrameSource" /> texture stretched across the current
///     viewport. The fragment shader premultiplies the sampled RGBA so the result
///     is compatible with <c>UpdateLayeredWindow</c>'s <c>AC_SRC_ALPHA</c> blend
///     (which expects premultiplied alpha). One textured triangle strip per frame.
/// </summary>
public sealed class FullscreenQuadRenderer : IDisposable
{
    // Fullscreen triangle strip (position.xy, uv.xy). Clip-space corners with
    // GL-convention UVs (origin bottom-left). Callers pass flipV=true when the
    // source uses DX origin (Y down) — e.g. Spout-delivered textures.
    private static readonly float[] QuadVertices =
    [
        -1f,
        -1f,
        0f,
        0f,
        1f,
        -1f,
        1f,
        0f,
        -1f,
        1f,
        0f,
        1f,
        1f,
        1f,
        1f,
        1f,
    ];

    private const string VertexSource = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aUV;
        out vec2 vUV;
        void main()
        {
            gl_Position = vec4(aPos, 0.0, 1.0);
            vUV = aUV;
        }
        """;

    // Premultiply on output: UpdateLayeredWindow's AC_SRC_ALPHA blend also expects
    // premultiplied alpha. Source FBO is straight-alpha so multiply RGB by A here.
    // uFlipV flips the V coordinate — Spout delivers DX-origin textures (Y down)
    // that need flipping when displayed through GL (Y up) clip space.
    private const string FragmentSource = """
        #version 330 core
        in vec2 vUV;
        out vec4 oColor;
        uniform sampler2D uTex;
        uniform int uFlipV;
        void main()
        {
            vec2 uv = uFlipV != 0 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
            vec4 c = texture(uTex, uv);
            oColor = vec4(c.rgb * c.a, c.a);
        }
        """;

    private readonly GL _gl;

    private uint _vao;
    private uint _vbo;
    private uint _program;
    private int _texUniform;
    private int _flipVUniform;

    public FullscreenQuadRenderer(GL gl)
    {
        _gl = gl;
        BuildBuffers();
        BuildProgram();
    }

    public unsafe void Draw(uint textureHandle, bool flipV)
    {
        if (textureHandle == 0)
        {
            return;
        }

        // We render this first into a cleared FBO, so there's nothing to blend
        // with — write the shader output directly.
        var blendWasEnabled = _gl.IsEnabled(EnableCap.Blend);
        if (blendWasEnabled)
        {
            _gl.Disable(EnableCap.Blend);
        }

        _gl.UseProgram(_program);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, textureHandle);
        _gl.Uniform1(_texUniform, 0);
        _gl.Uniform1(_flipVUniform, flipV ? 1 : 0);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);

        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.UseProgram(0);

        if (blendWasEnabled)
        {
            _gl.Enable(EnableCap.Blend);
        }
    }

    private unsafe void BuildBuffers()
    {
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (float* ptr = QuadVertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(QuadVertices.Length * sizeof(float)),
                ptr,
                BufferUsageARB.StaticDraw
            );
        }

        const uint stride = 4 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(
            1,
            2,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(2 * sizeof(float))
        );

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private void BuildProgram()
    {
        var vs = CompileShader(ShaderType.VertexShader, VertexSource);
        var fs = CompileShader(ShaderType.FragmentShader, FragmentSource);

        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vs);
        _gl.AttachShader(_program, fs);
        _gl.LinkProgram(_program);

        _gl.GetProgram(_program, GLEnum.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = _gl.GetProgramInfoLog(_program);
            throw new InvalidOperationException($"Overlay quad program link failed: {log}");
        }

        _gl.DetachShader(_program, vs);
        _gl.DetachShader(_program, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);

        _texUniform = _gl.GetUniformLocation(_program, "uTex");
        _flipVUniform = _gl.GetUniformLocation(_program, "uFlipV");
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, GLEnum.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Overlay quad {type} compile failed: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
