using System.Numerics;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     The two interactive elements the floating overlay exposes on hover:
///     a single drag button (top-right) and a single resize button (bottom-right).
///     Everything else is click-through.
/// </summary>
public enum OverlayHandle
{
    None,
    Drag,
    Resize,
}

/// <summary>
///     Draws the overlay's hover chrome: two small rounded buttons anchored to the
///     top-right (drag) and bottom-right (resize) corners. No border is drawn — the
///     buttons themselves are the only visual signal that the overlay is grabbable.
///     The entire chrome fades in and out via one global alpha uniform so hover
///     transitions stay smooth without per-element animation bookkeeping.
/// </summary>
public sealed class OverlayChromeRenderer : IDisposable
{
    // Button geometry in overlay-local pixel space. Not DPI-scaled (same convention
    // as the rest of the overlay); if we ever wire PerMonitorV2, multiply by scale.
    public const int ButtonSize = 30;
    public const int ButtonCornerRadius = 7;
    public const int ButtonEdgePadding = 6;

    // Colors tuned to be visible on both light and dark content behind the overlay.
    private static readonly Vector4 ButtonBg = new(0.08f, 0.08f, 0.10f, 0.88f);
    private static readonly Vector4 ButtonBgHot = new(0.16f, 0.16f, 0.18f, 0.92f);
    private static readonly Vector4 ButtonBorder = new(0.95f, 0.95f, 0.97f, 0.70f);
    private static readonly Vector4 IconColor = new(0.96f, 0.96f, 0.98f, 1.0f);

    // One draw call per button: a single quad covering the button rect, with an
    // SDF-driven fragment shader that produces a rounded-rect with a 1-px border.
    // Icons are drawn as small filled rects in a second pass (no rounding needed).
    private const string ButtonVertexSource = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aLocal;
        uniform vec2 uViewport;
        out vec2 vLocal;
        void main()
        {
            vec2 ndc = (aPos / uViewport) * 2.0 - 1.0;
            gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);
            vLocal = aLocal;
        }
        """;

    // SDF for a rounded rect centered at origin: box(d - halfSize + r) - r.
    // Output is premultiplied to match UpdateLayeredWindow's AC_SRC_ALPHA blend.
    private const string ButtonFragmentSource = """
        #version 330 core
        in vec2 vLocal;
        uniform vec2 uHalfSize;
        uniform float uRadius;
        uniform vec4 uFill;
        uniform vec4 uBorder;
        uniform float uAlpha;
        out vec4 oColor;
        void main()
        {
            vec2 d = abs(vLocal) - uHalfSize + vec2(uRadius);
            float sd = length(max(d, 0.0)) - uRadius + min(max(d.x, d.y), 0.0);
            float fillMask = 1.0 - smoothstep(-1.0, 0.0, sd);
            // 1-px border ring around the edge.
            float borderMask = (1.0 - smoothstep(-0.5, 0.5, abs(sd + 0.75))) * fillMask;
            vec4 c = mix(uFill, uBorder, borderMask);
            float a = c.a * fillMask * uAlpha;
            oColor = vec4(c.rgb * a, a);
        }
        """;

    // Flat-colored shader for icon dots (sharp corners are fine at this size).
    private const string IconVertexSource = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        uniform vec2 uViewport;
        void main()
        {
            vec2 ndc = (aPos / uViewport) * 2.0 - 1.0;
            gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);
        }
        """;

    private const string IconFragmentSource = """
        #version 330 core
        uniform vec4 uColor;
        uniform float uAlpha;
        out vec4 oColor;
        void main()
        {
            float a = uColor.a * uAlpha;
            oColor = vec4(uColor.rgb * a, a);
        }
        """;

    // Two buttons × up to 6 icon rects = 12 rects. Keep a small dynamic buffer
    // for the icons; the button geometry is a unit quad reused per button.
    private const int MaxIconRects = 16;

    private readonly GL _gl;
    private readonly float[] _iconScratch = new float[MaxIconRects * 6 * 2];

    private uint _buttonVao;
    private uint _buttonVbo;
    private uint _buttonProgram;
    private int _buttonViewportUniform;
    private int _buttonHalfSizeUniform;
    private int _buttonRadiusUniform;
    private int _buttonFillUniform;
    private int _buttonBorderUniform;
    private int _buttonAlphaUniform;

    private uint _iconVao;
    private uint _iconVbo;
    private uint _iconProgram;
    private int _iconViewportUniform;
    private int _iconColorUniform;
    private int _iconAlphaUniform;

    public OverlayChromeRenderer(GL gl)
    {
        _gl = gl;
        BuildButtonQuad();
        BuildIconBuffer();
        _buttonProgram = BuildProgram(ButtonVertexSource, ButtonFragmentSource, "button");
        _buttonViewportUniform = _gl.GetUniformLocation(_buttonProgram, "uViewport");
        _buttonHalfSizeUniform = _gl.GetUniformLocation(_buttonProgram, "uHalfSize");
        _buttonRadiusUniform = _gl.GetUniformLocation(_buttonProgram, "uRadius");
        _buttonFillUniform = _gl.GetUniformLocation(_buttonProgram, "uFill");
        _buttonBorderUniform = _gl.GetUniformLocation(_buttonProgram, "uBorder");
        _buttonAlphaUniform = _gl.GetUniformLocation(_buttonProgram, "uAlpha");

        _iconProgram = BuildProgram(IconVertexSource, IconFragmentSource, "icon");
        _iconViewportUniform = _gl.GetUniformLocation(_iconProgram, "uViewport");
        _iconColorUniform = _gl.GetUniformLocation(_iconProgram, "uColor");
        _iconAlphaUniform = _gl.GetUniformLocation(_iconProgram, "uAlpha");
    }

    /// <summary>
    ///     Classifies a cursor position in window-relative pixels into the drag button,
    ///     the resize button, or none (anywhere else — click-through territory).
    /// </summary>
    public static OverlayHandle HitTest(int x, int y, int width, int height)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return OverlayHandle.None;
        }

        var (dragX, dragY) = DragButtonPosition(width);
        if (IsInsideButton(x, y, dragX, dragY))
        {
            return OverlayHandle.Drag;
        }

        var (resizeX, resizeY) = ResizeButtonPosition(width, height);
        if (IsInsideButton(x, y, resizeX, resizeY))
        {
            return OverlayHandle.Resize;
        }

        return OverlayHandle.None;
    }

    private static bool IsInsideButton(int x, int y, int buttonX, int buttonY) =>
        x >= buttonX && x < buttonX + ButtonSize && y >= buttonY && y < buttonY + ButtonSize;

    private static (int X, int Y) DragButtonPosition(int width) =>
        (width - ButtonEdgePadding - ButtonSize, ButtonEdgePadding);

    private static (int X, int Y) ResizeButtonPosition(int width, int height) =>
        (width - ButtonEdgePadding - ButtonSize, height - ButtonEdgePadding - ButtonSize);

    public void Render(int windowWidth, int windowHeight, float alpha, OverlayHandle hot)
    {
        if (alpha <= 0f)
        {
            return;
        }

        var blendWasEnabled = _gl.IsEnabled(EnableCap.Blend);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

        var (dragX, dragY) = DragButtonPosition(windowWidth);
        DrawButton(windowWidth, windowHeight, dragX, dragY, alpha, hot == OverlayHandle.Drag);

        var (resizeX, resizeY) = ResizeButtonPosition(windowWidth, windowHeight);
        DrawButton(windowWidth, windowHeight, resizeX, resizeY, alpha, hot == OverlayHandle.Resize);

        BuildDragIcon(dragX, dragY);
        var dragRectCount = _iconRectCount;
        FlushIcons(windowWidth, windowHeight, alpha, dragRectCount, startRect: 0);

        BuildResizeIcon(resizeX, resizeY);
        var resizeRectCount = _iconRectCount - dragRectCount;
        FlushIcons(windowWidth, windowHeight, alpha, resizeRectCount, startRect: dragRectCount);

        _iconRectCount = 0;

        if (!blendWasEnabled)
        {
            _gl.Disable(EnableCap.Blend);
        }
    }

    private void DrawButton(int viewportW, int viewportH, int x, int y, float alpha, bool hot)
    {
        _gl.UseProgram(_buttonProgram);
        _gl.Uniform2(_buttonViewportUniform, (float)viewportW, (float)viewportH);
        _gl.Uniform2(_buttonHalfSizeUniform, ButtonSize * 0.5f, ButtonSize * 0.5f);
        _gl.Uniform1(_buttonRadiusUniform, (float)ButtonCornerRadius);
        var fill = hot ? ButtonBgHot : ButtonBg;
        _gl.Uniform4(_buttonFillUniform, fill.X, fill.Y, fill.Z, fill.W);
        _gl.Uniform4(
            _buttonBorderUniform,
            ButtonBorder.X,
            ButtonBorder.Y,
            ButtonBorder.Z,
            ButtonBorder.W
        );
        _gl.Uniform1(_buttonAlphaUniform, alpha);

        // Upload the button's screen-space position into the VBO (xy attribute).
        // The second attribute (local offset) is static in the quad definition.
        UpdateButtonQuadPosition(x, y);

        _gl.BindVertexArray(_buttonVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    // ── Icons ───────────────────────────────────────────────────────────────────

    private int _iconRectCount;

    private void BuildDragIcon(int buttonX, int buttonY)
    {
        // 2 columns × 3 rows of chunky dots — the classic "grip" pattern. Sized
        // big enough to read at a glance instead of looking like a ditzy speck cloud.
        const int dot = 4;
        const int gap = 3;
        var gridW = dot * 2 + gap;
        var gridH = dot * 3 + gap * 2;
        var startX = buttonX + (ButtonSize - gridW) / 2;
        var startY = buttonY + (ButtonSize - gridH) / 2;

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 2; col++)
            {
                var dx = startX + col * (dot + gap);
                var dy = startY + row * (dot + gap);
                PushIconRect(dx, dy, dot, dot);
            }
        }
    }

    private void BuildResizeIcon(int buttonX, int buttonY)
    {
        // Solid right triangle anchored to the bottom-right of the button, filling
        // most of the lower-right quadrant so it reads unambiguously as a resize-
        // corner grabber at any zoom. Seven 2-px rows, widths 2→14, so the
        // triangle occupies a 14×14 block in the bottom-right of the 30-px button.
        const int rowHeight = 2;
        const int rows = 7;
        const int rightInset = 4;
        const int bottomInset = 4;

        var rightEdge = buttonX + ButtonSize - rightInset;
        var bottomEdge = buttonY + ButtonSize - bottomInset;

        for (var i = 0; i < rows; i++)
        {
            // Row widths: 2, 4, 6, 8, 10, 12, 14 from top to bottom.
            var w = (i + 1) * 2;
            var x = rightEdge - w;
            var y = bottomEdge - (rows - i) * rowHeight;
            PushIconRect(x, y, w, rowHeight);
        }
    }

    private void PushIconRect(int x, int y, int w, int h)
    {
        if (_iconRectCount >= MaxIconRects)
        {
            return;
        }

        var idx = _iconRectCount * 6 * 2;
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

        _iconRectCount++;
    }

    private unsafe void FlushIcons(
        int viewportW,
        int viewportH,
        float alpha,
        int rectCount,
        int startRect
    )
    {
        if (rectCount == 0)
        {
            return;
        }

        _gl.UseProgram(_iconProgram);
        _gl.Uniform2(_iconViewportUniform, (float)viewportW, (float)viewportH);
        _gl.Uniform4(_iconColorUniform, IconColor.X, IconColor.Y, IconColor.Z, IconColor.W);
        _gl.Uniform1(_iconAlphaUniform, alpha);

        _gl.BindVertexArray(_iconVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _iconVbo);

        fixed (float* ptr = &_iconScratch[0])
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                (nuint)(_iconScratch.Length * sizeof(float)),
                ptr
            );
        }

        _gl.DrawArrays(PrimitiveType.Triangles, startRect * 6, (uint)(rectCount * 6));

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    // ── GL setup ────────────────────────────────────────────────────────────────

    private unsafe void BuildButtonQuad()
    {
        // Interleaved: position.xy (updated per button), local.xy (fixed — offsets
        // from the button center, in pixels). Six vertices, two triangles.
        var halfSize = ButtonSize * 0.5f;
        float[] initial =
        [
            0f,
            0f,
            -halfSize,
            -halfSize,
            ButtonSize,
            0f,
            halfSize,
            -halfSize,
            ButtonSize,
            ButtonSize,
            halfSize,
            halfSize,
            0f,
            0f,
            -halfSize,
            -halfSize,
            ButtonSize,
            ButtonSize,
            halfSize,
            halfSize,
            0f,
            ButtonSize,
            -halfSize,
            halfSize,
        ];

        _buttonVao = _gl.GenVertexArray();
        _buttonVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_buttonVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buttonVbo);
        fixed (float* ptr = initial)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(initial.Length * sizeof(float)),
                ptr,
                BufferUsageARB.DynamicDraw
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

    private unsafe void UpdateButtonQuadPosition(int x, int y)
    {
        var halfSize = ButtonSize * 0.5f;
        // Same layout as BuildButtonQuad but with position.xy translated.
        Span<float> verts =
        [
            x,
            y,
            -halfSize,
            -halfSize,
            x + ButtonSize,
            y,
            halfSize,
            -halfSize,
            x + ButtonSize,
            y + ButtonSize,
            halfSize,
            halfSize,
            x,
            y,
            -halfSize,
            -halfSize,
            x + ButtonSize,
            y + ButtonSize,
            halfSize,
            halfSize,
            x,
            y + ButtonSize,
            -halfSize,
            halfSize,
        ];

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buttonVbo);
        fixed (float* ptr = verts)
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                (nuint)(verts.Length * sizeof(float)),
                ptr
            );
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    private unsafe void BuildIconBuffer()
    {
        _iconVao = _gl.GenVertexArray();
        _iconVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_iconVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _iconVbo);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(_iconScratch.Length * sizeof(float)),
            (void*)0,
            BufferUsageARB.DynamicDraw
        );

        const uint stride = 2 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    private uint BuildProgram(string vertexSource, string fragmentSource, string name)
    {
        var vs = CompileShader(ShaderType.VertexShader, vertexSource, name);
        var fs = CompileShader(ShaderType.FragmentShader, fragmentSource, name);
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vs);
        _gl.AttachShader(program, fs);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, GLEnum.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = _gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"Overlay {name} program link failed: {log}");
        }

        _gl.DetachShader(program, vs);
        _gl.DetachShader(program, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return program;
    }

    private uint CompileShader(ShaderType type, string source, string name)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, GLEnum.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Overlay {name} {type} compile failed: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_buttonVbo);
        _gl.DeleteVertexArray(_buttonVao);
        _gl.DeleteBuffer(_iconVbo);
        _gl.DeleteVertexArray(_iconVao);
        _gl.DeleteProgram(_buttonProgram);
        _gl.DeleteProgram(_iconProgram);
    }
}
