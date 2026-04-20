using PersonaEngine.Lib.Live2D.Framework.Rendering.OpenGL;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.Live2D;

public class SilkNetApi : OpenGLApi
{
    private readonly GL Gl;

    private readonly int Height;

    private readonly int Width;

    public SilkNetApi(GL gl, int width, int height)
    {
        Width = width;
        Height = height;
        Gl = gl;
    }

    public override bool IsES2 => false;

    public override bool IsPhoneES2 => false;

    public override bool AlwaysClear => false;

    public override void GetWindowSize(out int w, out int h)
    {
        w = Width;
        h = Height;
    }

    public override void ActiveTexture(int bit)
    {
        Gl.ActiveTexture((TextureUnit)bit);
    }

    public override void AttachShader(int program, int shader)
    {
        Gl.AttachShader((uint)program, (uint)shader);
    }

    public override void BindBuffer(int target, int buffer)
    {
        Gl.BindBuffer((BufferTargetARB)target, (uint)buffer);
    }

    public override void BindFramebuffer(int target, int framebuffer)
    {
        Gl.BindFramebuffer((FramebufferTarget)target, (uint)framebuffer);
    }

    public override void BindTexture(int target, int texture)
    {
        Gl.BindTexture((TextureTarget)target, (uint)texture);
    }

    public override void BindVertexArrayOES(int array)
    {
        Gl.BindVertexArray((uint)array);
    }

    public override void BlendFunc(int sfactor, int dfactor)
    {
        Gl.BlendFunc((BlendingFactor)sfactor, (BlendingFactor)dfactor);
    }

    public override void BlendFuncSeparate(int srcRGB, int dstRGB, int srcAlpha, int dstAlpha)
    {
        Gl.BlendFuncSeparate(
            (BlendingFactor)srcRGB,
            (BlendingFactor)dstRGB,
            (BlendingFactor)srcAlpha,
            (BlendingFactor)dstAlpha
        );
    }

    public override void Clear(int mask)
    {
        Gl.Clear((ClearBufferMask)mask);
    }

    public override void ClearColor(float r, float g, float b, float a)
    {
        Gl.ClearColor(r, g, b, a);
    }

    public override void ClearDepthf(float depth)
    {
        Gl.ClearDepth(depth);
    }

    public override void ColorMask(bool r, bool g, bool b, bool a)
    {
        Gl.ColorMask(r, g, b, a);
    }

    public override void CompileShader(int shader)
    {
        Gl.CompileShader((uint)shader);
    }

    public override int CreateProgram()
    {
        return (int)Gl.CreateProgram();
    }

    public override int CreateShader(int type)
    {
        return (int)Gl.CreateShader((ShaderType)type);
    }

    public override void DeleteFramebuffer(int framebuffer)
    {
        Gl.DeleteFramebuffer((uint)framebuffer);
    }

    public override void DeleteProgram(int program)
    {
        Gl.DeleteProgram((uint)program);
    }

    public override void DeleteShader(int shader)
    {
        Gl.DeleteShader((uint)shader);
    }

    public override void DeleteTexture(int texture)
    {
        Gl.DeleteTexture((uint)texture);
    }

    public override void DetachShader(int program, int shader)
    {
        Gl.DetachShader((uint)program, (uint)shader);
    }

    public override void Disable(int cap)
    {
        Gl.Disable((EnableCap)cap);
    }

    public override void DisableVertexAttribArray(int index)
    {
        Gl.DisableVertexAttribArray((uint)index);
    }

    public override void DrawElements(int mode, int count, int type, nint indices)
    {
        unsafe
        {
            Gl.DrawElements(
                (PrimitiveType)mode,
                (uint)count,
                (DrawElementsType)type,
                (void*)indices
            );
        }
    }

    public override void Enable(int cap)
    {
        Gl.Enable((EnableCap)cap);
    }

    public override void EnableVertexAttribArray(int index)
    {
        Gl.EnableVertexAttribArray((uint)index);
    }

    public override void FramebufferTexture2D(
        int target,
        int attachment,
        int textarget,
        int texture,
        int level
    )
    {
        Gl.FramebufferTexture2D(
            (FramebufferTarget)target,
            (FramebufferAttachment)attachment,
            (TextureTarget)textarget,
            (uint)texture,
            level
        );
    }

    public override void FrontFace(int mode)
    {
        Gl.FrontFace((FrontFaceDirection)mode);
    }

    public override void GenerateMipmap(int target)
    {
        Gl.GenerateMipmap((TextureTarget)target);
    }

    public override int GenFramebuffer()
    {
        return (int)Gl.GenFramebuffer();
    }

    public override int GenTexture()
    {
        return (int)Gl.GenTexture();
    }

    public override int GetAttribLocation(int program, string name)
    {
        return Gl.GetAttribLocation((uint)program, name);
    }

    public override void GetBooleanv(int pname, bool[] data)
    {
        Gl.GetBoolean((GetPName)pname, out data[0]);
    }

    public override void GetIntegerv(int pname, out int data)
    {
        Gl.GetInteger((GetPName)pname, out data);
    }

    public override void GetIntegerv(int pname, int[] data)
    {
        Gl.GetInteger((GetPName)pname, out data[0]);
    }

    public override void GetProgramInfoLog(int program, out string infoLog)
    {
        var length = Gl.GetProgram((uint)program, GLEnum.InfoLogLength);
        infoLog = Gl.GetProgramInfoLog((uint)program);
    }

    public override unsafe void GetProgramiv(int program, int pname, int* length)
    {
        Gl.GetProgram((uint)program, (GLEnum)pname, length);
    }

    public override void GetShaderInfoLog(int shader, out string infoLog)
    {
        var length = Gl.GetShader((uint)shader, GLEnum.InfoLogLength);
        infoLog = Gl.GetShaderInfoLog((uint)shader);
    }

    public override unsafe void GetShaderiv(int shader, int pname, int* length)
    {
        Gl.GetShader((uint)shader, (GLEnum)pname, length);
    }

    public override int GetUniformLocation(int program, string name)
    {
        return Gl.GetUniformLocation((uint)program, name);
    }

    public override void GetVertexAttribiv(int index, int pname, out int @params)
    {
        Gl.GetVertexAttrib((uint)index, (VertexAttribPropertyARB)pname, out @params);
    }

    public override bool IsEnabled(int cap)
    {
        return Gl.IsEnabled((EnableCap)cap);
    }

    public override void LinkProgram(int program)
    {
        Gl.LinkProgram((uint)program);
    }

    public override void ShaderSource(int shader, string source)
    {
        Gl.ShaderSource((uint)shader, source);
    }

    public override void TexImage2D(
        int target,
        int level,
        int internalformat,
        int width,
        int height,
        int border,
        int format,
        int type,
        nint pixels
    )
    {
        unsafe
        {
            Gl.TexImage2D(
                (TextureTarget)target,
                level,
                (InternalFormat)internalformat,
                (uint)width,
                (uint)height,
                border,
                (PixelFormat)format,
                (PixelType)type,
                pixels == 0 ? null : (void*)pixels
            );
        }
    }

    public override void TexParameterf(int target, int pname, float param)
    {
        Gl.TexParameter((TextureTarget)target, (TextureParameterName)pname, param);
    }

    public override void TexParameteri(int target, int pname, int param)
    {
        Gl.TexParameter((TextureTarget)target, (TextureParameterName)pname, param);
    }

    public override void Uniform1i(int location, int v0)
    {
        Gl.Uniform1(location, v0);
    }

    public override void Uniform4f(int location, float v0, float v1, float v2, float v3)
    {
        Gl.Uniform4(location, v0, v1, v2, v3);
    }

    public override void UniformMatrix4fv(int location, int count, bool transpose, float[] value)
    {
        Gl.UniformMatrix4(location, (uint)count, transpose, value);
    }

    public override void UseProgram(int program)
    {
        Gl.UseProgram((uint)program);
    }

    public override void ValidateProgram(int program)
    {
        Gl.ValidateProgram((uint)program);
    }

    public override void VertexAttribPointer(
        int index,
        int size,
        int type,
        bool normalized,
        int stride,
        nint pointer
    )
    {
        unsafe
        {
            Gl.VertexAttribPointer(
                (uint)index,
                size,
                (VertexAttribPointerType)type,
                normalized,
                (uint)stride,
                (void*)pointer
            );
        }
    }

    public override void Viewport(int x, int y, int width, int height)
    {
        Gl.Viewport(x, y, (uint)width, (uint)height);
    }

    public override int GetError()
    {
        return (int)Gl.GetError();
    }

    public override int GenBuffer()
    {
        return (int)Gl.GenBuffer();
    }

    public override void BufferData(int target, int size, nint data, int usage)
    {
        unsafe
        {
            Gl.BufferData((BufferTargetARB)target, (nuint)size, (void*)data, (BufferUsageARB)usage);
        }
    }

    public override int GenVertexArray()
    {
        return (int)Gl.GenVertexArray();
    }

    public override void BindVertexArray(int array)
    {
        Gl.BindVertexArray((uint)array);
    }
}
